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

    [Fact]
    public void Lookup_button_is_guarded_against_duplicate_requests()
    {
        var script = Fixture("app.js");

        Assert.Contains("if (lookupInProgress) return;", script);
        Assert.Contains("lookupButton.disabled = true;", script);
        Assert.Contains("lookupButton.disabled = false;", script);
    }

    [Fact]
    public void Danfe_uses_a4_space_and_only_renders_transport_when_it_has_useful_data()
    {
        var script = Fixture("danfe-compact.js");
        var css = Fixture("danfe-compact.css");

        Assert.Contains("hasTransportData", script);
        Assert.Contains("transportSection", script);
        Assert.Contains("danfe-products-fill", script);
        Assert.Contains("min-height: 277mm", css);
        Assert.Contains("flex: 1", css);
    }

    [Fact]
    public void Danfe_paginates_products_by_available_vertical_space_instead_of_fixed_item_count()
    {
        var script = Fixture("danfe-compact.js");

        Assert.Contains("paginateProductsByAvailableSpace", script);
        Assert.Contains("estimateProductHeight", script);
        Assert.Contains("FIRST_PAGE_PRODUCT_SPACE_MM", script);
        Assert.Contains("paginateProducts = paginateProductsByAvailableSpace", script);
    }

    [Fact]
    public void Danfe_products_table_has_item_number_as_first_column()
    {
        var script = Fixture("danfe-compact.js");

        Assert.Contains("<th>Item</th><th>Código produto</th>", script);
        Assert.Contains("attr(det, 'det', 'nItem')", script);
        Assert.Contains("colspan=\"16\"", script);
    }

    [Fact]
    public void Danfe_item_column_has_fixed_width_and_does_not_wrap()
    {
        var script = Fixture("danfe-compact.js");
        var css = Fixture("danfe-compact.css");

        Assert.Contains("class=\"center item-col\"", script);
        Assert.Contains("class=\"code-col\"", script);
        Assert.Contains(".products-table col.item", css);
        Assert.Contains("width: 8mm", css);
        Assert.Contains("white-space: nowrap", css);
        Assert.Contains(".products-table .item-col", css);
    }
}
