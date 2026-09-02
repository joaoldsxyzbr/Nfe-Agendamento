using System.Text;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueFlowTests
{
    private const string AccessKey = "42260912345678000123550010000000011000000015";

    [Fact]
    public async Task Client_and_central_exchange_encrypted_lookup_through_share()
    {
        var root = NewRoot();
        try
        {
            var share = Path.Combine(root, "share");
            Directory.CreateDirectory(share);
            var paths = new SharedQueuePaths(share);
            paths.InitializeAsCentral();

            var keyStore = new CentralKeyStore(Path.Combine(root, "central.key"));
            await WriteHeartbeatAsync(paths, keyStore);

            var pending = new PendingRequestSecretStore(Path.Combine(root, "pending"));
            var client = new SharedQueueClient(paths, pending, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
            var processor = new SharedQueueProcessor(
                paths,
                keyStore,
                (key, _) => Task.FromResult(new NfeLookupResult(
                    NfeLookupStatus.Found,
                    "<nfeProc>xml-secreto</nfeProc>",
                    "138",
                    $"Documento {key[..4]} localizado.",
                    false)));

            var clientTask = client.LookupAsync(AccessKey);
            var requestPath = await WaitForSingleFileAsync(paths.QueueDirectory, "*.req");
            var requestText = await File.ReadAllTextAsync(requestPath);
            Assert.DoesNotContain(AccessKey, requestText, StringComparison.Ordinal);

            Assert.True(await processor.ProcessOneAsync());
            var responsePath = await WaitForSingleFileAsync(paths.ResponsesDirectory, "*.res");
            var responseText = await File.ReadAllTextAsync(responsePath);
            Assert.DoesNotContain("xml-secreto", responseText, StringComparison.Ordinal);
            Assert.DoesNotContain(AccessKey, responseText, StringComparison.Ordinal);

            var result = await clientTask;
            Assert.Equal(NfeLookupStatus.Found, result.Status);
            Assert.Equal("<nfeProc>xml-secreto</nfeProc>", result.Xml);
            Assert.False(File.Exists(responsePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Processor_rejects_tampered_request_without_calling_fiscal_service()
    {
        var root = NewRoot();
        try
        {
            var share = Path.Combine(root, "share");
            Directory.CreateDirectory(share);
            var paths = new SharedQueuePaths(share);
            paths.InitializeAsCentral();
            var keyStore = new CentralKeyStore(Path.Combine(root, "central.key"));
            var material = SharedQueueCrypto.CreateClientRequest(Guid.NewGuid(), AccessKey, keyStore.GetOrCreatePublicKey());
            material.Envelope.Ciphertext[0] ^= 1;
            await File.WriteAllBytesAsync(paths.RequestPath(material.Envelope.RequestId), JsonSerializer.SerializeToUtf8Bytes(material.Envelope));

            var calls = 0;
            var processor = new SharedQueueProcessor(paths, keyStore, (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new NfeLookupResult(NfeLookupStatus.Failed, null, null, "não deveria chamar", false));
            });

            Assert.True(await processor.ProcessOneAsync());
            Assert.Equal(0, calls);
            Assert.False(File.Exists(paths.ProcessingPath(material.Envelope.RequestId)));
            Assert.False(File.Exists(paths.ResponsePath(material.Envelope.RequestId)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Existing_response_prevents_reprocessing_recovered_item()
    {
        var root = NewRoot();
        try
        {
            var share = Path.Combine(root, "share");
            Directory.CreateDirectory(share);
            var paths = new SharedQueuePaths(share);
            paths.InitializeAsCentral();
            var keyStore = new CentralKeyStore(Path.Combine(root, "central.key"));
            var material = SharedQueueCrypto.CreateClientRequest(Guid.NewGuid(), AccessKey, keyStore.GetOrCreatePublicKey());
            await File.WriteAllBytesAsync(paths.ProcessingPath(material.Envelope.RequestId), JsonSerializer.SerializeToUtf8Bytes(material.Envelope));
            var response = SharedQueueCrypto.CreateResponse(
                material.Envelope.RequestId,
                new NfeLookupResult(NfeLookupStatus.NotFound, null, "137", "Não localizado.", false),
                material.AesKey);
            await File.WriteAllBytesAsync(paths.ResponsePath(material.Envelope.RequestId), JsonSerializer.SerializeToUtf8Bytes(response));

            var calls = 0;
            var processor = new SharedQueueProcessor(paths, keyStore, (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new NfeLookupResult(NfeLookupStatus.Failed, null, null, null, false));
            });

            await processor.MaintainAsync();

            Assert.Equal(0, calls);
            Assert.False(File.Exists(paths.ProcessingPath(material.Envelope.RequestId)));
            Assert.True(File.Exists(paths.ResponsePath(material.Envelope.RequestId)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WriteHeartbeatAsync(SharedQueuePaths paths, CentralKeyStore keyStore)
    {
        var heartbeat = new QueueHeartbeat(
            SharedQueueCrypto.ProtocolVersion,
            "CA03",
            DateTimeOffset.UtcNow,
            Convert.ToBase64String(keyStore.GetOrCreatePublicKey()),
            "test");
        await File.WriteAllBytesAsync(paths.StatusPath("heartbeat.json"), JsonSerializer.SerializeToUtf8Bytes(heartbeat));
    }

    private static async Task<string> WaitForSingleFileAsync(string directory, string pattern)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var file = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).SingleOrDefault();
            if (file is not null) return file;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Arquivo {pattern} não apareceu em {directory}.");
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-flow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
