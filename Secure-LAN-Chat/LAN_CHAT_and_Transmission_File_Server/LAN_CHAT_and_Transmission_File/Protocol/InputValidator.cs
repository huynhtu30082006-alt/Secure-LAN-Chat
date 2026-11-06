namespace LanChatServer;

public static class InputValidator
{
    public static bool IsValidUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username) ||
            username.Length > ServerConfig.MaxUsernameLength)
        {
            return false;
        }

        return !username.Any(c =>
            char.IsControl(c) ||
            c == '|' ||
            c == ',');
    }

    // Đăng ký: 8-64 ký tự, không ký tự điều khiển, không trùng username.
    public static bool IsValidNewPassword(string password, string username)
    {
        if (password.Length < ServerConfig.MinPasswordLength ||
            password.Length > ServerConfig.MaxPasswordLength ||
            string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        if (password.Any(char.IsControl))
            return false;

        return !password.Equals(username, StringComparison.OrdinalIgnoreCase);
    }

    // Đăng nhập: chỉ chặn đầu vào bất thường (rỗng / quá dài / ký tự điều khiển).
    public static bool IsValidLoginPassword(string password) =>
        password.Length > 0 &&
        password.Length <= ServerConfig.MaxPasswordLength &&
        !password.Any(char.IsControl);

    public static bool IsValidMessageContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content) ||
            content.Length > ServerConfig.MaxMessageLength)
        {
            return false;
        }

        return !content.Any(c =>
            c == '|' ||
            c == '\r' ||
            c == '\n' ||
            char.IsControl(c));
    }
}