using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class TrayApplicationContextTests
{
    [Fact]
    public void MenuLabels_contains_only_the_supported_tray_actions()
    {
        Assert.Equal(
            ["Abrir Central", "Abrir sistema", "Configurar certificado", "Verificar atualização", "Iniciar com o Windows", "Sair"],
            TrayApplicationContext.MenuLabels);
    }

    [Fact]
    public void Tray_no_longer_exposes_a_network_address_action()
    {
        Assert.DoesNotContain("Copiar endereço da Central", TrayApplicationContext.MenuLabels);
        Assert.DoesNotContain(TrayApplicationContext.MenuLabels, label => label.Contains("rede", StringComparison.OrdinalIgnoreCase));
    }
}
