using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LanChatServer;

public class ClientHandler
{
    private const int MaxAuthLineLength = 512;
    private const int MaxLineLength = 2048;

    private static readonly TimeSpan TlsTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(30);

    private readonly TcpClient _tcpClient;
    private readonly string _ip;
    private readonly ClientRegistry _registry;
    private readonly X509Certificate2 _certificate;
    private readonly UserStore _users;
    private readonly LoginThrottle _throttle;
    private readonly ConnectionLimiter _limiter;
    private readonly MessageRouter _router;

    public ClientHandler(
        TcpClient tcpClient,
        string ip,
        ClientRegistry registry,
        X509Certificate2 certificate,
        UserStore users,
        LoginThrottle throttle,
        ConnectionLimiter limiter)
    {
        _tcpClient = tcpClient;
        _ip = ip;
        _registry = registry;
        _certificate = certificate;
        _users = users;
        _throttle = throttle;
        _limiter = limiter;
        _router = new MessageRouter(registry);
    }

    public async Task RunAsync()
    {
        string address = _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";

        SslStream? stream = null;
        ClientSession? session = null;
        bool loggedIn = false;

        try
        {
            // 1. Bắt tay TLS: từ đây mọi dữ liệu (kể cả mật khẩu) đều được mã hóa.
            stream = await TlsHelper.AuthenticateServerAsync(
                _tcpClient, _certificate, TlsTimeout);

            var reader = new StreamReader(
                stream, Encoding.UTF8, false, 1024, leaveOpen: true);

            var writer = new StreamWriter(
                stream, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true
            };

            string ipKey = $"ip:{_ip}";

            if (_throttle.IsLocked(ipKey, out int ipWait))
            {
                await writer.WriteLineAsync($"LOGIN_FAIL|TOO_MANY_ATTEMPTS|{ipWait}");
                return;
            }

            // 2. Yêu cầu xác thực: LOGIN|username|password hoặc REGISTER|username|password
            await writer.WriteLineAsync("AUTH_REQUIRED");

            string? authLine;
            using (var authCts = new CancellationTokenSource(AuthTimeout))
            {
                authLine = await reader.ReadLineLimitedAsync(
                    MaxAuthLineLength, authCts.Token);
            }

            if (authLine == null)
                return;

            // Giới hạn 3 phần để mật khẩu được phép chứa ký tự '|'.
            string[] parts = authLine.Split('|', 3);

            bool isRegister = parts.Length == 3 &&
                parts[0].Equals("REGISTER", StringComparison.OrdinalIgnoreCase);
            bool isLogin = parts.Length == 3 &&
                parts[0].Equals("LOGIN", StringComparison.OrdinalIgnoreCase);

            if (!isRegister && !isLogin)
            {
                await writer.WriteLineAsync("LOGIN_FAIL|INVALID_MESSAGE");
                return;
            }

            string requestedName = parts[1].Trim();
            string password = parts[2];

            if (!InputValidator.IsValidUsername(requestedName))
            {
                _throttle.RegisterFailure(ipKey, ServerConfig.MaxFailedAttemptsPerIp);

                // Đăng nhập: không tiết lộ lý do cụ thể.
                await writer.WriteLineAsync(isRegister
                    ? "REGISTER_FAIL|INVALID_USERNAME"
                    : "LOGIN_FAIL|INVALID_CREDENTIALS");
                return;
            }

            string userKey = $"user:{requestedName.ToLowerInvariant()}";

            if (_throttle.IsLocked(userKey, out int userWait))
            {
                await writer.WriteLineAsync($"LOGIN_FAIL|TOO_MANY_ATTEMPTS|{userWait}");
                return;
            }

            string username = requestedName;

            if (isRegister)
            {
                if (!InputValidator.IsValidNewPassword(password, requestedName))
                {
                    await writer.WriteLineAsync("REGISTER_FAIL|WEAK_PASSWORD");
                    return;
                }

                if (_users.TryRegister(requestedName, password) != RegisterResult.Ok)
                {
                    _throttle.RegisterFailure(ipKey, ServerConfig.MaxFailedAttemptsPerIp);
                    await writer.WriteLineAsync("REGISTER_FAIL|USERNAME_EXISTS");
                    return;
                }

                Console.WriteLine($"[REGISTER] {username} from {address}");
            }
            else
            {
                string canonical = string.Empty;

                if (!InputValidator.IsValidLoginPassword(password) ||
                    !_users.Verify(requestedName, password, out canonical))
                {
                    _throttle.RegisterFailure(userKey, ServerConfig.MaxFailedAttemptsPerUser);
                    _throttle.RegisterFailure(ipKey, ServerConfig.MaxFailedAttemptsPerIp);

                    Console.WriteLine($"[LOGIN FAILED] {requestedName} from {address}");
                    await writer.WriteLineAsync("LOGIN_FAIL|INVALID_CREDENTIALS");
                    return;
                }

                _throttle.Reset(userKey);
                username = canonical; // đúng chữ hoa/thường như lúc đăng ký
            }

            session = new ClientSession(_tcpClient, reader, writer, username);

            // 3. Một tài khoản chỉ được đăng nhập ở một nơi.
            if (!_registry.TryAdd(session))
            {
                await writer.WriteLineAsync("LOGIN_FAIL|ALREADY_ONLINE");
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
            int violations = 0;

            while (true)
            {
                string? message = await reader.ReadLineLimitedAsync(MaxLineLength);

                if (message == null)
                {
                    break;
                }

                // Chỉ ghi tên lệnh, không ghi nội dung tin nhắn (riêng tư).
                string command = message.Split('|', 2)[0];
                if (command.Length > 20) command = command[..20];
                Console.WriteLine($"[{username}] {command}");

                if (!session.MessageLimiter.TryAcquire())
                {
                    violations++;

                    if (violations > ServerConfig.MaxRateViolations)
                    {
                        await session.SendAsync("ERROR|RATE_LIMIT_DISCONNECT");
                        break;
                    }

                    await session.SendAsync("ERROR|RATE_LIMITED");
                    continue;
                }

                if (message.Length >
                    ServerConfig.MaxMessageLength + ServerConfig.MaxUsernameLength + 50)
                {
                    await session.SendAsync("ERROR|MESSAGE_TOO_LONG");
                    continue;
                }

                await _router.ProcessAsync(session, message);
            }
        }
        catch (AuthenticationException ex)
        {
            Console.WriteLine($"[TLS ERROR] {address}: {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            Console.WriteLine($"[PROTOCOL ERROR] {address}: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[TIMEOUT] {address}");
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
            stream?.Dispose();
            _tcpClient.Close();
            _limiter.Release(_ip);
        }
    }
}
