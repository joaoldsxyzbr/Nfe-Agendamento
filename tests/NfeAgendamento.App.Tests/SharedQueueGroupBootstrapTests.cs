using System.Security.Cryptography;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueGroupBootstrapTests
{
    [Fact]
    public async Task Legacy_central_bootstrap_preserves_public_key_and_existing_client_can_import_without_repairing()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var centralState = new CentralStateService(new CentralSettingsStore(Path.Combine(temp.Path, "central.json")));
        centralState.SetConfiguredAsCentral(true);
        var centralKeys = new CentralKeyStore(Path.Combine(temp.Path, "central-key.bin"));
        var expectedPublic = centralKeys.GetOrCreatePublicKey();
        var legacyAuthorized = new AuthorizedClientStore(Path.Combine(temp.Path, "authorized.bin"));
        var clientId = Guid.NewGuid();
        var clientSecret = RandomNumberGenerator.GetBytes(32);
        legacyAuthorized.Authorize(clientId, "PC-02", clientSecret);

        var centralCandidate = new CandidateStateStore(Path.Combine(temp.Path, "central-candidate.bin"));
        var centralPairing = new ClientPairingStore(Path.Combine(temp.Path, "central-client.bin"));
        var bootstrap = new SharedQueueGroupBootstrapService(
            paths,
            centralState,
            centralKeys,
            legacyAuthorized,
            centralPairing,
            centralCandidate);

        await bootstrap.EnsureBootstrapAsync();
        Assert.True(centralCandidate.IsReady);
        Assert.True(new SharedGroupIdentityStore(paths).Exists);
        Assert.True(centralPairing.IsPaired);
        Assert.Equal(expectedPublic, centralPairing.Load()!.CentralPublicKey);

        var clientPairing = new ClientPairingStore(Path.Combine(temp.Path, "client-pairing.bin"));
        clientPairing.SavePaired(clientId, "PC-02", clientSecret, expectedPublic, "PC-CENTRAL");
        var clientCandidate = new CandidateStateStore(Path.Combine(temp.Path, "client-candidate.bin"));
        var clientBootstrap = new SharedQueueGroupBootstrapService(
            paths,
            new CentralStateService(new CentralSettingsStore(Path.Combine(temp.Path, "client-central.json"))),
            new CentralKeyStore(Path.Combine(temp.Path, "client-unused-key.bin")),
            new AuthorizedClientStore(Path.Combine(temp.Path, "client-authorized.bin")),
            clientPairing,
            clientCandidate);

        Assert.True(clientBootstrap.TryImportCandidateBundle());
        Assert.True(clientCandidate.IsReady);
        var groupKey = clientCandidate.Load()!;
        var actualPublic = new SharedGroupIdentityStore(paths).GetPublicKey(groupKey);
        Assert.Equal(expectedPublic, actualPublic);

        CryptographicOperations.ZeroMemory(groupKey);
        CryptographicOperations.ZeroMemory(expectedPublic);
        CryptographicOperations.ZeroMemory(clientSecret);
    }

    [Fact]
    public async Task Bootstrap_is_idempotent_and_does_not_replace_group_identity()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();
        var state = new CentralStateService(new CentralSettingsStore(Path.Combine(temp.Path, "central.json")));
        state.SetConfiguredAsCentral(true);
        var keys = new CentralKeyStore(Path.Combine(temp.Path, "central-key.bin"));
        var candidate = new CandidateStateStore(Path.Combine(temp.Path, "candidate.bin"));
        var service = new SharedQueueGroupBootstrapService(
            paths,
            state,
            keys,
            new AuthorizedClientStore(Path.Combine(temp.Path, "authorized.bin")),
            new ClientPairingStore(Path.Combine(temp.Path, "client.bin")),
            candidate);

        await service.EnsureBootstrapAsync();
        var firstKey = candidate.Load()!;
        var firstPublic = new SharedGroupIdentityStore(paths).GetPublicKey(firstKey);
        await service.EnsureBootstrapAsync();
        var secondKey = candidate.Load()!;
        var secondPublic = new SharedGroupIdentityStore(paths).GetPublicKey(secondKey);

        Assert.Equal(firstKey, secondKey);
        Assert.Equal(firstPublic, secondPublic);
        CryptographicOperations.ZeroMemory(firstKey);
        CryptographicOperations.ZeroMemory(secondKey);
        CryptographicOperations.ZeroMemory(firstPublic);
        CryptographicOperations.ZeroMemory(secondPublic);
    }

    [Fact]
    public async Task Bootstrap_reuses_prepared_candidate_key_after_interruption_before_group_identity()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var state = new CentralStateService(new CentralSettingsStore(Path.Combine(temp.Path, "central.json")));
        state.SetConfiguredAsCentral(true);
        var candidate = new CandidateStateStore(Path.Combine(temp.Path, "candidate.bin"));
        var preparedKey = RandomNumberGenerator.GetBytes(32);
        candidate.Save(preparedKey);

        var service = new SharedQueueGroupBootstrapService(
            paths,
            state,
            new CentralKeyStore(Path.Combine(temp.Path, "central-key.bin")),
            new AuthorizedClientStore(Path.Combine(temp.Path, "authorized.bin")),
            new ClientPairingStore(Path.Combine(temp.Path, "client.bin")),
            candidate);

        await service.EnsureBootstrapAsync();

        var recoveredKey = candidate.Load()!;
        Assert.Equal(preparedKey, recoveredKey);
        var publicKey = new SharedGroupIdentityStore(paths).GetPublicKey(preparedKey);
        Assert.NotEmpty(publicKey);

        CryptographicOperations.ZeroMemory(preparedKey);
        CryptographicOperations.ZeroMemory(recoveredKey);
        CryptographicOperations.ZeroMemory(publicKey);
    }

    [Fact]
    public async Task Client_rejects_candidate_bundle_when_pinned_public_key_does_not_match_group()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();
        var centralState = new CentralStateService(new CentralSettingsStore(Path.Combine(temp.Path, "central.json")));
        centralState.SetConfiguredAsCentral(true);
        var centralKeys = new CentralKeyStore(Path.Combine(temp.Path, "central-key.bin"));
        var legacyAuthorized = new AuthorizedClientStore(Path.Combine(temp.Path, "authorized.bin"));
        var clientId = Guid.NewGuid();
        var clientSecret = RandomNumberGenerator.GetBytes(32);
        legacyAuthorized.Authorize(clientId, "PC-02", clientSecret);
        var centralBootstrap = new SharedQueueGroupBootstrapService(
            paths,
            centralState,
            centralKeys,
            legacyAuthorized,
            new ClientPairingStore(Path.Combine(temp.Path, "central-client.bin")),
            new CandidateStateStore(Path.Combine(temp.Path, "central-candidate.bin")));
        await centralBootstrap.EnsureBootstrapAsync();

        using var otherRsa = RSA.Create(2048);
        var wrongPublic = otherRsa.ExportSubjectPublicKeyInfo();
        var clientPairing = new ClientPairingStore(Path.Combine(temp.Path, "client-pairing.bin"));
        clientPairing.SavePaired(clientId, "PC-02", clientSecret, wrongPublic, "PC-CENTRAL");
        var candidate = new CandidateStateStore(Path.Combine(temp.Path, "client-candidate.bin"));
        var clientBootstrap = new SharedQueueGroupBootstrapService(
            paths,
            new CentralStateService(new CentralSettingsStore(Path.Combine(temp.Path, "client-central.json"))),
            new CentralKeyStore(Path.Combine(temp.Path, "client-key.bin")),
            new AuthorizedClientStore(Path.Combine(temp.Path, "client-authorized.bin")),
            clientPairing,
            candidate);

        Assert.False(clientBootstrap.TryImportCandidateBundle());
        Assert.False(candidate.IsReady);
        CryptographicOperations.ZeroMemory(wrongPublic);
        CryptographicOperations.ZeroMemory(clientSecret);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
