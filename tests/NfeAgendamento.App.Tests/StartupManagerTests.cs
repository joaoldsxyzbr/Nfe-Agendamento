using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class StartupManagerTests
{
    [Fact]
    public void StartupManager_is_available_for_windows_startup_configuration()
    {
        Assert.NotNull(typeof(StartupManager));
        Assert.True(typeof(StartupManager).IsClass);
    }

    [Fact]
    public void Startup_command_preserves_lan_mode_for_central_pc()
    {
        var command = StartupManager.BuildStartupCommand(@"C:\Apps\NfeAgendamento.App.exe", lanMode: true);

        Assert.Equal("\"C:\\Apps\\NfeAgendamento.App.exe\" --lan", command);
    }
}
