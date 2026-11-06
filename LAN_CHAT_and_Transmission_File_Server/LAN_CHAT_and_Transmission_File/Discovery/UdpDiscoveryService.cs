using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LanChatServer;

public class UdpDiscoveryService
{
    public async Task RunAsync()
    {
        try
        {
            using var udp = new UdpClient(
                new IPEndPoint(IPAddress.Any, ServerConfig.UdpDiscoveryPort)
            );

            Console.WriteLine(
                $"UDP discovery listening on port {ServerConfig.UdpDiscoveryPort}..."
            );

            while (true)
            {
                UdpReceiveResult received = await udp.ReceiveAsync();

                string request = Encoding.UTF8.GetString(received.Buffer).Trim();

                if (!request.Equals("DISCOVER_SERVER",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(
                        $"[UDP] Ignored request from {received.RemoteEndPoint}"
                    );
                    continue;
                }

                IPAddress clientIp = received.RemoteEndPoint.Address;
                IPAddress serverIp = NetworkHelper.GetServerIpForClient(clientIp);

                if (serverIp.Equals(IPAddress.None))
                {
                    Console.WriteLine(
                        $"[UDP] No suitable IPv4 address for {clientIp}"
                    );
                    continue;
                }

                string response =
                    $"LAN_CHAT_SERVER|IP={serverIp}|TCP_PORT={ServerConfig.TcpPort}";

                byte[] bytes = Encoding.UTF8.GetBytes(response);
                await udp.SendAsync(bytes, received.RemoteEndPoint);

                Console.WriteLine(
                    $"[UDP DISCOVERY] {received.RemoteEndPoint} -> {response}"
                );
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[UDP ERROR] {ex.Message}");
        }
    }
}