using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class CentralModeTests
{
    [Fact]
    public void Central_settings_default_to_client_when_file_does_not_exist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "central.json");
        var store = new CentralSettingsStore(path);

        Assert.False(store.Load().ConfiguredAsCentral);
    }

    [Fact]
    public void Legacy_enabled_flag_is_not_migrated_to_configured_central()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "central.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "{\"Enabled\":true}");

            Assert.False(new CentralSettingsStore(path).Load().ConfiguredAsCentral);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Central_role_is_persisted()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "central.json");
        try
        {
            var store = new CentralSettingsStore(path);
            store.Save(new CentralSettings(true));

            Assert.True(new CentralSettingsStore(path).Load().ConfiguredAsCentral);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, "10.0.0.29", "http://10.0.0.29:17345")]
    [InlineData(false, "10.0.0.29", "http://127.0.0.1:17345")]
    public void Central_access_url_reflects_current_state(bool enabled, string address, string expected)
    {
        Assert.Equal(expected, CentralNetworkInfo.BuildAccessUrl(enabled, address));
    }

    [Fact]
    public void Central_window_exposes_expected_primary_actions()
    {
        Assert.Equal(
            ["Iniciar Central", "Parar Central", "Abrir sistema"],
            CentralForm.PrimaryActionLabels);
    }
}
