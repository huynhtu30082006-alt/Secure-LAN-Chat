
using System.Net;
using System.Net.Sockets;
using System.Text;

// UDP discovery port và TCP chat port.
const int UDP_DISCOVERY_PORT = 5001;
const int DEFAULT_TCP_PORT = 5000;

// ========================================
// STEP 1: DISCOVER SERVER THROUGH UDP
// ========================================

IPAddress serverIp;
int serverPort;

using (UdpClient udp = new UdpClient(0))
{
    udp.EnableBroadcast = true;

    byte[] requestBytes =
        Encoding.UTF8.GetBytes("DISCOVER_SERVER");

    IPEndPoint broadcastEndpoint = new IPEndPoint(
        IPAddress.Broadcast,
        UDP_DISCOVERY_PORT
    );

    Console.WriteLine("Searching for LAN Chat Server...");

    await udp.SendAsync(requestBytes, broadcastEndpoint);

    try
    {
        using CancellationTokenSource timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(5)
            );

        UdpReceiveResult result =
            await udp.ReceiveAsync(timeout.Token);

        string response =
            Encoding.UTF8.GetString(result.Buffer);

        Console.WriteLine(
            $"Discovery response: {response}"
        );

        // Expected format:
        // LAN_CHAT_SERVER|IP=192.168.1.10|TCP_PORT=5000
        string[] fields = response.Split('|');

        if (fields.Length != 3 ||
            fields[0] != "LAN_CHAT_SERVER" ||
            !fields[1].StartsWith("IP=") ||
            !fields[2].StartsWith("TCP_PORT=") ||
            !IPAddress.TryParse(
                fields[1]["IP=".Length..],
                out IPAddress? parsedIp) ||
            !int.TryParse(
                fields[2]["TCP_PORT=".Length..],
                out int parsedPort) ||
            parsedPort < 1 ||
            parsedPort > 65535)
        {
            Console.WriteLine("Invalid discovery response.");
            return;
        }

        serverIp = parsedIp;
        serverPort = parsedPort;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine(
            "Discovery timed out. No server was found."
        );

        Console.WriteLine(
            "Check that the server is running and UDP port 5001 is allowed."
        );

        return;
    }
}

// ========================================
// STEP 2: CONNECT THROUGH TCP
// ========================================

using TcpClient client = new TcpClient();

try
{
    await client.ConnectAsync(serverIp, serverPort);
}
catch (SocketException ex)
{
    Console.WriteLine(
        $"Could not connect to server {serverIp}:{serverPort}"
    );
    Console.WriteLine(ex.Message);
    return;
}

Console.WriteLine(
    $"Connected to server {serverIp}:{serverPort}!"
);

using NetworkStream stream = client.GetStream();

using StreamReader reader = new StreamReader(
    stream, Encoding.UTF8, false, 1024, true);

using StreamWriter writer = new StreamWriter(
    stream, new UTF8Encoding(false), 1024, true)
{
    AutoFlush = true
};

// ========================================
// STEP 3: LOGIN WITH USERNAME
// ========================================

// Receive username request from server.
string? loginRequest = await reader.ReadLineAsync();

if (loginRequest != "USERNAME")
{
    Console.WriteLine(
        $"Unexpected server response: {loginRequest}"
    );
    return;
}

// Send username.
Console.Write("Enter username: ");
string? username = Console.ReadLine();

if (string.IsNullOrWhiteSpace(username))
{
    Console.WriteLine("Username cannot be empty.");
    return;
}

await writer.WriteLineAsync(username.Trim());

// Read login result.
string? loginResponse = await reader.ReadLineAsync();

if (loginResponse == null)
{
    Console.WriteLine("Server disconnected.");
    return;
}

if (!loginResponse.StartsWith(
        "LOGIN_OK|", StringComparison.Ordinal))
{
    Console.WriteLine($"Login failed: {loginResponse}");
    return;
}

Console.WriteLine(
    $"Login successful: {username.Trim()}"
);

Console.WriteLine("Type a message. Type /exit to quit.");

// ========================================
// STEP 4: CHAT
// ========================================

while (true)
{
    Console.Write("> ");
    string? input = Console.ReadLine();

    if (input == null ||
        input.Equals(
            "/exit",
            StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    await writer.WriteLineAsync(input);

    string? response = await reader.ReadLineAsync();

    if (response == null)
    {
        Console.WriteLine("Server disconnected.");
        break;
    }

    Console.WriteLine($"SERVER: {response}");
}

Console.WriteLine("Client closed.");
