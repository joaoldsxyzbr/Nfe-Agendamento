using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class DanfeStaticAssetsTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Danfe_renderer_contains_full_fiscal_sections()
    {
        var script = Fixture("app.js");

        Assert.Contains("barcodeSvg", script);
        Assert.Contains("FATURA / DUPLICATA", script);
        Assert.Contains("PAGAMENTO", script);
        Assert.Contains("BASE DE CÁLC. ICMS S.T.", script);
        Assert.Contains("VALOR DO ICMS SUBST.", script);
        Assert.Contains("VALOR DO PIS", script);
        Assert.Contains("VALOR DA COFINS", script);
        Assert.Contains("RESERVADO AO FISCO", script);
        Assert.Contains("buildProductTaxData", script);
    }

    [Fact]
    public void Application_uses_dark_workspace_while_danfe_remains_print_white()
    {
        var html = Fixture("index.html");
        var css = Fixture("styles.css");

        Assert.Contains("class=\"app-shell\"", html);
        Assert.Contains("class=\"workspace-grid\"", html);
        Assert.Contains("color-scheme: dark", css);
        Assert.Contains(".danfe-page", css);
        Assert.Contains("background: #fff", css);
    }
}
