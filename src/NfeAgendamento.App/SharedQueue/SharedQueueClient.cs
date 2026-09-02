using System.Security.Cryptography;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;

namespace NfeAgendamento.App.SharedQueue;

public sealed record SharedQueueClientStatus(
    bool ShareAvailable,
    bool CentralOnline,
    string? CentralId,
    DateTimeOffset? LastHeartbeatUtc,
    string? Message);

public sealed class SharedQueueClient
{
    private static readonly TimeSpan HeartbeatMaxAge = TimeSpan.FromSeconds(10);
    private readonly SharedQueuePaths _paths;
    private readonly PendingRequestSecretStore _pendingSecrets;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _timeout;

    public SharedQueueClient(
        SharedQueuePaths paths,
        PendingRequestSecretStore pendingSecrets)
        : this(paths, pendingSecrets, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(90))
    {
    }

    public SharedQueueClient(
        SharedQueuePaths paths,
        PendingRequestSecretStore pendingSecrets,
        TimeSpan pollInterval,
        TimeSpan timeout)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _pendingSecrets = pendingSecrets ?? throw new ArgumentNullException(nameof(pendingSecrets));
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _pollInterval = pollInterval;
        _timeout = timeout;
    }

    public SharedQueueClientStatus GetStatus()
    {
        if (!_paths.ValidateForClient())
            return new SharedQueueClientStatus(false, false, null, null, $"A pasta '{SharedQueuePaths.DefaultRoot}' não está disponível ou não foi inicializada.");

        try
        {
            var heartbeat = ReadHeartbeat();
            if (heartbeat is null)
                return new SharedQueueClientStatus(true, false, null, null, "Central offline ou indisponível.");

            var online = DateTimeOffset.UtcNow - heartbeat.UpdatedUtc <= HeartbeatMaxAge
                && heartbeat.UpdatedUtc <= DateTimeOffset.UtcNow.AddMinutes(1);
            return new SharedQueueClientStatus(
                true,
                online,
                heartbeat.CentralId,
                heartbeat.UpdatedUtc,
                online ? null : "Central offline ou indisponível.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            return new SharedQueueClientStatus(true, false, null, null, ex.Message);
        }
    }

    public async Task<NfeLookupResult> LookupAsync(string accessKey, CancellationToken cancellationToken = default)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));

        var status = GetStatus();
        if (!status.ShareAvailable)
            return Failure($"A pasta compartilhada '{SharedQueuePaths.DefaultRoot}' não está disponível.");
        if (!status.CentralOnline)
            return Failure("Central offline ou indisponível.");

        QueueHeartbeat heartbeat;
        try
        {
            heartbeat = ReadHeartbeat() ?? throw new InvalidDataException("Heartbeat da Central indisponível.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidDataException)
        {
            return Failure($"Não foi possível validar a Central: {ex.Message}");
        }

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(heartbeat.PublicKeyBase64);
        }
        catch (FormatException)
        {
            return Failure("A identidade pública da Central é inválida.");
        }

        var requestId = Guid.NewGuid();
        ClientRequestMaterial material;
        try
        {
            material = SharedQueueCrypto.CreateClientRequest(requestId, accessKey, publicKey);
        }
        catch (CryptographicException)
        {
            return Failure("Não foi possível proteger a consulta para envio à Central.");
        }

        try
        {
            await _pendingSecrets.SaveAsync(requestId, material.AesKey, cancellationToken);
            await PublishRequestAsync(material.Envelope, cancellationToken);

            var deadline = DateTimeOffset.UtcNow + _timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var responsePath = _paths.ResponsePath(requestId);
                if (File.Exists(responsePath))
                {
                    var result = await ReadResponseAsync(requestId, responsePath, cancellationToken);
                    TryDelete(responsePath);
                    _pendingSecrets.Delete(requestId);
                    return result;
                }

                await Task.Delay(_pollInterval, cancellationToken);
            }

            return Failure("A Central recebeu a solicitação, mas a resposta excedeu o tempo limite.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return Failure($"Falha na comunicação com a Central: {ex.Message}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.AesKey);
        }
    }

    private QueueHeartbeat? ReadHeartbeat()
    {
        var path = _paths.StatusPath("heartbeat.json");
        if (!File.Exists(path))
            return null;

        var heartbeat = JsonSerializer.Deserialize<QueueHeartbeat>(File.ReadAllBytes(path));
        if (heartbeat is null || heartbeat.Version != SharedQueueCrypto.ProtocolVersion || string.IsNullOrWhiteSpace(heartbeat.PublicKeyBase64))
            return null;
        return heartbeat;
    }

    private async Task PublishRequestAsync(QueueRequestEnvelope envelope, CancellationToken cancellationToken)
    {
        var target = _paths.RequestPath(envelope.RequestId);
        var temporary = _paths.RequestTemporaryPath(envelope.RequestId);
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(envelope), cancellationToken);
            File.Move(temporary, target, overwrite: false);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private async Task<NfeLookupResult> ReadResponseAsync(Guid requestId, string responsePath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(responsePath, cancellationToken);
        var envelope = JsonSerializer.Deserialize<QueueResponseEnvelope>(bytes)
            ?? throw new InvalidDataException("Resposta da Central inválida.");
        if (envelope.RequestId != requestId)
            throw new CryptographicException("Resposta pertence a outra solicitação.");

        var key = await _pendingSecrets.LoadAsync(requestId, cancellationToken);
        try
        {
            return SharedQueueCrypto.OpenResponse(envelope, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static NfeLookupResult Failure(string message) =>
        new(NfeLookupStatus.Failed, null, null, message, false);

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
