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