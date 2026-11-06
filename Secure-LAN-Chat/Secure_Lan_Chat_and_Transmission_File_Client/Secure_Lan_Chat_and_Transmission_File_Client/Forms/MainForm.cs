using System.Diagnostics;
using Secure_Lan_Chat___Transmission_File_Client.Models;
using Secure_Lan_Chat___Transmission_File_Client.Services;

namespace Secure_Lan_Chat___Transmission_File_Client.Forms
{
    public partial class MainForm : Form
    {
        // Phải khớp ServerConfig của server.
        private const int MaxMessageLength = 1000;
        private const int MaxUsernameLength = 20;
        private const int MinPasswordLength = 8;
        private const int MaxPasswordLength = 64;

        private readonly TcpChatClient _chatClient = new TcpChatClient();

        private bool _isConnected;
        private bool _transferBusy;

        // Key của cuộc trò chuyện chung. Chứa ký tự điều khiển nên không thể trùng username hợp lệ.
        private const string EveryoneKey = "\u0001Everyone";

        // Lịch sử chat tách riêng theo từng cuộc trò chuyện: Everyone hoặc từng username.
        private readonly Dictionary<string, List<ChatRecord>> _histories =
            new Dictionary<string, List<ChatRecord>>(StringComparer.OrdinalIgnoreCase);

        // Số tin chưa đọc của các cuộc trò chuyện không đang mở.
        private readonly Dictionary<string, int> _unread =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Thư mục dữ liệu của người dùng đang đăng nhập (lịch sử chat + file nhận). Null nếu không tạo được.
        private LocalStore? _store;
        private bool _storeWarningShown;

        // Danh sách người đang online (không gồm bản thân) theo thông báo mới nhất của server.
        private List<string> _onlineUsers = new List<string>();

        private string _currentKey = EveryoneKey; // cuộc trò chuyện đang hiển thị trong khung chat
        private bool _updatingList;               // true khi đang chủ động sửa ListBox (bỏ qua SelectedIndexChanged)

        // Mục trong danh sách. Name == null nghĩa là "Everyone" (gửi cho tất cả).
        private sealed class RecipientItem
        {
            public string? Name { get; init; }
            public bool Online { get; init; } = true;
            public int Unread { get; set; }
            public string Key => Name ?? EveryoneKey;

            public override string ToString()
            {
                string text = Name ?? "[ Everyone ]";
                if (Unread > 0) text += $" ({Unread})";
                if (!Online) text += " - offline";
                return text;
            }
        }

        private static readonly RecipientItem EveryoneItem = new RecipientItem();

        private RecipientItem SelectedRecipient =>
            lstOnlineUsers.SelectedItem as RecipientItem ?? EveryoneItem;

        public MainForm()
        {
            InitializeComponent();

            // Register network events to update the GUI safely
            _chatClient.OnOnlineListUpdated += ChatClient_OnOnlineListUpdated;
            _chatClient.OnMessageReceived += ChatClient_OnMessageReceived;
            _chatClient.OnDisconnected += ChatClient_OnDisconnected;
            _chatClient.FileClient.OnProgress += FileClient_OnProgress;
            _chatClient.FileClient.OnFileReceived += FileClient_OnFileReceived;

            // Hộp thoại xác nhận chứng chỉ TLS của server ở lần kết nối đầu tiên.
            SecureConnection.ConfirmNewServer = ConfirmServerCertificate;

            SetConnected(false);
        }

        // Được gọi từ luồng mạng khi gặp một server chưa từng tin tưởng.
        private bool ConfirmServerCertificate(string host, string fingerprint)
        {
            if (IsDisposed || !IsHandleCreated) return false;

            Func<bool> ask = () => MessageBox.Show(
                this,
                $"You are connecting to \"{host}\" for the first time.\n\n" +
                "Its certificate fingerprint (SHA-256) is:\n\n" +
                $"{fingerprint}\n\n" +
                "Compare it with the fingerprint shown in the SERVER console. " +
                "Only continue if they are identical.\n\nTrust this server?",
                "Unknown server certificate",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;

            try
            {
                return InvokeRequired ? (bool)Invoke(ask) : ask();
            }
            catch (ObjectDisposedException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        // Luôn đẩy về UI thread bằng BeginInvoke (giữ đúng thứ tự, không ném lỗi khi form đã đóng).
        private void RunOnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try { BeginInvoke(action); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // ------------------------------------------------------------------ lịch sử chat

        private static string ToConversation(string key) =>
            key == EveryoneKey ? LocalStore.EveryoneConversation : key;

        private static string ToKey(string conversation) =>
            conversation == LocalStore.EveryoneConversation ? EveryoneKey : conversation;

        // Một dòng hiển thị: "[14:05] [Me]: hello"
        private string FormatRecord(ChatRecord r)
        {
            string time = r.TimeUtc.ToLocalTime().ToString("HH:mm");

            if (r.IsSystem)
                return $"[{time}] [System] {r.Text}";

            string who = r.Sender.Equals(_chatClient.Username, StringComparison.OrdinalIgnoreCase)
                ? "Me"
                : r.Sender;

            return $"[{time}] [{who}]: {r.Text}";
        }

        // Chỉ vẽ một dòng lên khung chat (không lưu lịch sử).
        private void AppendToView(string text)
        {
            rtbChatHistory.AppendText(text + Environment.NewLine);
            rtbChatHistory.SelectionStart = rtbChatHistory.TextLength;
            rtbChatHistory.ScrollToCaret(); // Auto-scroll to latest message
        }

        // Vẽ lại cả khung chat từ một danh sách tin.
        private void RenderConversation(List<ChatRecord>? list)
        {
            rtbChatHistory.Clear();

            if (list == null || list.Count == 0) return;

            rtbChatHistory.Text = string.Join(Environment.NewLine, list.Select(FormatRecord)) + Environment.NewLine;
            rtbChatHistory.SelectionStart = rtbChatHistory.TextLength;
            rtbChatHistory.ScrollToCaret();
        }

        // Thêm một dòng vào lịch sử của cuộc trò chuyện `key`; persist=true thì ghi luôn xuống đĩa.
        // Đang mở cuộc đó -> hiện ngay; ngược lại chỉ lưu và tăng số tin chưa đọc.
        private void AddRecord(string key, string sender, string text, bool isSystem, bool persist)
        {
            var record = new ChatRecord
            {
                TimeUtc = DateTime.UtcNow,
                Conversation = ToConversation(key),
                Sender = sender,
                Text = text,
                IsSystem = isSystem
            };

            if (!_histories.TryGetValue(key, out List<ChatRecord>? list))
            {
                list = new List<ChatRecord>();
                _histories[key] = list;
            }
            list.Add(record);

            if (persist && _store != null && !_store.Append(record) && !_storeWarningShown)
            {
                _storeWarningShown = true;
                AppendSystem("Warning: could not save messages to disk.");
            }

            if (string.Equals(key, _currentKey, StringComparison.OrdinalIgnoreCase))
            {
                AppendToView(FormatRecord(record));
            }
            else
            {
                _unread[key] = _unread.GetValueOrDefault(key) + 1;
                RefreshItem(key);
            }
        }

        // Tin nhắn của người dùng (gửi đi hoặc nhận về): luôn được lưu.
        private void AddMessage(string key, string sender, string text) =>
            AddRecord(key, sender, text, isSystem: false, persist: true);

        // Thông báo hệ thống: mặc định vào cuộc trò chuyện đang mở và KHÔNG lưu (trừ khi persist = true).
        private void AppendSystem(string text, string? key = null, bool persist = false) =>
            AddRecord(key ?? _currentKey, "System", text, isSystem: true, persist: persist);

        // Cập nhật chữ "(n)" của một mục trong danh sách.
        private void RefreshItem(string key)
        {
            for (int i = 0; i < lstOnlineUsers.Items.Count; i++)
            {
                if (lstOnlineUsers.Items[i] is RecipientItem item &&
                    string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    item.Unread = _unread.GetValueOrDefault(key);

                    bool wasSelected = lstOnlineUsers.SelectedIndex == i;
                    _updatingList = true;
                    try
                    {
                        lstOnlineUsers.Items[i] = item; // ép ListBox vẽ lại chữ
                        if (wasSelected) lstOnlineUsers.SelectedIndex = i;
                    }
                    finally
                    {
                        _updatingList = false;
                    }
                    return;
                }
            }
        }

        // Hiển thị lịch sử của mục đang chọn (nếu khác cuộc đang hiển thị).
        private void ShowSelectedConversation()
        {
            string key = SelectedRecipient.Key;
            if (string.Equals(key, _currentKey, StringComparison.OrdinalIgnoreCase)) return;

            _currentKey = key;
            _histories.TryGetValue(key, out List<ChatRecord>? list);
            RenderConversation(list);

            if (_unread.Remove(key))
                RefreshItem(key);
        }

        // Nạp lịch sử cũ từ đĩa sau khi đăng nhập.
        private void LoadHistory()
        {
            if (_store == null) return;

            Dictionary<string, List<ChatRecord>> fromDisk = _store.LoadAll();

            // Tin đến trong khoảnh khắc giữa lúc đăng nhập và lúc nạp lịch sử: lưu xuống đĩa rồi gộp vào cuối.
            var arrivedMeanwhile = _histories.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

            foreach (List<ChatRecord> list in arrivedMeanwhile.Values)
                foreach (ChatRecord record in list)
                    if (!record.IsSystem) _store.Append(record);

            _histories.Clear();

            foreach (var pair in fromDisk)
                _histories[ToKey(pair.Key)] = pair.Value;

            foreach (var pair in arrivedMeanwhile)
            {
                if (_histories.TryGetValue(pair.Key, out List<ChatRecord>? loaded))
                    loaded.AddRange(pair.Value);
                else
                    _histories[pair.Key] = pair.Value;
            }

            _currentKey = EveryoneKey;
            _histories.TryGetValue(EveryoneKey, out List<ChatRecord>? everyone);
            RenderConversation(everyone);

            // Những người đã từng chat nhưng đang offline cũng hiện trong danh sách để xem lại tin cũ.
            RebuildUserList();
        }

        // ------------------------------------------------------------------ trạng thái giao diện

        // Bật/tắt các control theo trạng thái kết nối.
        private void SetConnected(bool connected)
        {
            _isConnected = connected;

            btnConnect.Enabled = !connected;
            btnDisconnect.Enabled = connected;
            btnDiscover.Enabled = !connected;
            btnRegister.Enabled = !connected;
            txtServerIP.Enabled = !connected;
            txtUsername.Enabled = !connected;
            txtPassword.Enabled = !connected;
            btnOpenFolder.Enabled = connected && _store != null;

            txtMessage.Enabled = connected;
            btnSend.Enabled = connected;
            btnSendFile.Enabled = connected;

            if (!connected)
            {
                _transferBusy = false;
                btnCancelTransfer.Enabled = false;
                progressFile.Value = 0;
                lblFileStatus.Text = "File Status";

                _onlineUsers = new List<string>();

                _updatingList = true;
                lstOnlineUsers.Items.Clear();
                _updatingList = false;
                ShowSelectedConversation(); // về lại khung Everyone
            }

            UpdateTargetHint();
        }

        // 1. Automatically discover Server via UDP broadcast
        private async void btnDiscover_Click(object sender, EventArgs e)
        {
            btnDiscover.Enabled = false;
            try
            {
                var result = await UdpDiscoveryClient.DiscoverServerAsync();
                if (result.HasValue)
                {
                    txtServerIP.Text = result.Value.IP;
                    MessageBox.Show($"Server found: {result.Value.IP}");
                }
                else
                {
                    MessageBox.Show("No Server found in the LAN network.");
                }
            }
            finally
            {
                btnDiscover.Enabled = !_isConnected;
            }
        }

        // 2. Login / Register
        private async void btnConnect_Click(object sender, EventArgs e) => await ConnectAsync(register: false);

        private async void btnRegister_Click(object sender, EventArgs e) => await ConnectAsync(register: true);

        private async Task ConnectAsync(bool register)
        {
            string ip = txtServerIP.Text.Trim();
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Text;

            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter IP, Username and Password!");
                return;
            }

            if (username.Length > MaxUsernameLength || username.Contains('|') || username.Contains(','))
            {
                MessageBox.Show($"Invalid username: max {MaxUsernameLength} characters, and '|' or ',' are not allowed.");
                return;
            }

            if (register && (password.Length < MinPasswordLength || password.Length > MaxPasswordLength))
            {
                MessageBox.Show($"Password must be {MinPasswordLength}-{MaxPasswordLength} characters.");
                return;
            }

            // chặn bấm liên tiếp trong lúc đang kết nối
            btnConnect.Enabled = false;
            btnRegister.Enabled = false;

            // Phiên mới: xóa dữ liệu trong bộ nhớ của phiên trước (dữ liệu trên đĩa vẫn còn).
            _histories.Clear();
            _unread.Clear();
            _store = null;
            _storeWarningShown = false;
            _onlineUsers = new List<string>();
            _currentKey = EveryoneKey;
            rtbChatHistory.Clear();

            string res = await _chatClient.ConnectAndLoginAsync(ip, 5000, username, password, register);

            if (res == "SUCCESS")
            {
                txtPassword.Clear(); // không giữ mật khẩu trên giao diện

                // Tạo (hoặc mở lại) thư mục riêng của người dùng: ClientData\<username>\{Messages, ReceivedFiles}
                string? storeError = null;
                try
                {
                    _store = new LocalStore(_chatClient.Username);
                    _chatClient.FileClient.ReceiveFolder = _store.ReceivedFilesFolder;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _store = null;
                    _chatClient.FileClient.ReceiveFolder = null;
                    storeError = ex.Message;
                }

                SetConnected(true);
                LoadHistory(); // nạp lại tin nhắn cũ từ đĩa

                AppendSystem(register
                    ? $"Account created. Connected as {_chatClient.Username} (encrypted with TLS)."
                    : $"Connected as {_chatClient.Username} (encrypted with TLS).");

                if (_store != null)
                    AppendSystem($"Your folder: {_store.UserFolder}");
                else
                    AppendSystem($"Warning: could not create your folder ({storeError}). Messages will not be saved.");

                if (!_chatClient.FileClient.IsConnected)
                {
                    AppendSystem("Warning: file channel is unavailable, sending files will not work. " +
                                 "Try Disconnect and Login again.");
                }
            }
            else
            {
                btnConnect.Enabled = true;
                btnRegister.Enabled = true;
                MessageBox.Show(res, register ? "Register failed" : "Login failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 3. Disconnect cleanly
        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            _chatClient.Disconnect();
            SetConnected(false);
            AppendSystem("Disconnected.");
        }

        // Mở thư mục của người dùng trong Windows Explorer
        private void btnOpenFolder_Click(object sender, EventArgs e)
        {
            if (_store == null) return;

            try
            {
                Process.Start(new ProcessStartInfo(_store.UserFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the folder: {ex.Message}");
            }
        }

        // Server đóng kết nối / mạng đứt
        private void ChatClient_OnDisconnected(object? sender, string reason)
        {
            RunOnUi(() =>
            {
                SetConnected(false);
                AppendSystem($"Disconnected from server: {reason}");
            });
        }

        // 4. Update user list: Everyone + người đang online + người đã từng chat nhưng đang offline
        private void ChatClient_OnOnlineListUpdated(object? sender, List<string> users)
        {
            RunOnUi(() =>
            {
                // Dùng IsConnected của client (đã true trước khi vòng lặp đọc chạy) thay vì _isConnected:
                // danh sách online đầu tiên có thể tới trước khi SetConnected(true) kịp chạy.
                if (!_chatClient.IsConnected) return;

                string me = _chatClient.Username;
                string? previous = (lstOnlineUsers.SelectedItem as RecipientItem)?.Name;

                List<string> oldOnline = _onlineUsers;
                _onlineUsers = users
                    .Where(u => !u.Equals(me, StringComparison.OrdinalIgnoreCase)) // không tự chat với chính mình
                    .ToList();

                // Người đang chat riêng vừa thoát: ghi chú vào đúng cuộc trò chuyện đó.
                if (previous != null &&
                    oldOnline.Contains(previous, StringComparer.OrdinalIgnoreCase) &&
                    !_onlineUsers.Contains(previous, StringComparer.OrdinalIgnoreCase))
                {
                    AppendSystem($"{previous} went offline.", previous);
                }

                RebuildUserList();
            });
        }

        private void RebuildUserList()
        {
            if (!_chatClient.IsConnected) return;

            string? previous = (lstOnlineUsers.SelectedItem as RecipientItem)?.Name;

            _updatingList = true;
            lstOnlineUsers.BeginUpdate();
            try
            {
                lstOnlineUsers.Items.Clear();
                EveryoneItem.Unread = _unread.GetValueOrDefault(EveryoneKey);
                lstOnlineUsers.Items.Add(EveryoneItem);

                int selectIndex = 0;

                void AddUser(string name, bool online)
                {
                    int index = lstOnlineUsers.Items.Add(new RecipientItem
                    {
                        Name = name,
                        Online = online,
                        Unread = _unread.GetValueOrDefault(name)
                    });

                    if (previous != null && name.Equals(previous, StringComparison.OrdinalIgnoreCase))
                        selectIndex = index; // giữ nguyên lựa chọn sau khi danh sách làm mới
                }

                foreach (string user in _onlineUsers)
                    AddUser(user, online: true);

                // Người đã có lịch sử chat nhưng hiện không online.
                IEnumerable<string> offline = _histories.Keys
                    .Where(k => k != EveryoneKey && !_onlineUsers.Contains(k, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

                foreach (string user in offline)
                    AddUser(user, online: false);

                lstOnlineUsers.SelectedIndex = selectIndex;
            }
            finally
            {
                lstOnlineUsers.EndUpdate();
                _updatingList = false;
            }

            UpdateTargetHint();
            ShowSelectedConversation();
        }

        private void lstOnlineUsers_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_updatingList) return;

            UpdateTargetHint();
            ShowSelectedConversation();
        }

        private void UpdateTargetHint()
        {
            RecipientItem target = SelectedRecipient;

            txtMessage.PlaceholderText = !_isConnected
                ? string.Empty
                : target.Name == null
                    ? "Message to everyone..."
                    : target.Online
                        ? $"Private message to {target.Name}..."
                        : $"{target.Name} is offline";
        }

        // 5. Handle incoming chat messages
        private void ChatClient_OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            RunOnUi(() =>
            {
                switch (e.Type)
                {
                    case "PUBLIC": // vào cuộc trò chuyện chung
                        AddMessage(EveryoneKey, e.Sender, e.Content);
                        break;
                    case "PRIVATE": // vào cuộc trò chuyện riêng với người gửi
                        AddMessage(e.Sender, e.Sender, e.Content);
                        break;
                    default: // ERROR / SYSTEM
                        AppendSystem(e.Content);
                        break;
                }
            });
        }

        // 6. Update file transfer progress bar
        private void FileClient_OnProgress(object? sender, FileProgressEventArgs e)
        {
            RunOnUi(() =>
            {
                if (!_isConnected) return;

                progressFile.Value = e.Status == "Error" || e.Status == "Cancelled" ? 0 : e.Percent;

                lblFileStatus.Text = string.IsNullOrEmpty(e.Message)
                    ? $"{e.Status}: {e.Percent}% ({e.FileName})"
                    : e.Message;

                _transferBusy = e.IsBusy;
                btnCancelTransfer.Enabled = e.IsBusy;
            });
        }

        // Thông báo nhận file: thuộc cuộc trò chuyện với người gửi và được lưu lại cùng lịch sử.
        private void FileClient_OnFileReceived(object? sender, MessageReceivedEventArgs e)
        {
            RunOnUi(() => AppendSystem(e.Content, e.Sender, persist: true));
        }

        // 7. Send text message (Public or Private)
        private async void btnSend_Click(object sender, EventArgs e)
        {
            string msg = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(msg)) return;

            if (msg.Contains('|'))
            {
                MessageBox.Show("The '|' character is not allowed in messages.");
                return;
            }

            if (msg.Length > MaxMessageLength)
            {
                MessageBox.Show($"Message is too long (max {MaxMessageLength} characters).");
                return;
            }

            RecipientItem target = SelectedRecipient;

            if (target.Name != null && !target.Online)
            {
                AppendSystem($"{target.Name} is offline. You can only send messages to users who are online.");
                return;
            }

            try
            {
                if (target.Name != null)
                {
                    // Send private message to the selected user
                    await _chatClient.SendPrivateMessageAsync(target.Name, msg);
                    AddMessage(target.Name, _chatClient.Username, msg);
                }
                else
                {
                    // Send public broadcast message
                    await _chatClient.SendPublicMessageAsync(msg);
                    AddMessage(EveryoneKey, _chatClient.Username, msg); // Show own public message
                }

                txtMessage.Clear();
            }
            catch (Exception ex)
            {
                AppendSystem($"Could not send the message: {ex.Message}");
            }

            txtMessage.Focus();
        }

        // 8. Select and send a file to the selected user
        private async void btnSendFile_Click(object sender, EventArgs e)
        {
            RecipientItem target = SelectedRecipient;

            if (target.Name == null)
            {
                MessageBox.Show("Please select a specific user (not \"Everyone\") to send a file!");
                return;
            }

            if (!target.Online)
            {
                MessageBox.Show($"{target.Name} is offline. You can only send files to users who are online.");
                return;
            }

            if (!_chatClient.FileClient.IsConnected)
            {
                MessageBox.Show("The file channel is not connected yet. Please wait a moment or reconnect.");
                return;
            }

            using OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() != DialogResult.OK) return;

            try
            {
                await _chatClient.FileClient.SendFileAsync(target.Name, ofd.FileName);
            }
            catch (Exception ex)
            {
                AppendSystem($"Could not send the file: {ex.Message}");
            }
        }

        // 9. Cancel the running file transfer (send or receive)
        private void btnCancelTransfer_Click(object sender, EventArgs e)
        {
            btnCancelTransfer.Enabled = false;
            _chatClient.FileClient.CancelCurrentTransfer();
        }

        // 10. Close the window cleanly
        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_transferBusy)
            {
                var answer = MessageBox.Show(
                    "A file transfer is still in progress. Close anyway?",
                    "Transfer in progress",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            SecureConnection.ConfirmNewServer = null;
            _chatClient.Disconnect();
        }
    }
}
