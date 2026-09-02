using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class LocalHostTests
{
    [Fact]
    public void ListenUrl_is_fixed_to_loopback()
    {
        Assert.Equal("http://127.0.0.1:17345", LocalHost.ListenUrl);
        Assert.DoesNotContain("0.0.0.0", LocalHost.ListenUrl);
    }

    [Fact]
    public void Legacy_lan_argument_cannot_expose_the_server()
    {
        Assert.Equal("http://127.0.0.1:17345", LocalHost.GetListenUrl([]));
        Assert.Equal("http://127.0.0.1:17345", LocalHost.GetListenUrl(["--lan"]));
        Assert.Equal("http://127.0.0.1:17345", LocalHost.GetBrowserUrl(["--lan"]));
    }
}
