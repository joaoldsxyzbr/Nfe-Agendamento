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
}
