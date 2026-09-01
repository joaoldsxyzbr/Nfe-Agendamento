using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class TrayApplicationContextTests
{
    [Fact]
    public void MenuLabels_contains_only_the_supported_tray_actions()
    {
        Assert.Equal(
            ["Abrir Central", "Abrir sistema", "Copiar endereço da Central", "Configurar certificado", "Verificar atualização", "Iniciar com o Windows", "Sair"],
            TrayApplicationContext.MenuLabels);
    }

    [Fact]
    public void Access_menu_text_shows_shareable_url_when_central_is_enabled()
    {
        var text = TrayApplicationContext.BuildAccessMenuText(
            enabled: true,
            accessUrl: "http://10.0.0.29:17345");

        Assert.Equal("Acesso: http://10.0.0.29:17345", text);
    }

    [Fact]
    public void Access_menu_text_is_actionable_when_central_is_disabled()
    {
        var text = TrayApplicationContext.BuildAccessMenuText(
            enabled: false,
            accessUrl: LocalHost.ListenUrl);

        Assert.Equal("Acesso pela rede: desativado", text);
    }
}
