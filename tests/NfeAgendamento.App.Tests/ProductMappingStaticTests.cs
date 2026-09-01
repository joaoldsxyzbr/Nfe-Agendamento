using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class ProductMappingStaticTests
{
    private static string MappingScript() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "product-mapping.js"));

    [Fact]
    public void Product_mapping_uses_a_declarative_catalog_with_alias_conflict_validation()
    {
        var script = MappingScript();

        Assert.Contains("FERNANDO_KLEIN_CATALOG", script);
        Assert.Contains("aliases:", script);
        Assert.Contains("buildAliasIndex", script);
        Assert.Contains("validateCatalog", script);
        Assert.Contains("Alias conflitante", script);
    }

    [Fact]
    public void Product_mapping_normalizes_supplier_prefix_and_uses_strict_alias_lookup()
    {
        var script = MappingScript();

        Assert.Contains("replace(/[^A-Z0-9]+/g, ' ')", script);
        Assert.Contains("replace(/^VERDURAS(?:\\s+|$)/, '')", script);
        Assert.Contains("FERNANDO_KLEIN_ALIAS_INDEX[productName] || ''", script);
        Assert.DoesNotContain("includes(productName)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("startsWith(productName)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Product_mapping_keeps_known_supplier_aliases_explicit()
    {
        var script = MappingScript();

        Assert.Contains("['CEBOLA', 'CEBOLINHA']", script);
        Assert.Contains("['SALSA', 'SALSINHA']", script);
        Assert.Contains("['ALFACE AMERICANA', 'AMERICANA']", script);
    }
}
