using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace LanChatServer;

public static class TlsHelper
{
    // Bắt tay TLS (server). Có timeout để client treo không giữ kết nối mãi.
    public static async Task<SslStream> AuthenticateServerAsync(
        TcpClient client,
        X509Certificate2 certificate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);

        try
        {
            using var cts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            await ssl.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                cts.Token);

            return ssl;
        }
        catch
        {
            ssl.Dispose();
            throw;
        }
    }
}
