using System.Security.Cryptography;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;

namespace NfeAgendamento.App.SharedQueue;

public sealed record SharedQueueClientStatus(
    bool ShareAvailable,
    bool IsPaired,
    bool CentralOnline,
    string? CentralId,
    DateTimeOffset? LastHeartbeatUtc,
    string? Message);

public sealed class SharedQueueClient
{
    private static readonly TimeSpan HeartbeatMaxAge = TimeSpan.FromSeconds(10);
    public static TimeSpan DefaultLookupTimeout { get; } = TimeSpan.FromMinutes(3);

    private readonly SharedQueuePaths _paths;
    private readonly PendingRequestSecretStore _pendingSecrets;
    private readonly ClientPairingStore _pairingStore;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _timeout;

    public SharedQueueClient(
        SharedQueuePaths paths,
        PendingRequestSecretStore pendingSecrets,
        ClientPairingStore pairingStore)
        : this(paths, pendingSecrets, pairingStore, TimeSpan.FromMilliseconds(250), DefaultLookupTimeout)
    {
    }

    public SharedQueueClient(
        SharedQueuePaths paths,
        PendingRequestSecretStore pendingSecrets)
        : this(paths, pendingSecrets, new ClientPairingStore(), TimeSpan.FromMilliseconds(250), DefaultLookupTimeout)
    {
    }

    public SharedQueueClient(
        SharedQueuePaths paths,
        PendingRequestSecretStore pendingSecrets,
        TimeSpan pollInterval,
        TimeSpan timeout)
        : this(paths, pendingSecrets, new ClientPairingStore(), pollInterval, timeout)
    {
    }

    public SharedQueueClient(
        SharedQueuePaths paths,
        PendingRequestSecretStore pendingSecrets,
        ClientPairingStore pairingStore,
        TimeSpan pollInterval,
        TimeSpan timeout)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _pendingSecrets = pendingSecrets ?? throw new ArgumentNullException(nameof(pendingSecrets));
        _pairingStore = pairingStore ?? throw new ArgumentNullException(nameof(pairingStore));
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _pollInterval = pollInterval;
        _timeout = timeout;
    }

    public bool IsPaired => _pairingStore.IsPaired;

    public SharedQueueClientStatus GetStatus()
    {
        if (!_paths.ValidateForClient())
            return new SharedQueueClientStatus(false, IsPaired, false, null, null, $"A pasta '{SharedQueuePaths.DefaultRoot}' não está disponível ou não foi inicializada.");

        var pairing = _pairingStore.Load();
        if (pairing is null)
            return new SharedQueueClientStatus(true, false, false, null, null, "Este PC ainda não foi pareado com a Central.");

        try
        {
            var heartbeat = ReadHeartbeat(pairing.CentralPublicKey);
            if (heartbeat is null)
                return new SharedQueueClientStatus(true, true, false, pairing.CentralId, null, "Central offline ou indisponível.");

            var now = DateTimeOffset.UtcNow;
            var online = now - heartbeat.UpdatedUtc <= HeartbeatMaxAge
                && heartbeat.UpdatedUtc <= now.AddMinutes(1);
            return new SharedQueueClientStatus(
                true,
                true,
                online,
                heartbeat.CentralId,
                heartbeat.UpdatedUtc,
                online ? null : "Central offline ou indisponível.");
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or FormatException
            or CryptographicException
            or InvalidDataException)
        {
            return new SharedQueueClientStatus(true, true, false, pairing.CentralId, null, ex.Message);
        }
    }

    public async Task<NfeLookupResult> LookupAsync(string accessKey, CancellationToken cancellationToken = default)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));

        var status = GetStatus();
        if (!status.ShareAvailable)
            return Failure($"A pasta compartilhada '{SharedQueuePaths.DefaultRoot}' não está disponível.");
        if (!status.IsPaired)
            return Failure("Este PC ainda não foi pareado com a Central.");
        if (!status.CentralOnline)
            return Failure(status.Message ?? "Central offline ou indisponível.");

        ClientRequestCredentials credentials;
        try
        {
            credentials = _pairingStore.ReserveCredentials();
            _ = ReadHeartbeat(credentials.CentralPublicKey)
                ?? throw new InvalidDataException("Heartbeat da Central indisponível.");
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or FormatException
            or CryptographicException
            or InvalidDataException
            or InvalidOperationException)
        {
            return Failure($"Não foi possível validar a Central: {ex.Message}");
        }

        var requestId = Guid.NewGuid();
        ClientRequestMaterial material;
        try
        {
            material = SharedQueueCrypto.CreateClientRequest(
                requestId,
                accessKey,
                credentials.CentralPublicKey,
                credentials.ClientId,
                credentials.Sequence,
                credentials.ClientSecret);
        }
        catch (CryptographicException)
        {
            return Failure("Não foi possível proteger a consulta para envio à Central.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentials.ClientSecret);
            CryptographicOperations.ZeroMemory(credentials.CentralPublicKey);
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
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or CryptographicException
            or JsonException
            or InvalidDataException)
        {
            return Failure($"Falha na comunicação com a Central: {ex.Message}");
        }
        finally
        {
            TryDelete(_paths.RequestPath(requestId));
            _pendingSecrets.Delete(requestId);
            CryptographicOperations.ZeroMemory(material.AesKey);
        }
    }

    private QueueHeartbeat? ReadHeartbeat(byte[] pinnedPublicKey)
    {
        var path = _paths.StatusPath("heartbeat.json");
        if (!File.Exists(path))
            return null;

        var bytes = SharedQueueFileIO.ReadAllBytes(path, SharedQueueFileIO.MaxHeartbeatBytes);
        var heartbeat = JsonSerializer.Deserialize<QueueHeartbeat>(bytes);
        if (heartbeat is null
            || heartbeat.Version != SharedQueueCrypto.ProtocolVersion
            || string.IsNullOrWhiteSpace(heartbeat.PublicKeyBase64))
        {
            throw new InvalidDataException("Heartbeat da Central inválido.");
        }

        byte[] advertisedKey;
        try
        {
            advertisedKey = Convert.FromBase64String(heartbeat.PublicKeyBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Identidade pública da Central inválida.", ex);
        }

        try
        {
            if (!advertisedKey.AsSpan().SequenceEqual(pinnedPublicKey))
                throw new InvalidDataException("A identidade da Central mudou. Faça o pareamento novamente.");
            if (!SharedQueueCrypto.VerifyHeartbeatSignature(heartbeat, pinnedPublicKey))
                throw new InvalidDataException("A assinatura do heartbeat da Central é inválida.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(advertisedKey);
        }

        return heartbeat;
    }

    private async Task PublishRequestAsync(QueueRequestEnvelope envelope, CancellationToken cancellationToken)
    {
        var target = _paths.RequestPath(envelope.RequestId);
        var temporary = _paths.RequestTemporaryPath(envelope.RequestId);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            await SharedQueueFileIO.WriteAtomicAsync(
                temporary,
                target,
                bytes,
                SharedQueueFileIO.MaxRequestBytes,
                overwrite: false,
                cancellationToken);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private async Task<NfeLookupResult> ReadResponseAsync(Guid requestId, string responsePath, CancellationToken cancellationToken)
    {
        var bytes = await SharedQueueFileIO.ReadAllBytesAsync(responsePath, SharedQueueFileIO.MaxResponseBytes, cancellationToken);
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
