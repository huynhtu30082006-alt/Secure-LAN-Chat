using System.Net;
using System.Net.Sockets;

namespace LanChatServer;

public class ChatServer
{
    private readonly ClientRegistry _registry = new();

    public async Task RunAsync()
    {
        var listener = new TcpListener(IPAddress.Any, ServerConfig.TcpPort);

        try
        {
            listener.Start();

            _ = new UdpDiscoveryService().RunAsync();
            _ = new FileTransferServer(_registry).RunAsync();

            Console.WriteLine("================================");
            Console.WriteLine("       LAN CHAT TCP SERVER");
            Console.WriteLine("================================");
            Console.WriteLine($"TCP port: {ServerConfig.TcpPort}");
            Console.WriteLine($"UDP discovery port: {ServerConfig.UdpDiscoveryPort}");
            Console.WriteLine("Waiting for clients...\n");

            while (true)
            {
                TcpClient tcpClient = await listener.AcceptTcpClientAsync();

                Console.WriteLine($"[CONNECTED] {tcpClient.Client.RemoteEndPoint}");

                _ = new ClientHandler(tcpClient, _registry).RunAsync();
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