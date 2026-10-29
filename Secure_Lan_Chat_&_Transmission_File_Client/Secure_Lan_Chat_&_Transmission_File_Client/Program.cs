using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

// ============================================================
// LAN CHAT TEST CLIENT
// Dùng: chạy không tham số để tự tìm server bằng UDP,
// hoặc: LanChatClient.exe 192.168.1.10
// ============================================================

string host;
int tcpPort = 5000;

if (args.Length > 0)
{
    host = args[0];
}
else
{
    Console.WriteLine("Searching for server (UDP discovery)...");
    var found = await ChatClient.DiscoverServerAsync();

    if (found != null)
    {
        host = found.Value.Ip;
        tcpPort = found.Value.Port;
        Console.WriteLine($"Found server at {host}:{tcpPort}");
    }
    else
    {
        Console.Write("Discovery failed. Enter server IP (Enter = 127.0.0.1): ");
        string? typed = Console.ReadLine()?.Trim();
        host = string.IsNullOrEmpty(typed) ? "127.0.0.1" : typed;
    }
}

while (true)
{
    var client = new ChatClient(host, tcpPort);
    bool retryLogin = await client.RunAsync();

    if (!retryLogin)
    {
        break;
    }
}


// ============================================================
// CONSOLE OUTPUT (an toàn khi nhiều luồng cùng in)
// ============================================================

static class ConsoleUi
{
    private static readonly object Sync = new();

    public static void Print(string text)
    {
        lock (Sync)
        {
            Console.WriteLine($"\r{text}");
            Console.Write("> ");
        }
    }
}


// ============================================================
// CHAT CLIENT (kênh TCP 5000)
// ============================================================

sealed class ChatClient
{
    private const int DiscoveryPort = 5001;
    private const int MaxUsernameLength = 20;
    private const int MaxMessageLength = 1000;

    private readonly string _host;
    private readonly int _tcpPort;

    private StreamReader _reader = null!;
    private StreamWriter _writer = null!;
    private string _username = "";
    private volatile bool _exiting;

    private readonly TaskCompletionSource<FileChannelClient?> _fileReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ChatClient(string host, int tcpPort)
    {
        _host = host;
        _tcpPort = tcpPort;
    }

    // Trả về true nếu đăng nhập thất bại và muốn thử lại.
    public async Task<bool> RunAsync()
    {
        Console.Write("Username: ");
        string name = (Console.ReadLine() ?? "").Trim();

        if (name.Length == 0 ||
            name.Length > MaxUsernameLength ||
            name.Any(c => char.IsControl(c) || c == '|' || c == ','))
        {
            Console.WriteLine(
                $"Invalid username (max {MaxUsernameLength} chars, no '|' or ',').");
            return true;
        }

        using var tcp = new TcpClient();

        try
        {
            await tcp.ConnectAsync(_host, _tcpPort);
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Cannot connect to {_host}:{_tcpPort}: {ex.Message}");
            return false;
        }

        NetworkStream stream = tcp.GetStream();

        _reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true
        };

        // 1. Server gửi "USERNAME", client trả "LOGIN|name".
        string? prompt = await _reader.ReadLineAsync();

        if (prompt != "USERNAME")
        {
            Console.WriteLine($"Unexpected server greeting: {prompt ?? "(none)"}");
            return false;
        }

        await _writer.WriteLineAsync($"LOGIN|{name}");

        string? response = await _reader.ReadLineAsync();

        if (response == null)
        {
            Console.WriteLine("Server closed the connection.");
            return false;
        }

        string[] loginParts = response.Split('|');

        if (loginParts[0] != "LOGIN_OK")
        {
            string reason = loginParts.Length > 1 ? loginParts[1] : response;
            Console.WriteLine($"Login failed: {reason}");
            return reason == "USERNAME_TAKEN" || reason == "INVALID_USERNAME";
        }

        _username = name;
        Console.WriteLine($"Logged in as {_username}.");
        PrintHelp();

        _ = Task.Run(ReceiveLoopAsync);

        await InputLoopAsync();

        _exiting = true;
        return false;
    }

    // --------------------------------------------------------
    // Nhận message từ server
    // --------------------------------------------------------

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (true)
            {
                string? line = await _reader.ReadLineAsync();

                if (line == null)
                {
                    break;
                }

                HandleServerMessage(line);
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }

        if (!_exiting)
        {
            ConsoleUi.Print("Disconnected from server.");
            Environment.Exit(0);
        }
    }

    private void HandleServerMessage(string line)
    {
        string[] p = line.Split('|');

        switch (p[0])
        {
            case "CHAT" when p.Length == 3:
                ConsoleUi.Print($"[{p[1]}] {p[2]}");
                break;

            case "PRIVATE" when p.Length == 3:
                ConsoleUi.Print($"[PRIVATE from {p[1]}] {p[2]}");
                break;

            case "PRIVATE_SENT" when p.Length == 2:
                ConsoleUi.Print($"(private message delivered to {p[1]})");
                break;

            case "ONLINE_LIST":
                ConsoleUi.Print($"[ONLINE USERS] {(p.Length > 1 ? p[1] : "")}");
                break;

            case "FILE_CHANNEL" when p.Length == 3 && int.TryParse(p[1], out int port):
                StartFileChannel(port, p[2]);
                break;

            case "ERROR":
                ConsoleUi.Print($"[SERVER ERROR] {(p.Length > 1 ? p[1] : "")}");
                break;

            default:
                ConsoleUi.Print($"[SERVER] {line}");
                break;
        }
    }

    private void StartFileChannel(int port, string token)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                string saveDir = Path.Combine(
                    AppContext.BaseDirectory, $"File_Received_{_username}");

                FileChannelClient? channel = await FileChannelClient.ConnectAsync(
                    _host, port, _username, token, saveDir);

                _fileReady.TrySetResult(channel);

                ConsoleUi.Print(channel != null
                    ? $"[FILE] Channel ready. Received files go to: {saveDir}"
                    : "[FILE] File channel authentication failed.");
            }
            catch (Exception ex)
            {
                _fileReady.TrySetResult(null);
                ConsoleUi.Print($"[FILE] Cannot open file channel: {ex.Message}");
            }
        });
    }

    // --------------------------------------------------------
    // Nhập lệnh từ bàn phím
    // --------------------------------------------------------

    private async Task InputLoopAsync()
    {
        while (true)
        {
            Console.Write("> ");
            string? input = Console.ReadLine();

            if (input == null)
            {
                return;
            }

            input = input.Trim();

            if (input.Length == 0)
            {
                continue;
            }

            int space = input.IndexOf(' ');
            string command = (space < 0 ? input : input[..space]).ToLowerInvariant();
            string rest = space < 0 ? "" : input[(space + 1)..].Trim();

            switch (command)
            {
                case "/exit":
                    return;

                case "/help":
                    PrintHelp();
                    break;

                case "/online":
                    await _writer.WriteLineAsync("ONLINE_LIST");
                    break;

                case "/all":
                    {
                        string? error = ValidateContent(rest);

                        if (error != null)
                        {
                            Console.WriteLine(error);
                            break;
                        }

                        await _writer.WriteLineAsync($"CHAT|{rest}");
                        break;
                    }

                case "/to":
                    {
                        string[] parts = rest.Split(' ', 2,
                            StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length != 2)
                        {
                            Console.WriteLine("Usage: /to <user> <message>");
                            break;
                        }

                        string content = parts[1].Trim();
                        string? error = ValidateContent(content);

                        if (error != null)
                        {
                            Console.WriteLine(error);
                            break;
                        }

                        await _writer.WriteLineAsync($"PRIVATE|{parts[0]}|{content}");
                        break;
                    }

                case "/send":
                    HandleSendCommand(rest);
                    break;

                default:
                    Console.WriteLine(
                        "Unknown input. Use /all, /to, /online, /send, /help or /exit.");
                    break;
            }
        }
    }

    private void HandleSendCommand(string rest)
    {
        int space = rest.IndexOf(' ');

        if (space < 0)
        {
            Console.WriteLine("Usage: /send <user> \"<file path>\"");
            return;
        }

        string recipient = rest[..space];
        string path = rest[(space + 1)..].Trim().Trim('"');

        // Chạy nền để vẫn gõ chat được trong lúc đang gửi file.
        _ = Task.Run(async () =>
        {
            try
            {
                FileChannelClient? channel = await _fileReady.Task;

                if (channel == null)
                {
                    ConsoleUi.Print("[FILE] File channel is not available.");
                    return;
                }

                ConsoleUi.Print($"[FILE] Sending '{Path.GetFileName(path)}' to {recipient}...");
                await channel.SendFileAsync(recipient, path);
                ConsoleUi.Print($"[FILE] Sent '{Path.GetFileName(path)}' to {recipient}.");
            }
            catch (Exception ex)
            {
                ConsoleUi.Print($"[FILE] Send failed: {ex.Message}");
            }
        });
    }

    private static string? ValidateContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Message is empty.";
        }

        if (content.Length > MaxMessageLength)
        {
            return $"Message too long (max {MaxMessageLength} chars).";
        }

        if (content.Any(c => c == '|' || char.IsControl(c)))
        {
            return "Message cannot contain the '|' character.";
        }

        return null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  /all <message>            send to everyone");
        Console.WriteLine("  /to <user> <message>      private message");
        Console.WriteLine("  /online                   list online users");
        Console.WriteLine("  /send <user> \"<path>\"     send a file");
        Console.WriteLine("  /help                     show this help");
        Console.WriteLine("  /exit                     quit");
    }

    // --------------------------------------------------------
    // UDP discovery
    // --------------------------------------------------------

    public static async Task<(string Ip, int Port)?> DiscoverServerAsync()
    {
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };

            byte[] request = Encoding.UTF8.GetBytes("DISCOVER_SERVER");
            await udp.SendAsync(request, request.Length,
                new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            UdpReceiveResult result = await udp.ReceiveAsync(cts.Token);

            // LAN_CHAT_SERVER|IP=x.x.x.x|TCP_PORT=5000
            string[] p = Encoding.UTF8.GetString(result.Buffer).Trim().Split('|');

            if (p.Length == 3 &&
                p[0] == "LAN_CHAT_SERVER" &&
                p[1].StartsWith("IP=") &&
                p[2].StartsWith("TCP_PORT=") &&
                int.TryParse(p[2]["TCP_PORT=".Length..], out int port))
            {
                return (p[1]["IP=".Length..], port);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }

        return null;
    }
}


// ============================================================
// FILE CHANNEL CLIENT (kênh TCP 5002, kết nối liên tục)
//
// Một vòng lặp đọc duy nhất:
//   RECEIVE|sender|file|size  -> đọc 'size' byte và lưu file
//   SEND_OK / FILE_SENT / ERROR -> chuyển cho hàm đang gửi file
// ============================================================

sealed class FileChannelClient
{
    private const long MaxFileSize = 100L * 1024 * 1024;
    private const int BufferSize = 81920;

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly string _saveDir;

    private readonly Channel<string> _responses = Channel.CreateUnbounded<string>();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private FileChannelClient(TcpClient tcp, NetworkStream stream, string saveDir)
    {
        _tcp = tcp;
        _stream = stream;
        _saveDir = saveDir;
    }

    public static async Task<FileChannelClient?> ConnectAsync(
        string host, int port, string username, string token, string saveDir)
    {
        var tcp = new TcpClient();

        try
        {
            await tcp.ConnectAsync(host, port);
            NetworkStream stream = tcp.GetStream();

            // Handshake: FILE_CHANNEL|username|token
            await WriteLineAsync(stream, $"FILE_CHANNEL|{username}|{token}");

            string? response = await ReadLineAsync(stream);

            if (response != "FILE_CHANNEL_OK")
            {
                tcp.Close();
                return null;
            }

            var channel = new FileChannelClient(tcp, stream, saveDir);
            _ = channel.ReceiveLoopAsync();
            return channel;
        }
        catch
        {
            tcp.Close();
            throw;
        }
    }

    // --------------------------------------------------------
    // Vòng lặp đọc duy nhất của kênh
    // --------------------------------------------------------

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (true)
            {
                string? line = await ReadLineAsync(_stream);

                if (line == null)
                {
                    break;
                }

                string[] p = line.Split('|');

                if (p[0] == "RECEIVE" && p.Length == 4 &&
                    long.TryParse(p[3], out long size))
                {
                    await ReceiveFileAsync(p[1], p[2], size);
                }
                else
                {
                    _responses.Writer.TryWrite(line);
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }

        _responses.Writer.TryComplete();
        ConsoleUi.Print("[FILE] File channel closed.");
    }

    private async Task ReceiveFileAsync(string sender, string fileName, long size)
    {
        string safeName = Path.GetFileName(fileName);

        Directory.CreateDirectory(_saveDir);
        string target = GetUniquePath(Path.Combine(_saveDir, safeName));

        ConsoleUi.Print($"[FILE] Receiving '{safeName}' ({size} bytes) from {sender}...");

        bool completed = false;

        try
        {
            using (FileStream fs = File.Create(target))
            {
                byte[] buffer = new byte[BufferSize];
                long remaining = size;

                while (remaining > 0)
                {
                    int wanted = (int)Math.Min(buffer.Length, remaining);
                    int read = await _stream.ReadAsync(buffer.AsMemory(0, wanted));

                    if (read == 0)
                    {
                        throw new IOException("Connection lost during transfer.");
                    }

                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    remaining -= read;
                }
            }

            completed = true;
        }
        finally
        {
            if (!completed && File.Exists(target))
            {
                File.Delete(target);
            }
        }

        ConsoleUi.Print($"[FILE] Saved from {sender}: {target}");
    }

    // --------------------------------------------------------
    // Gửi file: SEND -> đợi SEND_OK -> gửi body -> đợi FILE_SENT
    // --------------------------------------------------------

    public async Task SendFileAsync(string recipient, string path)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
        {
            throw new FileNotFoundException($"File not found: {path}");
        }

        if (info.Length == 0 || info.Length > MaxFileSize)
        {
            throw new IOException("File size must be between 1 byte and 100 MB.");
        }

        if (info.Name.Contains('|'))
        {
            throw new IOException("File name cannot contain '|'.");
        }

        await _sendLock.WaitAsync();

        try
        {
            // Bỏ các phản hồi cũ còn sót lại.
            while (_responses.Reader.TryRead(out _)) { }

            await WriteLineAsync(_stream, $"SEND|{recipient}|{info.Name}|{info.Length}");

            string response = await ReadResponseAsync(TimeSpan.FromSeconds(10));

            if (response != "SEND_OK")
            {
                throw new IOException($"Server refused: {response}");
            }

            try
            {
                using FileStream fs = File.OpenRead(path);
                byte[] buffer = new byte[BufferSize];
                long remaining = info.Length;

                while (remaining > 0)
                {
                    int wanted = (int)Math.Min(buffer.Length, remaining);
                    int read = await fs.ReadAsync(buffer.AsMemory(0, wanted));

                    if (read == 0)
                    {
                        throw new IOException("File changed while sending.");
                    }

                    await _stream.WriteAsync(buffer.AsMemory(0, read));
                    remaining -= read;
                }

                await _stream.FlushAsync();
            }
            catch
            {
                // Body dang dở thì stream không còn đồng bộ, phải đóng kênh.
                _tcp.Close();
                throw;
            }

            string done = await ReadResponseAsync(TimeSpan.FromSeconds(120));

            if (!done.StartsWith("FILE_SENT"))
            {
                throw new IOException($"Transfer failed: {done}");
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<string> ReadResponseAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            return await _responses.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("No response from server.");
        }
        catch (ChannelClosedException)
        {
            throw new IOException("File channel closed.");
        }
    }

    // --------------------------------------------------------
    // Helpers
    // --------------------------------------------------------

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);

        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static async Task WriteLineAsync(NetworkStream stream, string line)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(line + "\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    // Đọc từng byte để không "ăn" nhầm dữ liệu file nằm ngay sau header.
    private static async Task<string?> ReadLineAsync(NetworkStream stream)
    {
        using var buffer = new MemoryStream();
        byte[] one = new byte[1];

        while (true)
        {
            int n = await stream.ReadAsync(one.AsMemory(0, 1));

            if (n == 0)
            {
                if (buffer.Length == 0)
                {
                    return null;
                }

                throw new IOException("Connection closed inside header.");
            }

            if (one[0] == (byte)'\n')
            {
                break;
            }

            if (one[0] != (byte)'\r')
            {
                buffer.WriteByte(one[0]);
            }

            if (buffer.Length > 2048)
            {
                throw new IOException("Header too long.");
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}