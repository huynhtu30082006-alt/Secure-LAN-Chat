using System;

namespace Secure_Lan_Chat___Transmission_File_Client.Models
{
    public class MessageReceivedEventArgs : EventArgs
    {
        public string Type { get; set; } = string.Empty; // PUBLIC, PRIVATE, SYSTEM, ERROR
        public string Sender { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class FileProgressEventArgs : EventArgs
    {
        public string FileName { get; set; } = string.Empty;
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }

        // Waiting, Sending, Receiving, Sent, Received, Cancelled, Error, Ready
        public string Status { get; set; } = string.Empty;

        // Nếu có thì UI hiển thị nguyên văn; nếu rỗng UI tự ghép "Status: x% (file)".
        public string Message { get; set; } = string.Empty;

        // true khi đang có một lần truyền file chạy (để bật/tắt nút Cancel).
        public bool IsBusy { get; set; }

        public int Percent => TotalBytes > 0
            ? (int)Math.Clamp(BytesTransferred * 100 / TotalBytes, 0, 100)
            : 0;
    }
}
