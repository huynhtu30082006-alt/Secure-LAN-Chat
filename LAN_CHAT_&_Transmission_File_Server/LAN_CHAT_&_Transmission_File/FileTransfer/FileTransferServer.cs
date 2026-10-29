
//using System.Collections.Concurrent;
//using System.Net;
//using System.Net.Sockets;
//using System.Text;

//public sealed class FileTransferServer
//{
//    private const int FILE_PORT = 5002;
//    private const long MAX_FILE_SIZE = 100L * 1024 * 1024; // 100 MB
//    private const int BUFFER_SIZE = 81920;

//    private readonly ConcurrentDictionary<string, ClientSession> _clients;
//    private readonly ConcurrentDictionary<string, FileChannel> _channels =
//        new(StringComparer.OrdinalIgnoreCase);

//    public FileTransferServer(
//        ConcurrentDictionary<string, ClientSession> clients)
//    {
//        _clients = clients;
//    }

//    public async Task RunAsync(CancellationToken cancellationToken = default)
//    {
//        var listener = new TcpListener(IPAddress.Any, FILE_PORT);
//        listener.Start();

//        Console.WriteLine($"[FILE SERVER] Listening on TCP port {FILE_PORT}");

//        try
//        {
//            while (!cancellationToken.IsCancellationRequested)
//            {
//                TcpClient client = await listener.AcceptTcpClientAsync(
//                    cancellationToken);

//                _ = HandleConnectionAsync(client, cancellationToken);
//            }
//        }
//        finally
//        {
//            listener.Stop();
//        }
//    }

//    private async Task HandleConnectionAsync(
//        TcpClient tcpClient,
//        CancellationToken cancellationToken)
//    {
//        string? username = null;
//        FileChannel? channel = null;

//        try
//        {
//            NetworkStream stream = tcpClient.GetStream();

//            // Đọc handshake trước khi cho phép truyền file.
//            string? handshake = await ReadLineAsync(stream, cancellationToken);

//            if (handshake == null)
//                return;

//            string[] auth = handshake.Split('|');

//            // FILE_CHANNEL|username|token
//            if (auth.Length != 3 ||
//                auth[0] != "FILE_CHANNEL" ||
//                !_clients.TryGetValue(auth[1], out ClientSession? session) ||
//                !string.Equals(
//                    session.FileToken,
//                    auth[2],
//                    StringComparison.Ordinal))
//            {
//                await WriteLineAsync(stream, "ERROR|FILE_AUTH_FAILED",
//                    cancellationToken);
//                return;
//            }

//            username = session.Username;
//            channel = new FileChannel(tcpClient, stream, username);

//            if (_channels.TryGetValue(username, out FileChannel? oldChannel))
//                oldChannel.Close();

//            _channels[username] = channel;

//            await WriteLineAsync(stream, "FILE_CHANNEL_OK",
//                cancellationToken);

//            Console.WriteLine($"[FILE CHANNEL] {username} connected.");

//            while (!cancellationToken.IsCancellationRequested)
//            {
//                string? header = await ReadLineAsync(
//                    stream, cancellationToken);

//                if (header == null)
//                    break;

//                string[] parts = header.Split('|');

//                // SEND|recipient|filename|length
//                if (parts.Length != 4 || parts[0] != "SEND")
//                {
//                    await WriteLineAsync(stream, "ERROR|INVALID_FILE_HEADER",
//                        cancellationToken);
//                    continue;
//                }

//                string recipientName = parts[1];
//                string fileName = parts[2];

//                if (!long.TryParse(parts[3], out long fileSize) ||
//                    fileSize < 0 || fileSize > MAX_FILE_SIZE ||
//                    string.IsNullOrWhiteSpace(fileName) ||
//                    fileName.Contains('|') ||
//                    fileName.Any(char.IsControl) ||
//                    Path.GetFileName(fileName) != fileName)
//                {
//                    await WriteLineAsync(stream, "ERROR|INVALID_FILE",
//                        cancellationToken);
//                    return;
//                }

//                if (!_clients.ContainsKey(recipientName) ||
//                    !_channels.TryGetValue(
//                        recipientName, out FileChannel? recipient))
//                {
//                    await WriteLineAsync(stream, "ERROR|USER_NOT_ONLINE",
//                        cancellationToken);

//                    // Không đọc file body khi header không được chấp nhận.
//                    // Client phải đợi phản hồi này trước khi gửi file.
//                    continue;
//                }

//                bool delivered = await ForwardFileAsync(
//                    stream,
//                    channel,
//                    recipient,
//                    fileName,
//                    fileSize,
//                    cancellationToken);

//                if (!delivered)
//                {
//                    await WriteLineAsync(stream, "ERROR|FILE_TRANSFER_FAILED",
//                        cancellationToken);
//                    continue;
//                }

//                await WriteLineAsync(
//                    stream,
//                    $"FILE_SENT|{recipient.Username}|{fileName}",
//                    cancellationToken);

//                Console.WriteLine(
//                    $"[FILE SENT] {username} -> {recipient.Username}: " +
//                    $"{fileName} ({fileSize} bytes)");
//            }
//        }
//        catch (OperationCanceledException)
//        {
//            // Server đang dừng.
//        }
//        catch (IOException ex)
//        {
//            Console.WriteLine($"[FILE IO ERROR] {ex.Message}");
//        }
//        catch (SocketException ex)
//        {
//            Console.WriteLine($"[FILE SOCKET ERROR] {ex.Message}");
//        }
//        catch (ObjectDisposedException)
//        {
//            // Kết nối đã đóng.
//        }
//        finally
//        {
//            if (username != null &&
//                channel != null &&
//                _channels.TryGetValue(username, out FileChannel? current) &&
//                ReferenceEquals(current, channel))
//            {
//                _channels.TryRemove(username, out _);
//            }

//            channel?.Close();

//            if (channel == null)
//                tcpClient.Close();

//            if (username != null)
//                Console.WriteLine($"[FILE CHANNEL CLOSED] {username}");
//        }
//    }

//    private static async Task<bool> ForwardFileAsync(
//        NetworkStream senderStream,
//        FileChannel sender,
//        FileChannel recipient,
//        string fileName,
//        long fileSize,
//        CancellationToken cancellationToken)
//    {
//        // Giữ lock trong suốt header + body để file không bị trộn
//        // với một file khác gửi đồng thời đến cùng người nhận.
//        await recipient.WriteLock.WaitAsync(cancellationToken);

//        try
//        {
//            if (recipient.IsClosed)
//                return false;

//            await WriteLineAsync(
//                recipient.Stream,
//                $"RECEIVE|{sender.Username}|{fileName}|{fileSize}",
//                cancellationToken);

//            byte[] buffer = new byte[BUFFER_SIZE];
//            long remaining = fileSize;

//            while (remaining > 0)
//            {
//                int wanted = (int)Math.Min(buffer.Length, remaining);
//                int read = await senderStream.ReadAsync(
//                    buffer.AsMemory(0, wanted), cancellationToken);

//                if (read == 0)
//                    throw new IOException("Sender disconnected during transfer.");

//                await recipient.Stream.WriteAsync(
//                    buffer.AsMemory(0, read), cancellationToken);

//                remaining -= read;
//            }

//            await recipient.Stream.FlushAsync(cancellationToken);
//            return true;
//        }
//        catch (Exception ex) when (
//            ex is IOException ||
//            ex is SocketException ||
//            ex is ObjectDisposedException)
//        {
//            Console.WriteLine($"[FILE FORWARD ERROR] {ex.Message}");
//            return false;
//        }
//        finally
//        {
//            recipient.WriteLock.Release();
//        }
//    }

//    // Đọc từng byte để tránh StreamReader đọc trước dữ liệu nhị phân
//    // nằm ngay sau header.
//    private static async Task<string?> ReadLineAsync(
//        NetworkStream stream,
//        CancellationToken cancellationToken)
//    {
//        using var buffer = new MemoryStream();
//        byte[] oneByte = new byte[1];

//        while (true)
//        {
//            int count = await stream.ReadAsync(
//                oneByte.AsMemory(0, 1), cancellationToken);

//            if (count == 0)
//            {
//                if (buffer.Length == 0)
//                    return null;

//                throw new IOException("Connection closed inside header.");
//            }

//            if (oneByte[0] == (byte)'\n')
//                break;

//            if (oneByte[0] != (byte)'\r')
//                buffer.WriteByte(oneByte[0]);

//            if (buffer.Length > 2048)
//                throw new IOException("File protocol header too long.");
//        }

//        return Encoding.UTF8.GetString(buffer.ToArray());
//    }

//    private static async Task WriteLineAsync(
//        NetworkStream stream,
//        string message,
//        CancellationToken cancellationToken)
//    {
//        byte[] bytes = Encoding.UTF8.GetBytes(message + "\n");
//        await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
//        await stream.FlushAsync(cancellationToken);
//    }

//    private sealed class FileChannel
//    {
//        public TcpClient TcpClient { get; }
//        public NetworkStream Stream { get; }
//        public string Username { get; }
//        public SemaphoreSlim WriteLock { get; } = new(1, 1);

//        public bool IsClosed => !TcpClient.Connected;

//        public FileChannel(
//            TcpClient tcpClient,
//            NetworkStream stream,
//            string username)
//        {
//            TcpClient = tcpClient;
//            Stream = stream;
//            Username = username;
//        }

//        public void Close()
//        {
//            try { TcpClient.Close(); }
//            catch { /* Ignore close errors. */ }
//        }
//    }
//}


using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

namespace LanChatServer;

public sealed class FileTransferServer
{
    private const long MAX_FILE_SIZE = 100L * 1024 * 1024; // 100 MB
    private const int BUFFER_SIZE = 81920;

    private readonly ClientRegistry _clients;
    private readonly ConcurrentDictionary<string, FileChannel> _channels =
        new(StringComparer.OrdinalIgnoreCase);

    public FileTransferServer(ClientRegistry clients)
    {
        _clients = clients;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var listener = new TcpListener(IPAddress.Any, ServerConfig.FilePort);
        listener.Start();

        Console.WriteLine($"[FILE SERVER] Listening on TCP port {ServerConfig.FilePort}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(
                    cancellationToken);

                _ = HandleConnectionAsync(client, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        string? username = null;
        FileChannel? channel = null;

        try
        {
            NetworkStream stream = tcpClient.GetStream();

            // Đọc handshake trước khi cho phép truyền file.
            string? handshake = await ReadLineAsync(stream, cancellationToken);

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

                // SEND|recipient|filename|length
                if (parts.Length != 4 || parts[0] != "SEND")
                {
                    await channel.SendControlAsync("ERROR|INVALID_FILE_HEADER",
                        cancellationToken);
                    continue;
                }

                string recipientName = parts[1];
                string fileName = parts[2];

                if (!long.TryParse(parts[3], out long fileSize) ||
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

                bool delivered = await ForwardFileAsync(
                    stream,
                    channel,
                    recipient,
                    fileName,
                    fileSize,
                    cancellationToken);

                if (!delivered)
                {
                    await channel.SendControlAsync("ERROR|FILE_TRANSFER_FAILED",
                        cancellationToken);

                    // Body của người gửi có thể còn dang dở trên stream,
                    // nên đóng kênh thay vì đọc tiếp.
                    return;
                }

                await channel.SendControlAsync(
                    $"FILE_SENT|{recipient.Username}|{fileName}",
                    cancellationToken);

                Console.WriteLine(
                    $"[FILE SENT] {username} -> {recipient.Username}: " +
                    $"{fileName} ({fileSize} bytes)");
            }
        }
        catch (OperationCanceledException)
        {
            // Server đang dừng.
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

            if (username != null)
                Console.WriteLine($"[FILE CHANNEL CLOSED] {username}");
        }
    }

    private static async Task<bool> ForwardFileAsync(
        NetworkStream senderStream,
        FileChannel sender,
        FileChannel recipient,
        string fileName,
        long fileSize,
        CancellationToken cancellationToken)
    {
        // Giữ lock trong suốt header + body để file không bị trộn
        // với một file khác gửi đồng thời đến cùng người nhận.
        await recipient.WriteLock.WaitAsync(cancellationToken);

        try
        {
            if (recipient.IsClosed)
                return false;

            await WriteLineAsync(
                recipient.Stream,
                $"RECEIVE|{sender.Username}|{fileName}|{fileSize}",
                cancellationToken);

            byte[] buffer = new byte[BUFFER_SIZE];
            long remaining = fileSize;

            while (remaining > 0)
            {
                int wanted = (int)Math.Min(buffer.Length, remaining);
                int read = await senderStream.ReadAsync(
                    buffer.AsMemory(0, wanted), cancellationToken);

                if (read == 0)
                    throw new IOException("Sender disconnected during transfer.");

                await recipient.Stream.WriteAsync(
                    buffer.AsMemory(0, read), cancellationToken);

                remaining -= read;
            }

            await recipient.Stream.FlushAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is SocketException ||
            ex is ObjectDisposedException)
        {
            Console.WriteLine($"[FILE FORWARD ERROR] {ex.Message}");
            return false;
        }
        finally
        {
            recipient.WriteLock.Release();
        }
    }

    // Đọc từng byte để tránh StreamReader đọc trước dữ liệu nhị phân
    // nằm ngay sau header.
    private static async Task<string?> ReadLineAsync(
        NetworkStream stream,
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
        NetworkStream stream,
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
        public NetworkStream Stream { get; }
        public string Username { get; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);

        public bool IsClosed => !TcpClient.Connected;

        public FileChannel(
            TcpClient tcpClient,
            NetworkStream stream,
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