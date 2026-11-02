using System.IO;
using System.Net.Sockets;
using System.Text;
using SecureLanChatClient.Models;

namespace SecureLanChatClient.Services
{
    public class FileTransferClient
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        public event EventHandler<FileProgressEventArgs>? OnProgress;
        public event EventHandler<MessageReceivedEventArgs>? OnFileReceived;

        public async Task<bool> ConnectAsync(string ip, int filePort, string username, string token)
        {
            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(ip, filePort);
                _stream = _tcpClient.GetStream();

                // 1. Handshake Kênh truyền File
                await WriteLineAsync($"FILE_CHANNEL|{username}|{token}");
                string? response = await ReadLineAsync();

                if (response == "FILE_CHANNEL_OK")
                {
                    // Lắng nghe luồng nhận file ngầm
                    _ = ListenForIncomingFilesAsync();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public async Task SendFileAsync(string recipient, string filePath)
        {
            if (_stream == null || _tcpClient == null || !_tcpClient.Connected) return;

            var fileInfo = new FileInfo(filePath);
            string fileName = fileInfo.Name;
            long fileSize = fileInfo.Length;

            // 1. Gửi Header
            await WriteLineAsync($"SEND|{recipient}|{fileName}|{fileSize}");

            // 2. Chờ Server đồng ý (SEND_OK)
            string? response = await ReadLineAsync();
            if (response != "SEND_OK")
            {
                OnProgress?.Invoke(this, new FileProgressEventArgs { Status = $"Lỗi gửi file: {response}" });
                return;
            }

            // 3. Gửi Dữ Liệu Nhị Phân (Binary Data)
            byte[] buffer = new byte[81920];
            using var fileStream = File.OpenRead(filePath);
            long totalSent = 0;
            int bytesRead;

            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await _stream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalSent += bytesRead;

                OnProgress?.Invoke(this, new FileProgressEventArgs
                {
                    FileName = fileName,
                    BytesTransferred = totalSent,
                    TotalBytes = fileSize,
                    Status = "Sending"
                });
            }
            await _stream.FlushAsync();
        }

        private async Task ListenForIncomingFilesAsync()
        {
            try
            {
                while (_tcpClient != null && _tcpClient.Connected)
                {
                    string? header = await ReadLineAsync();
                    if (header == null) break;

                    var parts = header.Split('|');
                    if (parts.Length == 4 && parts[0] == "RECEIVE")
                    {
                        string sender = parts[1];
                        string fileName = parts[2];
                        long fileSize = long.Parse(parts[3]);

                        // Lưu file vào thư mục Downloads của máy
                        string saveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                        string savePath = Path.Combine(saveFolder, "LAN_" + fileName);

                        byte[] buffer = new byte[81920];
                        long remaining = fileSize;

                        using (var fileStream = File.Create(savePath))
                        {
                            while (remaining > 0)
                            {
                                int read = await _stream!.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
                                if (read == 0) throw new IOException("Bị ngắt kết nối khi đang nhận file.");

                                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                                remaining -= read;

                                OnProgress?.Invoke(this, new FileProgressEventArgs
                                {
                                    FileName = fileName,
                                    BytesTransferred = fileSize - remaining,
                                    TotalBytes = fileSize,
                                    Status = "Receiving"
                                });
                            }
                        }

                        OnFileReceived?.Invoke(this, new MessageReceivedEventArgs
                        {
                            Type = "SYSTEM",
                            Sender = sender,
                            Content = $"Đã nhận file '{fileName}' lưu tại: {savePath}"
                        });
                    }
                }
            }
            catch { }
        }

        private async Task<string?> ReadLineAsync()
        {
            using var ms = new MemoryStream();
            byte[] oneByte = new byte[1];
            while (true)
            {
                int count = await _stream!.ReadAsync(oneByte.AsMemory(0, 1));
                if (count == 0) return ms.Length == 0 ? null : Encoding.UTF8.GetString(ms.ToArray());
                if (oneByte[0] == (byte)'\n') break;
                if (oneByte[0] != (byte)'\r') ms.WriteByte(oneByte[0]);
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private async Task WriteLineAsync(string msg)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(msg + "\n");
            await _stream!.WriteAsync(bytes);
            await _stream.FlushAsync();
        }
    }
}