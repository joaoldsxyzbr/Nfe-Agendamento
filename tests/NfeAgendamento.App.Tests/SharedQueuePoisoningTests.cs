using System.Security.Cryptography;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueuePoisoningTests
{
    private const string AccessKey = "42260912345678000123550010000000011000000015";

    [Fact]
    public async Task Invalid_oldest_filename_cannot_starve_valid_authenticated_request()
    {
        var root = NewRoot();
        try
        {
            var share = Path.Combine(root, "share");
            Directory.CreateDirectory(share);
            var paths = new SharedQueuePaths(share);
            paths.InitializeAsCentral();

            var poison = Path.Combine(paths.QueueDirectory, "arquivo-invalido.req");
            await File.WriteAllTextAsync(poison, "lixo");
            File.SetCreationTimeUtc(poison, DateTime.UtcNow.AddMinutes(-5));

            var keyStore = new CentralKeyStore(Path.Combine(root, "central.key"));
            var authorized = new AuthorizedClientStore(Path.Combine(root, "authorized.bin"));
            var clientId = Guid.NewGuid();
            var secret = RandomNumberGenerator.GetBytes(32);
            authorized.Authorize(clientId, "CA02", secret);
            var request = SharedQueueCrypto.CreateClientRequest(
                Guid.NewGuid(),
                AccessKey,
                keyStore.GetOrCreatePublicKey(),
                clientId,
                1,
                secret);
            await File.WriteAllBytesAsync(
                paths.RequestPath(request.Envelope.RequestId),
                JsonSerializer.SerializeToUtf8Bytes(request.Envelope));

            var calls = 0;
            var processor = new SharedQueueProcessor(paths, keyStore, authorized, (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new NfeLookupResult(NfeLookupStatus.Found, "<xml/>", "138", "ok", false));
            });

            var processed = await processor.ProcessOneAsync();

            Assert.True(processed);
            Assert.Equal(1, calls);
            Assert.False(File.Exists(poison));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-poison-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
