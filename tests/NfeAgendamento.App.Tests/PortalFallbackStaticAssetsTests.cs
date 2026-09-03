using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class PortalFallbackStaticAssetsTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Consumo_indevido_offers_portal_fallback_only_on_active_leader()
    {
        var html = Fixture("index.html");
        var script = Fixture("portal-fallback.js");
        var program = Fixture("Program.cs");

        Assert.Contains("id=\"portalFallback\"", html, StringComparison.Ordinal);
        Assert.Contains("/portal-fallback.js", html, StringComparison.Ordinal);
        Assert.Contains("/api/nfe/portal-fallback", script, StringComparison.Ordinal);
        Assert.Contains("centralActive", script, StringComparison.Ordinal);
        Assert.DoesNotContain("configuredAsCentral", script, StringComparison.Ordinal);
        Assert.Contains("consumo_indevido", script, StringComparison.Ordinal);
        Assert.Contains("PortalNfeFallbackLauncher", program, StringComparison.Ordinal);
        Assert.Contains("SharedQueueCentralService central", program, StringComparison.Ordinal);
        Assert.Contains("central.CanProcessWork()", program, StringComparison.Ordinal);
        Assert.Contains("leader_inactive", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Portal_fallback_does_not_automate_captcha()
    {
        var program = Fixture("Program.cs");
        var script = Fixture("portal-fallback.js");

        Assert.DoesNotContain("hcaptcha.com/siteverify", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hcaptcha.com/siteverify", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("g-recaptcha-response", program, StringComparison.OrdinalIgnoreCase);
    }
}
