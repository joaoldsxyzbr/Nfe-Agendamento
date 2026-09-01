using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NfeAgendamento.App;

public static class CentralNetworkInfo
{
    public static IPAddress? FindLanIPv4() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Where(network => network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .FirstOrDefault(IsUsableLanIPv4);

    public static string BuildAccessUrl(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("O endereço precisa ser IPv4.", nameof(address));

        return $"http://{address}:{LocalHost.Port}";
    }

    public static string BuildAccessUrl(bool enabled, string address)
    {
        if (!enabled)
            return LocalHost.ListenUrl;

        if (!IPAddress.TryParse(address, out var parsed))
            return LocalHost.ListenUrl;

        return BuildAccessUrl(parsed);
    }

    public static string GetAccessUrl(bool enabled)
    {
        if (!enabled)
            return LocalHost.ListenUrl;

        var address = FindLanIPv4();
        return address is null ? LocalHost.ListenUrl : BuildAccessUrl(address);
    }

    private static bool IsUsableLanIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;

        var bytes = address.GetAddressBytes();
        return !(bytes[0] == 169 && bytes[1] == 254);
    }
}
