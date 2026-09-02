using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NfeAgendamento.App.Fiscal;

namespace NfeAgendamento.App.SharedQueue;

public sealed class SharedQueueProcessor
{
    private static readonly TimeSpan ProcessingRecoveryAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RequestMaxAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TemporaryRetention = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResponseRetention = TimeSpan.FromMinutes(30);

    private readonly SharedQueuePaths _paths;
    private readonly CentralKeyStore _keyStore;
    private readonly AuthorizedClientStore _authorizedClients;
    private readonly Func<string, CancellationToken, Task<NfeLookupResult>> _lookup;

    public SharedQueueProcessor(
        SharedQueuePaths paths,
        CentralKeyStore keyStore,
        AuthorizedClientStore authorizedClients,
        IServiceScopeFactory scopeFactory)
        : this(
            paths,
            keyStore,
            authorizedClients,
            async (accessKey, cancellationToken) =>
            {
                using var scope = scopeFactory.CreateScope();
                return await scope.ServiceProvider
                    .GetRequiredService<NfeLookupService>()
                    .LookupAsync(accessKey, cancellationToken);
            })
    {
    }

    public SharedQueueProcessor(
        SharedQueuePaths paths,
        CentralKeyStore keyStore,
        Func<string, CancellationToken, Task<NfeLookupResult>> lookup)
        : this(paths, keyStore, new AuthorizedClientStore(), lookup)
    {
    }

    public SharedQueueProcessor(
        SharedQueuePaths paths,
        CentralKeyStore keyStore,
        AuthorizedClientStore authorizedClients,
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

        var processingPath = _paths.ProcessingPath(requestId);
        try
        {
            File.Move(candidate, processingPath, overwrite: false);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        var responsePath = _paths.ResponsePath(requestId);
        if (File.Exists(responsePath))
        {
            TryDelete(processingPath);
            return true;
        }

        byte[]? aesKey = null;
        try
        {
            var requestBytes = await SharedQueueFileIO.ReadAllBytesAsync(
                processingPath,
                SharedQueueFileIO.MaxRequestBytes,
                cancellationToken);
            var envelope = JsonSerializer.Deserialize<QueueRequestEnvelope>(requestBytes)
                ?? throw new InvalidDataException("Envelope vazio.");
            if (envelope.RequestId != requestId)
                throw new InvalidDataException("O identificador do envelope não corresponde ao arquivo.");

            var now = DateTimeOffset.UtcNow;
            if (envelope.CreatedUtc > now.AddMinutes(1) || now - envelope.CreatedUtc > RequestMaxAge)
                throw new InvalidDataException("Solicitação expirada.");

            using var privateKey = _keyStore.OpenPrivateKey();
            var opened = SharedQueueCrypto.OpenRequest(envelope, privateKey);
            aesKey = opened.AesKey;

            if (!_authorizedClients.TryAuthenticateAndAdvance(envelope, out var authenticationError))
            {
                var denied = new NfeLookupResult(
                    NfeLookupStatus.Failed,
                    null,
                    null,
                    authenticationError,
                    false);
                await PublishResponseAsync(
                    SharedQueueCrypto.CreateResponse(requestId, denied, aesKey),
                    cancellationToken);
                TryDelete(processingPath);
                return true;
            }

            if (!AccessKeyValidator.IsValid(opened.Payload.AccessKey))
                throw new InvalidDataException("Chave NF-e inválida no envelope.");

            var result = await _lookup(opened.Payload.AccessKey, cancellationToken);
            var response = SharedQueueCrypto.CreateResponse(requestId, result, aesKey);
            await PublishResponseAsync(response, cancellationToken);
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
            if (aesKey is not null)
                CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    public Task MaintainAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_paths.ValidateForClient())
            return Task.CompletedTask;

        RecoverProcessingFiles(DateTimeOffset.UtcNow);
        CleanupPattern(_paths.QueueDirectory, "*.tmp", TemporaryRetention, DateTimeOffset.UtcNow);
        CleanupPattern(_paths.ResponsesDirectory, "*.tmp", TemporaryRetention, DateTimeOffset.UtcNow);
        CleanupPattern(_paths.StatusDirectory, "heartbeat.*.tmp", TemporaryRetention, DateTimeOffset.UtcNow);
        CleanupPattern(_paths.PairingDirectory, "*.tmp", TemporaryRetention, DateTimeOffset.UtcNow);
        CleanupPattern(_paths.PairingDirectory, "*.pair.processing", TemporaryRetention, DateTimeOffset.UtcNow);
        CleanupPattern(_paths.PairingDirectory, "*.pair.res", TemporaryRetention, DateTimeOffset.UtcNow);
        CleanupPattern(_paths.ResponsesDirectory, "*.res", ResponseRetention, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    private string? FindNextValidCandidate(out Guid requestId)
    {
        requestId = Guid.Empty;
        string[] candidates;
        try
        {
            candidates = Directory.EnumerateFiles(_paths.QueueDirectory, "*.req", SearchOption.TopDirectoryOnly)
                .OrderBy(File.GetCreationTimeUtc)
                .ToArray();
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
            await SharedQueueFileIO.WriteAtomicAsync(
                temporary,
                target,
                bytes,
                SharedQueueFileIO.MaxResponseBytes,
                overwrite: true,
                cancellationToken);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private void RecoverProcessingFiles(DateTimeOffset now)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_paths.ProcessingDirectory, "*.req", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return;
        }

        foreach (var processingPath in files)
        {
            if (!TryParseRequestId(processingPath, out var requestId))
            {
                TryDelete(processingPath);
                continue;
            }

            try
            {
                if (SharedQueueFileIO.IsReparsePoint(processingPath))
                {
                    TryDelete(processingPath);
                    continue;
                }

                if (File.Exists(_paths.ResponsePath(requestId)))
                {
                    TryDelete(processingPath);
                    continue;
                }

                var age = now - new DateTimeOffset(File.GetLastWriteTimeUtc(processingPath), TimeSpan.Zero);
                if (age < ProcessingRecoveryAge)
                    continue;

                var queuePath = _paths.RequestPath(requestId);
                if (!File.Exists(queuePath))
                    File.Move(processingPath, queuePath, overwrite: false);
                else
                    TryDelete(processingPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
            }
        }
    }

    private static void CleanupPattern(string directory, string pattern, TimeSpan retention, DateTimeOffset now)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return;
        }

        foreach (var path in files)
        {
            try
            {
                if (SharedQueueFileIO.IsReparsePoint(path))
                    continue;
                var age = now - new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                if (age >= retention)
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
            }
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
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public sealed class SharedQueueProcessingHostedService : BackgroundService
{
    private readonly SharedQueueCentralService _central;
    private readonly SharedQueuePairingProcessor _pairing;
    private readonly SharedQueueProcessor _processor;

    public SharedQueueProcessingHostedService(
        SharedQueueCentralService central,
        SharedQueuePairingProcessor pairing,
        SharedQueueProcessor processor)
    {
        _central = central ?? throw new ArgumentNullException(nameof(central));
        _pairing = pairing ?? throw new ArgumentNullException(nameof(pairing));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
