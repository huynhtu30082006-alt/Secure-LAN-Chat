using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanChatServer;

// Chứng chỉ TLS tự ký, tự tạo ở lần chạy đầu và lưu vào data/server.pfx.
// Client sẽ "ghim" (pin) dấu vân tay SHA-256 của chứng chỉ này ở lần kết nối đầu tiên.
public static class CertificateProvider
{
    public static X509Certificate2 LoadOrCreate(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, "server.pfx");

        if (File.Exists(path))
        {
            try
            {
                X509Certificate2 existing =
                    X509CertificateLoader.LoadPkcs12FromFile(path, null);

                // Sắp hết hạn (< 30 ngày) thì tạo mới.
                if (existing.NotAfter > DateTime.Now.AddDays(30))
                    return existing;

                existing.Dispose();
                Console.WriteLine("[TLS] Certificate is about to expire. Creating a new one...");
            }
            catch (CryptographicException ex)
            {
                Console.WriteLine($"[TLS] Could not load server.pfx ({ex.Message}). Creating a new one...");
            }
        }

        return CreateAndSave(path);
    }

    private static X509Certificate2 CreateAndSave(string path)
    {
        using RSA rsa = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=Secure LAN Chat Server",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName);
        san.AddIpAddress(IPAddress.Loopback);

        try
        {
            foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    san.AddIpAddress(address);
            }
        }
        catch (SocketException)
        {
            // Không lấy được danh sách IP: bỏ qua, client dùng pin dấu vân tay chứ không kiểm tra tên.
        }

        request.CertificateExtensions.Add(san.Build());

        using X509Certificate2 created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));

        byte[] pfx = created.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, pfx);

        // Nạp lại từ PFX: trên Windows, khóa của chứng chỉ vừa tạo (ephemeral)
        // không dùng được trực tiếp cho SslStream.
        Console.WriteLine($"[TLS] Created new certificate: {path}");
        return X509CertificateLoader.LoadPkcs12(pfx, null);
    }

    // "AA:BB:CC:..." để người dùng đối chiếu giữa console của server và hộp thoại của client.
    public static string Fingerprint(X509Certificate2 certificate)
    {
        string hex = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        return string.Join(":", hex.Chunk(2).Select(c => new string(c)));
    }
}
