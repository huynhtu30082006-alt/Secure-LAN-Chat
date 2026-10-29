using System.Net.Sockets;

namespace LanChatServer;

public class ClientSession : IDisposable
{
    public TcpClient TcpClient { get; }
    public StreamReader Reader { get; }
    public StreamWriter Writer { get; }
    public string Username { get; }

    // Ngăn nhiều luồng ghi chen vào nhau trên cùng một stream.
    public SemaphoreSlim WriteLock { get; } = new SemaphoreSlim(1, 1);
    public string FileToken { get; } = Guid.NewGuid().ToString("N");

    public ClientSession(
        TcpClient tcpClient,
        StreamReader reader,
        StreamWriter writer,
        string username)
    {
        TcpClient = tcpClient;
        Reader = reader;
        Writer = writer;
        Username = username;
    }

    public async Task SendAsync(string message)
    {
        await WriteLock.WaitAsync();

        try
        {
            await Writer.WriteLineAsync(message);
            await Writer.FlushAsync();
        }
        finally
        {
            WriteLock.Release();
        }
    }

    // Trả về false thay vì ném lỗi khi client đã ngắt kết nối.
    public async Task<bool> TrySendAsync(string message)
    {
        try
        {
            await SendAsync(message);
            return true;
        }
        catch (IOException) { return false; }
        catch (SocketException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    public void Dispose()
    {
        Writer.Dispose();
        Reader.Dispose();
        WriteLock.Dispose();
    }
}