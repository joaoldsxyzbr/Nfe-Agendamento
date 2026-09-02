using System.Security.Cryptography;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueClientCleanupTests
{
    private const string AccessKey = "42260912345678000123550010000000011000000015";

    [Fact]
    public async Task Timeout_removes_unclaimed_request_and_local_pending_key()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-client-cleanup-tests", Guid.NewGuid().ToString("N"));
        var share = Path.Combine(root, "share");
        var pendingRoot = Path.Combine(root, "pending");
        Directory.CreateDirectory(share);
        try
        {
            var paths = new SharedQueuePaths(share);
            paths.InitializeAsCentral();
            var keyStore = new CentralKeyStore(Path.Combine(root, "central.key"));
            var publicKey = keyStore.GetOrCreatePublicKey();
            var heartbeat = new QueueHeartbeat(
                SharedQueueCrypto.ProtocolVersion,
                "CA03",
                DateTimeOffset.UtcNow,
                Convert.ToBase64String(publicKey),
                "test");
            using (var privateKey = keyStore.OpenPrivateKey())
                heartbeat = heartbeat with { SignatureBase64 = SharedQueueCrypto.SignHeartbeat(heartbeat, privateKey) };
            await File.WriteAllBytesAsync(
                paths.StatusPath("heartbeat.json"),
                JsonSerializer.SerializeToUtf8Bytes(heartbeat));

            var pairing = new ClientPairingStore(Path.Combine(root, "client-pairing.bin"));
            pairing.SavePaired(
                Guid.NewGuid(),
                "CA02",
                RandomNumberGenerator.GetBytes(32),
                publicKey,
                "CA03");

            var pending = new PendingRequestSecretStore(pendingRoot);
            var client = new SharedQueueClient(
                paths,
                pending,
                pairing,
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(100));

            var result = await client.LookupAsync(AccessKey);

            Assert.Equal(NfeLookupStatus.Failed, result.Status);
            Assert.Empty(Directory.EnumerateFiles(paths.QueueDirectory, "*.req", SearchOption.TopDirectoryOnly));
            Assert.False(Directory.Exists(pendingRoot) && Directory.EnumerateFiles(pendingRoot, "*.key", SearchOption.TopDirectoryOnly).Any());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
