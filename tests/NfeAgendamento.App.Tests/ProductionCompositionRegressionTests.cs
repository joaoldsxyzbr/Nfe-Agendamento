using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class ProductionCompositionRegressionTests
{
    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Production_must_not_register_central_pairing_or_remote_queue_services()
    {
        var program = ReadFixture("Program.cs");

        Assert.DoesNotContain("SharedQueueCentralService", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedQueueClient", program, StringComparison.Ordinal);
        Assert.DoesNotContain("PairingCodeService", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedQueuePairing", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedQueueGroupPairing", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedQueueGroupRotation", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedQueueGroupBootstrapService", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_must_not_expose_pairing_endpoints()
    {
        var program = ReadFixture("Program.cs");

        Assert.DoesNotContain("/api/pairing/code", program, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/pairing/client", program, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/pairing/clients", program, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/pairing/revoke", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_uses_local_xml_cache_and_shared_fiscal_coordination()
    {
        var program = ReadFixture("Program.cs");

        Assert.Contains("new EncryptedXmlCache()", program, StringComparison.Ordinal);
        Assert.Contains("new FiscalCooldownStore(sp.GetRequiredService<SharedQueuePaths>())", program, StringComparison.Ordinal);
        Assert.Contains("new FiscalOperationGate(sp.GetRequiredService<SharedQueuePaths>())", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CandidateStateStore", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_exposes_shared_lock_mode_without_pairing()
    {
        var program = ReadFixture("Program.cs");

        Assert.Contains("mode = \"shared_lock\"", program, StringComparison.Ordinal);
        Assert.Contains("pairingRequired = false", program, StringComparison.Ordinal);
        Assert.Contains("shareAvailable = paths.ValidateForClient()", program, StringComparison.Ordinal);
        Assert.Contains("sharedFolder = paths.Root", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_exposes_exact_running_app_version_for_update_health_check()
    {
        var program = ReadFixture("Program.cs");

        Assert.Contains("appVersion = CurrentAppVersion(),", program, StringComparison.Ordinal);
        Assert.Contains("private static string CurrentAppVersion()", program, StringComparison.Ordinal);
        Assert.Contains("GetName().Version?.ToString(3)", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_configuration_contains_no_pairing_controls_or_script()
    {
        var html = ReadFixture("index.html");

        Assert.DoesNotContain("pairing.js", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pairingCode", html, StringComparison.Ordinal);
        Assert.DoesNotContain("generatePairingCode", html, StringComparison.Ordinal);
        Assert.DoesNotContain("authorizedClientsPanel", html, StringComparison.Ordinal);
        Assert.Contains("Pasta compartilhada", html, StringComparison.OrdinalIgnoreCase);
    }
}
