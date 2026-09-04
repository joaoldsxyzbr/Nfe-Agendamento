using System.Security.Cryptography;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueGroupRotationStorageTests
{
    [Fact]
    public async Task Prepared_rotation_does_not_replace_active_state_until_promotion()
    {
        using var temp = new TempDirectory();
        var paths = new SharedQueuePaths(temp.Path);
        paths.InitializeAsCentral();

        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        using var oldRsa = RSA.Create(2048);
        using var newRsa = RSA.Create(2048);
        var oldPrivate = oldRsa.ExportPkcs8PrivateKey();
        var newPrivate = newRsa.ExportPkcs8PrivateKey();
        var oldPublic = oldRsa.ExportSubjectPublicKeyInfo();
        var newPublic = newRsa.ExportSubjectPublicKeyInfo();

        var candidate = new CandidateStateStore(Path.Combine(temp.Path, "candidate.bin"));
        candidate.Save(oldKey);
        var identity = new SharedGroupIdentityStore(paths);
        identity.Initialize(oldKey, oldPrivate);

        var authorized = new SharedAuthorizedClientStore(paths, candidate);
        var clientId = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(32);
        authorized.Authorize(clientId, "PC-01", secret);

        var cooldown = new FiscalCooldownStore(paths, candidate);
        var now = DateTimeOffset.UtcNow;
        await cooldown.BlockFor656Async(now);
        var cooldownState = await cooldown.ReadAsync();

        var activeIdentityBefore = File.ReadAllBytes(paths.GroupIdentityPath);
        var activeClientsBefore = File.ReadAllBytes(paths.AuthorizedClientsPath);
        var activeCooldownBefore = File.ReadAllBytes(paths.StatusPath("fiscal-cooldown.bin"));
        var rotationId = Guid.NewGuid();
        var storage = new SharedQueueGroupRotationStorage(paths);

        await storage.PrepareAsync(
            rotationId,
            newKey,
            newPrivate,
            authorized.Snapshot(),
            cooldownState);

        Assert.Equal(activeIdentityBefore, File.ReadAllBytes(paths.GroupIdentityPath));
        Assert.Equal(activeClientsBefore, File.ReadAllBytes(paths.AuthorizedClientsPath));
        Assert.Equal(activeCooldownBefore, File.ReadAllBytes(paths.StatusPath("fiscal-cooldown.bin")));
        Assert.True(File.Exists(paths.RotationIdentityPreparedPath(rotationId)));
        Assert.True(File.Exists(paths.RotationAuthorizedPreparedPath(rotationId)));
        Assert.True(File.Exists(paths.RotationCooldownPreparedPath(rotationId)));

        storage.Promote(rotationId);
        candidate.Save(newKey);

        Assert.Equal(newPublic, identity.GetPublicKey(newKey));
        Assert.Single(new SharedAuthorizedClientStore(paths, candidate).Snapshot());
        var rotatedCooldown = await new FiscalCooldownStore(paths, candidate).ReadAsync();
        Assert.Equal(cooldownState.BlockedUntilUtc, rotatedCooldown.BlockedUntilUtc);
        Assert.ThrowsAny<CryptographicException>(() => identity.GetPublicKey(oldKey));

        CryptographicOperations.ZeroMemory(oldKey);
        CryptographicOperations.ZeroMemory(newKey);
        CryptographicOperations.ZeroMemory(oldPrivate);
        CryptographicOperations.ZeroMemory(newPrivate);
        CryptographicOperations.ZeroMemory(oldPublic);
        CryptographicOperations.ZeroMemory(newPublic);
        CryptographicOperations.ZeroMemory(secret);
    }

    [Fact]
    public async Task Rotation_marker_round_trips_and_is_cleared_explicitly()
    {
        using var temp = new TempDirectory();
        var paths = new SharedQueuePaths(temp.Path);
        paths.InitializeAsCentral();
        var storage = new SharedQueueGroupRotationStorage(paths);
        var marker = new GroupRotationMarker(1, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        await storage.WriteMarkerAsync(marker);

        Assert.True(storage.HasPending);
        Assert.Equal(marker, storage.ReadMarker());
        storage.ClearMarker();
        Assert.False(storage.HasPending);
    }

    [Fact]
    public async Task Shared_cache_can_be_purged_explicitly_during_key_rotation()
    {
        using var temp = new TempDirectory();
        var paths = new SharedQueuePaths(temp.Path);
        paths.InitializeAsCentral();
        var key = RandomNumberGenerator.GetBytes(32);
        var candidate = new CandidateStateStore(Path.Combine(temp.Path, "candidate.bin"));
        candidate.Save(key);
        var cache = new EncryptedXmlCache(paths, candidate);

        await cache.PutAsync("42260912345678000123550010000000011000000015", "<nfeProc />");
        Assert.NotEmpty(Directory.EnumerateFiles(paths.CacheDirectory, "*.bin"));

        await cache.PurgeAllAsync();

        Assert.Empty(Directory.EnumerateFiles(paths.CacheDirectory, "*.bin"));
        CryptographicOperations.ZeroMemory(key);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-rotation-storage", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
