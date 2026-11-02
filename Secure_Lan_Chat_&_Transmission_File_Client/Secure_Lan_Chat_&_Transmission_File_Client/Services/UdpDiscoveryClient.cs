using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SecureLanChatClient.Services
{
    public class UdpDiscoveryClient
    {
        private const int UDP_PORT = 5001;

        public static async Task<(string IP, int Port)?> DiscoverServerAsync(int timeoutMs = 3000)
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;

            byte[] requestBytes = Encoding.UTF8.GetBytes("DISCOVER_SERVER");
            var endPoint = new IPEndPoint(IPAddress.Broadcast, UDP_PORT);

            await udp.SendAsync(requestBytes, requestBytes.Length, endPoint);

            var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                var result = await udp.ReceiveAsync(cts.Token);
                string response = Encoding.UTF8.GetString(result.Buffer).Trim();

                // Dạng phản hồi: LAN_CHAT_SERVER|IP=192.168.1.5|TCP_PORT=5000
                var parts = response.Split('|');
                if (parts.Length >= 3 && parts[0] == "LAN_CHAT_SERVER")
                {
                    string ip = parts[1].Replace("IP=", "");
                    int port = int.Parse(parts[2].Replace("TCP_PORT=", ""));
                    return (ip, port);
                }
            }
            catch (OperationCanceledException) { } // Hết thời gian chờ mà không thấy Server
            catch (Exception) { }

            return null;
        }
    }
}