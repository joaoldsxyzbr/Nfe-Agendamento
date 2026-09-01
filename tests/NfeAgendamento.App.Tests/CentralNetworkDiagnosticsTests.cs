using System.Net;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.Http;
using NfeAgendamento.App;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class CentralNetworkDiagnosticsTests
{
    [Fact]
    public void Preferred_address_prioritizes_private_interface_with_gateway()
    {
        var candidates = new[]
        {
            new LanAddressCandidate(IPAddress.Parse("172.30.0.1"), hasGateway: false, NetworkInterfaceType.Ethernet),
            new LanAddressCandidate(IPAddress.Parse("10.0.0.29"), hasGateway: true, NetworkInterfaceType.Ethernet),
            new LanAddressCandidate(IPAddress.Parse("192.168.56.1"), hasGateway: false, NetworkInterfaceType.Ethernet)
        };

        var selected = CentralNetworkInfo.SelectPreferredIPv4(candidates);

        Assert.Equal(IPAddress.Parse("10.0.0.29"), selected);
    }

    [Fact]
    public void Mdns_uses_the_same_preferred_address_as_the_central_panel()
    {
        Assert.Equal(CentralNetworkInfo.FindLanIPv4(), NetworkNameService.GetAdvertisedAddress());
    }

    [Fact]
    public void Lan_host_matches_only_an_actual_local_ipv4_on_the_expected_port()
    {
        var addresses = new[]
        {
            IPAddress.Parse("10.0.0.29"),
            IPAddress.Parse("192.168.10.8")
        };

        Assert.True(CentralNetworkInfo.MatchesLanHost(new HostString("10.0.0.29", LocalHost.Port), addresses));
        Assert.False(CentralNetworkInfo.MatchesLanHost(new HostString("10.0.0.30", LocalHost.Port), addresses));
        Assert.False(CentralNetworkInfo.MatchesLanHost(new HostString("evil.example", LocalHost.Port), addresses));
        Assert.False(CentralNetworkInfo.MatchesLanHost(new HostString("10.0.0.29", 9999), addresses));
    }

    [Fact]
    public void Diagnostic_is_ready_when_lan_listener_and_private_firewall_rule_are_ok()
    {
        var snapshot = CentralNetworkDiagnostics.Evaluate(
            centralEnabled: true,
            lanAddress: IPAddress.Parse("10.0.0.29"),
            listeners:
            [
                new IPEndPoint(IPAddress.Any, LocalHost.Port),
                new IPEndPoint(IPAddress.Loopback, LocalHost.Port)
            ],
            firewallStatus: FirewallRuleStatus.Configured);

        Assert.True(snapshot.IsReady);
        Assert.Equal(NetworkHealthStatus.Ok, snapshot.NetworkStatus);
        Assert.Equal(NetworkHealthStatus.Ok, snapshot.ListenerStatus);
        Assert.Equal(NetworkHealthStatus.Ok, snapshot.FirewallStatus);
        Assert.Equal("http://10.0.0.29:17345", snapshot.AccessUrl);
    }

    [Fact]
    public void Diagnostic_reports_firewall_configuration_when_rule_is_missing()
    {
        var snapshot = CentralNetworkDiagnostics.Evaluate(
            centralEnabled: true,
            lanAddress: IPAddress.Parse("10.0.0.29"),
            listeners: [new IPEndPoint(IPAddress.Any, LocalHost.Port)],
            firewallStatus: FirewallRuleStatus.Missing);

        Assert.False(snapshot.IsReady);
        Assert.Equal(NetworkHealthStatus.ActionRequired, snapshot.FirewallStatus);
        Assert.Contains("firewall", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diagnostic_detects_listener_restricted_to_loopback()
    {
        var snapshot = CentralNetworkDiagnostics.Evaluate(
            centralEnabled: true,
            lanAddress: IPAddress.Parse("10.0.0.29"),
            listeners: [new IPEndPoint(IPAddress.Loopback, LocalHost.Port)],
            firewallStatus: FirewallRuleStatus.Configured);

        Assert.False(snapshot.IsReady);
        Assert.Equal(NetworkHealthStatus.Error, snapshot.ListenerStatus);
    }

    [Fact]
    public void Firewall_script_is_restricted_to_private_profile_tcp_port_and_current_program()
    {
        var script = WindowsFirewallService.BuildEnsureRuleScript(@"C:\NfeAgendamento\NfeAgendamento.App.exe");

        Assert.Contains("LocalPort 17345", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Protocol TCP", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Profile Private", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"C:\NfeAgendamento\NfeAgendamento.App.exe", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Profile Any", script, StringComparison.OrdinalIgnoreCase);
    }
}
