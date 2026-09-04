using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NfeAgendamento.App.SharedQueue;

public sealed class SharedQueueGroupBootstrapService
{
    private static readonly byte[] LegacyAuthorizedEntropy = Encoding.UTF8.GetBytes("NfeAgendamento.SharedQueue.AuthorizedClients.v1");

    private readonly object _sync = new();
    private readonly SharedQueuePaths _paths;
    private readonly CentralStateService _legacyCentralState;
    private readonly CentralKeyStore _legacyCentralKeyStore;
    private readonly string _legacyAuthorizedPath;
    private readonly ClientPairingStore _clientPairingStore;
    private readonly CandidateStateStore _candidateState;
    private readonly CandidateBundleStore _candidateBundles;
    private readonly SharedGroupIdentityStore _groupIdentity;

    public SharedQueueGroupBootstrapService(
        SharedQueuePaths paths,
        CentralStateService legacyCentralState,
        CentralKeyStore legacyCentralKeyStore,
        AuthorizedClientStore legacyAuthorizedClients,
        ClientPairingStore clientPairingStore,
        CandidateStateStore candidateState,
        string? legacyAuthorizedPath = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _legacyCentralState = legacyCentralState ?? throw new ArgumentNullException(nameof(legacyCentralState));
        _legacyCentralKeyStore = legacyCentralKeyStore ?? throw new ArgumentNullException(nameof(legacyCentralKeyStore));
        _ = legacyAuthorizedClients ?? throw new ArgumentNullException(nameof(legacyAuthorizedClients));
        _legacyAuthorizedPath = Path.GetFullPath(
            legacyAuthorizedPath ?? Path.Combine(AppPaths.StateRoot, "shared-queue", "authorized-clients.bin"));
        _clientPairingStore = clientPairingStore ?? throw new ArgumentNullException(nameof(clientPairingStore));
        _candidateState = candidateState ?? throw new ArgumentNullException(nameof(candidateState));
        _candidateBundles = new CandidateBundleStore(paths);
        _groupIdentity = new SharedGroupIdentityStore(paths);
    }

    public bool IsCandidateReady => _candidateState.IsReady && _groupIdentity.Exists;

    public async Task EnsureBootstrapAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_paths.ValidateForClient())
            throw new InvalidOperationException($"A pasta compartilhada '{SharedQueuePaths.DefaultRoot}' não está disponível.");

        if (IsCandidateReady)
        {
            EnsureLegacyCentralClientIdentity();
            return;
        }

        if (TryImportCandidateBundle())
            return;

        lock (_sync)
        {
            if (IsCandidateReady)
            {
                EnsureLegacyCentralClientIdentity();
                return;
            }
        }

        if (_groupIdentity.Exists)
            return;

        if (!_legacyCentralState.IsConfiguredAsCentral)
            return;

        var groupKey = _candidateState.Load();
        if (groupKey is null)
        {
            groupKey = RandomNumberGenerator.GetBytes(32);
            _candidateState.Save(groupKey);
        }

        var privateKey = _legacyCentralKeyStore.ExportPrivateKeyPkcs8();
        var publicKey = _legacyCentralKeyStore.GetOrCreatePublicKey();
        var fingerprint = SHA256.HashData(publicKey);
        List<AuthorizedClientSnapshot>? legacyClients = null;
        try
        {
            _groupIdentity.Initialize(groupKey, privateKey);

            legacyClients = ReadLegacyAuthorizedClients();
            var sharedAuthorized = new SharedAuthorizedClientStore(_paths, _candidateState);
            sharedAuthorized.ReplaceFromLegacy(legacyClients);

            foreach (var client in legacyClients)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _candidateBundles.WriteAsync(
                    client.ClientId,
                    client.Secret,
                    new CandidateBundlePayload(groupKey, fingerprint),
                    cancellationToken);
            }

            EnsureLegacyCentralClientIdentity();
        }
        finally
        {
            if (legacyClients is not null)
                ZeroClients(legacyClients);
            CryptographicOperations.ZeroMemory(groupKey);
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(publicKey);
            CryptographicOperations.ZeroMemory(fingerprint);
        }
    }

    public bool TryImportCandidateBundle()
    {
        if (IsCandidateReady)
            return true;
        if (!_groupIdentity.Exists)
            return false;

        var paired = _clientPairingStore.Load();
        if (paired is null)
            return false;

        CandidateBundlePayload? bundle = null;
        byte[]? expectedFingerprint = null;
        byte[]? actualPublicKey = null;
        byte[]? actualFingerprint = null;
        try
        {
            if (!File.Exists(_paths.CandidateBundlePath(paired.ClientId)))
                return false;

            bundle = _candidateBundles.Read(paired.ClientId, paired.ClientSecret);
            expectedFingerprint = SHA256.HashData(paired.CentralPublicKey);
            if (!CryptographicOperations.FixedTimeEquals(expectedFingerprint, bundle.CentralPublicKeySha256))
                return false;

            actualPublicKey = _groupIdentity.GetPublicKey(bundle.GroupStateKey);
            actualFingerprint = SHA256.HashData(actualPublicKey);
            if (!CryptographicOperations.FixedTimeEquals(expectedFingerprint, actualFingerprint))
                return false;

            _candidateState.Save(bundle.GroupStateKey);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException)
        {
            return false;
        }
        finally
        {
            ZeroPairingState(paired);
            if (bundle is not null)
            {
                CryptographicOperations.ZeroMemory(bundle.GroupStateKey);
                CryptographicOperations.ZeroMemory(bundle.CentralPublicKeySha256);
            }
            if (expectedFingerprint is not null) CryptographicOperations.ZeroMemory(expectedFingerprint);
            if (actualPublicKey is not null) CryptographicOperations.ZeroMemory(actualPublicKey);
            if (actualFingerprint is not null) CryptographicOperations.ZeroMemory(actualFingerprint);
        }
    }

    private void EnsureLegacyCentralClientIdentity()
    {
        if (!_legacyCentralState.IsConfiguredAsCentral || _clientPairingStore.IsPaired || !IsCandidateReady)
            return;

        var groupKey = _candidateState.Load()
            ?? throw new InvalidOperationException("Estado seguro do grupo indisponível para concluir a migração.");
        var publicKey = _groupIdentity.GetPublicKey(groupKey);
        var clientSecret = RandomNumberGenerator.GetBytes(32);
        try
        {
            var clientId = Guid.NewGuid();
            var sharedAuthorized = new SharedAuthorizedClientStore(_paths, _candidateState);
            sharedAuthorized.Authorize(clientId, Environment.MachineName, clientSecret);
            _clientPairingStore.SavePaired(
                clientId,
                Environment.MachineName,
                clientSecret,
                publicKey,
                Environment.MachineName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(groupKey);
            CryptographicOperations.ZeroMemory(publicKey);
            CryptographicOperations.ZeroMemory(clientSecret);
        }
    }

    private List<AuthorizedClientSnapshot> ReadLegacyAuthorizedClients()
    {
        if (!File.Exists(_legacyAuthorizedPath))
            return [];

        var protectedBytes = File.ReadAllBytes(_legacyAuthorizedPath);
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(protectedBytes, LegacyAuthorizedEntropy, DataProtectionScope.CurrentUser);
            var clients = JsonSerializer.Deserialize<List<AuthorizedClientSnapshot>>(plain) ?? [];
            foreach (var client in clients)
            {
                if (client.ClientId == Guid.Empty
                    || string.IsNullOrWhiteSpace(client.ClientName)
                    || client.Secret is null || client.Secret.Length != 32
                    || client.LastSequence < 0)
                {
                    throw new CryptographicException("Estado legado de clientes autorizados inválido.");
                }
            }
            return clients;
        }
        catch (JsonException ex)
        {
            throw new CryptographicException("Estado legado de clientes autorizados inválido.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static void ZeroClients(IEnumerable<AuthorizedClientSnapshot> clients)
    {
        foreach (var client in clients)
            if (client.Secret is not null) CryptographicOperations.ZeroMemory(client.Secret);
    }

    private static void ZeroPairingState(ClientPairingState state)
    {
        if (state.ClientSecret is not null) CryptographicOperations.ZeroMemory(state.ClientSecret);
        if (state.CentralPublicKey is not null) CryptographicOperations.ZeroMemory(state.CentralPublicKey);
    }
}
