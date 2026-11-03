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
            this.btnDiscover = new System.Windows.Forms.Button();
            this.btnConnect = new System.Windows.Forms.Button();
            this.lstOnlineUsers = new System.Windows.Forms.ListBox();
            this.rtbChatHistory = new System.Windows.Forms.RichTextBox();
            this.txtMessage = new System.Windows.Forms.TextBox();
            this.btnSend = new System.Windows.Forms.Button();
            this.btnSendFile = new System.Windows.Forms.Button();
            this.progressFile = new System.Windows.Forms.ProgressBar();
            this.lblFileStatus = new System.Windows.Forms.Label();
            this.SuspendLayout();

            // txtServerIP
            this.txtServerIP.Location = new System.Drawing.Point(12, 12);
            this.txtServerIP.Name = "txtServerIP";
            this.txtServerIP.Size = new System.Drawing.Size(150, 23);
            this.txtServerIP.TabIndex = 0;
            this.txtServerIP.PlaceholderText = "Server IP";

            // btnDiscover
            this.btnDiscover.Location = new System.Drawing.Point(168, 11);
            this.btnDiscover.Name = "btnDiscover";
            this.btnDiscover.Size = new System.Drawing.Size(75, 25);
            this.btnDiscover.TabIndex = 1;
            this.btnDiscover.Text = "Discover";
            this.btnDiscover.UseVisualStyleBackColor = true;
            this.btnDiscover.Click += new System.EventHandler(this.btnDiscover_Click);

            // txtUsername
            this.txtUsername.Location = new System.Drawing.Point(255, 12);
            this.txtUsername.Name = "txtUsername";
            this.txtUsername.Size = new System.Drawing.Size(120, 23);
            this.txtUsername.TabIndex = 2;
            this.txtUsername.PlaceholderText = "Username";

            // btnConnect
            this.btnConnect.Location = new System.Drawing.Point(381, 11);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(75, 25);
            this.btnConnect.TabIndex = 3;
            this.btnConnect.Text = "Connect";
            this.btnConnect.UseVisualStyleBackColor = true;
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);

            // lstOnlineUsers
            this.lstOnlineUsers.FormattingEnabled = true;
            this.lstOnlineUsers.ItemHeight = 15;
            this.lstOnlineUsers.Location = new System.Drawing.Point(480, 50);
            this.lstOnlineUsers.Name = "lstOnlineUsers";
            this.lstOnlineUsers.Size = new System.Drawing.Size(180, 304);
            this.lstOnlineUsers.TabIndex = 4;

            // rtbChatHistory
            this.rtbChatHistory.Location = new System.Drawing.Point(12, 50);
            this.rtbChatHistory.Name = "rtbChatHistory";
            this.rtbChatHistory.ReadOnly = true;
            this.rtbChatHistory.Size = new System.Drawing.Size(462, 304);
            this.rtbChatHistory.TabIndex = 5;
            this.rtbChatHistory.Text = "";

            // txtMessage
            this.txtMessage.Location = new System.Drawing.Point(12, 368);
            this.txtMessage.Name = "txtMessage";
            this.txtMessage.Size = new System.Drawing.Size(381, 23);
            this.txtMessage.TabIndex = 6;

            // btnSend
            this.btnSend.Location = new System.Drawing.Point(399, 367);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(75, 25);
            this.btnSend.TabIndex = 7;
            this.btnSend.Text = "Send";
            this.btnSend.UseVisualStyleBackColor = true;
            this.btnSend.Click += new System.EventHandler(this.btnSend_Click);

            // btnSendFile
            this.btnSendFile.Location = new System.Drawing.Point(480, 367);
            this.btnSendFile.Name = "btnSendFile";
            this.btnSendFile.Size = new System.Drawing.Size(180, 25);
            this.btnSendFile.TabIndex = 8;
            this.btnSendFile.Text = "Send File";
            this.btnSendFile.UseVisualStyleBackColor = true;
            this.btnSendFile.Click += new System.EventHandler(this.btnSendFile_Click);

            // progressFile
            this.progressFile.Location = new System.Drawing.Point(12, 405);
            this.progressFile.Name = "progressFile";
            this.progressFile.Size = new System.Drawing.Size(462, 23);
            this.progressFile.TabIndex = 9;

            // lblFileStatus
            this.lblFileStatus.AutoSize = true;
            this.lblFileStatus.Location = new System.Drawing.Point(480, 409);
            this.lblFileStatus.Name = "lblFileStatus";
            this.lblFileStatus.Size = new System.Drawing.Size(65, 15);
            this.lblFileStatus.TabIndex = 10;
            this.lblFileStatus.Text = "File Status";

            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(678, 440);
            this.Controls.Add(this.lblFileStatus);
            this.Controls.Add(this.progressFile);
            this.Controls.Add(this.btnSendFile);
            this.Controls.Add(this.btnSend);
            this.Controls.Add(this.txtMessage);
            this.Controls.Add(this.rtbChatHistory);
            this.Controls.Add(this.lstOnlineUsers);
            this.Controls.Add(this.btnConnect);
            this.Controls.Add(this.txtUsername);
            this.Controls.Add(this.btnDiscover);
            this.Controls.Add(this.txtServerIP);
            this.Name = "MainForm";
            this.Text = "Secure LAN Chat & File Transfer Client";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.TextBox txtServerIP;
        private System.Windows.Forms.TextBox txtUsername;
        private System.Windows.Forms.Button btnDiscover;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.ListBox lstOnlineUsers;
        private System.Windows.Forms.RichTextBox rtbChatHistory;
        private System.Windows.Forms.TextBox txtMessage;
        private System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.Button btnSendFile;
        private System.Windows.Forms.ProgressBar progressFile;
        private System.Windows.Forms.Label lblFileStatus;
    }
}