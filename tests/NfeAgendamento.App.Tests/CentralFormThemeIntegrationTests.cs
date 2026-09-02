using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class CentralFormThemeIntegrationTests
{
    private static string CentralFormSource() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CentralForm.cs"));

    [Fact]
    public void Central_form_keeps_brand_theme_and_shows_shared_queue_status()
    {
        var source = CentralFormSource();

        Assert.Contains("BackColor = CentralTheme.Background", source, StringComparison.Ordinal);
        Assert.Contains("ForeColor = CentralTheme.BrandBlue", source, StringComparison.Ordinal);
        Assert.Contains("BackColor = CentralTheme.BrandYellow", source, StringComparison.Ordinal);
        Assert.Contains("FlatStyle = FlatStyle.Flat", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Central NFe Agendamento\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Iniciar Central\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Parar Central\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Abrir sistema\"", source, StringComparison.Ordinal);
        Assert.Contains("Papel deste PC", source, StringComparison.Ordinal);
        Assert.Contains("Pasta compartilhada", source, StringComparison.Ordinal);
        Assert.Contains("SharedQueuePaths.DefaultRoot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Configurar firewall", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WindowsFirewallService", source, StringComparison.Ordinal);
    }
}
