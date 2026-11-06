# Secure LAN Chat & File Transfer

Ứng dụng **chat và truyền file trong mạng LAN**, viết bằng **C# / .NET 10**, có **mã hóa TLS**, **đăng nhập bằng mật khẩu** và **kiểm tra toàn vẹn file**. Dự án gồm hai chương trình:

| Thành phần | Loại | Vai trò |
|---|---|---|
| **Server** | Ứng dụng console | Nhận kết nối, xác thực người dùng, chuyển tin nhắn và chuyển tiếp file giữa các client |
| **Client** | Ứng dụng Windows Forms | Giao diện để đăng ký, đăng nhập, chat, gửi/nhận file |

---

## Mục lục

1. [Tính năng](#1-tính-năng)
2. [Yêu cầu hệ thống](#2-yêu-cầu-hệ-thống)
3. [Cấu trúc thư mục](#3-cấu-trúc-thư-mục)
4. [Cài đặt và chạy](#4-cài-đặt-và-chạy)
5. [Hướng dẫn sử dụng](#5-hướng-dẫn-sử-dụng)
6. [Cách hoạt động](#6-cách-hoạt-động)
7. [Dữ liệu được lưu ở đâu](#7-dữ-liệu-được-lưu-ở-đâu)
8. [Bảo mật](#8-bảo-mật)
9. [Quy định và giới hạn](#9-quy-định-và-giới-hạn)
10. [Xử lý sự cố](#10-xử-lý-sự-cố)
11. [Giới hạn đã biết](#11-giới-hạn-đã-biết)

---

## 1. Tính năng

**Kết nối**
- Tự tìm server trong mạng LAN bằng nút **Discover** (UDP broadcast), hoặc nhập IP bằng tay.
- **Register** (đăng ký) và **Login** (đăng nhập) bằng tên và mật khẩu; **Disconnect** để ngắt kết nối nhẹ nhàng.

**Chat**
- Chat chung cho mọi người (**Everyone**) và chat riêng từng người.
- Mỗi cuộc trò chuyện có khung chat riêng, giống Messenger/Zalo: bấm vào ai thì chỉ hiện tin của người đó.
- Số **tin chưa đọc** hiện cạnh tên, ví dụ `Tu (2)`.
- Danh sách hiện cả người **đang offline** nhưng từng chat với bạn, để xem lại tin cũ.
- **Lịch sử chat được lưu trên máy** và nạp lại ở lần đăng nhập sau, kể cả sau khi tắt server và client.

**File**
- Gửi file riêng cho một người (tối đa 100 MB), có thanh tiến trình và nút **Cancel**.
- Người nhận được file tự động lưu vào thư mục riêng của mình.
- Kiểm tra toàn vẹn bằng **SHA-256**: file bị hỏng hoặc bị sửa trên đường đi sẽ bị xóa và cảnh báo.
- Tự kết nối lại kênh file khi bị đứt hoặc sau khi hủy.

**Bảo mật**
- Mã hóa **TLS 1.2/1.3** cho cả kênh chat và kênh file.
- Mật khẩu chỉ lưu ở dạng **hash PBKDF2** có salt, không lưu mật khẩu gốc.
- **Ghim chứng chỉ** của server để chống giả mạo (man-in-the-middle).
- **Khóa tạm** khi đăng nhập sai nhiều lần, **giới hạn tốc độ** gửi tin, gửi file và số kết nối.

---

## 2. Yêu cầu hệ thống

- **Windows 10/11** (client dùng Windows Forms).
- **.NET 10 SDK** và **Visual Studio** hỗ trợ .NET 10 (hoặc dùng lệnh `dotnet`).
- Các máy trong cùng một mạng LAN (hoặc chạy tất cả trên một máy để thử).
- Cho phép các cổng sau qua tường lửa Windows: **TCP 5000** (chat), **TCP 5002** (file), **UDP 5001** (tự tìm server).

---

## 3. Cấu trúc thư mục

```
Secure-LAN-Chat/
├── README.md
├── LAN_CHAT_and_Transmission_File_Server/        ← SERVER
│   └── LAN_CHAT_and_Transmission_File/
│       ├── Program.cs                 điểm khởi động
│       ├── ServerConfig.cs            cổng và mọi giới hạn
│       ├── Core/                      ChatServer, ClientHandler, ClientRegistry, ClientSession
│       ├── Protocol/                  MessageRouter, InputValidator, LineReader
│       ├── FileTransfer/              FileTransferServer
│       ├── Discovery/                 UdpDiscoveryService, NetworkHelper
│       └── Security/                  CertificateProvider, TlsHelper, PasswordHasher,
│                                      UserStore, LoginThrottle, RateLimiters
└── Secure_Lan_Chat_and_Transmission_File_Client/  ← CLIENT
    └── Secure_Lan_Chat_and_Transmission_File_Client/
        ├── Program.cs
        ├── Forms/                     MainForm (giao diện và logic)
        ├── Models/                    NetworkMessage, ChatRecord
        └── Services/                  TcpChatClient, FileTransferClient, SecureConnection,
                                       UdpDiscoveryClient, LocalStore
```

---

## 4. Cài đặt và chạy

### 4.1 Lấy mã nguồn

Giải nén vào một đường dẫn **ngắn**, ví dụ `C:\dev\LanChat\`.
> Đường dẫn quá dài (ví dụ nằm sâu trong `Downloads\...`) có thể gây lỗi build *"Could not find a part of the path"* do Windows giới hạn khoảng 260 ký tự.

### 4.2 Build

1. Mở file solution của **server** (`.sln`/`.slnx` trong thư mục `LAN_CHAT_and_Transmission_File_Server`) và chọn **Build → Rebuild Solution**.
2. Làm tương tự với solution của **client**.

### 4.3 Chạy

1. **Chạy server trước.** Khi Windows hỏi về tường lửa, chọn cho phép trên mạng riêng (Private).
   Console hiện các cổng, số tài khoản đã đăng ký, và **dòng dấu vân tay chứng chỉ (SHA-256)**. Hãy giữ cửa sổ này, bạn sẽ cần đối chiếu dấu vân tay ở lần đầu kết nối.
2. **Chạy client.** Có thể mở nhiều client cùng lúc: trong Visual Studio chọn **Debug → Start New Instance**, hoặc chạy file `.exe` trong `bin\Debug\net10.0-windows\` nhiều lần.

> Chạy tất cả trên một máy: nhập IP của chính máy đó (xem bằng lệnh `ipconfig`) hoặc `127.0.0.1` vào ô IP.

---

## 5. Hướng dẫn sử dụng

### Bước 1: Kết nối tới server
- Bấm **Discover** để tự tìm server, hoặc gõ IP của máy chạy server vào ô đầu tiên.

### Bước 2: Đăng ký tài khoản (lần đầu)
- Nhập **Username** và **Password**, bấm **Register**.
- Tên tối đa 20 ký tự, không chứa `|` hoặc `,`. Mật khẩu 8–64 ký tự và khác tên đăng nhập.

### Bước 3: Xác nhận chứng chỉ (chỉ ở lần đầu tới một server)
- Hộp thoại hiện **dấu vân tay chứng chỉ**. So sánh với dòng dấu vân tay trên **console của server**.
- Giống hệt thì chọn **Yes**; khác thì chọn **No** (có thể có người đang giả mạo server).
- Client nhớ dấu vân tay này; các lần sau sẽ không hỏi lại.

### Bước 4: Đăng nhập
- Lần sau, nhập tên và mật khẩu rồi bấm **Login**.
- Mỗi tài khoản chỉ đăng nhập ở **một nơi** tại một thời điểm.

### Bước 5: Chat
- Bên phải là danh sách cuộc trò chuyện: **`[ Everyone ]`** (chat chung) và từng người dùng.
- **Chat chung:** chọn `[ Everyone ]`, gõ tin ở ô dưới cùng, bấm **Send**.
- **Chat riêng:** chọn tên một người rồi gõ tin. Ô nhập hiện gợi ý đang nhắn cho ai.
- Tin mới ở cuộc trò chuyện chưa mở hiện số bên cạnh tên, ví dụ `Tu (1)`. Bấm vào để đọc.
- Người **đang offline** hiện thêm chữ `- offline`: bạn xem được tin cũ nhưng không gửi được tin mới.
- Bạn không tự chat với chính mình (tên bạn không có trong danh sách).

### Bước 6: Gửi file
- Chọn **một người đang online** (không chọn `[ Everyone ]`), bấm **Send File** và chọn file.
- Thanh tiến trình và dòng trạng thái hiện bên dưới. Bấm **Cancel** để hủy giữa chừng.
- Người nhận thấy thông báo trong cuộc trò chuyện với bạn, kèm đường dẫn file đã lưu.

### Bước 7: Xem dữ liệu của bạn
- Bấm **My Folder** để mở thư mục riêng của tài khoản (xem [mục 7](#7-dữ-liệu-được-lưu-ở-đâu)).

### Bước 8: Thoát
- Bấm **Disconnect** hoặc đóng cửa sổ. Nếu đang truyền file, app hỏi xác nhận trước khi đóng.

---

## 6. Cách hoạt động

### 6.1 Sơ đồ tổng quan

```
   Client A                         SERVER                         Client B
 ┌───────────┐   TLS / TCP 5000  ┌────────────────┐  TLS / TCP 5000 ┌───────────┐
 │  MainForm │◄─────── chat ────►│  ChatServer    │◄──── chat ─────►│  MainForm │
 │ TcpChat   │                   │  ClientHandler │                 │ TcpChat   │
 │ Client    │   TLS / TCP 5002  │  MessageRouter │  TLS / TCP 5002 │ Client    │
 │ FileTrans-│◄─────── file ────►│  FileTransfer- │◄──── file ─────►│ FileTrans-│
 │ ferClient │                   │  Server        │                 │ ferClient │
 │ LocalStore│   UDP 5001        │  UserStore     │   UDP 5001      │ LocalStore│
 └─────┬─────┘◄── discovery ────►│  (users.json)  │                 └─────┬─────┘
       │                         └────────────────┘                       │
  ClientData\<user>\                                              ClientData\<user>\
```

Mỗi client dùng **hai kết nối TCP** tới server: một cho chat (cổng 5000) và một cho file (cổng 5002). Cả hai đều được mã hóa TLS. Server **không lưu tin nhắn và không lưu file**, chỉ chuyển tiếp.

### 6.2 Tự tìm server (UDP)

1. Client broadcast chuỗi `DISCOVER_SERVER` tới cổng UDP 5001.
2. Server trả lời `LAN_CHAT_SERVER|IP=<ip>|TCP_PORT=5000`.
3. Client điền IP vào ô nhập. Gói này không mã hóa nhưng chỉ chứa IP và cổng.

### 6.3 Đăng nhập

1. Client mở TCP tới server rồi **bắt tay TLS**. Chứng chỉ được đối chiếu với dấu vân tay đã ghim (hoặc hỏi người dùng ở lần đầu).
2. Server gửi `AUTH_REQUIRED`; client gửi `LOGIN|tên|mật khẩu` hoặc `REGISTER|tên|mật khẩu` (đã được TLS mã hóa).
3. Server kiểm tra: đang bị khóa tạm không → định dạng hợp lệ → mật khẩu đúng (so hash PBKDF2). Đăng ký thì tạo hash mới và ghi `users.json`.
4. Thành công: server trả `LOGIN_OK|tên` và `FILE_CHANNEL|5002|token`, rồi gửi danh sách online cho mọi người.
5. Client mở **kết nối TLS thứ hai** tới cổng 5002, gửi token để gắn kênh file vào phiên chat.
6. Client tạo thư mục `ClientData\<tên>\` và nạp lại lịch sử chat từ đĩa.

### 6.4 Chat

- **Chung:** client gửi `CHAT|nội dung`. Server chuyển `CHAT|người gửi|nội dung` cho tất cả, trừ người gửi.
- **Riêng:** client gửi `PRIVATE|người nhận|nội dung`. Server chuyển tới đúng người nhận và xác nhận `PRIVATE_SENT`.
- Phía client, mỗi tin được lưu vào lịch sử đúng cuộc trò chuyện và **ghi xuống đĩa ngay lập tức**.

### 6.5 Truyền file

1. Người gửi tính **SHA-256** của file, rồi gửi `SEND|người nhận|tên file|kích thước|sha256`.
2. Server kiểm tra (tên file, kích thước ≤ 100 MB, hash hợp lệ, người nhận đang online, chưa quá giới hạn tần suất) rồi trả `SEND_OK`.
3. Người gửi đẩy từng khối 80 KB; server chuyển ngay từng khối sang người nhận (không lưu lại trên server).
4. Người nhận ghi file vào `ReceivedFiles\`, vừa ghi vừa tính SHA-256, cuối cùng **so khớp** với hash người gửi khai báo: đúng thì báo thành công, sai thì xóa file và cảnh báo.
5. Server báo `FILE_SENT` cho người gửi.

Nếu một bên **Cancel**, đứt mạng hoặc im lặng quá 30 giây, lần truyền dừng, file dở dang bị xóa, và hai client tự kết nối lại kênh file.

### 6.6 Bảng lệnh giao thức (mỗi lệnh là một dòng chữ UTF-8, phân tách bằng `|`)

| Chiều | Lệnh | Ý nghĩa |
|---|---|---|
| S → C | `AUTH_REQUIRED` | Yêu cầu xác thực |
| C → S | `LOGIN\|tên\|mk` / `REGISTER\|tên\|mk` | Đăng nhập / đăng ký |
| S → C | `LOGIN_OK\|tên`, `FILE_CHANNEL\|5002\|token` | Thành công |
| S → C | `LOGIN_FAIL\|mã`, `REGISTER_FAIL\|mã` | Thất bại |
| S → C | `ONLINE_LIST\|a,b,c` | Danh sách người online |
| C → S | `CHAT\|nội dung` / `PRIVATE\|người nhận\|nội dung` | Gửi tin |
| S → C | `CHAT\|người gửi\|nội dung` / `PRIVATE\|người gửi\|nội dung` | Nhận tin |
| S → C | `ERROR\|mã` | Lỗi |
| C → S (file) | `FILE_CHANNEL\|tên\|token`, `SEND\|...` | Gắn kênh file / xin gửi file |
| S → C (file) | `FILE_CHANNEL_OK`, `SEND_OK`, `RECEIVE\|...`, `FILE_SENT\|...` | Phản hồi / chuyển file |

---

## 7. Dữ liệu được lưu ở đâu

| Dữ liệu | Vị trí | Ghi chú |
|---|---|---|
| Tài khoản (tên, salt, hash) | **Server:** `data\users.json` cạnh file `.exe` của server | Còn sau khi tắt/bật server. Không có mật khẩu gốc |
| Chứng chỉ TLS và khóa riêng | **Server:** `data\server.pfx` | Tự tạo ở lần chạy đầu. Giữ kín thư mục này |
| Lịch sử chat | **Client:** `ClientData\<tên>\Messages\*.jsonl` | Mỗi cuộc trò chuyện một file, mỗi dòng một tin |
| File nhận được | **Client:** `ClientData\<tên>\ReceivedFiles\` | Trùng tên thì thành `a (1).docx` |
| Dấu vân tay server đã tin tưởng | **Client:** `%AppData%\SecureLanChat\known_servers.txt` | Mỗi dòng: `địa-chỉ <Tab> dấu-vân-tay` |
| Người đang online, bộ đếm giới hạn | RAM của server | Mất khi tắt server |
| Số tin chưa đọc | RAM của client | Mất khi đóng app |

`ClientData\` nằm cạnh file `.exe` của client (thường là `...\bin\Debug\net10.0-windows\ClientData\`). Server **không** lưu nội dung tin nhắn hay file và không ghi nội dung tin ra console.

---

## 8. Bảo mật

| Mối đe dọa | Biện pháp |
|---|---|
| Nghe lén tin nhắn, file, mật khẩu trên đường truyền | TLS 1.2/1.3 cho cả chat và file |
| Giả mạo server | Ghim dấu vân tay chứng chỉ; chứng chỉ đổi thì từ chối và cảnh báo |
| Lộ mật khẩu khi rò rỉ `users.json` | PBKDF2-HMAC-SHA256, 600.000 vòng, salt ngẫu nhiên, so sánh thời gian cố định |
| Dò mật khẩu | Khóa tạm 5 phút: 5 lần sai/tài khoản hoặc 10 lần sai/IP trong 10 phút. Lỗi báo chung chung, không lộ tài khoản nào tồn tại |
| Làm ngập server | 15 tin/10 giây, 10 lần gửi file/phút, 10 kết nối/IP/cổng, timeout bắt tay, giới hạn độ dài dòng |
| File hỏng hoặc bị sửa | So khớp SHA-256 ở người nhận |
| Tên file độc hại (`..\x`) | Server và client đều cắt về tên file thuần |
| Mạo danh qua kênh file | Token ngẫu nhiên 128-bit cho từng phiên, gửi qua kênh TLS |

---

## 9. Quy định và giới hạn

| Mục | Giá trị |
|---|---|
| Tên đăng nhập | tối đa 20 ký tự, không chứa `\|` hoặc `,` |
| Mật khẩu | 8–64 ký tự, khác tên đăng nhập |
| Tin nhắn | tối đa 1000 ký tự, không chứa `\|` |
| File | tối đa 100 MB, tên không chứa `\|`, không rỗng |
| Cổng | TCP 5000 (chat), UDP 5001 (tìm server), TCP 5002 (file) |
| Tin nhắn nhanh | 15 tin/10 giây; vi phạm hơn 30 lần thì bị ngắt kết nối |
| Gửi file | 10 lần/phút; im lặng quá 30 giây thì dừng |
| Lịch sử nạp lại | tối đa 2000 tin gần nhất mỗi cuộc trò chuyện |

Các giá trị của server nằm trong `ServerConfig.cs`.

---

## 10. Xử lý sự cố

| Hiện tượng | Nguyên nhân và cách xử lý |
|---|---|
| Build lỗi *"Could not find a part of the path"* | Đường dẫn quá dài. Chép dự án ra thư mục ngắn như `C:\dev\LanChat\`, Clean rồi Rebuild |
| *"The debug executable ... does not exist"* | Build thất bại nên chưa có file `.exe`. Xem **Error List** (chọn *Build Only*) để biết lỗi |
| **Discover** không tìm thấy server | Nhập IP bằng tay (xem `ipconfig`). Máy có card ảo (VMware, WSL...) đôi khi làm broadcast đi sai card. Kiểm tra tường lửa cho UDP 5001 |
| Không kết nối được | Server đã chạy chưa? Đúng IP chưa? Tường lửa đã mở TCP 5000 và 5002 chưa? |
| Báo **"certificate has CHANGED"** | Chứng chỉ của server khác lần trước (ví dụ đã xóa thư mục `data` của server). Nếu bạn biết lý do, xóa dòng của server đó trong `%AppData%\SecureLanChat\known_servers.txt`. Nếu không biết lý do, **đừng kết nối** |
| *"Wrong username or password"* | Sai thông tin, hoặc chưa đăng ký. Bấm **Register** lần đầu |
| *"Too many failed attempts"* | Đang bị khóa tạm 5 phút vì đăng nhập sai nhiều lần. Chờ rồi thử lại |
| *"already logged in on another client"* | Tài khoản đang đăng nhập ở nơi khác. Đóng phiên đó hoặc dùng tài khoản khác |
| Mất tài khoản sau khi chép/xóa thư mục build của server | Tài khoản nằm ở `data\users.json` trong thư mục `bin` của server. Sao lưu thư mục `data` nếu cần giữ |
| Không thấy lịch sử chat cũ | Phải đăng nhập đúng tài khoản và chạy đúng thư mục `.exe` (dữ liệu nằm cạnh file `.exe`) |

---

## 11. Giới hạn đã biết

- **Chưa có mã hóa đầu cuối**: tin nhắn được mã hóa trên đường truyền, nhưng server vẫn đọc được khi chuyển tiếp.
- **Lịch sử chat lưu dạng chữ thường** trên máy client; ai truy cập được máy đều đọc được `Messages\*.jsonl`.
- Khóa riêng `server.pfx` không đặt mật khẩu; cần bảo vệ thư mục `data` của server.
- Lần kết nối đầu tiên dựa vào việc **người dùng tự đối chiếu** dấu vân tay chứng chỉ.
- Người gửi file không được báo nếu người nhận phát hiện file hỏng.
- Khóa tạm theo tài khoản có thể bị lạm dụng để cố tình chặn người khác đăng nhập.
- Chỉ gửi file cho từng người, chưa gửi cho cả nhóm; chưa có phòng chat nhóm riêng.
- Client chỉ chạy trên Windows.
