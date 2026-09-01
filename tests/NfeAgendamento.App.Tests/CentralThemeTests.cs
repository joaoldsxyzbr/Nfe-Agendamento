using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class CentralThemeTests
{
    [Fact]
    public void Theme_uses_prado_blue_and_yellow_identity()
    {
        Assert.Equal(Color.FromArgb(13, 61, 112), CentralTheme.BrandBlue);
        Assert.Equal(Color.FromArgb(246, 201, 21), CentralTheme.BrandYellow);
    }

    [Fact]
    public void Theme_keeps_a_simple_light_surface_for_windows_admin_panel()
    {
        Assert.Equal(Color.FromArgb(246, 248, 251), CentralTheme.Background);
        Assert.Equal(Color.FromArgb(255, 255, 255), CentralTheme.Surface);
        Assert.Equal(Color.FromArgb(25, 37, 52), CentralTheme.Text);
    }
}
