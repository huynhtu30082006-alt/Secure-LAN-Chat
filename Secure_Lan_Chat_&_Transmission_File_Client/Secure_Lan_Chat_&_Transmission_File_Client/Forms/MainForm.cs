using Secure_Lan_Chat___Transmission_File_Client.Services;

namespace Secure_Lan_Chat___Transmission_File_Client.Forms
{
    public partial class MainForm : Form
    {
        private readonly TcpChatClient _chatClient = new TcpChatClient();

        public MainForm()
        {
            InitializeComponent();

            // Register network events to update the GUI safely
            _chatClient.OnOnlineListUpdated += ChatClient_OnOnlineListUpdated;
            _chatClient.OnMessageReceived += ChatClient_OnMessageReceived;
            _chatClient.FileClient.OnProgress += FileClient_OnProgress;
        }

        // 1. Automatically discover Server via UDP broadcast
        private async void btnDiscover_Click(object sender, EventArgs e)
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

        // 2. Login to the chat server
        private async void btnConnect_Click(object sender, EventArgs e)
        {
            string ip = txtServerIP.Text.Trim();
            string username = txtUsername.Text.Trim();

            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(username))
            {
                MessageBox.Show("Please enter IP and Username!");
                return;
            }

            string res = await _chatClient.ConnectAndLoginAsync(ip, 5000, username);
            if (res == "SUCCESS")
            {
                MessageBox.Show("Login successful!");
                btnConnect.Enabled = false; // Disable button after successful login
            }
            else
            {
                MessageBox.Show($"Error: {res}");
            }
        }

        // 3. Update online user list (Using Invoke to prevent Cross-Thread exceptions)
        private void ChatClient_OnOnlineListUpdated(object? sender, List<string> users)
        {
            if (this.IsDisposed) return;
            this.Invoke(new Action(() =>
            {
                lstOnlineUsers.Items.Clear();
                foreach (var u in users)
                {
                    lstOnlineUsers.Items.Add(u);
                }
            }));
        }

        // 4. Handle incoming chat messages
        private void ChatClient_OnMessageReceived(object? sender, Secure_Lan_Chat___Transmission_File_Client.Models.MessageReceivedEventArgs e)
        {
            if (this.IsDisposed) return;
            this.Invoke(new Action(() =>
            {
                rtbChatHistory.AppendText($"[{e.Sender}]: {e.Content}\r\n");
                rtbChatHistory.ScrollToCaret(); // Auto-scroll to latest message
            }));
        }

        // 5. Update file transfer progress bar
        private void FileClient_OnProgress(object? sender, Secure_Lan_Chat___Transmission_File_Client.Models.FileProgressEventArgs e)
        {
            if (this.IsDisposed) return;
            this.Invoke(new Action(() =>
            {
                if (e.TotalBytes > 0)
                {
                    int percent = (int)((e.BytesTransferred * 100) / e.TotalBytes);
                    progressFile.Value = percent;
                    lblFileStatus.Text = $"{e.Status}: {percent}% ({e.FileName})";
                }
            }));
        }

        // 6. Send text message (Public or Private)
        private async void btnSend_Click(object sender, EventArgs e)
        {
            string msg = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(msg)) return;

            if (lstOnlineUsers.SelectedItem != null)
            {
                // Send private message if a user is selected
                string recipient = lstOnlineUsers.SelectedItem.ToString()!;
                await _chatClient.SendPrivateMessageAsync(recipient, msg);
                rtbChatHistory.AppendText($"[Private to {recipient}]: {msg}\r\n");
            }
            else
            {
                // Send public broadcast message
                await _chatClient.SendPublicMessageAsync(msg);
                rtbChatHistory.AppendText($"[Me]: {msg}\r\n"); // Show own public message
            }

            rtbChatHistory.ScrollToCaret();
            txtMessage.Clear();
        }

        // 7. Select and send a file to the selected user
        private async void btnSendFile_Click(object sender, EventArgs e)
        {
            if (lstOnlineUsers.SelectedItem == null)
            {
                MessageBox.Show("Please select a recipient from the Online list to send a file!");
                return;
            }

            using OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                string recipient = lstOnlineUsers.SelectedItem.ToString()!;
                await _chatClient.FileClient.SendFileAsync(recipient, ofd.FileName);
            }
        }
    }
}