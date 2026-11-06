using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LanChatServer;

public sealed class FileTransferServer
{
    private const long MAX_FILE_SIZE = 100L * 1024 * 1024; // 100 MB
    private const int BUFFER_SIZE = 81920;

    // Nếu một bên im lặng quá lâu giữa chừng khi đang truyền file thì coi như hỏng,
    // tránh việc bên còn lại bị treo vĩnh viễn ở x%.
    private static readonly TimeSpan TransferIoTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    private readonly ClientRegistry _clients;
    private readonly X509Certificate2 _certificate;
    private readonly ConnectionLimiter _limiter;
    private readonly LoginThrottle _throttle;
    private readonly ConcurrentDictionary<string, FileChannel> _channels =
        new(StringComparer.OrdinalIgnoreCase);

    private enum ForwardResult
    {
        Ok,
        RecipientFailed, // người nhận lỗi/hủy, nhưng body của người gửi đã được đọc hết -> kênh người gửi vẫn dùng được
        SenderFailed     // người gửi đứt giữa chừng -> stream người gửi không còn đồng bộ
    }

    public FileTransferServer(
        ClientRegistry clients,
        X509Certificate2 certificate,
        ConnectionLimiter limiter,
        LoginThrottle throttle)
    {
        _clients = clients;
        _certificate = certificate;
        _limiter = limiter;
        _throttle = throttle;
    }

    // SHA-256 dạng hex: đúng 64 ký tự 0-9 a-f.
    private static bool IsValidSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var listener = new TcpListener(IPAddress.Any, ServerConfig.FilePort);
        listener.Start();

        Console.WriteLine($"[FILE SERVER] Listening on TCP port {ServerConfig.FilePort} (TLS)");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(
                    cancellationToken);

                string ip = (client.Client.RemoteEndPoint as IPEndPoint)
                    ?.Address.ToString() ?? "unknown";

                if (!_limiter.TryAcquire(ip))
                {
                    Console.WriteLine($"[FILE REJECTED] Too many connections from {ip}");
                    client.Close();
                    continue;
                }

                _ = HandleConnectionAsync(client, ip, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleConnectionAsync(
        TcpClient tcpClient,
        string ip,
        CancellationToken cancellationToken)
    {
        string? username = null;
        FileChannel? channel = null;
        Stream? stream = null;
        string ipKey = $"ip:{ip}";

        try
        {
            // Bắt tay TLS: header và nội dung file đều được mã hóa.
            stream = await TlsHelper.AuthenticateServerAsync(
                tcpClient, _certificate, HandshakeTimeout, cancellationToken);

            if (_throttle.IsLocked(ipKey, out _))
                return;

            // Đọc handshake (có timeout) trước khi cho phép truyền file.
            string? handshake;
            using (var hsCts = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                hsCts.CancelAfter(HandshakeTimeout);
                handshake = await ReadLineAsync(stream, hsCts.Token);
            }

            if (handshake == null)
                return;

            string[] auth = handshake.Split('|');

            // FILE_CHANNEL|username|token
            if (auth.Length != 3 ||
                auth[0] != "FILE_CHANNEL" ||
                !_clients.TryGet(auth[1], out ClientSession? session) ||
                !string.Equals(
                    session.FileToken,
                    auth[2],
                    StringComparison.Ordinal))
            {
                _throttle.RegisterFailure(ipKey, ServerConfig.MaxFailedAttemptsPerIp);

                await WriteLineAsync(stream, "ERROR|FILE_AUTH_FAILED",
                    cancellationToken);
                return;
            }

            username = session.Username;
            channel = new FileChannel(tcpClient, stream, username);

            if (_channels.TryGetValue(username, out FileChannel? oldChannel))
                oldChannel.Close();

            _channels[username] = channel;

            await channel.SendControlAsync("FILE_CHANNEL_OK", cancellationToken);

            Console.WriteLine($"[FILE CHANNEL] {username} connected.");

            while (!cancellationToken.IsCancellationRequested)
            {
                string? header = await ReadLineAsync(stream, cancellationToken);

                if (header == null)
                    break;

                string[] parts = header.Split('|');

                // SEND|recipient|filename|length|sha256
                if (parts.Length != 5 || parts[0] != "SEND")
                {
                    await channel.SendControlAsync("ERROR|INVALID_FILE_HEADER",
                        cancellationToken);
                    continue;
                }

                if (!channel.SendLimiter.TryAcquire())
                {
                    await channel.SendControlAsync("ERROR|RATE_LIMITED",
                        cancellationToken);
                    continue;
                }

                string recipientName = parts[1];
                string fileName = parts[2];
                string sha256 = parts[4].ToLowerInvariant();

                if (!long.TryParse(parts[3], out long fileSize) ||
                    !IsValidSha256(sha256) ||
                    fileSize <= 0 || fileSize > MAX_FILE_SIZE ||
                    string.IsNullOrWhiteSpace(fileName) ||
                    fileName.Contains('|') ||
                    fileName.Any(char.IsControl) ||
                    Path.GetFileName(fileName) != fileName)
                {
                    // Chưa gửi SEND_OK nên client chưa gửi body, nhưng
                    // để an toàn vẫn đóng kênh.
                    await channel.SendControlAsync("ERROR|INVALID_FILE",
                        cancellationToken);
                    return;
                }

                if (recipientName.Equals(username,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await channel.SendControlAsync("ERROR|CANNOT_SEND_TO_SELF",
                        cancellationToken);
                    continue;
                }

                if (!_clients.TryGet(recipientName, out _) ||
                    !_channels.TryGetValue(
                        recipientName, out FileChannel? recipient))
                {
                    await channel.SendControlAsync("ERROR|USER_NOT_ONLINE",
                        cancellationToken);
                    continue;
                }

                // Header hợp lệ: báo client được phép gửi body.
                await channel.SendControlAsync("SEND_OK", cancellationToken);

                ForwardResult result = await ForwardFileAsync(
                    stream,
                    channel,
                    recipient,
                    fileName,
                    fileSize,
                    sha256,
                    cancellationToken);

                if (result == ForwardResult.SenderFailed)
                {
                    // Body của người gửi dang dở trên stream, không thể đọc
                    // tiếp header kế tiếp -> đóng kênh (finally sẽ dọn).
                    return;
                }

                if (result == ForwardResult.RecipientFailed)
                {
                    // Body đã được đọc bỏ hết nên kênh người gửi vẫn đồng bộ.
                    await channel.SendControlAsync("ERROR|FILE_TRANSFER_FAILED",
                        cancellationToken);
                    continue;
                }

                await channel.SendControlAsync(
                    $"FILE_SENT|{recipient.Username}|{fileName}",
                    cancellationToken);

                Console.WriteLine(
                    $"[FILE SENT] {username} -> {recipient.Username}: " +
                    $"{fileName} ({fileSize} bytes)");
            }
        }
        catch (AuthenticationException ex)
        {
            Console.WriteLine($"[FILE TLS ERROR] {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            // Server đang dừng hoặc quá thời gian bắt tay.
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[FILE IO ERROR] {ex.Message}");
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[FILE SOCKET ERROR] {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            // Kết nối đã đóng.
        }
        finally
        {
            if (username != null &&
                channel != null &&
                _channels.TryGetValue(username, out FileChannel? current) &&
                ReferenceEquals(current, channel))
            {
                _channels.TryRemove(username, out _);
            }

            if (channel != null)
                channel.Close();
            else
                tcpClient.Close();

            stream?.Dispose();
            _limiter.Release(ip);

            if (username != null)
                Console.WriteLine($"[FILE CHANNEL CLOSED] {username}");
        }
    }

    private static bool IsNetworkError(Exception ex) =>
        ex is IOException ||
        ex is SocketException ||
        ex is ObjectDisposedException ||
        ex is OperationCanceledException; // gồm cả timeout

    private static async Task<ForwardResult> ForwardFileAsync(
        Stream senderStream,
        FileChannel sender,
        FileChannel recipient,
        string fileName,
        long fileSize,
        string sha256,
        CancellationToken cancellationToken)
    {
        // Giữ lock trong suốt header + body để file không bị trộn
        // với một file khác gửi đồng thời đến cùng người nhận.
        await recipient.WriteLock.WaitAsync(cancellationToken);

        bool headerSent = false;
        bool recipientAlive = !recipient.IsClosed;

        try
        {
            if (recipientAlive)
            {
                try
                {
                    await WriteWithTimeoutAsync(
                        recipient.Stream,
                        Encoding.UTF8.GetBytes(
                            $"RECEIVE|{sender.Username}|{fileName}|{fileSize}|{sha256}\n"),
                        cancellationToken);

                    headerSent = true;
                }
                catch (Exception ex) when (IsNetworkError(ex))
                {
                    Console.WriteLine($"[FILE FORWARD ERROR] {ex.Message}");
                    recipientAlive = false;
                    recipient.Close();
                }
            }

            byte[] buffer = new byte[BUFFER_SIZE];
            long remaining = fileSize;

            while (remaining > 0)
            {
                int wanted = (int)Math.Min(buffer.Length, remaining);
                int read;

                try
                {
                    using var timeout =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);
                    timeout.CancelAfter(TransferIoTimeout);

                    read = await senderStream.ReadAsync(
                        buffer.AsMemory(0, wanted), timeout.Token);
                }
                catch (Exception ex) when (IsNetworkError(ex))
                {
                    Console.WriteLine(
                        $"[FILE FORWARD ERROR] Sender failed: {ex.Message}");

                    // Người nhận đã nhận header + một phần body: stream của họ
                    // không còn đồng bộ -> đóng để client tự kết nối lại,
                    // thay vì treo mãi ở x%.
                    if (headerSent && recipientAlive)
                        recipient.Close();

                    return ForwardResult.SenderFailed;
                }

                if (read == 0)
                {
                    Console.WriteLine(
                        "[FILE FORWARD ERROR] Sender disconnected during transfer.");

                    if (headerSent && recipientAlive)
                        recipient.Close();

                    return ForwardResult.SenderFailed;
                }

                remaining -= read;

                if (!recipientAlive)
                    continue; // vẫn đọc bỏ hết body để giữ kênh người gửi đồng bộ

                try
                {
                    await WriteWithTimeoutAsync(
                        recipient.Stream,
                        buffer.AsMemory(0, read),
                        cancellationToken);
                }
                catch (Exception ex) when (IsNetworkError(ex))
                {
                    Console.WriteLine($"[FILE FORWARD ERROR] Recipient failed: {ex.Message}");
                    recipientAlive = false;
                    recipient.Close();
                }
            }

            if (!recipientAlive)
                return ForwardResult.RecipientFailed;

            try
            {
                await recipient.Stream.FlushAsync(cancellationToken);
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                recipient.Close();
                return ForwardResult.RecipientFailed;
            }

            return ForwardResult.Ok;
        }
        finally
        {
            recipient.WriteLock.Release();
        }
    }

    private static async Task WriteWithTimeoutAsync(
        Stream stream,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TransferIoTimeout);

        await stream.WriteAsync(data, timeout.Token);
    }

    // Đọc từng byte để tránh StreamReader đọc trước dữ liệu nhị phân
    // nằm ngay sau header.
    private static async Task<string?> ReadLineAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] oneByte = new byte[1];

        while (true)
        {
            int count = await stream.ReadAsync(
                oneByte.AsMemory(0, 1), cancellationToken);

            if (count == 0)
            {
                if (buffer.Length == 0)
                    return null;

                throw new IOException("Connection closed inside header.");
            }

            if (oneByte[0] == (byte)'\n')
                break;

            if (oneByte[0] != (byte)'\r')
                buffer.WriteByte(oneByte[0]);

            if (buffer.Length > 2048)
                throw new IOException("File protocol header too long.");
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task WriteLineAsync(
        Stream stream,
        string message,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message + "\n");
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private sealed class FileChannel
    {
        public TcpClient TcpClient { get; }
        public Stream Stream { get; }
        public string Username { get; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);

        // Giới hạn số lần gửi file của người dùng này.
        public SlidingWindowRateLimiter SendLimiter { get; } =
            new(ServerConfig.MaxFilesPerMinute, TimeSpan.FromMinutes(1));

        public bool IsClosed => !TcpClient.Connected;

        public FileChannel(
            TcpClient tcpClient,
            Stream stream,
            string username)
        {
            TcpClient = tcpClient;
            Stream = stream;
            Username = username;
        }

        // Gửi dòng điều khiển (SEND_OK, FILE_SENT, ERROR...) có lock,
        // để không chen vào giữa một file đang được chuyển tới user này.
        public async Task SendControlAsync(
            string message,
            CancellationToken cancellationToken)
        {
            await WriteLock.WaitAsync(cancellationToken);

            try
            {
                await WriteLineAsync(Stream, message, cancellationToken);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        public void Close()
        {
            try { TcpClient.Close(); }
            catch { /* Ignore close errors. */ }
        }
    }
}
