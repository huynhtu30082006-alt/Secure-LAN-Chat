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
        public string Status { get; set; } = string.Empty;
    }
}