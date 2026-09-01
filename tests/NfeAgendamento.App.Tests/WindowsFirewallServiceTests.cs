using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class WindowsFirewallServiceTests
{
    [Fact]
    public void Firewall_rule_is_limited_to_private_tcp_port_and_current_program()
    {
        var script = WindowsFirewallService.BuildEnsureRuleScript(@"C:\Apps\NfeAgendamento.App.exe");

        Assert.Contains("-Direction Inbound", script, StringComparison.Ordinal);
        Assert.Contains("-Action Allow", script, StringComparison.Ordinal);
        Assert.Contains("-Protocol TCP", script, StringComparison.Ordinal);
        Assert.Contains("-LocalPort 17345", script, StringComparison.Ordinal);
        Assert.Contains("-Profile Private", script, StringComparison.Ordinal);
        Assert.Contains("-Program $program", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-Profile Any", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Firewall_rule_escapes_powershell_literal_in_program_path()
    {
        var script = WindowsFirewallService.BuildEnsureRuleScript(@"C:\Joao's Apps\NfeAgendamento.App.exe");

        Assert.Contains("C:\\Joao''s Apps\\NfeAgendamento.App.exe", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Firewall_rule_rejects_empty_program_path()
    {
        Assert.Throws<ArgumentException>(() => WindowsFirewallService.BuildEnsureRuleScript(" "));
    }
}
