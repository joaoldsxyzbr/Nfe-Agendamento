using System.Security.Cryptography;
using System.Text.Json;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class PairingBindingSecurityTests
{
    [Fact]
    public async Task Client_rejects_pairing_response_with_different_request_id()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-pairing-binding-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var share = Path.Combine(root, "share");
            Directory.CreateDirectory(share);
            var paths = new SharedQueuePaths(share);
            paths.InitializeAsCentral();

            var store = new ClientPairingStore(Path.Combine(root, "client-pairing.bin"));
            var client = new SharedQueuePairingClient(paths, store, TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(3));
            var codes = new PairingCodeService();
            var code = codes.Generate();
            var pairingKey = PairingCodeService.DeriveKey(code.Code);

            try
            {
                var pairingTask = client.PairAsync(code.Code);
                var requestPath = await WaitForSingleFileAsync(paths.PairingDirectory, "*.pair.req");
                var requestEnvelope = JsonSerializer.Deserialize<QueuePairingRequestEnvelope>(await File.ReadAllBytesAsync(requestPath))
                    ?? throw new InvalidDataException("Pedido de pareamento ausente no teste.");
                var requestPayload = SharedQueuePairingCrypto.OpenRequest(requestEnvelope, pairingKey);

                var centralKeyStore = new CentralKeyStore(Path.Combine(root, "central.key"));
                var responsePayload = new QueuePairingResponsePayload(
                    requestPayload.ClientId,
                    requestPayload.ClientName,
                    RandomNumberGenerator.GetBytes(32),
                    "CENTRAL-TESTE",
                    centralKeyStore.GetOrCreatePublicKey());

                var wrongRequestId = Guid.NewGuid();
                Assert.NotEqual(requestEnvelope.RequestId, wrongRequestId);
                var responseEnvelope = SharedQueuePairingCrypto.CreateResponse(wrongRequestId, responsePayload, pairingKey);
                await File.WriteAllBytesAsync(
                    paths.PairingResponsePath(requestEnvelope.RequestId),
                    JsonSerializer.SerializeToUtf8Bytes(responseEnvelope));

                var result = await pairingTask;

                Assert.False(result.Success);
                Assert.False(store.IsPaired);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pairingKey);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<string> WaitForSingleFileAsync(string directory, string pattern)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var file = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).SingleOrDefault();
            if (file is not null)
                return file;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Arquivo {pattern} não apareceu em {directory}.");
    }
}
