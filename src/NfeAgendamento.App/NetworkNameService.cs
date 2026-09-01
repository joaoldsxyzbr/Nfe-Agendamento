using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NfeAgendamento.App;

public sealed class NetworkNameService : IDisposable
{
    private const string HostName = "nfeagendamento.local";
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private readonly UdpClient _client;
    private readonly IPAddress _address;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;

    private NetworkNameService(IPAddress address)
    {
        _address = address;
        _client = new UdpClient(AddressFamily.InterNetwork);
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
        _client.JoinMulticastGroup(MulticastAddress);
        _worker = Task.Run(RespondAsync);
    }

    public static IPAddress? GetAdvertisedAddress() => CentralNetworkInfo.FindLanIPv4();

    public static NetworkNameService? Start()
    {
        var address = GetAdvertisedAddress();
        if (address is null)
            return null;

        try { return new NetworkNameService(address); }
        catch (SocketException) { return null; }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _client.Dispose();
        try { _worker.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _stop.Dispose();
    }

    private async Task RespondAsync()
    {
        using var registration = _stop.Token.Register(() => _client.Close());
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var packet = await _client.ReceiveAsync(_stop.Token);
                if (!TryBuildResponse(packet.Buffer, out var response))
                    continue;
                await _client.SendAsync(response, response.Length, new IPEndPoint(MulticastAddress, 5353));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (_stop.IsCancellationRequested) { break; }
        }
    }

    private byte[]? BuildResponse(byte[] query)
    {
        if (!TryReadQuestion(query, out var nameEnd, out var queryType))
            return null;
        if (queryType != 1 && queryType != 255)
            return null;

        var response = new List<byte>(64);
        response.AddRange(query.AsSpan(0, 2).ToArray());
        response.AddRange(new byte[] { 0x84, 0x00, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        response.AddRange(query.AsSpan(12, nameEnd - 12).ToArray());
        response.AddRange(new byte[] { 0, 1, 0x80, 1, 0, 0, 0, 120, 0, 4 });
        response.AddRange(_address.GetAddressBytes());
        return response.ToArray();
    }

    private bool TryBuildResponse(byte[] query, out byte[] response)
    {
        response = BuildResponse(query)!;
        return response is not null;
    }

    private static bool TryReadQuestion(byte[] packet, out int nameEnd, out ushort queryType)
    {
        nameEnd = 0;
        queryType = 0;
        if (packet.Length < 17 || (packet[2] & 0x80) != 0)
            return false;
        var offset = 12;
        var labels = new StringBuilder();
        while (offset < packet.Length)
        {
            var length = packet[offset++];
            if (length == 0) break;
            if (length > 63 || offset + length > packet.Length) return false;
            if (labels.Length > 0) labels.Append('.');
            labels.Append(Encoding.ASCII.GetString(packet, offset, length));
            offset += length;
        }
        if (!string.Equals(labels.ToString(), HostName, StringComparison.OrdinalIgnoreCase) || offset + 4 > packet.Length)
            return false;
        nameEnd = offset;
        queryType = (ushort)((packet[offset] << 8) | packet[offset + 1]);
        return true;
    }
}
