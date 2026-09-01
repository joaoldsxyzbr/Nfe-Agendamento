using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class TrayUpdaterIntegrationTests
{
    private static string TraySource() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TrayApplicationContext.cs"));

    [Fact]
    public void Update_action_installs_verified_package_and_restarts_app()
    {
        var source = TraySource();

        Assert.Contains("PrepareUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("LaunchPreparedUpdate", source, StringComparison.Ordinal);
        Assert.Contains("Environment.ProcessId", source, StringComparison.Ordinal);
        Assert.Contains("ExitApplication();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Abra o repositório para baixar", source, StringComparison.Ordinal);
    }
}
