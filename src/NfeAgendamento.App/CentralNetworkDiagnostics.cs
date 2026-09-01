using System.Net;
using System.Net.NetworkInformation;

namespace NfeAgendamento.App;

public enum NetworkHealthStatus
{
    Inactive,
    Ok,
    ActionRequired,
    Error,
    Unknown
}

public enum FirewallRuleStatus
{
    Configured,
    Missing,
    Unavailable
}

public sealed record NetworkDiagnosticSnapshot(
    bool IsReady,
    NetworkHealthStatus NetworkStatus,
    NetworkHealthStatus ListenerStatus,
    NetworkHealthStatus FirewallStatus,
    string AccessUrl,
    string Summary);

public static class CentralNetworkDiagnostics
{
    public static NetworkDiagnosticSnapshot Capture(bool centralEnabled, FirewallRuleStatus firewallStatus)
    {
        var address = CentralNetworkInfo.FindLanIPv4();
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return Evaluate(centralEnabled, address, listeners, firewallStatus);
    }

    public static NetworkDiagnosticSnapshot Evaluate(
        bool centralEnabled,
        IPAddress? lanAddress,
        IEnumerable<IPEndPoint> listeners,
        FirewallRuleStatus firewallStatus)
    {
        ArgumentNullException.ThrowIfNull(listeners);

        if (!centralEnabled)
        {
            return new NetworkDiagnosticSnapshot(
                IsReady: false,
                NetworkStatus: lanAddress is null ? NetworkHealthStatus.Unknown : NetworkHealthStatus.Inactive,
                ListenerStatus: NetworkHealthStatus.Inactive,
                FirewallStatus: NetworkHealthStatus.Inactive,
                AccessUrl: LocalHost.ListenUrl,
                Summary: "Central parada. O acesso pela rede está bloqueado.");
        }

        var networkStatus = lanAddress is null ? NetworkHealthStatus.Error : NetworkHealthStatus.Ok;
        var listeningOnLan = listeners.Any(IsLanListener);
        var listenerStatus = listeningOnLan ? NetworkHealthStatus.Ok : NetworkHealthStatus.Error;
        var firewallHealth = firewallStatus switch
        {
            FirewallRuleStatus.Configured => NetworkHealthStatus.Ok,
            FirewallRuleStatus.Missing => NetworkHealthStatus.ActionRequired,
            _ => NetworkHealthStatus.Unknown
        };

        var accessUrl = lanAddress is null ? LocalHost.ListenUrl : CentralNetworkInfo.BuildAccessUrl(lanAddress);
        var ready = networkStatus == NetworkHealthStatus.Ok
            && listenerStatus == NetworkHealthStatus.Ok
            && firewallHealth == NetworkHealthStatus.Ok;

        var summary = networkStatus == NetworkHealthStatus.Error
            ? "Não foi encontrado um IPv4 de rede utilizável neste PC."
            : listenerStatus == NetworkHealthStatus.Error
                ? $"A porta {LocalHost.Port} não está ouvindo nas interfaces da rede."
                : firewallStatus == FirewallRuleStatus.Missing
                    ? "A rede está pronta, mas o firewall do Windows precisa ser configurado."
                    : firewallStatus == FirewallRuleStatus.Unavailable
                        ? "Rede e porta estão prontas, mas não foi possível verificar o firewall do Windows."
                        : "Rede pronta para acesso dos outros computadores.";

        return new NetworkDiagnosticSnapshot(
            ready,
            networkStatus,
            listenerStatus,
            firewallHealth,
            accessUrl,
            summary);
    }

    private static bool IsLanListener(IPEndPoint endpoint)
    {
        if (endpoint.Port != LocalHost.Port)
            return false;

        if (endpoint.Address.Equals(IPAddress.Any) || endpoint.Address.Equals(IPAddress.IPv6Any))
            return true;

        return !IPAddress.IsLoopback(endpoint.Address);
    }
}
