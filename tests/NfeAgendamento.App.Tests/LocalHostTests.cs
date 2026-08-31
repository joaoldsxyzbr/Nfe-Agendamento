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
}
