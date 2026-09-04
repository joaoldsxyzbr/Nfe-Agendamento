using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class PortalFallbackStaticAssetsTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Consumo_indevido_offers_portal_fallback_on_any_authorized_pc()
    {
        var html = Fixture("index.html");
        var script = Fixture("portal-fallback.js");
        var program = Fixture("Program.cs");

        Assert.Contains("id=\"portalFallback\"", html, StringComparison.Ordinal);
        Assert.Contains("/portal-fallback.js", html, StringComparison.Ordinal);
        Assert.Contains("Baixar pelo Portal", html, StringComparison.Ordinal);
        Assert.Contains("/api/nfe/portal-fallback", script, StringComparison.Ordinal);
        Assert.Contains("portalFallbackAvailable", script, StringComparison.Ordinal);
        Assert.DoesNotContain("centralActive", script, StringComparison.Ordinal);
        Assert.Contains("consumo_indevido", script, StringComparison.Ordinal);
        Assert.Contains("PortalNfeFallbackLauncher", program, StringComparison.Ordinal);
        Assert.Contains("var portalFallbackAvailable = group.IsCandidateReady;", program, StringComparison.Ordinal);
        Assert.Contains("if (!group.IsCandidateReady)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("group.IsCandidateReady || state.IsConfiguredAsCentral", program, StringComparison.Ordinal);
        Assert.DoesNotContain("!group.IsCandidateReady && !state.IsConfiguredAsCentral", program, StringComparison.Ordinal);
        Assert.DoesNotContain("central.CanProcessWork()", program, StringComparison.Ordinal);
        Assert.Contains("portal_not_authorized", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Portal_fallback_returns_cached_xml_to_site_without_fiscal_polling()
    {
        var script = Fixture("portal-fallback.js");
        var program = Fixture("Program.cs");

        Assert.Contains("/api/nfe/cache/{accessKey}", program, StringComparison.Ordinal);
        Assert.Contains("cache.TryGetAsync(accessKey", program, StringComparison.Ordinal);
        Assert.Contains("`/api/nfe/cache/${encodeURIComponent(accessKey)}`", script, StringComparison.Ordinal);
        Assert.Contains("globalThis.lookup", script, StringComparison.Ordinal);
        Assert.Contains("atualizada automaticamente", script, StringComparison.OrdinalIgnoreCase);
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
