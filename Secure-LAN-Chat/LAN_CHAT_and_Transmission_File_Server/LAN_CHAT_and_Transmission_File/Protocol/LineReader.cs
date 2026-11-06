using System.Text;

namespace LanChatServer;

public static class LineReaderExtensions
{
    // ReadLineAsync gốc không có giới hạn độ dài: client gửi mãi không xuống dòng sẽ ăn hết RAM.
    // Hàm này ném InvalidDataException nếu dòng dài hơn maxLength.
    public static async Task<string?> ReadLineLimitedAsync(
        this StreamReader reader,
        int maxLength,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        char[] one = new char[1];

        while (true)
        {
            int read = await reader.ReadAsync(one.AsMemory(0, 1), cancellationToken);

            if (read == 0)
                return sb.Length == 0 ? null : sb.ToString();

            char c = one[0];

            if (c == '\n')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                    sb.Length--;

                return sb.ToString();
            }

            sb.Append(c);

            if (sb.Length > maxLength)
                throw new InvalidDataException("Line too long.");
        }
    }
}
