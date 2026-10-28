
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Net.NetworkInformation;
using System.Linq;

const int PORT = 5000;

// Lưu các client đã đăng nhập thành công.
// Username không phân biệt chữ hoa/chữ thường.
var clients = new ConcurrentDictionary<string, TcpClient>(
    StringComparer.OrdinalIgnoreCase
);

TcpListener server = new TcpListener(IPAddress.Any, PORT);

try
{
    server.Start();

    const int UDP_DISCOVERY_PORT = 5001;

    // Khởi động UDP discovery song song với TCP server.
    _ = RunUdpDiscoveryAsync(UDP_DISCOVERY_PORT, PORT);

    Console.WriteLine("================================");
    Console.WriteLine("       LAN CHAT TCP SERVER");
    Console.WriteLine("================================");
    Console.WriteLine($"Server is listening on port {PORT}...");
    Console.WriteLine("Waiting for clients...\n");

    while (true)
    {
        TcpClient client = await server.AcceptTcpClientAsync();

        Console.WriteLine(
            $"[CONNECTED] {client.Client.RemoteEndPoint}"
        );

        // Xử lý mỗi client độc lập.
        _ = HandleClientAsync(client, clients);
    }
}
catch (SocketException ex)
{
    Console.WriteLine($"[SERVER ERROR] {ex.Message}");
}
finally
{
    server.Stop();
}

static async Task HandleClientAsync(
    TcpClient client,
    ConcurrentDictionary<string, TcpClient> clients)
{
    string address =
        client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

    string? username = null;

    try
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        using (StreamReader reader = new StreamReader(
            stream, Encoding.UTF8, false, 1024, true))
        using (StreamWriter writer = new StreamWriter(
            stream, new UTF8Encoding(false), 1024, true)
        {
            AutoFlush = true
        })
        {
            // 1. Yêu cầu client gửi username.
            await writer.WriteLineAsync("USERNAME");

            username = await reader.ReadLineAsync();

            // 2. Kiểm tra username hợp lệ.
            if (string.IsNullOrWhiteSpace(username))
            {
                await writer.WriteLineAsync(
                    "LOGIN_FAIL|Username cannot be empty."
                );
                return;
            }

            username = username.Trim();

            if (username.Length > 20 ||
                username.Any(char.IsControl) ||
                username.Contains('|'))
            {
                await writer.WriteLineAsync(
                    "LOGIN_FAIL|Invalid username."
                );
                return;
            }

            // 3. Thêm username vào danh sách online.
            // TryAdd đảm bảo chỉ một client đăng nhập được
            // bằng cùng một username tại một thời điểm.
            if (!clients.TryAdd(username, client))
            {
                await writer.WriteLineAsync(
                    "LOGIN_FAIL|Username already in use."
                );

                Console.WriteLine(
                    $"[LOGIN REJECTED] {username} from {address}"
                );

                return;
            }

            await writer.WriteLineAsync(
                $"LOGIN_OK|{username}"
            );

            Console.WriteLine(
                $"[LOGIN SUCCESS] {username} from {address}"
            );

            Console.WriteLine(
                $"[ONLINE USERS] {clients.Count}"
            );

            // 4. Tiếp tục nhận tin nhắn từ client.
            string? message;

            while ((message = await reader.ReadLineAsync()) != null)
            {
                Console.WriteLine($"[{username}] {message}");

                // Tạm thời chỉ echo lại cho chính client.
                // Chat nhóm và chat riêng sẽ làm ở bước tiếp theo.
                await writer.WriteLineAsync(
                    $"Server received: {message}"
                );
            }
        }
    }
    catch (IOException ex)
    {
        Console.WriteLine($"[IO ERROR] {address}: {ex.Message}");
    }
    catch (SocketException ex)
    {
        Console.WriteLine($"[SOCKET ERROR] {address}: {ex.Message}");
    }

    finally
    {
        // Chỉ xóa nếu username vẫn ánh xạ tới chính client này.
        if (username != null &&
            ((ICollection<KeyValuePair<string, TcpClient>>)clients)
                .Remove(new KeyValuePair<string, TcpClient>(
                    username, client)))
        {
            Console.WriteLine(
                $"[LOGOUT] {username} disconnected."
            );

            Console.WriteLine(
                $"[ONLINE USERS] {clients.Count}"
            );
        }
        else
        {
            Console.WriteLine($"[DISCONNECTED] {address}");
        }
    }

}


static async Task RunUdpDiscoveryAsync(
    int udpPort,
    int tcpPort)
{
    using UdpClient udp = new UdpClient(
        new IPEndPoint(IPAddress.Any, udpPort)
    );

    Console.WriteLine(
        $"UDP discovery is listening on port {udpPort}..."
    );

    while (true)
    {
        UdpReceiveResult received = await udp.ReceiveAsync();

        string request = Encoding.UTF8.GetString(
            received.Buffer
        ).Trim();

        // Chỉ phản hồi đúng yêu cầu discovery.
        if (!request.Equals(
                "DISCOVER_SERVER",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"[UDP] Ignored request from {received.RemoteEndPoint}"
            );
            continue;
        }

        IPAddress clientIp = received.RemoteEndPoint.Address;

        IPAddress serverIp = GetServerIpForClient(clientIp);

        string response =
            $"LAN_CHAT_SERVER|IP={serverIp}|TCP_PORT={tcpPort}";

        byte[] responseBytes = Encoding.UTF8.GetBytes(response);

        await udp.SendAsync(
            responseBytes,
            received.RemoteEndPoint
        );

        Console.WriteLine(
            $"[UDP DISCOVERY] Request from {received.RemoteEndPoint}"
        );

        Console.WriteLine(
            $"[UDP DISCOVERY] Responded with IP={serverIp}, TCP_PORT={tcpPort}"
        );
    }
}

static IPAddress GetServerIpForClient(IPAddress clientIp)
{
    // Nếu client đang test trên cùng máy qua loopback,
    // trả về loopback để client có thể kết nối ngay.
    if (IPAddress.IsLoopback(clientIp))
    {
        return IPAddress.Loopback;
    }

    var candidates = new List<IPAddress>();

    foreach (NetworkInterface network in
             NetworkInterface.GetAllNetworkInterfaces())
    {
        if (network.OperationalStatus != OperationalStatus.Up)
        {
            continue;
        }

        IPInterfaceProperties properties =
            network.GetIPProperties();

        foreach (UnicastIPAddressInformation unicast
                 in properties.UnicastAddresses)
        {
            IPAddress localIp = unicast.Address;

            if (localIp.AddressFamily != AddressFamily.InterNetwork ||
                IPAddress.IsLoopback(localIp))
            {
                continue;
            }

            candidates.Add(localIp);

            IPAddress mask = unicast.IPv4Mask;

            byte[] clientBytes = clientIp.GetAddressBytes();
            byte[] localBytes = localIp.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();

            bool sameSubnet = true;

            for (int i = 0; i < 4; i++)
            {
                if ((clientBytes[i] & maskBytes[i]) !=
                    (localBytes[i] & maskBytes[i]))
                {
                    sameSubnet = false;
                    break;
                }
            }

            if (sameSubnet)
            {
                return localIp;
            }
        }
    }

    // Nếu không tìm được interface cùng subnet,
    // dùng interface IPv4 đang hoạt động đầu tiên.
    return candidates.FirstOrDefault() ?? IPAddress.None;
}
