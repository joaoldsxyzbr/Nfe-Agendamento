using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SafetyRegressionTests
{
    private const string FiscalKey = "35260812345678000195550010000000011000000018";
    private const string QueueKey = "42260912345678000123550010000000011000000015";

    [Fact]
    public async Task Ambiguous_network_failure_is_not_retried()
    {
        using var temp = new TemporaryDirectory("fiscal-network");
        var transport = new ThrowingTransport(new HttpRequestException("falha ambígua de rede"));
        var service = CreateLookupService(temp.Path, transport);

        var result = await service.LookupAsync(FiscalKey);

        Assert.Equal(NfeLookupStatus.Failed, result.Status);
        Assert.Equal(1, transport.CallCount);
        Assert.Contains("não será repetida", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_503_failure_is_not_retried()
    {
        using var temp = new TemporaryDirectory("fiscal-http");
        var transport = new ThrowingTransport(new HttpRequestException("indisponível", null, HttpStatusCode.ServiceUnavailable));
        var service = CreateLookupService(temp.Path, transport);

        var result = await service.LookupAsync(FiscalKey);

        Assert.Equal(NfeLookupStatus.Failed, result.Status);
        Assert.Equal(1, transport.CallCount);
        Assert.Contains("não será repetida", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recovered_authenticated_request_returns_safe_interruption_without_second_fiscal_call()
    {
        using var temp = new TemporaryDirectory("queue-recovery");
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var keyStore = new CentralKeyStore(Path.Combine(temp.Path, "central.key"));
        var authorized = new AuthorizedClientStore(Path.Combine(temp.Path, "authorized.bin"));
        var clientId = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(32);
        authorized.Authorize(clientId, "CA02", secret);

        var material = SharedQueueCrypto.CreateClientRequest(
            Guid.NewGuid(),
            QueueKey,
            keyStore.GetOrCreatePublicKey(),
            clientId,
            1,
            secret);
        await File.WriteAllBytesAsync(
            paths.RequestPath(material.Envelope.RequestId),
            JsonSerializer.SerializeToUtf8Bytes(material.Envelope));

        var fiscalCalls = 0;
        var interruptedProcessor = new SharedQueueProcessor(paths, keyStore, authorized, (_, _) =>
        {
            Interlocked.Increment(ref fiscalCalls);
            throw new IOException("queda simulada depois da autenticação");
        });

        Assert.False(await interruptedProcessor.ProcessOneAsync());
        Assert.Equal(1, fiscalCalls);

        var processingPath = paths.ProcessingPath(material.Envelope.RequestId);
        Assert.True(File.Exists(processingPath));
        File.SetLastWriteTimeUtc(processingPath, DateTime.UtcNow.AddMinutes(-3));
        await interruptedProcessor.MaintainAsync();

        var recoveryProcessor = new SharedQueueProcessor(paths, keyStore, authorized, (_, _) =>
        {
            Interlocked.Increment(ref fiscalCalls);
            return Task.FromResult(new NfeLookupResult(NfeLookupStatus.Found, "<xml/>", "138", "não deveria repetir", false));
        });

        Assert.True(await recoveryProcessor.ProcessOneAsync());
        Assert.Equal(1, fiscalCalls);

        var responseBytes = await File.ReadAllBytesAsync(paths.ResponsePath(material.Envelope.RequestId));
        var responseEnvelope = JsonSerializer.Deserialize<QueueResponseEnvelope>(responseBytes)
            ?? throw new InvalidDataException("Resposta de recuperação inválida.");
        var recovered = SharedQueueCrypto.OpenResponse(responseEnvelope, material.AesKey);

        Assert.Equal(NfeLookupStatus.Failed, recovered.Status);
        Assert.Contains("não foi repetida", recovered.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static NfeLookupService CreateLookupService(string root, INfeDistributionTransport transport) =>
        new(
            transport,
            new EncryptedXmlCache(Path.Combine(root, "cache"), TimeProvider.System, TimeSpan.FromHours(24)),
            new FiscalCooldownStore(Path.Combine(root, "cooldown.bin")),
            delay: (_, _) => Task.CompletedTask);

    private sealed class ThrowingTransport(HttpRequestException exception) : INfeDistributionTransport
    {
        public int CallCount { get; private set; }

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw exception;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string suffix)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "nfe-agendamento-safety-tests",
                $"{suffix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
