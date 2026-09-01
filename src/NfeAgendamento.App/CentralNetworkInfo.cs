using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace NfeAgendamento.App;

public sealed record LanAddressCandidate
{
    public LanAddressCandidate(IPAddress address, bool hasGateway, NetworkInterfaceType interfaceType)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        HasGateway = hasGateway;
        InterfaceType = interfaceType;
    }

    public IPAddress Address { get; }
    public bool HasGateway { get; }
    public NetworkInterfaceType InterfaceType { get; }
}

public static class CentralNetworkInfo
{
    public static IPAddress? FindLanIPv4() =>
        SelectPreferredIPv4(GetLanAddressCandidates());

    public static IReadOnlyList<IPAddress> GetLanIPv4Addresses() =>
        GetLanAddressCandidates()
            .Select(candidate => candidate.Address)
            .Distinct()
            .ToArray();

    public static bool IsLocalLanHost(HostString host) =>
        MatchesLanHost(host, GetLanIPv4Addresses());

    public static bool MatchesLanHost(HostString host, IEnumerable<IPAddress> localAddresses)
    {
        ArgumentNullException.ThrowIfNull(localAddresses);

        if (host.Port != LocalHost.Port || !IPAddress.TryParse(host.Host, out var requestedAddress))
            return false;
        if (!IsUsableLanIPv4(requestedAddress))
            return false;

        return localAddresses.Any(address => address.Equals(requestedAddress));
    }

    public static IPAddress? SelectPreferredIPv4(IEnumerable<LanAddressCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => IsUsableLanIPv4(candidate.Address))
            .OrderByDescending(candidate => candidate.HasGateway)
            .ThenByDescending(candidate => IsPrivateIPv4(candidate.Address))
            .ThenByDescending(candidate => IsPhysicalLan(candidate.InterfaceType))
            .Select(candidate => candidate.Address)
            .FirstOrDefault();
    }

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

    private static IReadOnlyList<LanAddressCandidate> GetLanAddressCandidates()
    {
        var candidates = new List<LanAddressCandidate>();

        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up)
                continue;
            if (network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            IPInterfaceProperties properties;
            try
            {
                properties = network.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var hasGateway = properties.GatewayAddresses.Any(gateway => IsUsableGateway(gateway.Address));
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (!IsUsableLanIPv4(unicast.Address))
                    continue;

                candidates.Add(new LanAddressCandidate(unicast.Address, hasGateway, network.NetworkInterfaceType));
            }
        }

        return candidates;
    }

    private static bool IsUsableGateway(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork
        && !address.Equals(IPAddress.Any)
        && !IPAddress.IsLoopback(address);

    private static bool IsUsableLanIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;

        var bytes = address.GetAddressBytes();
        return !(bytes[0] == 169 && bytes[1] == 254)
            && !address.Equals(IPAddress.Any)
            && !address.Equals(IPAddress.Broadcast);
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static bool IsPhysicalLan(NetworkInterfaceType type) =>
        type is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.Wireless80211;
}
