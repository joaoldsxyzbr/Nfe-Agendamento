using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class ReleaseReadinessBehaviorTests
{
    private const string ValidKey = "35260812345678000195550010000000011000000018";

    [Fact]
    public async Task Cooldown_656_survives_new_service_instance_and_blocks_transport()
    {
        using var temp = new TemporaryDirectory();
        var cooldownPath = Path.Combine(temp.Path, "cooldown.bin");
        var firstTransport = new FixedTransport(new NfeDistributionResponse("656", "Consumo indevido", null));
        var first = CreateService(temp.Path, firstTransport, new FiscalCooldownStore(cooldownPath));

        var firstResult = await first.LookupAsync(ValidKey);
        Assert.Equal(NfeLookupStatus.Blocked, firstResult.Status);
        Assert.Equal(1, firstTransport.CallCount);

        var secondTransport = new FixedTransport(new NfeDistributionResponse("137", "Nenhum documento localizado", null));
        var second = CreateService(temp.Path, secondTransport, new FiscalCooldownStore(cooldownPath));

        var secondResult = await second.LookupAsync(ValidKey);

        Assert.Equal(NfeLookupStatus.Blocked, secondResult.Status);
        Assert.Equal(0, secondTransport.CallCount);
        Assert.NotNull(secondResult.BlockedUntilUtc);
    }

    [Fact]
    public void Bootstrap_exposes_only_operational_session_and_lan_fields()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Program.cs"));
        var start = source.IndexOf("app.MapGet(\"/api/bootstrap\"", StringComparison.Ordinal);
        var end = source.IndexOf("app.MapGet(\"/api/certificates\"", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Bloco /api/bootstrap não foi localizado no Program.cs.");
        var bootstrap = source[start..end];

        Assert.Contains("csrfToken", bootstrap, StringComparison.Ordinal);
        Assert.Contains("lanMode", bootstrap, StringComparison.Ordinal);
        Assert.Contains("accessUrl", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("Thumbprint", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Subject", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Certificate", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Xml", bootstrap, StringComparison.OrdinalIgnoreCase);
    }

    private static NfeLookupService CreateService(
        string root,
        INfeDistributionTransport transport,
        FiscalCooldownStore cooldown) =>
        new(
            transport,
            new EncryptedXmlCache(Path.Combine(root, "cache"), TimeProvider.System, TimeSpan.FromHours(24)),
            cooldown,
            delay: (_, _) => Task.CompletedTask,
            gate: new FiscalOperationGate(),
            coordinator: new FiscalRequestCoordinator());

    private sealed class FixedTransport(NfeDistributionResponse response) : INfeDistributionTransport
    {
        public int CallCount { get; private set; }

        public Task<NfeDistributionResponse> QueryByAccessKeyAsync(string accessKey, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-release-readiness-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
