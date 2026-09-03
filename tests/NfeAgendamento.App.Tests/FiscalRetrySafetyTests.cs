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
    public async Task Leadership_loss_is_fail_closed_and_not_retried()
    {
        using var temp = new TemporaryDirectory();
        var transport = new LeadershipLostTransport();
        var service = CreateService(temp.Path, transport);

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Failed, result.Status);
        Assert.Equal(1, transport.CallCount);
        Assert.Contains("liderança", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shared_queue_default_timeout_covers_queue_wait_plus_one_fiscal_timeout()
    {
        Assert.True(
            SharedQueueClient.DefaultLookupTimeout >= TimeSpan.FromMinutes(3),
            $"Timeout padrão atual: {SharedQueueClient.DefaultLookupTimeout}.");
    }

    private static NfeLookupService CreateService(string root, INfeDistributionTransport transport) =>
        new(
            transport,
            new EncryptedXmlCache(Path.Combine(root, "cache"), TimeProvider.System, TimeSpan.FromHours(24)),
            new FiscalCooldownStore(Path.Combine(root, "cooldown.bin")),
            delay: (_, _) => Task.CompletedTask);

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

    private sealed class LeadershipLostTransport : INfeDistributionTransport
    {
        public int CallCount { get; private set; }

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var exceptionType = typeof(NfeLookupService).Assembly.GetType(
                "NfeAgendamento.App.Fiscal.FiscalLeadershipLostException");
            if (exceptionType is null)
                throw new InvalidOperationException("FiscalLeadershipLostException ainda não existe.");

            var exception = Activator.CreateInstance(exceptionType, "A liderança fiscal foi perdida antes do envio.") as Exception
                ?? throw new InvalidOperationException("Não foi possível criar a exceção de perda de liderança.");
            throw exception;
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
