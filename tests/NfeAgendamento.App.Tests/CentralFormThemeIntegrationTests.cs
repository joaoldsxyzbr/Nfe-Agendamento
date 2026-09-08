using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class CentralFormThemeIntegrationTests
{
    private static string CentralFormSource() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CentralForm.cs"));

    [Fact]
    public void Status_form_keeps_brand_theme_and_shows_shared_lock_mode()
    {
        var source = CentralFormSource();

        Assert.Contains("BackColor = CentralTheme.Background", source, StringComparison.Ordinal);
        Assert.Contains("ForeColor = CentralTheme.BrandBlue", source, StringComparison.Ordinal);
        Assert.Contains("BackColor = CentralTheme.BrandYellow", source, StringComparison.Ordinal);
        Assert.Contains("FlatStyle = FlatStyle.Flat", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Fila NFe Agendamento\"", source, StringComparison.Ordinal);
        Assert.Contains("Coordenação por pasta compartilhada", source, StringComparison.Ordinal);
        Assert.Contains("Lock fiscal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Líder automático", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Candidato em espera", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Heartbeat", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Iniciar Central\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Parar Central\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Abrir sistema\"", source, StringComparison.Ordinal);
        Assert.Contains("Pasta compartilhada", source, StringComparison.Ordinal);
        Assert.Contains("Environment.MachineName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedQueueCentralService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedQueueClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Configurar firewall", source, StringComparison.OrdinalIgnoreCase);
    }
}
