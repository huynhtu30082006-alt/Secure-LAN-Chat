using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Secure_Lan_Chat___Transmission_File_Client.Models;

namespace Secure_Lan_Chat___Transmission_File_Client.Services
{
    /// <summary>
    /// Kênh truyền file. QUAN TRỌNG: chỉ có DUY NHẤT MỘT vòng lặp đọc trên stream
    /// (RunReaderLoopAsync). Mọi phản hồi của server cho lần gửi (SEND_OK, FILE_SENT, ERROR)
    /// được vòng lặp này đẩy vào hàng đợi _responses để SendFileAsync lấy ra.
    /// (Bản cũ có 2 nơi cùng đọc một stream nên "SEND_OK" bị đọc lẫn/mất -> gửi bị treo.)
    /// </summary>
    public class FileTransferClient
    {
        private const long MaxFileSize = 100L * 1024 * 1024; // phải khớp MAX_FILE_SIZE của server
        private const int BufferSize = 81920;

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SendOkTimeout = TimeSpan.FromSeconds(60); // có thể phải chờ file khác tới cùng người nhận
        private static readonly TimeSpan SentConfirmTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(30);

        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly Channel<string> _responses = Channel.CreateUnbounded<string>();

        private TcpClient? _tcp;
        private Stream? _stream;
        private CancellationTokenSource _lifetime = new CancellationTokenSource();

        private string _ip = string.Empty;
        private int _port;
        private string _username = string.Empty;
        private string _token = string.Empty;

        private volatile bool _channelReady;
        private volatile bool _cancelRequested;
        private volatile bool _quietReconnect; // đã báo lỗi/hủy rồi, đừng ghi đè bằng thông báo kết nối lại

        public event EventHandler<FileProgressEventArgs>? OnProgress;
        public event EventHandler<MessageReceivedEventArgs>? OnFileReceived;

        public bool IsConnected => _channelReady;

        /// <summary>Thư mục lưu file nhận được (ClientData\&lt;user&gt;\ReceivedFiles). Null = dùng thư mục Downloads.</summary>
        public string? ReceiveFolder { get; set; }

        // ---------------------------------------------------------------- kết nối

        public async Task<bool> ConnectAsync(string ip, int filePort, string username, string token)
        {
            _ip = ip;
            _port = filePort;
            _username = username;
            _token = token;

            CancellationTokenSource old = _lifetime;
            _lifetime = new CancellationTokenSource();
            try { old.Cancel(); } catch { } // không Dispose: các tác vụ cũ có thể còn dùng token này

            if (!await OpenChannelAsync(_lifetime.Token))
                return false;

            _ = RunReaderLoopAsync(_lifetime.Token);
            return true;
        }

        private async Task<bool> OpenChannelAsync(CancellationToken token)
        {
            TcpClient? tcp = null;
            try
            {
                tcp = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(ConnectTimeout);

                await tcp.ConnectAsync(_ip, _port, cts.Token);
                Stream stream = await SecureConnection.AuthenticateAsync(tcp, _ip, cts.Token); // TLS, chứng chỉ đã ghim

                await WriteLineAsync(stream, $"FILE_CHANNEL|{_username}|{_token}", cts.Token);
                string? response = await ReadLineAsync(stream, cts.Token);

                if (response != "FILE_CHANNEL_OK")
                {
                    tcp.Close();
                    return false;
                }

                while (_responses.Reader.TryRead(out _)) { } // bỏ phản hồi cũ của kênh trước

                _tcp = tcp;
                _stream = stream;
                _channelReady = true;
                return true;
            }
            catch
            {
                tcp?.Close();
                return false;
            }
        }

        // Đóng kênh hiện tại (không hủy vòng đời) -> vòng lặp nền sẽ tự kết nối lại.
        private void CloseChannel()
        {
            _channelReady = false;
            TcpClient? tcp = _tcp;
            if (tcp == null) return;

            try { tcp.Client.Shutdown(SocketShutdown.Both); } catch { }
            try { tcp.Close(); } catch { }
        }

        /// <summary>Ngắt hẳn kênh file (khi Disconnect hoặc đóng ứng dụng).</summary>
        public void Disconnect()
        {
            try { _lifetime.Cancel(); } catch { }
            CloseChannel();
        }

        /// <summary>
        /// Hủy lần truyền đang chạy (gửi hoặc nhận). Vì giao thức truyền theo độ dài cố định,
        /// cách sạch nhất là đóng kênh; server sẽ dọn phía bên kia, client tự kết nối lại.
        /// </summary>
        public void CancelCurrentTransfer()
        {
            _cancelRequested = true;
            _quietReconnect = true;
            CloseChannel();
        }

        // ---------------------------------------------------------------- vòng lặp đọc duy nhất

        private async Task RunReaderLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Stream? stream = _stream;
                if (stream != null)
                    await ReadSessionAsync(stream, token);

                _channelReady = false;
                if (token.IsCancellationRequested) return;

                bool quiet = _quietReconnect;
                _quietReconnect = false;

                if (!quiet)
                    Report(string.Empty, 0, 0, "Error", "File channel lost. Reconnecting...", false);

                bool reconnected = false;
                for (int attempt = 0; attempt < 5 && !token.IsCancellationRequested; attempt++)
                {
                    CloseChannel();
                    try { await Task.Delay(attempt == 0 ? 300 : 1500, token); }
                    catch (OperationCanceledException) { return; }

                    if (await OpenChannelAsync(token))
                    {
                        reconnected = true;
                        break;
                    }
                }

                if (!reconnected)
                {
                    if (!token.IsCancellationRequested)
                        Report(string.Empty, 0, 0, "Error",
                            "File channel unavailable. Disconnect and connect again to send files.", false);
                    return;
                }

                _cancelRequested = false;

                if (!quiet)
                    Report(string.Empty, 0, 0, "Ready", "File channel ready.", false);
            }
        }

        private async Task ReadSessionAsync(Stream stream, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    string? line = await ReadLineAsync(stream, token);
                    if (line == null) break;

                    if (line.StartsWith("RECEIVE|"))
                    {
                        await ReceiveFileAsync(stream, line, token);
                    }
                    else if (line == "SEND_OK" ||
                             line.StartsWith("FILE_SENT|") ||
                             line.StartsWith("ERROR|"))
                    {
                        _responses.Writer.TryWrite(line);
                    }
                }
            }
            catch (Exception)
            {
                // Mọi lỗi (mạng, đóng kênh, không ghi được file...) đều kết thúc phiên;
                // vòng lặp nền sẽ kết nối lại. Không để exception thoát ra task nền.
            }
            finally
            {
                // Đánh thức SendFileAsync đang chờ phản hồi (nếu có).
                _responses.Writer.TryWrite("ERROR|CHANNEL_LOST");
            }
        }

        // ---------------------------------------------------------------- nhận file

        private async Task ReceiveFileAsync(Stream stream, string header, CancellationToken token)
        {
            string[] parts = header.Split('|');
            if (parts.Length != 5 || !long.TryParse(parts[3], out long fileSize) || fileSize <= 0 ||
                parts[4].Length != 64 || !parts[4].All(Uri.IsHexDigit))
                throw new IOException("Invalid file header from server.");

            string sender = parts[1];
            string expectedHash = parts[4];
            string fileName = SanitizeFileName(parts[2]);

            string saveFolder = !string.IsNullOrWhiteSpace(ReceiveFolder)
                ? ReceiveFolder
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(saveFolder);
            string savePath = GetUniquePath(saveFolder, fileName); // trùng tên thì thành "a (1).docx"

            Report(fileName, 0, fileSize, "Receiving", $"Receiving from {sender}: {fileName}", true);

            bool completed = false;
            bool hashOk = false;
            try
            {
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(token);
                byte[] buffer = new byte[BufferSize];
                long remaining = fileSize;
                int lastPercent = -1;

                using (var fs = new FileStream(savePath, FileMode.CreateNew, FileAccess.Write,
                           FileShare.None, BufferSize, useAsync: true))
                {
                    while (remaining > 0)
                    {
                        // Người gửi im lặng quá lâu -> thôi, đừng treo mãi ở x%.
                        inactivity.CancelAfter(IoTimeout);

                        int read = await stream.ReadAsync(
                            buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                            inactivity.Token);

                        if (read == 0)
                            throw new IOException("Connection closed while receiving the file.");

                        await fs.WriteAsync(buffer.AsMemory(0, read), token);
                        hasher.AppendData(buffer, 0, read);
                        remaining -= read;

                        int percent = (int)((fileSize - remaining) * 100 / fileSize);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            Report(fileName, fileSize - remaining, fileSize, "Receiving", string.Empty, true);
                        }
                    }
                }

                // Kiểm tra toàn vẹn: SHA-256 người nhận tính được phải khớp SHA-256 người gửi khai báo.
                string actualHash = Convert.ToHexString(hasher.GetHashAndReset());
                hashOk = string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
                completed = hashOk; // sai hash -> finally sẽ xóa file
            }
            catch (Exception ex)
            {
                _quietReconnect = true;

                string message = _cancelRequested
                    ? "Transfer cancelled."
                    : ex is OperationCanceledException && !token.IsCancellationRequested
                        ? $"Receive timed out: {fileName}"
                        : $"Receive interrupted: {fileName}";

                Report(fileName, 0, fileSize, _cancelRequested ? "Cancelled" : "Error", message, false);
                throw; // framing đã hỏng -> kết thúc phiên, vòng lặp nền sẽ kết nối lại
            }
            finally
            {
                if (!completed)
                {
                    try { File.Delete(savePath); } catch { }
                }
            }

            if (!hashOk)
            {
                // Đã đọc đủ byte nên kênh vẫn đồng bộ, không cần kết nối lại.
                Report(fileName, 0, fileSize, "Error",
                    $"Integrity check FAILED, file discarded: {fileName}", false);

                OnFileReceived?.Invoke(this, new MessageReceivedEventArgs
                {
                    Type = "SYSTEM",
                    Sender = sender,
                    Content = $"WARNING: file '{fileName}' from {sender} was corrupted or " +
                              "modified in transit (SHA-256 mismatch). It was deleted."
                });
                return;
            }

            Report(fileName, fileSize, fileSize, "Received", $"Received: {fileName}", false);

            OnFileReceived?.Invoke(this, new MessageReceivedEventArgs
            {
                Type = "SYSTEM",
                Sender = sender,
                Content = $"Received file '{fileName}' from {sender} (SHA-256 verified). Saved to: {savePath}"
            });
        }

        private static string SanitizeFileName(string name)
        {
            string clean = Path.GetFileName(name);
            foreach (char c in Path.GetInvalidFileNameChars())
                clean = clean.Replace(c, '_');

            return string.IsNullOrWhiteSpace(clean) ? "file" : clean;
        }

        // Tránh ghi đè file cùng tên: LAN_a.docx, LAN_a (1).docx, ...
        private static string GetUniquePath(string folder, string fileName)
        {
            string path = Path.Combine(folder, fileName);
            if (!File.Exists(path)) return path;

            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);

            for (int i = 1; ; i++)
            {
                path = Path.Combine(folder, $"{name} ({i}){ext}");
                if (!File.Exists(path)) return path;
            }
        }

        // ---------------------------------------------------------------- gửi file

        public async Task SendFileAsync(string recipient, string filePath)
        {
            if (!_channelReady || _stream == null)
            {
                Report(string.Empty, 0, 0, "Error",
                    "File channel is not connected. Cannot send file.", false);
                return;
            }

            FileInfo info = new FileInfo(filePath);
            string fileName = info.Name;

            if (!info.Exists) { Report(fileName, 0, 0, "Error", "File not found.", false); return; }
            if (info.Length == 0) { Report(fileName, 0, 0, "Error", "Cannot send an empty file.", false); return; }
            if (info.Length > MaxFileSize) { Report(fileName, 0, 0, "Error", "File is larger than 100 MB.", false); return; }
            if (fileName.Contains('|')) { Report(fileName, 0, 0, "Error", "File name must not contain '|'.", false); return; }

            // Chỉ cho một lần gửi tại một thời điểm.
            if (!await _sendLock.WaitAsync(0))
            {
                Report(fileName, 0, 0, "Error", "Another file is still being sent. Please wait.", false);
                return;
            }

            long fileSize = info.Length;
            long sent = 0;
            Stream stream = _stream!;

            try
            {
                CancellationToken token = _lifetime.Token;
                _cancelRequested = false;

                while (_responses.Reader.TryRead(out _)) { } // bỏ phản hồi cũ còn sót

                // 0. Tính SHA-256 của file để người nhận kiểm tra toàn vẹn
                Report(fileName, 0, fileSize, "Preparing", $"Calculating checksum: {fileName}", true);
                string sha256 = await ComputeSha256Async(filePath, token);

                Report(fileName, 0, fileSize, "Waiting", $"Waiting for server: {fileName}", true);

                // 1. Gửi header, chờ server đồng ý
                await WriteLineAsync(stream, $"SEND|{recipient}|{fileName}|{fileSize}|{sha256}", token);

                string response = await WaitResponseAsync(SendOkTimeout);
                if (response != "SEND_OK")
                {
                    // Server từ chối trước khi gửi body: kênh vẫn đồng bộ, không cần đóng.
                    Report(fileName, 0, fileSize, "Error", DescribeFileError(response), false);
                    return;
                }

                // 2. Gửi body đúng bằng fileSize byte
                byte[] buffer = new byte[BufferSize];
                int lastPercent = -1;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, BufferSize, useAsync: true);
                using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(token);

                while (sent < fileSize)
                {
                    int read = await fs.ReadAsync(
                        buffer.AsMemory(0, (int)Math.Min(buffer.Length, fileSize - sent)), token);

                    if (read == 0)
                        throw new IOException("The file changed while it was being sent.");

                    writeCts.CancelAfter(IoTimeout);
                    await stream.WriteAsync(buffer.AsMemory(0, read), writeCts.Token);
                    sent += read;

                    int percent = (int)(sent * 100 / fileSize);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        Report(fileName, sent, fileSize, "Sending", string.Empty, true);
                    }
                }

                await stream.FlushAsync(token);

                // 3. Chờ server xác nhận đã chuyển xong
                response = await WaitResponseAsync(SentConfirmTimeout);

                if (response.StartsWith("FILE_SENT|"))
                    Report(fileName, fileSize, fileSize, "Sent", $"Sent to {recipient}: {fileName}", false);
                else
                    Report(fileName, sent, fileSize, "Error", DescribeFileError(response), false);
            }
            catch (Exception ex)
            {
                // Không rõ server/recipient đang ở trạng thái nào -> đóng kênh để đồng bộ lại.
                _quietReconnect = true;

                string message = _cancelRequested
                    ? "Transfer cancelled."
                    : ex is TimeoutException
                        ? "No response from the server. Transfer aborted."
                        : $"Send failed: {ex.Message}";

                Report(fileName, sent, fileSize, _cancelRequested ? "Cancelled" : "Error", message, false);
                CloseChannel();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken token)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, BufferSize, useAsync: true);

            byte[] hash = await SHA256.HashDataAsync(fs, token);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task<string> WaitResponseAsync(TimeSpan timeout)
        {
            CancellationToken lifetime = _lifetime.Token;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            cts.CancelAfter(timeout);

            string response;
            try
            {
                response = await _responses.Reader.ReadAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for the server.");
            }

            if (response == "ERROR|CHANNEL_LOST")
                throw new IOException("The file channel was lost.");

            return response;
        }

        private static string DescribeFileError(string response) => response switch
        {
            "ERROR|USER_NOT_ONLINE" => "Recipient is not online or has no file channel.",
            "ERROR|CANNOT_SEND_TO_SELF" => "You cannot send a file to yourself.",
            "ERROR|INVALID_FILE" => "Server rejected the file name or size.",
            "ERROR|INVALID_FILE_HEADER" => "Server rejected the file request.",
            "ERROR|FILE_TRANSFER_FAILED" => "Transfer failed: the recipient disconnected or cancelled.",
            "ERROR|RATE_LIMITED" => "You are sending files too often. Please wait a minute.",
            _ => $"Server response: {response}"
        };

        // ---------------------------------------------------------------- tiện ích

        private void Report(string fileName, long done, long total, string status, string message, bool busy)
        {
            OnProgress?.Invoke(this, new FileProgressEventArgs
            {
                FileName = fileName,
                BytesTransferred = done,
                TotalBytes = total,
                Status = status,
                Message = message,
                IsBusy = busy
            });
        }

        // Đọc từng byte để không "ăn lố" dữ liệu nhị phân nằm ngay sau header.
        private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken token)
        {
            using var ms = new MemoryStream();
            byte[] oneByte = new byte[1];

            while (true)
            {
                int count = await stream.ReadAsync(oneByte.AsMemory(0, 1), token);

                if (count == 0)
                {
                    if (ms.Length == 0) return null;
                    throw new IOException("Connection closed inside a header.");
                }

                if (oneByte[0] == (byte)'\n') break;
                if (oneByte[0] != (byte)'\r') ms.WriteByte(oneByte[0]);

                if (ms.Length > 2048)
                    throw new IOException("Header too long.");
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static async Task WriteLineAsync(Stream stream, string msg, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(msg + "\n");
            await stream.WriteAsync(bytes.AsMemory(), token);
            await stream.FlushAsync(token);
        }
    }
}
