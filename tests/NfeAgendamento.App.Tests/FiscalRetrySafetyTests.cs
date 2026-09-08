using System.Net;
using System.Net.Http;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class FiscalRetrySafetyTests
{
    private const string ValidKey = "35260812345678000195550010000000011000000018";

    [Fact]
    public async Task Http_429_is_not_retried()
    {
        using var temp = new TemporaryDirectory();
        var transport = new RateLimitedTransport();
        var service = CreateService(temp.Path, transport);

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Failed, result.Status);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task Ambiguous_transport_timeout_is_not_retried()
    {
        using var temp = new TemporaryDirectory();
        var transport = new TimedOutTransport();
        var service = CreateService(temp.Path, transport);

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Failed, result.Status);
        Assert.Equal(1, transport.CallCount);
        Assert.Contains("tempo limite", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unavailable_shared_folder_is_fail_closed_before_transport()
    {
        using var temp = new TemporaryDirectory();
        var transport = new CountingTransport();
        var missingShare = Path.Combine(temp.Path, "missing-share");
        var service = CreateService(
            temp.Path,
            transport,
            new FiscalOperationGate(new SharedQueuePaths(missingShare)));

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Failed, result.Status);
        Assert.Equal(0, transport.CallCount);
        Assert.Contains("pasta compartilhada", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static NfeLookupService CreateService(
        string root,
        INfeDistributionTransport transport,
        FiscalOperationGate? gate = null) =>
        new(
            transport,
            new EncryptedXmlCache(Path.Combine(root, "cache"), TimeProvider.System, TimeSpan.FromHours(24)),
            new FiscalCooldownStore(Path.Combine(root, "cooldown.bin")),
            delay: (_, _) => Task.CompletedTask,
            gate: gate);

    private sealed class RateLimitedTransport : INfeDistributionTransport
    {
        public int CallCount { get; private set; }

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new HttpRequestException("HTTP 429 de teste", null, HttpStatusCode.TooManyRequests);
        }
    }

    private sealed class TimedOutTransport : INfeDistributionTransport
    {
        public int CallCount { get; private set; }

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new TaskCanceledException("timeout de teste");
        }
    }

    private sealed class CountingTransport : INfeDistributionTransport
    {
        public int CallCount { get; private set; }

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new NfeDistributionResponse("137", "Nenhum documento localizado", null));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-retry-safety-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
