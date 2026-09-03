using System.Security.Cryptography;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedAuthorizedClientStoreTests
{
    private const string AccessKey = "42260912345678000123550010000000011000000015";

    [Fact]
    public void Sequence_and_replay_state_survive_leader_change()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            var paths = new SharedQueuePaths(root);
            paths.InitializeAsCentral();
            var groupKey = RandomNumberGenerator.GetBytes(32);
            var firstState = new CandidateStateStore(Path.Combine(root, "first-candidate.bin"));
            var secondState = new CandidateStateStore(Path.Combine(root, "second-candidate.bin"));
            firstState.Save(groupKey);
            secondState.Save(groupKey);
            var first = new SharedAuthorizedClientStore(paths, firstState);
            var second = new SharedAuthorizedClientStore(paths, secondState);

            var clientId = Guid.NewGuid();
            var secret = RandomNumberGenerator.GetBytes(32);
            first.Authorize(clientId, "PC-01", secret);

            using var rsa = RSA.Create(2048);
            var publicKey = rsa.ExportSubjectPublicKeyInfo();
            var material = SharedQueueCrypto.CreateClientRequest(Guid.NewGuid(), AccessKey, publicKey, clientId, 1, secret);
            try
            {
                Assert.True(first.TryAuthenticateAndAdvance(material.Envelope, out var firstError), firstError);
                Assert.False(second.TryAuthenticateAndAdvance(material.Envelope, out var replayError));
                Assert.Contains("repetida", replayError, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, second.Snapshot().Single().LastSequence);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(material.AesKey);
                CryptographicOperations.ZeroMemory(publicKey);
                CryptographicOperations.ZeroMemory(secret);
                CryptographicOperations.ZeroMemory(groupKey);
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Shared_state_rejects_tampering_instead_of_falling_back_to_empty()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            var paths = new SharedQueuePaths(root);
            paths.InitializeAsCentral();
            var groupKey = RandomNumberGenerator.GetBytes(32);
            var candidate = new CandidateStateStore(Path.Combine(root, "candidate.bin"));
            candidate.Save(groupKey);
            var store = new SharedAuthorizedClientStore(paths, candidate);
            var secret = RandomNumberGenerator.GetBytes(32);
            store.Authorize(Guid.NewGuid(), "PC-01", secret);

            var bytes = File.ReadAllBytes(paths.AuthorizedClientsPath);
            bytes[^1] ^= 0x41;
            File.WriteAllBytes(paths.AuthorizedClientsPath, bytes);

            Assert.ThrowsAny<CryptographicException>(() => store.Snapshot());
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(groupKey);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Legacy_snapshot_can_be_migrated_without_resetting_sequence()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            var paths = new SharedQueuePaths(root);
            paths.InitializeAsCentral();
            var groupKey = RandomNumberGenerator.GetBytes(32);
            var candidate = new CandidateStateStore(Path.Combine(root, "candidate.bin"));
            candidate.Save(groupKey);
            var shared = new SharedAuthorizedClientStore(paths, candidate);
            var legacy = new AuthorizedClientStore(Path.Combine(root, "legacy-authorized.bin"));
            var secret = RandomNumberGenerator.GetBytes(32);
            var clientId = Guid.NewGuid();
            legacy.Authorize(clientId, "PC legado", secret);

            using var rsa = RSA.Create(2048);
            var publicKey = rsa.ExportSubjectPublicKeyInfo();
            var material = SharedQueueCrypto.CreateClientRequest(Guid.NewGuid(), AccessKey, publicKey, clientId, 7, secret);
            try
            {
                Assert.True(legacy.TryAuthenticateAndAdvance(material.Envelope, out var error), error);
                shared.ReplaceFromLegacy(legacy.Snapshot());
                var migrated = shared.Snapshot().Single();
                Assert.Equal(clientId, migrated.ClientId);
                Assert.Equal(7, migrated.LastSequence);
                Assert.Equal(secret, migrated.Secret);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(material.AesKey);
                CryptographicOperations.ZeroMemory(publicKey);
                CryptographicOperations.ZeroMemory(secret);
                CryptographicOperations.ZeroMemory(groupKey);
            }
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
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
