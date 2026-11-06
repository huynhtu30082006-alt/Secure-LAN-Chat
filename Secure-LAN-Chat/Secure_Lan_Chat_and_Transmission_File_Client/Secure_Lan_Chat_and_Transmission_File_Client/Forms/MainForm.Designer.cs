namespace Secure_Lan_Chat___Transmission_File_Client.Forms
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.txtServerIP = new System.Windows.Forms.TextBox();
            this.txtUsername = new System.Windows.Forms.TextBox();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.btnRegister = new System.Windows.Forms.Button();
            this.btnOpenFolder = new System.Windows.Forms.Button();
            this.btnDiscover = new System.Windows.Forms.Button();
            this.btnConnect = new System.Windows.Forms.Button();
            this.btnDisconnect = new System.Windows.Forms.Button();
            this.lstOnlineUsers = new System.Windows.Forms.ListBox();
            this.rtbChatHistory = new System.Windows.Forms.RichTextBox();
            this.txtMessage = new System.Windows.Forms.TextBox();
            this.btnSend = new System.Windows.Forms.Button();
            this.btnSendFile = new System.Windows.Forms.Button();
            this.progressFile = new System.Windows.Forms.ProgressBar();
            this.btnCancelTransfer = new System.Windows.Forms.Button();
            this.lblFileStatus = new System.Windows.Forms.Label();
            this.SuspendLayout();

            // txtServerIP
            this.txtServerIP.Location = new System.Drawing.Point(12, 12);
            this.txtServerIP.Name = "txtServerIP";
            this.txtServerIP.Size = new System.Drawing.Size(130, 23);
            this.txtServerIP.TabIndex = 0;
            this.txtServerIP.PlaceholderText = "Server IP";

            // btnDiscover
            this.btnDiscover.Location = new System.Drawing.Point(148, 11);
            this.btnDiscover.Name = "btnDiscover";
            this.btnDiscover.Size = new System.Drawing.Size(70, 25);
            this.btnDiscover.TabIndex = 1;
            this.btnDiscover.Text = "Discover";
            this.btnDiscover.UseVisualStyleBackColor = true;
            this.btnDiscover.Click += new System.EventHandler(this.btnDiscover_Click);

            // txtUsername
            this.txtUsername.Location = new System.Drawing.Point(226, 12);
            this.txtUsername.Name = "txtUsername";
            this.txtUsername.Size = new System.Drawing.Size(140, 23);
            this.txtUsername.TabIndex = 2;
            this.txtUsername.PlaceholderText = "Username";

            // txtPassword
            this.txtPassword.Location = new System.Drawing.Point(372, 12);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.PlaceholderText = "Password";
            this.txtPassword.Size = new System.Drawing.Size(140, 23);
            this.txtPassword.TabIndex = 3;
            this.txtPassword.UseSystemPasswordChar = true;

            // btnConnect
            this.btnConnect.Location = new System.Drawing.Point(12, 43);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(85, 25);
            this.btnConnect.TabIndex = 4;
            this.btnConnect.Text = "Login";
            this.btnConnect.UseVisualStyleBackColor = true;
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);

            // btnRegister
            this.btnRegister.Location = new System.Drawing.Point(103, 43);
            this.btnRegister.Name = "btnRegister";
            this.btnRegister.Size = new System.Drawing.Size(85, 25);
            this.btnRegister.TabIndex = 5;
            this.btnRegister.Text = "Register";
            this.btnRegister.UseVisualStyleBackColor = true;
            this.btnRegister.Click += new System.EventHandler(this.btnRegister_Click);

            // btnDisconnect
            this.btnDisconnect.Enabled = false;
            this.btnDisconnect.Location = new System.Drawing.Point(194, 43);
            this.btnDisconnect.Name = "btnDisconnect";
            this.btnDisconnect.Size = new System.Drawing.Size(95, 25);
            this.btnDisconnect.TabIndex = 6;
            this.btnDisconnect.Text = "Disconnect";
            this.btnDisconnect.UseVisualStyleBackColor = true;
            this.btnDisconnect.Click += new System.EventHandler(this.btnDisconnect_Click);

            // btnOpenFolder
            this.btnOpenFolder.Enabled = false;
            this.btnOpenFolder.Location = new System.Drawing.Point(295, 43);
            this.btnOpenFolder.Name = "btnOpenFolder";
            this.btnOpenFolder.Size = new System.Drawing.Size(110, 25);
            this.btnOpenFolder.TabIndex = 7;
            this.btnOpenFolder.Text = "My Folder";
            this.btnOpenFolder.UseVisualStyleBackColor = true;
            this.btnOpenFolder.Click += new System.EventHandler(this.btnOpenFolder_Click);

            // lstOnlineUsers
            this.lstOnlineUsers.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lstOnlineUsers.FormattingEnabled = true;
            this.lstOnlineUsers.ItemHeight = 15;
            this.lstOnlineUsers.Location = new System.Drawing.Point(500, 78);
            this.lstOnlineUsers.Name = "lstOnlineUsers";
            this.lstOnlineUsers.Size = new System.Drawing.Size(188, 276);
            this.lstOnlineUsers.TabIndex = 5;
            this.lstOnlineUsers.SelectedIndexChanged += new System.EventHandler(this.lstOnlineUsers_SelectedIndexChanged);

            // rtbChatHistory
            this.rtbChatHistory.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbChatHistory.Location = new System.Drawing.Point(12, 78);
            this.rtbChatHistory.Name = "rtbChatHistory";
            this.rtbChatHistory.ReadOnly = true;
            this.rtbChatHistory.Size = new System.Drawing.Size(482, 276);
            this.rtbChatHistory.TabIndex = 6;
            this.rtbChatHistory.Text = "";

            // txtMessage
            this.txtMessage.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtMessage.Enabled = false;
            this.txtMessage.Location = new System.Drawing.Point(12, 368);
            this.txtMessage.Name = "txtMessage";
            this.txtMessage.Size = new System.Drawing.Size(401, 23);
            this.txtMessage.TabIndex = 7;

            // btnSend
            this.btnSend.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSend.Enabled = false;
            this.btnSend.Location = new System.Drawing.Point(419, 367);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(75, 25);
            this.btnSend.TabIndex = 8;
            this.btnSend.Text = "Send";
            this.btnSend.UseVisualStyleBackColor = true;
            this.btnSend.Click += new System.EventHandler(this.btnSend_Click);

            // btnSendFile
            this.btnSendFile.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSendFile.Enabled = false;
            this.btnSendFile.Location = new System.Drawing.Point(500, 367);
            this.btnSendFile.Name = "btnSendFile";
            this.btnSendFile.Size = new System.Drawing.Size(188, 25);
            this.btnSendFile.TabIndex = 9;
            this.btnSendFile.Text = "Send File";
            this.btnSendFile.UseVisualStyleBackColor = true;
            this.btnSendFile.Click += new System.EventHandler(this.btnSendFile_Click);

            // progressFile
            this.progressFile.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressFile.Location = new System.Drawing.Point(12, 405);
            this.progressFile.Name = "progressFile";
            this.progressFile.Size = new System.Drawing.Size(401, 23);
            this.progressFile.TabIndex = 10;

            // btnCancelTransfer
            this.btnCancelTransfer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancelTransfer.Enabled = false;
            this.btnCancelTransfer.Location = new System.Drawing.Point(419, 404);
            this.btnCancelTransfer.Name = "btnCancelTransfer";
            this.btnCancelTransfer.Size = new System.Drawing.Size(75, 25);
            this.btnCancelTransfer.TabIndex = 11;
            this.btnCancelTransfer.Text = "Cancel";
            this.btnCancelTransfer.UseVisualStyleBackColor = true;
            this.btnCancelTransfer.Click += new System.EventHandler(this.btnCancelTransfer_Click);

            // lblFileStatus
            this.lblFileStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.lblFileStatus.AutoEllipsis = true;
            this.lblFileStatus.AutoSize = false;
            this.lblFileStatus.Location = new System.Drawing.Point(500, 409);
            this.lblFileStatus.Name = "lblFileStatus";
            this.lblFileStatus.Size = new System.Drawing.Size(188, 20);
            this.lblFileStatus.TabIndex = 12;
            this.lblFileStatus.Text = "File Status";

            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(700, 440);
            this.Controls.Add(this.lblFileStatus);
            this.Controls.Add(this.btnCancelTransfer);
            this.Controls.Add(this.progressFile);
            this.Controls.Add(this.btnSendFile);
            this.Controls.Add(this.btnSend);
            this.Controls.Add(this.txtMessage);
            this.Controls.Add(this.rtbChatHistory);
            this.Controls.Add(this.lstOnlineUsers);
            this.Controls.Add(this.btnDisconnect);
            this.Controls.Add(this.btnRegister);
            this.Controls.Add(this.btnOpenFolder);
            this.Controls.Add(this.txtPassword);
            this.Controls.Add(this.btnConnect);
            this.Controls.Add(this.txtUsername);
            this.Controls.Add(this.btnDiscover);
            this.Controls.Add(this.txtServerIP);
            this.MinimumSize = new System.Drawing.Size(716, 479);
            this.Name = "MainForm";
            this.Text = "Secure LAN Chat & File Transfer Client (TLS)";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.TextBox txtServerIP;
        private System.Windows.Forms.TextBox txtUsername;
        private System.Windows.Forms.TextBox txtPassword;
        private System.Windows.Forms.Button btnRegister;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.Button btnDiscover;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.Button btnDisconnect;
        private System.Windows.Forms.ListBox lstOnlineUsers;
        private System.Windows.Forms.RichTextBox rtbChatHistory;
        private System.Windows.Forms.TextBox txtMessage;
        private System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.Button btnSendFile;
        private System.Windows.Forms.ProgressBar progressFile;
        private System.Windows.Forms.Button btnCancelTransfer;
        private System.Windows.Forms.Label lblFileStatus;
    }
}
