using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Secure_Lan_Chat___Transmission_File_Client.Models;

namespace Secure_Lan_Chat___Transmission_File_Client.Services
{
    /// <summary>
    /// Thư mục dữ liệu riêng của MỘT người dùng trên máy client:
    ///
    ///   ClientData\
    ///     └── &lt;username&gt;\
    ///           ├── Messages\        mỗi cuộc trò chuyện một file .jsonl (mỗi dòng một tin nhắn)
    ///           │     ├── Everyone.jsonl
    ///           │     └── Private_&lt;tên người kia&gt;.jsonl
    ///           └── ReceivedFiles\   file nhận được từ người khác
    ///
    /// Thư mục nằm cạnh file .exe của client. Tắt server, tắt client, đăng nhập lại thì lịch sử vẫn được nạp lại.
    /// </summary>
    public sealed class LocalStore
    {
        public const string EveryoneConversation = "*";

        private const int MaxRecordsPerConversation = 2000; // chỉ nạp tối đa N tin gần nhất của mỗi cuộc

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            // Giữ nguyên tiếng Việt có dấu trong file (không đổi thành \uXXXX) để mở bằng Notepad vẫn đọc được.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly object _lock = new object();

        public static string RootFolder => Path.Combine(AppContext.BaseDirectory, "ClientData");

        public string UserFolder { get; }
        public string MessagesFolder { get; }
        public string ReceivedFilesFolder { get; }

        /// <summary>Tạo (nếu chưa có) thư mục của người dùng. Có thể ném IOException/UnauthorizedAccessException.</summary>
        public LocalStore(string username)
        {
            UserFolder = Path.Combine(RootFolder, ToSafeName(username));
            MessagesFolder = Path.Combine(UserFolder, "Messages");
            ReceivedFilesFolder = Path.Combine(UserFolder, "ReceivedFiles");

            Directory.CreateDirectory(MessagesFolder);
            Directory.CreateDirectory(ReceivedFilesFolder);
        }

        /// <summary>Ghi thêm một tin nhắn vào cuối file của cuộc trò chuyện. Trả về false nếu ghi lỗi.</summary>
        public bool Append(ChatRecord record)
        {
            try
            {
                string path = FileFor(record.Conversation);
                string line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;

                lock (_lock)
                {
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }

                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>Đọc toàn bộ lịch sử: key = "*" hoặc username, value = các tin theo thứ tự thời gian.</summary>
        public Dictionary<string, List<ChatRecord>> LoadAll()
        {
            var result = new Dictionary<string, List<ChatRecord>>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(MessagesFolder))
                return result;

            foreach (string file in Directory.EnumerateFiles(MessagesFolder, "*.jsonl"))
            {
                var list = new List<ChatRecord>();

                try
                {
                    foreach (string line in File.ReadLines(file, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            ChatRecord? record = JsonSerializer.Deserialize<ChatRecord>(line, JsonOptions);

                            if (record != null && !string.IsNullOrEmpty(record.Conversation))
                                list.Add(record);
                        }
                        catch (JsonException)
                        {
                            // Bỏ qua dòng hỏng (ví dụ máy tắt đột ngột khi đang ghi), không làm mất cả file.
                        }
                    }
                }
                catch (IOException) { continue; }

                if (list.Count == 0) continue;

                list = list.OrderBy(r => r.TimeUtc).ToList();

                if (list.Count > MaxRecordsPerConversation)
                    list = list.GetRange(list.Count - MaxRecordsPerConversation, MaxRecordsPerConversation);

                result[list[0].Conversation] = list;
            }

            return result;
        }

        private string FileFor(string conversation)
        {
            string name = conversation == EveryoneConversation
                ? "Everyone.jsonl"
                : $"Private_{ToSafeName(conversation)}.jsonl";

            return Path.Combine(MessagesFolder, name);
        }

        /// <summary>
        /// Biến username thành tên thư mục/file hợp lệ trên Windows. Nếu phải đổi ký tự (hoặc trùng tên
        /// dành riêng như CON, NUL) thì thêm 8 ký tự hash để hai tên khác nhau không bị trùng thư mục.
        /// </summary>
        public static string ToSafeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);

            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            string safe = sb.ToString().TrimEnd('.', ' ');

            if (safe.Length == 0 || safe != name || IsReservedName(safe))
            {
                string hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(name.ToLowerInvariant())))[..8];

                safe = (safe.Length == 0 ? "user" : safe) + "_" + hash;
            }

            return safe;
        }

        private static bool IsReservedName(string name)
        {
            string upper = name.ToUpperInvariant();

            if (upper is "CON" or "PRN" or "AUX" or "NUL")
                return true;

            return (upper.StartsWith("COM") || upper.StartsWith("LPT")) &&
                   upper.Length == 4 && char.IsDigit(upper[3]);
        }
    }
}
