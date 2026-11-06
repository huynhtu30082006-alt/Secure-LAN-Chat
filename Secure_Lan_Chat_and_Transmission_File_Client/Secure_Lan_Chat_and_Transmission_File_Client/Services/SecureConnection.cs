using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Secure_Lan_Chat___Transmission_File_Client.Services
{
    /// <summary>
    /// TLS phía client với chứng chỉ tự ký của server, theo kiểu "ghim" (trust on first use):
    /// lần đầu kết nối tới một server, người dùng đối chiếu dấu vân tay SHA-256 (hiển thị trên
    /// console của server) rồi chọn tin tưởng; dấu vân tay được lưu lại. Những lần sau chứng chỉ
    /// phải khớp, nếu khác (có thể bị tấn công man-in-the-middle) thì từ chối kết nối.
    /// </summary>
    public static class SecureConnection
    {
        private static readonly object FileLock = new object();
        private static string? _lastError;

        /// <summary>(host, dấu vân tay) -> true nếu người dùng tin tưởng. Form gán hàm này.</summary>
        public static Func<string, string, bool>? ConfirmNewServer { get; set; }

        private static string StorePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SecureLanChat", "known_servers.txt");

        /// <summary>Lấy (và xóa) thông báo lỗi chi tiết của lần xác thực chứng chỉ gần nhất.</summary>
        public static string? TakeLastError()
        {
            string? error = _lastError;
            _lastError = null;
            return error;
        }

        public static async Task<SslStream> AuthenticateAsync(
            TcpClient tcp, string host, CancellationToken token)
        {
            var ssl = new SslStream(
                tcp.GetStream(),
                leaveInnerStreamOpen: false,
                (sender, certificate, chain, errors) => ValidateCertificate(host, certificate));

            try
            {
                await ssl.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = host,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    },
                    token);

                return ssl;
            }
            catch
            {
                ssl.Dispose();
                throw;
            }
        }

        private static bool ValidateCertificate(string host, X509Certificate? certificate)
        {
            if (certificate == null)
            {
                _lastError = "The server did not present a certificate.";
                return false;
            }

            string fingerprint = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

            lock (FileLock)
            {
                Dictionary<string, string> pins = LoadPins();

                if (pins.TryGetValue(host, out string? pinned))
                {
                    if (string.Equals(pinned, fingerprint, StringComparison.OrdinalIgnoreCase))
                        return true;

                    _lastError =
                        $"WARNING: the certificate of server {host} has CHANGED!\n\n" +
                        $"Expected: {Format(pinned)}\n" +
                        $"Received: {Format(fingerprint)}\n\n" +
                        "This could be a man-in-the-middle attack. If you know the server's " +
                        $"certificate was regenerated, delete the line for {host} in:\n{StorePath}";
                    return false;
                }
            }

            // Server lạ: hỏi người dùng (ngoài lock vì có hiển thị hộp thoại).
            bool trust = ConfirmNewServer?.Invoke(host, Format(fingerprint)) ?? false;

            if (!trust)
            {
                _lastError = "You did not trust the server certificate. Connection cancelled.";
                return false;
            }

            lock (FileLock)
            {
                Dictionary<string, string> pins = LoadPins();
                pins[host] = fingerprint;
                SavePins(pins);
            }

            return true;
        }

        // "AA:BB:CC:..." giống định dạng in trên console của server.
        public static string Format(string hex) =>
            string.Join(":", hex.Chunk(2).Select(c => new string(c)));

        private static Dictionary<string, string> LoadPins()
        {
            var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!File.Exists(StorePath)) return pins;

                foreach (string line in File.ReadAllLines(StorePath))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                        pins[parts[0]] = parts[1];
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return pins;
        }

        private static void SavePins(Dictionary<string, string> pins)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllLines(StorePath, pins.Select(p => $"{p.Key}\t{p.Value}"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
