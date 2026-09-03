using System.Security.Cryptography;
using System.Text.Json;

namespace NfeAgendamento.App.SharedQueue;

public sealed class SharedQueueGroupPairingProcessor
{
    private static readonly TimeSpan MaxRequestAge = TimeSpan.FromMinutes(2);
    private readonly SharedQueuePaths _paths;
    private readonly PairingCodeService _codes;
    private readonly SharedAuthorizedClientStore _authorizedClients;
    private readonly CentralKeyStore _centralKeyStore;
    private readonly CandidateStateStore _candidateState;
    private readonly CandidateBundleStore _candidateBundles;

    public SharedQueueGroupPairingProcessor(
        SharedQueuePaths paths,
        PairingCodeService codes,
        SharedAuthorizedClientStore authorizedClients,
        CentralKeyStore centralKeyStore,
        CandidateStateStore candidateState,
        CandidateBundleStore candidateBundles)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _codes = codes ?? throw new ArgumentNullException(nameof(codes));
        _authorizedClients = authorizedClients ?? throw new ArgumentNullException(nameof(authorizedClients));
        _centralKeyStore = centralKeyStore ?? throw new ArgumentNullException(nameof(centralKeyStore));
        _candidateState = candidateState ?? throw new ArgumentNullException(nameof(candidateState));
        _candidateBundles = candidateBundles ?? throw new ArgumentNullException(nameof(candidateBundles));
    }

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken = default)
    {
        if (!_paths.ValidateForClient() || !_codes.TryGetActiveKey(out var pairingKey))
            return false;

        try
        {
            var candidate = FindNextValidCandidate(out var requestId);
            if (candidate is null)
                return false;

            var processing = _paths.PairingProcessingPath(requestId);
            try { File.Move(candidate, processing, overwrite: false); }
            catch (IOException) { return false; }

            try
            {
                var bytes = await SharedQueueFileIO.ReadAllBytesAsync(processing, SharedQueueFileIO.MaxPairingBytes, cancellationToken);
                var envelope = JsonSerializer.Deserialize<QueuePairingRequestEnvelope>(bytes)
                    ?? throw new InvalidDataException("Pedido de pareamento vazio.");
                if (envelope.RequestId != requestId)
                    throw new InvalidDataException("Identificador do pareamento inválido.");
                var now = DateTimeOffset.UtcNow;
                if (envelope.CreatedUtc > now.AddMinutes(1) || now - envelope.CreatedUtc > MaxRequestAge)
                    throw new InvalidDataException("Pedido de pareamento expirado.");

                var request = SharedQueuePairingCrypto.OpenRequest(envelope, pairingKey);
                if (request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.ClientName))
                    throw new InvalidDataException("Identidade do cliente inválida.");

                var groupKey = _candidateState.Load()
                    ?? throw new InvalidOperationException("O líder ainda não possui o estado seguro do grupo.");
                var clientSecret = RandomNumberGenerator.GetBytes(32);
                var publicKey = _centralKeyStore.GetOrCreatePublicKey();
                var fingerprint = SHA256.HashData(publicKey);
                try
                {
                    _authorizedClients.Authorize(request.ClientId, request.ClientName, clientSecret);
                    await _candidateBundles.WriteAsync(
                        request.ClientId,
                        clientSecret,
                        new CandidateBundlePayload(groupKey, fingerprint),
                        cancellationToken);

                    var response = SharedQueuePairingCrypto.CreateResponse(
                        requestId,
                        new QueuePairingResponsePayload(
                            request.ClientId,
                            request.ClientName,
                            clientSecret,
                            Environment.MachineName,
                            publicKey),
                        pairingKey);
                    var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response);
                    var responseTemp = _paths.PairingResponseTemporaryPath(requestId);
                    try
                    {
                        await SharedQueueFileIO.WriteAtomicAsync(
                            responseTemp,
                            _paths.PairingResponsePath(requestId),
                            responseBytes,
                            SharedQueueFileIO.MaxPairingBytes,
                            overwrite: true,
                            cancellationToken);
                    }
                    finally
                    {
                        TryDelete(responseTemp);
                        CryptographicOperations.ZeroMemory(responseBytes);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(groupKey);
                    CryptographicOperations.ZeroMemory(clientSecret);
                    CryptographicOperations.ZeroMemory(publicKey);
                    CryptographicOperations.ZeroMemory(fingerprint);
                }

                TryDelete(processing);
                return true;
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or InvalidOperationException)
            {
                TryDelete(processing);
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pairingKey);
        }
    }

    private string? FindNextValidCandidate(out Guid requestId)
    {
        requestId = Guid.Empty;
        string[] candidates;
        try
        {
            candidates = Directory.EnumerateFiles(_paths.PairingDirectory, "*.pair.req", SearchOption.TopDirectoryOnly)
                .OrderBy(File.GetCreationTimeUtc)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            var name = Path.GetFileName(candidate);
            const string suffix = ".pair.req";
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(name[..^suffix.Length], "N", out var parsedId)
                || parsedId == Guid.Empty)
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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
