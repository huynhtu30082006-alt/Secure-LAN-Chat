using System.Net.Sockets;
using System.Text;

namespace LanChatServer;

public class ClientHandler
{
    private readonly TcpClient _tcpClient;
    private readonly ClientRegistry _registry;
    private readonly MessageRouter _router;

    public ClientHandler(TcpClient tcpClient, ClientRegistry registry)
    {
        _tcpClient = tcpClient;
        _registry = registry;
        _router = new MessageRouter(registry);
    }

    public async Task RunAsync()
    {
        string address = _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";

        ClientSession? session = null;
        bool loggedIn = false;

        try
        {
            NetworkStream stream = _tcpClient.GetStream();

            var reader = new StreamReader(
                stream, Encoding.UTF8, false, 1024, leaveOpen: true);

            var writer = new StreamWriter(
                stream, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true
            };

            // 1. Yêu cầu đăng nhập.
            await writer.WriteLineAsync("USERNAME");

            string? loginMessage = await reader.ReadLineAsync();

            if (loginMessage == null)
            {
                return;
            }

            string[] loginParts = loginMessage.Split('|');

            if (loginParts.Length != 2 ||
                !loginParts[0].Equals("LOGIN", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("LOGIN_FAIL|INVALID_MESSAGE");
                return;
            }

            string username = loginParts[1].Trim();

            // 2. Kiểm tra username.
            if (!InputValidator.IsValidUsername(username))
            {
                await writer.WriteLineAsync("LOGIN_FAIL|INVALID_USERNAME");
                return;
            }

            session = new ClientSession(_tcpClient, reader, writer, username);

            // 3. Không cho hai client trùng username.
            if (!_registry.TryAdd(session))
            {
                await writer.WriteLineAsync("LOGIN_FAIL|USERNAME_TAKEN");
                return;
            }

            loggedIn = true;

            await session.SendAsync($"LOGIN_OK|{username}");
            await session.SendAsync(
                $"FILE_CHANNEL|{ServerConfig.FilePort}|{session.FileToken}");

            Console.WriteLine($"[LOGIN SUCCESS] {username} from {address}");
            Console.WriteLine($"[ONLINE USERS] {_registry.Count}");

            await _registry.BroadcastOnlineListAsync();

            // 4. Nhận và xử lý message liên tục.
            while (true)
            {
                string? message = await reader.ReadLineAsync();

                if (message == null)
                {
                    break;
                }

                Console.WriteLine($"[{username}] {message}");

                if (message.Length >
                    ServerConfig.MaxMessageLength + ServerConfig.MaxUsernameLength + 50)
                {
                    await session.SendAsync("ERROR|MESSAGE_TOO_LONG");
                    continue;
                }

                await _router.ProcessAsync(session, message);
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
        catch (ObjectDisposedException)
        {
            // Client hoặc stream đã được đóng.
        }
        finally
        {
            if (loggedIn && session != null)
            {
                if (_registry.Remove(session))
                {
                    Console.WriteLine($"[LOGOUT] {session.Username} disconnected.");
                    Console.WriteLine($"[ONLINE USERS] {_registry.Count}");

                    await _registry.BroadcastOnlineListAsync();
                }
            }
            else
            {
                Console.WriteLine($"[DISCONNECTED] {address}");
            }

            session?.Dispose();
            _tcpClient.Close();
        }
    }
}