using System.Security.Cryptography;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueGroupStateTests
{
    [Fact]
    public void Candidate_state_round_trips_group_key_with_local_protection()
    {
        var root = TempDirectory();
        try
        {
            var key = RandomNumberGenerator.GetBytes(32);
            var store = new CandidateStateStore(Path.Combine(root, "candidate.bin"));

            store.Save(key);
            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.Equal(key, loaded);
            Assert.True(store.IsReady);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(loaded!);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Candidate_bundle_is_bound_to_client_secret_and_rejects_tampering()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            var paths = new SharedQueuePaths(root);
            paths.InitializeAsCentral();
            var clientId = Guid.NewGuid();
            var secret = RandomNumberGenerator.GetBytes(32);
            var wrongSecret = RandomNumberGenerator.GetBytes(32);
            var groupKey = RandomNumberGenerator.GetBytes(32);
            var fingerprint = RandomNumberGenerator.GetBytes(32);
            var store = new CandidateBundleStore(paths);

            await store.WriteAsync(
                clientId,
                secret,
                new CandidateBundlePayload(groupKey, fingerprint));

            var payload = store.Read(clientId, secret);
            Assert.Equal(groupKey, payload.GroupStateKey);
            Assert.Equal(fingerprint, payload.CentralPublicKeySha256);
            Assert.Throws<CryptographicException>(() => store.Read(clientId, wrongSecret));

            var path = paths.CandidateBundlePath(clientId);
            var bytes = File.ReadAllBytes(path);
            bytes[^1] ^= 0x5A;
            File.WriteAllBytes(path, bytes);
            Assert.Throws<CryptographicException>(() => store.Read(clientId, secret));

            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(wrongSecret);
            CryptographicOperations.ZeroMemory(groupKey);
            CryptographicOperations.ZeroMemory(fingerprint);
            CryptographicOperations.ZeroMemory(payload.GroupStateKey);
            CryptographicOperations.ZeroMemory(payload.CentralPublicKeySha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Shared_group_identity_preserves_existing_public_key()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            var paths = new SharedQueuePaths(root);
            paths.InitializeAsCentral();
            var legacy = new CentralKeyStore(Path.Combine(root, "legacy.key"));
            var originalPublic = legacy.GetOrCreatePublicKey();
            var privateKey = legacy.ExportPrivateKeyPkcs8();
            var groupKey = RandomNumberGenerator.GetBytes(32);
            var group = new SharedGroupIdentityStore(paths);

            group.Initialize(groupKey, privateKey);
            var migratedPublic = group.GetPublicKey(groupKey);
            using var reopened = group.OpenPrivateKey(groupKey);
            var reopenedPublic = reopened.ExportSubjectPublicKeyInfo();

            Assert.Equal(originalPublic, migratedPublic);
            Assert.Equal(originalPublic, reopenedPublic);

            CryptographicOperations.ZeroMemory(originalPublic);
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(groupKey);
            CryptographicOperations.ZeroMemory(migratedPublic);
            CryptographicOperations.ZeroMemory(reopenedPublic);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "NfeAgendamento.Tests", Guid.NewGuid().ToString("N"));

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
