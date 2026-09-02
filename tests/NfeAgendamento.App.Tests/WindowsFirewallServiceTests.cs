using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class WindowsFirewallServiceTests
{
    [Fact]
    public void Firewall_rule_is_stable_and_limited_to_corporate_lan()
    {
        var script = WindowsFirewallService.BuildEnsureRuleScript();

        Assert.Contains("-Direction Inbound", script, StringComparison.Ordinal);
        Assert.Contains("-Action Allow", script, StringComparison.Ordinal);
        Assert.Contains("-Protocol TCP", script, StringComparison.Ordinal);
        Assert.Contains("-LocalPort 17345", script, StringComparison.Ordinal);
        Assert.Contains("-Profile Domain,Private", script, StringComparison.Ordinal);
        Assert.Contains("-RemoteAddress LocalSubnet", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-Program", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-Profile Any", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-Profile Public", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Firewall_status_check_requires_same_restricted_rule()
    {
        var script = WindowsFirewallService.BuildCheckRuleScript();

        Assert.Contains("Get-NetFirewallPortFilter", script, StringComparison.Ordinal);
        Assert.Contains("Get-NetFirewallAddressFilter", script, StringComparison.Ordinal);
        Assert.Contains("$remote[0] -ieq 'LocalSubnet'", script, StringComparison.Ordinal);
        Assert.Contains("([int]$_.Profile -band 3) -eq 3", script, StringComparison.Ordinal);
        Assert.Contains("([int]$_.Profile -band 4) -eq 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-NetFirewallApplicationFilter", script, StringComparison.OrdinalIgnoreCase);
    }
}
