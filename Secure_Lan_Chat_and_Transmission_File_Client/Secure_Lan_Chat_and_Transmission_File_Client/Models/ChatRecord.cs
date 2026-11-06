using System;
using System.Text.Json.Serialization;

namespace Secure_Lan_Chat___Transmission_File_Client.Models
{
    /// <summary>Một dòng trong lịch sử chat (cũng là một dòng trong file .jsonl trên đĩa).</summary>
    public sealed class ChatRecord
    {
        [JsonPropertyName("time")]
        public DateTime TimeUtc { get; set; }

        // "*" = cuộc trò chuyện chung (Everyone); ngược lại là username của người chat riêng.
        [JsonPropertyName("conversation")]
        public string Conversation { get; set; } = string.Empty;

        [JsonPropertyName("sender")]
        public string Sender { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        // true: thông báo hệ thống (ví dụ "đã nhận file ...").
        [JsonPropertyName("system")]
        public bool IsSystem { get; set; }
    }
}
