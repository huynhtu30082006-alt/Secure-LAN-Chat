using System.IO;
using System.Net.Sockets;
using System.Text;
using Secure_Lan_Chat___Transmission_File_Client.Models;

namespace Secure_Lan_Chat___Transmission_File_Client.Services
{
    public class TcpChatClient
    {
        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;

        public event EventHandler<MessageReceivedEventArgs>? OnMessageReceived;
        public event EventHandler<List<string>>? OnOnlineListUpdated;

        public FileTransferClient FileClient { get; } = new FileTransferClient();
        public string Username { get; private set; } = string.Empty;

        public async Task<string> ConnectAndLoginAsync(string ip, int port, string username)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);
                NetworkStream stream = _client.GetStream();

                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

                // 1. Chờ dòng "USERNAME" từ Server
                string? greeting = await _reader.ReadLineAsync();
                if (greeting != "USERNAME") return "Lỗi giao thức Server.";

                // 2. Gửi lệnh Đăng nhập
                await _writer.WriteLineAsync($"LOGIN|{username}");

                // 3. Đọc kết quả Đăng nhập
                string? response = await _reader.ReadLineAsync();
                if (response != null && response.StartsWith("LOGIN_OK"))
                {
                    Username = username;

                    // 4. Đọc dòng thông tin kênh truyền file
                    string? fileChannelInfo = await _reader.ReadLineAsync();
                    if (fileChannelInfo != null && fileChannelInfo.StartsWith("FILE_CHANNEL"))
                    {
                        var parts = fileChannelInfo.Split('|');
                        int filePort = int.Parse(parts[1]);
                        string token = parts[2];

                        // Kết nối song song Kênh Truyền File
                        await FileClient.ConnectAsync(ip, filePort, username, token);
                    }

                    // Bắt đầu vòng lặp đọc tin nhắn ngầm
                    _ = ListenLoopAsync();
                    return "SUCCESS";
                }
                else
                {
                    return response ?? "Đăng nhập thất bại.";
                }
            }
            catch (Exception ex)
            {
                return $"Lỗi kết nối: {ex.Message}";
            }
        }

        private async Task ListenLoopAsync()
        {
            try
            {
                while (_client != null && _client.Connected)
                {
                    string? line = await _reader!.ReadLineAsync();
                    if (line == null) break;

                    ProcessIncomingLine(line);
                }
            }
            catch { }
        }

        private void ProcessIncomingLine(string line)
        {
            var parts = line.Split('|');
            string command = parts[0];

            if (command == "ONLINE_LIST" && parts.Length > 1)
            {
                var users = parts[1].Split(',').ToList();
                OnOnlineListUpdated?.Invoke(this, users);
            }
            else if (command == "PUBLIC" && parts.Length >= 3)
            {
                OnMessageReceived?.Invoke(this, new MessageReceivedEventArgs
                {
                    Type = "PUBLIC",
                    Sender = parts[1],
                    Content = parts[2]
                });
            }
            else if (command == "PRIVATE" && parts.Length >= 3)
            {
                OnMessageReceived?.Invoke(this, new MessageReceivedEventArgs
                {
                    Type = "PRIVATE",
                    Sender = parts[1],
                    Content = parts[2]
                });
            }
        }

        public async Task SendPublicMessageAsync(string message)
        {
            if (_writer != null) await _writer.WriteLineAsync($"MSG_ALL|{message}");
        }

        public async Task SendPrivateMessageAsync(string recipient, string message)
        {
            if (_writer != null) await _writer.WriteLineAsync($"MSG_PRIV|{recipient}|{message}");
        }
    }
}