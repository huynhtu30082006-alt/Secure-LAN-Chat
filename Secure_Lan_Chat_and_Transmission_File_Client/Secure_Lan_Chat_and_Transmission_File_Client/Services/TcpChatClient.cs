using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Secure_Lan_Chat___Transmission_File_Client.Models;

namespace Secure_Lan_Chat___Transmission_File_Client.Services
{
    public class TcpChatClient
    {
        private readonly object _stateLock = new object();
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        private TcpClient? _client;
        private SslStream? _ssl;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private bool _active;

        public event EventHandler<MessageReceivedEventArgs>? OnMessageReceived;
        public event EventHandler<List<string>>? OnOnlineListUpdated;

        // Server đóng kết nối / mạng đứt (KHÔNG bắn khi người dùng chủ động Disconnect).
        public event EventHandler<string>? OnDisconnected;

        public FileTransferClient FileClient { get; } = new FileTransferClient();
        public string Username { get; private set; } = string.Empty;
        public bool IsConnected => _active;

        public async Task<string> ConnectAndLoginAsync(
            string ip, int port, string username, string password, bool register)
        {
            CloseConnection(); // dọn phiên cũ (nếu có)

            TcpClient? client = null;
            SslStream? ssl = null;
            try
            {
                client = new TcpClient();
                using (var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await client.ConnectAsync(ip, port, connectCts.Token);
                }

                // Bắt tay TLS. Lần đầu có thể phải chờ người dùng xác nhận chứng chỉ nên cho 60 giây.
                using (var tlsCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    ssl = await SecureConnection.AuthenticateAsync(client, ip, tlsCts.Token);
                }

                var reader = new StreamReader(ssl, new UTF8Encoding(false));
                var writer = new StreamWriter(ssl, new UTF8Encoding(false)) { AutoFlush = true };

                using var handshakeCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                // 1. Server yêu cầu xác thực (hoặc từ chối ngay nếu IP đang bị khóa tạm)
                string? greeting = await reader.ReadLineAsync(handshakeCts.Token);
                if (greeting != "AUTH_REQUIRED")
                {
                    ssl.Dispose();
                    client.Close();
                    return DescribeLoginFailure(greeting);
                }

                // 2. Gửi đăng nhập / đăng ký (đã được mã hóa bởi TLS)
                string command = register ? "REGISTER" : "LOGIN";
                await writer.WriteLineAsync($"{command}|{username}|{password}");

                // 3. Đọc kết quả. Thành công: LOGIN_OK|tên-đã-đăng-ký
                string? response = await reader.ReadLineAsync(handshakeCts.Token);
                if (response == null || !response.StartsWith("LOGIN_OK|"))
                {
                    ssl.Dispose();
                    client.Close();
                    return DescribeLoginFailure(response);
                }

                string canonicalName = response.Split('|', 2)[1];

                // 4. Đọc thông tin kênh truyền file
                string? fileChannelInfo = await reader.ReadLineAsync(handshakeCts.Token);

                lock (_stateLock)
                {
                    _client = client;
                    _ssl = ssl;
                    _reader = reader;
                    _writer = writer;
                    _cts = new CancellationTokenSource();
                    _active = true;
                }

                Username = canonicalName;

                if (fileChannelInfo != null && fileChannelInfo.StartsWith("FILE_CHANNEL"))
                {
                    var parts = fileChannelInfo.Split('|');
                    if (parts.Length == 3 && int.TryParse(parts[1], out int filePort))
                    {
                        // Kết nối song song kênh truyền file (cũng qua TLS).
                        // Nếu thất bại, FileClient.IsConnected = false và UI sẽ báo.
                        await FileClient.ConnectAsync(ip, filePort, canonicalName, parts[2]);
                    }
                }

                // Bắt đầu vòng lặp đọc tin nhắn ngầm
                _ = ListenLoopAsync(reader, _cts!.Token);
                return "SUCCESS";
            }
            catch (OperationCanceledException)
            {
                string? tlsError = SecureConnection.TakeLastError();
                ssl?.Dispose();
                client?.Close();
                return tlsError ?? "Connection timed out.";
            }
            catch (Exception ex)
            {
                // Lỗi chứng chỉ (bị đổi, không tin tưởng...) có thông báo riêng, rõ hơn lỗi chung của TLS.
                string? tlsError = SecureConnection.TakeLastError();
                ssl?.Dispose();
                client?.Close();
                CloseConnection();

                if (tlsError != null) return tlsError;
                if (ex is AuthenticationException) return $"TLS error: {ex.Message}";
                return $"Connection error: {ex.Message}";
            }
        }

        private static string DescribeLoginFailure(string? response)
        {
            if (response == null) return "Server closed the connection.";

            string[] parts = response.Split('|');

            // LOGIN_FAIL|TOO_MANY_ATTEMPTS|<giây>
            if (parts.Length == 3 && parts[1] == "TOO_MANY_ATTEMPTS")
                return $"Too many failed attempts. Please try again in {parts[2]} seconds.";

            return response switch
            {
                "LOGIN_FAIL|INVALID_CREDENTIALS" => "Wrong username or password.",
                "LOGIN_FAIL|ALREADY_ONLINE" => "This account is already logged in on another client.",
                "LOGIN_FAIL|INVALID_MESSAGE" => "Server rejected the login request.",
                "REGISTER_FAIL|USERNAME_EXISTS" => "This username is already registered. Use Login instead.",
                "REGISTER_FAIL|INVALID_USERNAME" => "Invalid username (max 20 characters, no '|' or ',').",
                "REGISTER_FAIL|WEAK_PASSWORD" =>
                    "Password rejected: use 8-64 characters, and do not use your username as the password.",
                _ => response
            };
        }

        private async Task ListenLoopAsync(StreamReader reader, CancellationToken token)
        {
            string reason = "The server closed the connection.";

            try
            {
                while (!token.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(token);
                    if (line == null) break;

                    ProcessIncomingLine(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ex) { reason = ex.Message; }
            catch (ObjectDisposedException) { }

            // Chỉ báo "mất kết nối" nếu không phải do người dùng chủ động ngắt.
            if (CloseConnection())
            {
                OnDisconnected?.Invoke(this, reason);
            }
        }

        private void ProcessIncomingLine(string line)
        {
            // Giới hạn 3 phần để nội dung tin nhắn không bị cắt nếu có ký tự '|'.
            string[] parts = line.Split('|', 3);
            string command = parts[0];

            switch (command)
            {
                case "ONLINE_LIST":
                    {
                        var users = parts.Length > 1
                            ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                            : new List<string>();
                        OnOnlineListUpdated?.Invoke(this, users);
                        break;
                    }

                // Server gửi: CHAT|sender|content
                case "CHAT" when parts.Length == 3:
                    OnMessageReceived?.Invoke(this, new MessageReceivedEventArgs
                    {
                        Type = "PUBLIC",
                        Sender = parts[1],
                        Content = parts[2]
                    });
                    break;

                // Server gửi: PRIVATE|sender|content
                case "PRIVATE" when parts.Length == 3:
                    OnMessageReceived?.Invoke(this, new MessageReceivedEventArgs
                    {
                        Type = "PRIVATE",
                        Sender = parts[1],
                        Content = parts[2]
                    });
                    break;

                case "PRIVATE_SENT":
                    break; // xác nhận đã chuyển tin riêng, không cần hiển thị

                case "ERROR":
                    OnMessageReceived?.Invoke(this, new MessageReceivedEventArgs
                    {
                        Type = "ERROR",
                        Sender = "Server",
                        Content = DescribeServerError(parts.Length > 1 ? parts[1] : string.Empty)
                    });
                    break;
            }
        }

        private static string DescribeServerError(string code) => code switch
        {
            "INVALID_MESSAGE" => "Message rejected (empty, too long, or contains '|').",
            "MESSAGE_TOO_LONG" => "Message is too long.",
            "USER_NOT_ONLINE" => "That user is not online.",
            "CANNOT_MESSAGE_SELF" => "You cannot send a private message to yourself.",
            "UNKNOWN_COMMAND" => "The server did not understand the command.",
            "RATE_LIMITED" => "You are sending messages too fast. Please slow down.",
            "RATE_LIMIT_DISCONNECT" => "You were disconnected for flooding the server.",
            _ => $"Server error: {code}"
        };

        // Giao thức server: CHAT|content
        public Task SendPublicMessageAsync(string message) =>
            SendLineAsync($"CHAT|{message}");

        // Giao thức server: PRIVATE|recipient|content
        public Task SendPrivateMessageAsync(string recipient, string message) =>
            SendLineAsync($"PRIVATE|{recipient}|{message}");

        private async Task SendLineAsync(string line)
        {
            StreamWriter? writer = _writer;
            if (!_active || writer == null)
                throw new InvalidOperationException("Not connected to the server.");

            await _writeLock.WaitAsync();
            try
            {
                await writer.WriteLineAsync(line);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // Ngắt kết nối "nhẹ nhàng": đóng kênh file, shutdown socket rồi mới đóng.
        public void Disconnect() => CloseConnection();

        // Trả về true nếu thực sự vừa đóng một phiên đang hoạt động.
        private bool CloseConnection()
        {
            lock (_stateLock)
            {
                if (!_active) return false;
                _active = false;
            }

            try { _cts?.Cancel(); } catch { }

            FileClient.Disconnect();

            try { _ssl?.Dispose(); } catch { } // gửi TLS close_notify rồi đóng
            try { _client?.Close(); } catch { }

            return true;
        }
    }
}
