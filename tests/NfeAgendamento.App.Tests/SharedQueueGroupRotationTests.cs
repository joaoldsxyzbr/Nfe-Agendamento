using System.Security.Cryptography;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueGroupRotationTests
{
    [Fact]
    public async Task Revoke_rotates_group_key_and_rsa_preserves_remaining_clients_and_cooldown()
    {
        using var temp = new TempDirectory();
        var setup = await CreateGroupAsync(temp.Path, includeThirdClient: true);
        var oldKey = setup.Candidate.Load()!;
        var oldPublic = setup.Identity.GetPublicKey(oldKey);
        var oldPairing = setup.Pairing.Load()!;
        var oldSequence = oldPairing.NextSequence;
        var oldCooldown = await setup.Cooldown.ReadAsync();

        var result = await setup.Service.RevokeAsync(setup.TargetId);

        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(setup.Paths.CandidateBundlePath(setup.TargetId)));
        Assert.False(new SharedQueueGroupRotationStorage(setup.Paths).HasPending);

        var newKey = setup.Candidate.Load()!;
        var newPublic = setup.Identity.GetPublicKey(newKey);
        Assert.NotEqual(oldKey, newKey);
        Assert.NotEqual(oldPublic, newPublic);

        var remaining = setup.Authorized.Snapshot();
        Assert.DoesNotContain(remaining, item => item.ClientId == setup.TargetId);
        Assert.Contains(remaining, item => item.ClientId == setup.LeaderId);
        Assert.Contains(remaining, item => item.ClientId == setup.OtherId);

        var otherBundle = setup.Bundles.Read(setup.OtherId, setup.OtherSecret);
        Assert.Equal(newKey, otherBundle.GroupStateKey);
        Assert.Equal(SHA256.HashData(newPublic), otherBundle.CentralPublicKeySha256);

        var newCooldown = await setup.Cooldown.ReadAsync();
        Assert.Equal(oldCooldown.BlockedUntilUtc, newCooldown.BlockedUntilUtc);

        var updatedPairing = setup.Pairing.Load()!;
        Assert.Equal(oldPairing.ClientId, updatedPairing.ClientId);
        Assert.Equal(oldPairing.ClientSecret, updatedPairing.ClientSecret);
        Assert.Equal(oldSequence, updatedPairing.NextSequence);
        Assert.Equal(newPublic, updatedPairing.CentralPublicKey);

        CryptographicOperations.ZeroMemory(oldKey);
        CryptographicOperations.ZeroMemory(newKey);
        CryptographicOperations.ZeroMemory(oldPublic);
        CryptographicOperations.ZeroMemory(newPublic);
        CryptographicOperations.ZeroMemory(otherBundle.GroupStateKey);
        CryptographicOperations.ZeroMemory(otherBundle.CentralPublicKeySha256);
        ZeroPairing(oldPairing);
        ZeroPairing(updatedPairing);
        ZeroClients(remaining);
    }

    [Fact]
    public async Task Revoke_rejects_self_without_changing_group_identity()
    {
        using var temp = new TempDirectory();
        var setup = await CreateGroupAsync(temp.Path, includeThirdClient: false);
        var key = setup.Candidate.Load()!;
        var publicBefore = setup.Identity.GetPublicKey(key);

        var result = await setup.Service.RevokeAsync(setup.LeaderId);

        Assert.False(result.Success);
        Assert.Contains("próprio", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(publicBefore, setup.Identity.GetPublicKey(key));
        Assert.Equal(2, setup.Authorized.Count);
        Assert.False(new SharedQueueGroupRotationStorage(setup.Paths).HasPending);

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(publicBefore);
    }

    [Fact]
    public async Task Pending_rotation_is_completed_idempotently_after_promotion_was_already_started()
    {
        using var temp = new TempDirectory();
        var setup = await CreateGroupAsync(temp.Path, includeThirdClient: false);
        var storage = new SharedQueueGroupRotationStorage(setup.Paths);
        var oldCooldown = await setup.Cooldown.ReadAsync();
        var remaining = setup.Authorized.Snapshot()
            .Where(item => item.ClientId != setup.TargetId)
            .ToArray();

        var rotationId = Guid.NewGuid();
        var newKey = RandomNumberGenerator.GetBytes(32);
        using var rsa = RSA.Create(2048);
        var newPrivate = rsa.ExportPkcs8PrivateKey();
        var newPublic = rsa.ExportSubjectPublicKeyInfo();
        var fingerprint = SHA256.HashData(newPublic);

        await setup.Bundles.WriteAsync(
            setup.LeaderId,
            setup.LeaderSecret,
            new CandidateBundlePayload(newKey, fingerprint));
        await storage.PrepareAsync(rotationId, newKey, newPrivate, remaining, oldCooldown);
        await storage.WriteMarkerAsync(new GroupRotationMarker(1, rotationId, setup.TargetId, DateTimeOffset.UtcNow));

        // Simula queda depois que a promoção começou. A promoção deve poder ser repetida.
        storage.Promote(rotationId);
        var completed = await setup.Service.CompletePendingAsync();

        Assert.True(completed);
        Assert.False(storage.HasPending);
        Assert.False(File.Exists(setup.Paths.CandidateBundlePath(setup.TargetId)));
        var localKey = setup.Candidate.Load()!;
        Assert.Equal(newKey, localKey);
        Assert.Equal(newPublic, setup.Identity.GetPublicKey(localKey));
        Assert.Equal(newPublic, setup.Pairing.Load()!.CentralPublicKey);
        Assert.Single(setup.Authorized.Snapshot());
        Assert.Equal(oldCooldown.BlockedUntilUtc, (await setup.Cooldown.ReadAsync()).BlockedUntilUtc);

        CryptographicOperations.ZeroMemory(newKey);
        CryptographicOperations.ZeroMemory(localKey);
        CryptographicOperations.ZeroMemory(newPrivate);
        CryptographicOperations.ZeroMemory(newPublic);
        CryptographicOperations.ZeroMemory(fingerprint);
        ZeroClients(remaining);
    }

    [Fact]
    public async Task Stale_candidate_is_not_ready_until_rotated_bundle_updates_key_and_pin()
    {
        using var temp = new TempDirectory();
        var setup = await CreateGroupAsync(temp.Path, includeThirdClient: false);
        var oldPairing = setup.Pairing.Load()!;
        var oldSequence = oldPairing.NextSequence;
        var oldKey = setup.Candidate.Load()!;

        var storage = new SharedQueueGroupRotationStorage(setup.Paths);
        var remaining = setup.Authorized.Snapshot()
            .Where(item => item.ClientId == setup.LeaderId)
            .ToArray();
        var rotationId = Guid.NewGuid();
        var newKey = RandomNumberGenerator.GetBytes(32);
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportPkcs8PrivateKey();
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var fingerprint = SHA256.HashData(publicKey);
        await setup.Bundles.WriteAsync(setup.LeaderId, setup.LeaderSecret, new CandidateBundlePayload(newKey, fingerprint));
        await storage.PrepareAsync(rotationId, newKey, privateKey, remaining, await setup.Cooldown.ReadAsync());
        storage.Promote(rotationId);

        Assert.False(setup.Bootstrap.IsCandidateReady);
        Assert.True(setup.Bootstrap.TryImportCandidateBundle());
        Assert.True(setup.Bootstrap.IsCandidateReady);

        var updated = setup.Pairing.Load()!;
        Assert.Equal(oldPairing.ClientId, updated.ClientId);
        Assert.Equal(oldPairing.ClientSecret, updated.ClientSecret);
        Assert.Equal(oldSequence, updated.NextSequence);
        Assert.Equal(publicKey, updated.CentralPublicKey);

        CryptographicOperations.ZeroMemory(oldKey);
        CryptographicOperations.ZeroMemory(newKey);
        CryptographicOperations.ZeroMemory(privateKey);
        CryptographicOperations.ZeroMemory(publicKey);
        CryptographicOperations.ZeroMemory(fingerprint);
        ZeroPairing(oldPairing);
        ZeroPairing(updated);
        ZeroClients(remaining);
    }

    private static async Task<GroupSetup> CreateGroupAsync(string root, bool includeThirdClient)
    {
        var share = Path.Combine(root, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var key = RandomNumberGenerator.GetBytes(32);
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportPkcs8PrivateKey();
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var fingerprint = SHA256.HashData(publicKey);
        var candidate = new CandidateStateStore(Path.Combine(root, "candidate.bin"));
        candidate.Save(key);
        var identity = new SharedGroupIdentityStore(paths);
        identity.Initialize(key, privateKey);

        var leaderId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var otherId = includeThirdClient ? Guid.NewGuid() : Guid.Empty;
        var leaderSecret = RandomNumberGenerator.GetBytes(32);
        var targetSecret = RandomNumberGenerator.GetBytes(32);
        var otherSecret = includeThirdClient ? RandomNumberGenerator.GetBytes(32) : Array.Empty<byte>();

        var authorized = new SharedAuthorizedClientStore(paths, candidate);
        authorized.Authorize(leaderId, "PC-LIDER", leaderSecret);
        authorized.Authorize(targetId, "PC-REMOVER", targetSecret);
        if (includeThirdClient)
            authorized.Authorize(otherId, "PC-OUTRO", otherSecret);

        var pairing = new ClientPairingStore(Path.Combine(root, "pairing.bin"));
        pairing.SavePaired(leaderId, "PC-LIDER", leaderSecret, publicKey, "PC-LIDER");
        _ = pairing.ReserveCredentials();

        var bundles = new CandidateBundleStore(paths);
        await bundles.WriteAsync(leaderId, leaderSecret, new CandidateBundlePayload(key, fingerprint));
        await bundles.WriteAsync(targetId, targetSecret, new CandidateBundlePayload(key, fingerprint));
        if (includeThirdClient)
            await bundles.WriteAsync(otherId, otherSecret, new CandidateBundlePayload(key, fingerprint));

        var cooldown = new FiscalCooldownStore(paths, candidate);
        await cooldown.BlockFor656Async(DateTimeOffset.UtcNow);
        var cache = new EncryptedXmlCache(paths, candidate);
        await cache.PutAsync("42260912345678000123550010000000011000000015", "<nfeProc />");

        var bootstrap = new SharedQueueGroupBootstrapService(
            paths,
            new CentralStateService(new CentralSettingsStore(Path.Combine(root, "central.json"))),
            new CentralKeyStore(candidate, identity),
            new AuthorizedClientStore(Path.Combine(root, "legacy-authorized.bin")),
            pairing,
            candidate,
            Path.Combine(root, "legacy-authorized.bin"));

        var service = new SharedQueueGroupRotationService(
            paths,
            candidate,
            pairing,
            authorized,
            bundles,
            new SharedQueueGroupRotationStorage(paths),
            cooldown,
            cache);

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(privateKey);
        CryptographicOperations.ZeroMemory(publicKey);
        CryptographicOperations.ZeroMemory(fingerprint);
        CryptographicOperations.ZeroMemory(targetSecret);

        return new GroupSetup(
            paths,
            candidate,
            identity,
            authorized,
            pairing,
            bundles,
            cooldown,
            cache,
            bootstrap,
            service,
            leaderId,
            targetId,
            otherId,
            leaderSecret,
            otherSecret);
    }

    private sealed record GroupSetup(
        SharedQueuePaths Paths,
        CandidateStateStore Candidate,
        SharedGroupIdentityStore Identity,
        SharedAuthorizedClientStore Authorized,
        ClientPairingStore Pairing,
        CandidateBundleStore Bundles,
        FiscalCooldownStore Cooldown,
        EncryptedXmlCache Cache,
        SharedQueueGroupBootstrapService Bootstrap,
        SharedQueueGroupRotationService Service,
        Guid LeaderId,
        Guid TargetId,
        Guid OtherId,
        byte[] LeaderSecret,
        byte[] OtherSecret);

    private static void ZeroPairing(ClientPairingState state)
    {
        CryptographicOperations.ZeroMemory(state.ClientSecret);
        CryptographicOperations.ZeroMemory(state.CentralPublicKey);
    }

    private static void ZeroClients(IEnumerable<AuthorizedClientSnapshot> clients)
    {
        foreach (var client in clients)
            CryptographicOperations.ZeroMemory(client.Secret);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-group-rotation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
