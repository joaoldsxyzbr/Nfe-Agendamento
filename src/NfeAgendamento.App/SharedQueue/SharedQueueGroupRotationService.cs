using System.Security.Cryptography;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Storage;

namespace NfeAgendamento.App.SharedQueue;

public sealed record GroupRotationResult(bool Success, string Message);

public sealed class SharedQueueGroupRotationService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SharedQueuePaths _paths;
    private readonly CandidateStateStore _candidateState;
    private readonly ClientPairingStore _clientPairing;
    private readonly SharedAuthorizedClientStore _authorizedClients;
    private readonly CandidateBundleStore _candidateBundles;
    private readonly CandidateIdentityTransitionStore _candidateTransitions;
    private readonly SharedQueueGroupRotationStorage _storage;
    private readonly FiscalCooldownStore _cooldown;
    private readonly EncryptedXmlCache _cache;
    private int _rotationInProgress;

    public SharedQueueGroupRotationService(
        SharedQueuePaths paths,
        CandidateStateStore candidateState,
        ClientPairingStore clientPairing,
        SharedAuthorizedClientStore authorizedClients,
        CandidateBundleStore candidateBundles,
        SharedQueueGroupRotationStorage storage,
        FiscalCooldownStore cooldown,
        EncryptedXmlCache cache)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _candidateState = candidateState ?? throw new ArgumentNullException(nameof(candidateState));
        _clientPairing = clientPairing ?? throw new ArgumentNullException(nameof(clientPairing));
        _authorizedClients = authorizedClients ?? throw new ArgumentNullException(nameof(authorizedClients));
        _candidateBundles = candidateBundles ?? throw new ArgumentNullException(nameof(candidateBundles));
        _candidateTransitions = new CandidateIdentityTransitionStore(paths);
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _cooldown = cooldown ?? throw new ArgumentNullException(nameof(cooldown));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public bool BlocksFiscalWork => Volatile.Read(ref _rotationInProgress) != 0 || _storage.HasPending;

    public async Task<GroupRotationResult> RevokeAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        if (clientId == Guid.Empty)
            return new GroupRotationResult(false, "Identidade do PC inválida.");

        await _gate.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _rotationInProgress, 1);
        try
        {
            if (_storage.HasPending && !await CompletePendingCoreAsync(cancellationToken))
                return new GroupRotationResult(false, "Existe uma rotação de confiança pendente que ainda não pôde ser concluída.");

            var local = _clientPairing.Load();
            if (local is null)
                return new GroupRotationResult(false, "Este PC não possui uma identidade local válida para administrar o grupo.");

            try
            {
                if (local.ClientId == clientId)
                    return new GroupRotationResult(false, "O líder não pode revogar o próprio PC. Transfira a liderança antes de removê-lo.");

                var currentClients = _authorizedClients.Snapshot().ToArray();
                try
                {
                    if (!currentClients.Any(item => item.ClientId == clientId))
                        return new GroupRotationResult(false, "O PC informado não está na lista de dispositivos autorizados.");

                    var remaining = currentClients
                        .Where(item => item.ClientId != clientId)
                        .Select(CloneClient)
                        .ToArray();
                    try
                    {
                        if (!remaining.Any(item => item.ClientId == local.ClientId))
                            return new GroupRotationResult(false, "O líder atual precisa permanecer autorizado durante a rotação.");

                        var oldGroupKey = _candidateState.Load();
                        if (oldGroupKey is null)
                            return new GroupRotationResult(false, "Estado seguro atual do grupo indisponível.");

                        byte[]? oldPublicKey = null;
                        byte[]? oldFingerprint = null;
                        byte[]? newGroupKey = null;
                        byte[]? newPrivateKey = null;
                        byte[]? newPublicKey = null;
                        byte[]? newFingerprint = null;
                        GroupIdentityTransition? newTransition = null;
                        GroupIdentityTransition[]? nextTransitions = null;
                        var previousTransitions = new Dictionary<Guid, GroupIdentityTransition[]>();
                        var rotationId = Guid.NewGuid();
                        var markerWritten = false;
                        try
                        {
                            var identity = new SharedGroupIdentityStore(_paths);
                            oldPublicKey = identity.GetPublicKey(oldGroupKey);
                            oldFingerprint = SHA256.HashData(oldPublicKey);
                            var cooldown = await _cooldown.ReadAsync(cancellationToken);

                            newGroupKey = RandomNumberGenerator.GetBytes(32);
                            using (var rsa = RSA.Create(2048))
                            {
                                newPrivateKey = rsa.ExportPkcs8PrivateKey();
                                newPublicKey = rsa.ExportSubjectPublicKeyInfo();
                            }
                            newFingerprint = SHA256.HashData(newPublicKey);

                            using (var oldPrivateKey = identity.OpenPrivateKey(oldGroupKey))
                                newTransition = GroupRotationProof.Create(oldPrivateKey, oldPublicKey, newPublicKey);

                            foreach (var client in remaining)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var existing = _candidateTransitions.Read(client.ClientId, client.Secret)
                                    .Select(CloneTransition)
                                    .ToArray();
                                previousTransitions[client.ClientId] = existing;
                            }

                            var leaderHistory = previousTransitions[local.ClientId];
                            nextTransitions = leaderHistory
                                .Select(CloneTransition)
                                .Append(CloneTransition(newTransition))
                                .ToArray();

                            foreach (var client in remaining)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await _candidateBundles.WriteAsync(
                                    client.ClientId,
                                    client.Secret,
                                    new CandidateBundlePayload(newGroupKey, newFingerprint),
                                    cancellationToken);
                                await _candidateTransitions.WriteAsync(
                                    client.ClientId,
                                    client.Secret,
                                    nextTransitions,
                                    cancellationToken);
                            }

                            await _storage.PrepareAsync(
                                rotationId,
                                newGroupKey,
                                newPrivateKey,
                                remaining,
                                cooldown,
                                cancellationToken);

                            await _storage.WriteMarkerAsync(
                                new GroupRotationMarker(1, rotationId, clientId, DateTimeOffset.UtcNow),
                                cancellationToken);
                            markerWritten = true;

                            if (!await CompletePendingCoreAsync(cancellationToken))
                                return new GroupRotationResult(false, "A revogação foi preparada, mas a promoção segura ficou pendente para recuperação automática.");

                            return new GroupRotationResult(true, "PC removido e chaves do grupo rotacionadas com sucesso.");
                        }
                        catch (OperationCanceledException)
                        {
                            if (!markerWritten)
                            {
                                await RestoreOldBundlesAsync(remaining, oldGroupKey, oldFingerprint, CancellationToken.None);
                                await RestoreOldTransitionsAsync(remaining, previousTransitions, CancellationToken.None);
                            }
                            throw;
                        }
                        catch (Exception ex) when (ex is IOException
                            or UnauthorizedAccessException
                            or CryptographicException
                            or InvalidDataException
                            or InvalidOperationException)
                        {
                            if (!markerWritten)
                            {
                                await RestoreOldBundlesAsync(remaining, oldGroupKey, oldFingerprint, CancellationToken.None);
                                await RestoreOldTransitionsAsync(remaining, previousTransitions, CancellationToken.None);
                                _storage.CleanupPrepared(rotationId);
                            }
                            return new GroupRotationResult(false, $"Não foi possível concluir a revogação: {ex.Message}");
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(oldGroupKey);
                            if (oldPublicKey is not null) CryptographicOperations.ZeroMemory(oldPublicKey);
                            if (oldFingerprint is not null) CryptographicOperations.ZeroMemory(oldFingerprint);
                            if (newGroupKey is not null) CryptographicOperations.ZeroMemory(newGroupKey);
                            if (newPrivateKey is not null) CryptographicOperations.ZeroMemory(newPrivateKey);
                            if (newPublicKey is not null) CryptographicOperations.ZeroMemory(newPublicKey);
                            if (newFingerprint is not null) CryptographicOperations.ZeroMemory(newFingerprint);
                            if (newTransition is not null) CandidateIdentityTransitionStore.Zero([newTransition]);
                            if (nextTransitions is not null) CandidateIdentityTransitionStore.Zero(nextTransitions);
                            foreach (var transitions in previousTransitions.Values)
                                CandidateIdentityTransitionStore.Zero(transitions);
                        }
                    }
                    finally
                    {
                        ZeroClients(remaining);
                    }
                }
                finally
                {
                    ZeroClients(currentClients);
                }
            }
            finally
            {
                ZeroPairing(local);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _rotationInProgress, 0);
            _gate.Release();
        }
    }

    public async Task<bool> CompletePendingAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _rotationInProgress, 1);
        try
        {
            return await CompletePendingCoreAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _rotationInProgress, 0);
            _gate.Release();
        }
    }

    private async Task<bool> CompletePendingCoreAsync(CancellationToken cancellationToken)
    {
        var marker = _storage.ReadMarker();
        if (marker is null)
            return true;

        if (!_storage.PreparedFilesExist(marker.RotationId))
            return false;

        var local = _clientPairing.Load();
        if (local is null || local.ClientId == marker.RevokedClientId)
        {
            if (local is not null) ZeroPairing(local);
            return false;
        }

        CandidateBundlePayload? bundle = null;
        byte[]? publicKey = null;
        byte[]? fingerprint = null;
        IReadOnlyList<GroupIdentityTransition>? transitions = null;
        try
        {
            if (!File.Exists(_paths.CandidateBundlePath(local.ClientId)))
                return false;

            bundle = _candidateBundles.Read(local.ClientId, local.ClientSecret);
            publicKey = _storage.GetPreparedPublicKey(marker.RotationId, bundle.GroupStateKey);
            fingerprint = SHA256.HashData(publicKey);
            if (!CryptographicOperations.FixedTimeEquals(fingerprint, bundle.CentralPublicKeySha256))
                return false;

            transitions = _candidateTransitions.Read(local.ClientId, local.ClientSecret);
            if (!GroupRotationProof.VerifyChain(local.CentralPublicKey, publicKey, transitions))
                return false;

            _storage.Promote(marker.RotationId);
            _candidateState.Save(bundle.GroupStateKey);
            _clientPairing.UpdateCentralPublicKey(publicKey);

            TryDelete(_paths.CandidateBundlePath(marker.RevokedClientId));
            _candidateTransitions.Delete(marker.RevokedClientId);
            await _cache.PurgeAllAsync(cancellationToken);

            // O marcador é removido antes dos arquivos preparados: uma queda depois
            // deste ponto deixa apenas resíduos inofensivos, nunca uma rotação ambígua.
            _storage.ClearMarker();
            _storage.CleanupPrepared(marker.RotationId);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or CryptographicException
            or InvalidDataException
            or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            ZeroPairing(local);
            if (bundle is not null)
            {
                CryptographicOperations.ZeroMemory(bundle.GroupStateKey);
                CryptographicOperations.ZeroMemory(bundle.CentralPublicKeySha256);
            }
            if (publicKey is not null) CryptographicOperations.ZeroMemory(publicKey);
            if (fingerprint is not null) CryptographicOperations.ZeroMemory(fingerprint);
            if (transitions is not null) CandidateIdentityTransitionStore.Zero(transitions);
        }
    }

    private async Task RestoreOldBundlesAsync(
        IEnumerable<AuthorizedClientSnapshot> remaining,
        byte[] oldGroupKey,
        byte[]? oldFingerprint,
        CancellationToken cancellationToken)
    {
        if (oldFingerprint is null)
            return;

        foreach (var client in remaining)
        {
            try
            {
                await _candidateBundles.WriteAsync(
                    client.ClientId,
                    client.Secret,
                    new CandidateBundlePayload(oldGroupKey, oldFingerprint),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException)
            {
            }
        }
    }

    private async Task RestoreOldTransitionsAsync(
        IEnumerable<AuthorizedClientSnapshot> remaining,
        IReadOnlyDictionary<Guid, GroupIdentityTransition[]> previousTransitions,
        CancellationToken cancellationToken)
    {
        foreach (var client in remaining)
        {
            if (!previousTransitions.TryGetValue(client.ClientId, out var transitions))
                continue;

            try
            {
                if (transitions.Length == 0)
                    _candidateTransitions.Delete(client.ClientId);
                else
                    await _candidateTransitions.WriteAsync(client.ClientId, client.Secret, transitions, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException)
            {
            }
        }
    }

    private static AuthorizedClientSnapshot CloneClient(AuthorizedClientSnapshot client) =>
        client with { Secret = client.Secret.ToArray() };

    private static GroupIdentityTransition CloneTransition(GroupIdentityTransition transition) =>
        transition with
        {
            PreviousPublicKeySha256 = transition.PreviousPublicKeySha256.ToArray(),
            NewPublicKey = transition.NewPublicKey.ToArray(),
            Signature = transition.Signature.ToArray()
        };

    private static void ZeroClients(IEnumerable<AuthorizedClientSnapshot> clients)
    {
        foreach (var client in clients)
            if (client.Secret is not null) CryptographicOperations.ZeroMemory(client.Secret);
    }

    private static void ZeroPairing(ClientPairingState state)
    {
        CryptographicOperations.ZeroMemory(state.ClientSecret);
        CryptographicOperations.ZeroMemory(state.CentralPublicKey);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
