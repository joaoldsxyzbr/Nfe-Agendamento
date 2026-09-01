using System.Net;
using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class CentralAppTests
{
    [Fact]
    public void Missing_settings_default_to_central_enabled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nfe-central-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "central.json");

        try
        {
            var store = new CentralSettingsStore(path);

            var settings = store.Load();

            Assert.True(settings.Enabled);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Central_enabled_state_is_persisted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nfe-central-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "central.json");

        try
        {
            var store = new CentralSettingsStore(path);
            store.Save(new CentralSettings(false));

            Assert.False(new CentralSettingsStore(path).Load().Enabled);

            store.Save(new CentralSettings(true));
            Assert.True(new CentralSettingsStore(path).Load().Enabled);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Access_url_uses_lan_ipv4_and_fixed_port()
    {
        var url = CentralNetworkInfo.BuildAccessUrl(IPAddress.Parse("10.0.0.29"));

        Assert.Equal("http://10.0.0.29:17345", url);
    }
}
