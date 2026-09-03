using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NfeAgendamento.App.Fiscal;

namespace NfeAgendamento.App.SharedQueue;

public sealed class SharedQueueGroupProcessor
{
    private static readonly TimeSpan ProcessingRecoveryAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RequestMaxAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RecoveryRequestMaxAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TemporaryRetention = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResponseRetention = TimeSpan.FromMinutes(30);

    private readonly SharedQueuePaths _paths;
    private readonly CentralKeyStore _keyStore;
    private readonly SharedAuthorizedClientStore _authorizedClients;
    private readonly Func<string, CancellationToken, Task<NfeLookupResult>> _lookup;

    public SharedQueueGroupProcessor(
        SharedQueuePaths paths,
        CentralKeyStore keyStore,
        SharedAuthorizedClientStore authorizedClients,
        IServiceScopeFactory scopeFactory)
        : this(
            paths,
            keyStore,
            authorizedClients,
            async (accessKey, cancellationToken) =>
            {
                using var scope = scopeFactory.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<NfeLookupService>().LookupAsync(accessKey, cancellationToken);
            })
    {
    }

    public SharedQueueGroupProcessor(
        SharedQueuePaths paths,
        CentralKeyStore keyStore,
        SharedAuthorizedClientStore authorizedClients,
        Func<string, CancellationToken, Task<NfeLookupResult>> lookup)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _authorizedClients = authorizedClients ?? throw new ArgumentNullException(nameof(authorizedClients));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken = default)
    {
        if (!_paths.ValidateForClient())
            return false;

        var candidate = FindNextValidCandidate(out var requestId);
        if (candidate is null)
            return false;

        var recoveredCandidate = IsRecoveredCandidate(candidate);
        var processingPath = _paths.ProcessingPath(requestId);
        try { File.Move(candidate, processingPath, overwrite: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }

        var responsePath = _paths.ResponsePath(requestId);
        if (File.Exists(responsePath))
        {
            TryDelete(processingPath);
            return true;
        }

        byte[]? aesKey = null;
        try
        {
            var requestBytes = await SharedQueueFileIO.ReadAllBytesAsync(processingPath, SharedQueueFileIO.MaxRequestBytes, cancellationToken);
            var envelope = JsonSerializer.Deserialize<QueueRequestEnvelope>(requestBytes)
                ?? throw new InvalidDataException("Envelope vazio.");
            if (envelope.RequestId != requestId)
                throw new InvalidDataException("O identificador do envelope não corresponde ao arquivo.");

            var now = DateTimeOffset.UtcNow;
            var maxRequestAge = recoveredCandidate ? RecoveryRequestMaxAge : RequestMaxAge;
            if (envelope.CreatedUtc > now.AddMinutes(1) || now - envelope.CreatedUtc > maxRequestAge)
                throw new InvalidDataException("Solicitação expirada.");

            using var privateKey = _keyStore.OpenPrivateKey();
            var opened = SharedQueueCrypto.OpenRequest(envelope, privateKey);
            aesKey = opened.AesKey;

            if (!_authorizedClients.TryAuthenticateAndAdvance(envelope, out var authenticationError))
            {
                var interruptedRecovery = recoveredCandidate
                    && authenticationError.Contains("repetida", StringComparison.OrdinalIgnoreCase);
                var message = interruptedRecovery
                    ? "A liderança da fila foi interrompida durante esta consulta. Por segurança, a tentativa não foi repetida automaticamente na SEFAZ. Faça uma nova consulta."
                    : authenticationError;
                await PublishResponseAsync(
                    SharedQueueCrypto.CreateResponse(
                        requestId,
                        new NfeLookupResult(NfeLookupStatus.Failed, null, null, message, false),
                        aesKey),
                    cancellationToken);
                TryDelete(processingPath);
                return true;
            }

            if (!AccessKeyValidator.IsValid(opened.Payload.AccessKey))
                throw new InvalidDataException("Chave NF-e inválida no envelope.");

            var result = await _lookup(opened.Payload.AccessKey, cancellationToken);
            await PublishResponseAsync(SharedQueueCrypto.CreateResponse(requestId, result, aesKey), cancellationToken);
            TryDelete(processingPath);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException)
        {
            TryDelete(processingPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (aesKey is not null) CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    public Task MaintainAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_paths.ValidateForClient()) return Task.CompletedTask;
        var now = DateTimeOffset.UtcNow;
        RecoverProcessingFiles(now);
        CleanupPattern(_paths.QueueDirectory, "*.tmp", TemporaryRetention, now);
        CleanupPattern(_paths.ResponsesDirectory, "*.tmp", TemporaryRetention, now);
        CleanupPattern(_paths.StatusDirectory, "heartbeat.*.tmp", TemporaryRetention, now);
        CleanupPattern(_paths.PairingDirectory, "*.tmp", TemporaryRetention, now);
        CleanupPattern(_paths.PairingDirectory, "*.pair.processing", TemporaryRetention, now);
        CleanupPattern(_paths.PairingDirectory, "*.pair.res", TemporaryRetention, now);
        CleanupPattern(_paths.ResponsesDirectory, "*.res", ResponseRetention, now);
        return Task.CompletedTask;
    }

    private string? FindNextValidCandidate(out Guid requestId)
    {
        requestId = Guid.Empty;
        string[] candidates;
        try
        {
            candidates = Directory.EnumerateFiles(_paths.QueueDirectory, "*.req", SearchOption.TopDirectoryOnly)
                .OrderBy(File.GetCreationTimeUtc).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (!TryParseRequestId(candidate, out var parsedId))
            {
                TryDelete(candidate);
                continue;
            }
            try
            {
                if (SharedQueueFileIO.IsReparsePoint(candidate))
                {
                    TryDelete(candidate);
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }
            requestId = parsedId;
            return candidate;
        }
        return null;
    }

    private async Task PublishResponseAsync(QueueResponseEnvelope response, CancellationToken cancellationToken)
    {
        var target = _paths.ResponsePath(response.RequestId);
        var temporary = _paths.ResponseTemporaryPath(response.RequestId);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
            await SharedQueueFileIO.WriteAtomicAsync(temporary, target, bytes, SharedQueueFileIO.MaxResponseBytes, true, cancellationToken);
        }
        finally { TryDelete(temporary); }
    }

    private void RecoverProcessingFiles(DateTimeOffset now)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(_paths.ProcessingDirectory, "*.req", SearchOption.TopDirectoryOnly).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { return; }

        foreach (var processingPath in files)
        {
            if (!TryParseRequestId(processingPath, out var requestId))
            {
                TryDelete(processingPath);
                continue;
            }
            try
            {
                if (SharedQueueFileIO.IsReparsePoint(processingPath)) { TryDelete(processingPath); continue; }
                if (File.Exists(_paths.ResponsePath(requestId))) { TryDelete(processingPath); continue; }
                var age = now - new DateTimeOffset(File.GetLastWriteTimeUtc(processingPath), TimeSpan.Zero);
                if (age < ProcessingRecoveryAge) continue;
                var queuePath = _paths.RequestPath(requestId);
                if (!File.Exists(queuePath)) File.Move(processingPath, queuePath, overwrite: false);
                else TryDelete(processingPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
    }

    private static bool IsRecoveredCandidate(string path)
    {
        try
        {
            var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            return DateTimeOffset.UtcNow - lastWrite >= ProcessingRecoveryAge;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void CleanupPattern(string directory, string pattern, TimeSpan retention, DateTimeOffset now)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { return; }
        foreach (var path in files)
        {
            try
            {
                if (SharedQueueFileIO.IsReparsePoint(path)) continue;
                var age = now - new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                if (age >= retention) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
    }

    private static bool TryParseRequestId(string path, out Guid requestId)
    {
        var fileName = Path.GetFileName(path);
        if (!fileName.EndsWith(".req", StringComparison.OrdinalIgnoreCase))
        {
            requestId = Guid.Empty;
            return false;
        }
        return Guid.TryParseExact(fileName[..^4], "N", out requestId) && requestId != Guid.Empty;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

public sealed class AutomaticSharedQueueProcessingHostedService : BackgroundService
{
    private readonly SharedQueueCentralService _central;
    private readonly SharedQueueGroupPairingProcessor _pairing;
    private readonly SharedQueueGroupProcessor _processor;

    public AutomaticSharedQueueProcessingHostedService(
        SharedQueueCentralService central,
        SharedQueueGroupPairingProcessor pairing,
        SharedQueueGroupProcessor processor)
    {
        _central = central;
        _pairing = pairing;
        _processor = processor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextMaintenance = DateTimeOffset.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_central.IsActive)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                if (DateTimeOffset.UtcNow >= nextMaintenance)
                {
                    await _processor.MaintainAsync(stoppingToken);
                    nextMaintenance = DateTimeOffset.UtcNow.AddSeconds(30);
                }

                var paired = await _pairing.ProcessOneAsync(stoppingToken);
                var processed = await _processor.ProcessOneAsync(stoppingToken);
                if (!paired && !processed)
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
