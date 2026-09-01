using System.Net;
using System.Net.Http;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class NfeLookupHardeningTests
{
    private const string ValidKey = "35260812345678000195550010000000011000000018";

    [Fact]
    public async Task Corrupt_cooldown_state_fails_closed_before_transport()
    {
        using var temp = new TemporaryDirectory();
        var cooldownPath = Path.Combine(temp.Path, "cooldown.bin");
        await File.WriteAllBytesAsync(cooldownPath, [1, 2, 3, 4, 5]);
        var transport = new CountingTransport();
        var service = new NfeLookupService(
            transport,
            new EncryptedXmlCache(Path.Combine(temp.Path, "cache"), TimeProvider.System, TimeSpan.FromHours(24)),
            new FiscalCooldownStore(cooldownPath));

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Failed, result.Status);
        Assert.Equal(0, transport.CallCount);
        Assert.Contains("estado fiscal", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Persistence_failure_after_656_keeps_process_blocked_without_second_transport_call()
    {
        using var temp = new TemporaryDirectory();
        var invalidCooldownPath = Path.Combine(temp.Path, "cooldown.bin");
        Directory.CreateDirectory(invalidCooldownPath);
        var cooldown = new FiscalCooldownStore(invalidCooldownPath);
        var transport = new SequenceTransport();
        var service = new NfeLookupService(
            transport,
            new EncryptedXmlCache(Path.Combine(temp.Path, "cache"), TimeProvider.System, TimeSpan.FromHours(24)),
            cooldown,
            delay: (_, _) => Task.CompletedTask,
            gate: new FiscalOperationGate(),
            coordinator: new FiscalRequestCoordinator());

        var first = await service.LookupAsync(ValidKey);
        var second = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Blocked, first.Status);
        Assert.Equal(NfeLookupStatus.Blocked, second.Status);
        Assert.NotNull(first.BlockedUntilUtc);
        Assert.NotNull(second.BlockedUntilUtc);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task Non_transient_http_error_is_not_retried()
    {
        using var temp = new TemporaryDirectory();
        var transport = new HttpFailureTransport(HttpStatusCode.BadRequest);
        var delayCount = 0;
        var service = new NfeLookupService(
            transport,
            new EncryptedXmlCache(Path.Combine(temp.Path, "cache"), TimeProvider.System, TimeSpan.FromHours(24)),
            new FiscalCooldownStore(Path.Combine(temp.Path, "cooldown.bin")),
            delay: (_, _) =>
            {
                delayCount++;
                return Task.CompletedTask;
            });

        var result = await service.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Failed, result.Status);
        Assert.Equal(1, transport.CallCount);
        Assert.Equal(0, delayCount);
    }

    [Fact]
    public async Task Deduplicated_lookup_writes_one_audit_record_without_full_key()
    {
        using var temp = new TemporaryDirectory();
        var auditPath = Path.Combine(temp.Path, "fiscal-audit.jsonl");
        var audit = new FiscalAuditLog(auditPath);
        var coordinator = new FiscalRequestCoordinator();
        var gate = new FiscalOperationGate();
        var transport = new BlockingTransport();
        var cooldown = new FiscalCooldownStore(Path.Combine(temp.Path, "cooldown.bin"));
        var cache = new EncryptedXmlCache(Path.Combine(temp.Path, "cache"), TimeProvider.System, TimeSpan.FromHours(24));
        var service1 = new NfeLookupService(transport, cache, cooldown, gate: gate, coordinator: coordinator, audit: audit);
        var service2 = new NfeLookupService(transport, cache, cooldown, gate: gate, coordinator: coordinator, audit: audit);

        var first = service1.LookupAsync(ValidKey);
        await transport.FirstCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service2.LookupAsync(ValidKey);
        transport.ReleaseFirstCall.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        var lines = await File.ReadAllLinesAsync(auditPath);
        Assert.Single(lines);
        Assert.DoesNotContain(ValidKey, lines[0], StringComparison.Ordinal);
        Assert.Contains("NotFound", lines[0], StringComparison.Ordinal);
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

    private sealed class SequenceTransport : INfeDistributionTransport
    {
        public int CallCount { get; private set; }

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CallCount == 1
                ? new NfeDistributionResponse("656", "Consumo indevido", null)
                : new NfeDistributionResponse("137", "Nenhum documento localizado", null));
        }
    }

    private sealed class HttpFailureTransport(HttpStatusCode statusCode) : INfeDistributionTransport
    {
        public int CallCount { get; private set; }

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new HttpRequestException("Falha HTTP simulada.", null, statusCode);
        }
    }

    private sealed class BlockingTransport : INfeDistributionTransport
    {
        public int CallCount;
        public TaskCompletionSource FirstCallEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            FirstCallEntered.TrySetResult();
            await ReleaseFirstCall.Task.WaitAsync(cancellationToken);
            return new NfeDistributionResponse("137", "Nenhum documento localizado", null);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-hardening-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
