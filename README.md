# Secure LAN Chat & File Transfer

A chat and file-transfer application for local networks, written in C# / .NET 10. Traffic is encrypted with TLS, users sign in with a password, and transferred files are verified with SHA-256.

The project has two programs:

- **Server**: a console app that authenticates users and relays messages and files between clients.
- **Client**: a Windows Forms app for registering, logging in, chatting, and sending/receiving files.

The server only relays. It does not store message contents or files.

## Features

- Discover the server automatically on the LAN (UDP broadcast) or enter its IP manually
- Register and log in with a username and password (one active session per account)
- Public chat (Everyone) and private chat, each with its own conversation view and unread counter
- Chat history saved on the client and reloaded on the next login
- Send files up to 100 MB to an online user, with progress bar and cancel button
- SHA-256 integrity check on every received file; corrupted files are deleted
- TLS 1.2/1.3 on both channels, with server certificate pinning
- Brute-force lockout, rate limiting, and per-IP connection limits

## Tech Stack

- C# / .NET 10
- `TcpListener` / `TcpClient`, `UdpClient`, `SslStream`, `async/await`
- Windows Forms (client)
- PBKDF2-HMAC-SHA256 for password hashing

## Getting Started

### Prerequisites

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or Visual Studio with .NET 10 support)
- Firewall rules allowing **TCP 5000**, **TCP 5002**, and **UDP 5001**

> Place the project in a short path such as `C:\dev\LanChat\`. Very long paths can cause the build error "Could not find a part of the path".

### Run

Start the server first:

```powershell
dotnet run --project .\LAN_CHAT_and_Transmission_File_Server\LAN_CHAT_and_Transmission_File
```

The console prints the ports and the server certificate's SHA-256 fingerprint. Keep it open.

Then start one or more clients:

```powershell
dotnet run --project .\Secure_Lan_Chat_and_Transmission_File_Client\Secure_Lan_Chat_and_Transmission_File_Client
```

You can also open the `.slnx` solutions in Visual Studio. To run several clients, use **Debug → Start New Instance** or launch the built `.exe` multiple times. To test on a single machine, use `127.0.0.1` as the server IP.

## Usage

1. Click **Discover** or type the server's IP.
2. Enter a username and password, then click **Register** (first time) or **Login**.
3. On the first connection to a server, compare the fingerprint in the dialog with the one on the server console. Choose **Yes** only if they match.
4. Select `[ Everyone ]` or a user in the list, type a message, and click **Send**.
5. To send a file, select an online user, click **Send File**, and pick a file. Use **Cancel** to abort.
6. Click **My Folder** to open your data folder (chat history and received files).

Username: up to 20 characters, no `|` or `,`. Password: 8–64 characters, different from the username. Messages: up to 1000 characters, no `|`.

## Project Structure

```
Secure-LAN-Chat/
├── LAN_CHAT_and_Transmission_File_Server/
│   └── LAN_CHAT_and_Transmission_File/
│       ├── Core/           ChatServer, ClientHandler, ClientSession, ClientRegistry
│       ├── Protocol/       MessageRouter, InputValidator, LineReader
│       ├── FileTransfer/   FileTransferServer
│       ├── Discovery/      UdpDiscoveryService, NetworkHelper
│       ├── Security/       CertificateProvider, TlsHelper, PasswordHasher,
│       │                   UserStore, LoginThrottle, RateLimiters
│       ├── Program.cs
│       └── ServerConfig.cs
└── Secure_Lan_Chat_and_Transmission_File_Client/
    └── Secure_Lan_Chat_and_Transmission_File_Client/
        ├── Forms/          MainForm
        ├── Models/         NetworkMessage, ChatRecord
        ├── Services/       TcpChatClient, FileTransferClient, SecureConnection,
        │                   UdpDiscoveryClient, LocalStore
        └── Program.cs
```

## How It Works

| Port | Protocol | Purpose |
|---|---|---|
| 5000 | TCP + TLS | Chat: login, public/private messages, online list |
| 5001 | UDP | Server discovery |
| 5002 | TCP + TLS | File transfer |

Each client opens two TCP connections: one for chat and one for files. After a successful login the server sends a random token over the chat channel; the client presents it on the file channel to attach it to the same session.

Commands are single UTF-8 lines with fields separated by `|`, for example:

```
LOGIN|alice|password        CHAT|hello everyone        PRIVATE|bob|hi
SEND|bob|report.pdf|1048576|<sha256>
```

Files are sent in 80 KB chunks. The server forwards each chunk to the recipient without storing it, and the recipient compares the SHA-256 of the received data with the hash declared by the sender.

## Data Storage

| Data | Location |
|---|---|
| User accounts (salted hashes) | Server: `data\users.json` next to the server `.exe` |
| TLS certificate and key | Server: `data\server.pfx` (created on first run) |
| Chat history | Client: `ClientData\<username>\Messages\*.jsonl` |
| Received files | Client: `ClientData\<username>\ReceivedFiles\` |
| Trusted server fingerprints | Client: `%AppData%\SecureLanChat\known_servers.txt` |

## Security

- **TLS 1.2/1.3** on chat and file channels
- **Certificate pinning**: the fingerprint is confirmed on first connect and verified on every later connect
- **PBKDF2-HMAC-SHA256** (600,000 iterations, random salt); no plaintext passwords are stored
- **Lockout** for 5 minutes after 5 failed logins per account or 10 per IP within 10 minutes
- **Rate limits**: 15 messages per 10 seconds, 10 file sends per minute, 10 connections per IP per port
- **SHA-256** verification of every transferred file
- File names are reduced to plain names on both server and client

Limits are defined in `ServerConfig.cs`.

## Troubleshooting

| Problem | Fix |
|---|---|
| Discover finds no server | Enter the IP manually (`ipconfig`). Check the firewall for UDP 5001. Virtual adapters (VMware, WSL, VPN) can interfere |
| Cannot connect | Check the server is running, the IP is correct, and TCP 5000/5002 are allowed |
| "certificate has CHANGED" | The server's certificate differs from the saved one. If you know why (e.g. its `data` folder was deleted), remove that server's line from `known_servers.txt`; otherwise do not connect |
| "Too many failed attempts" | Wait 5 minutes, or restart the server |
| "already logged in on another client" | Close the other session or use another account |
| Cannot send files | Click **Disconnect** and log in again to reopen the file channel |
| Old history missing | Log in with the same account and run the same `.exe` folder (data is stored next to it) |

## Known Limitations

- No end-to-end encryption: the server can read messages while relaying them
- Messages and files are not stored for offline users
- Chat history is stored as plain text on the client; `server.pfx` has no password
- The first connection depends on the user comparing the fingerprint manually
- The client hard-codes chat port 5000
- File transfer is one-to-one only; there are no group rooms
- The client runs on Windows only

## Authors

- _Name 1_: server and networking
- _Name 2_: client, GUI, and file transfer

## License

_Not specified yet._
