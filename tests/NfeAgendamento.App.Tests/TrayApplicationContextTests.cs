using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class TrayApplicationContextTests
{
    [Fact]
    public void MenuLabels_contains_only_the_supported_tray_actions()
    {
        Assert.Equal(
            ["Abrir sistema", "Configurar certificado", "Verificar atualização", "Sair"],
            TrayApplicationContext.MenuLabels);
    }
}
