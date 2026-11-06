using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace LanChatServer;

public class ChatServer
{
    private readonly ClientRegistry _registry = new();
    private readonly LoginThrottle _throttle = new();
    private readonly ConnectionLimiter _chatLimiter = new(ServerConfig.MaxConnectionsPerIp);
    private readonly ConnectionLimiter _fileLimiter = new(ServerConfig.MaxConnectionsPerIp);
    private readonly X509Certificate2 _certificate;
    private readonly UserStore _users;

    public ChatServer()
    {
        // Chứng chỉ TLS và cơ sở dữ liệu tài khoản nằm trong thư mục "data" cạnh file .exe.
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDirectory);

        _certificate = CertificateProvider.LoadOrCreate(dataDirectory);
        _users = new UserStore(Path.Combine(dataDirectory, "users.json"));
    }

    public async Task RunAsync()
    {
        var listener = new TcpListener(IPAddress.Any, ServerConfig.TcpPort);

        try
        {
            listener.Start();

            _ = new UdpDiscoveryService().RunAsync();
            _ = new FileTransferServer(
                _registry, _certificate, _fileLimiter, _throttle).RunAsync();

            Console.WriteLine("================================");
            Console.WriteLine("   SECURE LAN CHAT TCP SERVER");
            Console.WriteLine("================================");
            Console.WriteLine($"TCP port: {ServerConfig.TcpPort} (TLS)");
            Console.WriteLine($"UDP discovery port: {ServerConfig.UdpDiscoveryPort}");
            Console.WriteLine($"Registered accounts: {_users.Count}");
            Console.WriteLine("Certificate SHA-256 fingerprint (compare with the client's first-connect dialog):");
            Console.WriteLine(CertificateProvider.Fingerprint(_certificate));
            Console.WriteLine("Waiting for clients...\n");

            while (true)
            {
                TcpClient tcpClient = await listener.AcceptTcpClientAsync();

                string ip = (tcpClient.Client.RemoteEndPoint as IPEndPoint)
                    ?.Address.ToString() ?? "unknown";

                if (!_chatLimiter.TryAcquire(ip))
                {
                    Console.WriteLine($"[REJECTED] Too many connections from {ip}");
                    tcpClient.Close();
                    continue;
                }

                Console.WriteLine($"[CONNECTED] {tcpClient.Client.RemoteEndPoint}");

                _ = new ClientHandler(
                    tcpClient, ip, _registry, _certificate,
                    _users, _throttle, _chatLimiter).RunAsync();
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[SERVER SOCKET ERROR] {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("[SERVER] Listener stopped.");
        }
        finally
        {
            listener.Stop();

            foreach (ClientSession session in _registry.Snapshot())
            {
                session.TcpClient.Close();
            }

            Console.WriteLine("[SERVER] Stopped.");
        }
    }
}
