using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class CentralAppTests
{
    [Fact]
    public void Missing_settings_default_to_client()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nfe-central-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "central.json");
        try
        {
            Assert.False(new CentralSettingsStore(path).Load().ConfiguredAsCentral);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_existing_settings_fail_closed_as_client()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nfe-central-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "central.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "{ arquivo corrompido");
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
        var root = Path.Combine(Path.GetTempPath(), $"nfe-central-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "central.json");
        try
        {
            var store = new CentralSettingsStore(path);
            store.Save(new CentralSettings(true));
            Assert.True(new CentralSettingsStore(path).Load().ConfiguredAsCentral);

            store.Save(new CentralSettings(false));
            Assert.False(new CentralSettingsStore(path).Load().ConfiguredAsCentral);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--lan")]
    public void Server_always_listens_only_on_loopback(string? argument)
    {
        var args = argument is null ? null : new[] { argument };
        Assert.Equal("http://127.0.0.1:17345", LocalHost.GetListenUrl(args));
        Assert.Equal("http://127.0.0.1:17345", LocalHost.GetBrowserUrl(args));
    }
}
