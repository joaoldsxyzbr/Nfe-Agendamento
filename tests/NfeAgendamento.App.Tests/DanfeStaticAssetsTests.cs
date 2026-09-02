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
        Assert.Contains("color-scheme: dark", css);
        Assert.Contains(".danfe-page", css);
        Assert.Contains("background: #fff", css);
    }

    [Fact]
    public void Application_does_not_require_password_or_authentication_routes()
    {
        var html = Fixture("index.html");
        var script = Fixture("app.js");
        var program = Fixture("Program.cs");

        Assert.DoesNotContain("authGate", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authPassword", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/auth", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/auth", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalSessionService", program, StringComparison.Ordinal);
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
    public void New_lookup_clears_previous_result_and_is_left_of_lookup_button()
    {
        var html = Fixture("index.html");

        Assert.Contains("id=\"newLookup\"", html);
        Assert.Contains(">Nova consulta<", html);
        Assert.True(html.IndexOf("id=\"newLookup\"", StringComparison.Ordinal) < html.IndexOf("id=\"lookup\"", StringComparison.Ordinal));
        Assert.Contains("function resetLookup()", html);
        Assert.Contains("accessKey.value = '';", html);
        Assert.Contains("actions.hidden = true;", html);
        Assert.Contains("accessKey.focus();", html);
        Assert.Contains("document.getElementById('newLookup').addEventListener('click', resetLookup);", html);
    }

    [Fact]
    public void Application_uses_three_operational_tabs_without_step_badges()
    {
        var html = Fixture("index.html");
        var css = Fixture("ui-adjustments.css");

        Assert.Contains("role=\"tablist\"", html);
        Assert.Contains("data-tab=\"lookup\"", html);
        Assert.Contains("data-tab=\"batch\"", html);
        Assert.Contains("data-tab=\"config\"", html);
        Assert.Contains("id=\"tabPanelLookup\"", html);
        Assert.Contains("id=\"tabPanelBatch\"", html);
        Assert.Contains("id=\"tabPanelConfig\"", html);
        Assert.Contains("/tabs.js", html);
        Assert.DoesNotContain("step-badge", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".app-tabs", css);
        Assert.Contains(".tab-button[aria-selected=\"true\"]", css);
        Assert.Contains("body {\n  background: #081522;", css);
    }

    [Fact]
    public void Batch_lookup_reuses_individual_endpoint_instead_of_exposing_backend_batch_route()
    {
        var html = Fixture("index.html");
        var program = Fixture("Program.cs");

        Assert.Contains("Consulta em lote", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"batchInput\"", html, StringComparison.Ordinal);
        Assert.Contains("/batch.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/nfe/batch", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BatchLookupService", program, StringComparison.Ordinal);
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
