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
    public void Startup_command_only_launches_the_central_application()
    {
        var command = StartupManager.BuildStartupCommand(@"C:\Apps\NfeAgendamento.App.exe");

        Assert.Equal("\"C:\\Apps\\NfeAgendamento.App.exe\"", command);
        Assert.DoesNotContain("--lan", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_command_quotes_paths_with_spaces()
    {
        var command = StartupManager.BuildStartupCommand(@"C:\NFe Agendamento\NfeAgendamento.App.exe");

        Assert.Equal("\"C:\\NFe Agendamento\\NfeAgendamento.App.exe\"", command);
    }
}
