namespace LanChatServer;

public class MessageRouter
{
    private readonly ClientRegistry _registry;

    public MessageRouter(ClientRegistry registry)
    {
        _registry = registry;
    }

    public async Task ProcessAsync(ClientSession sender, string message)
    {
        string[] parts = message.Split('|');
        string command = parts[0].ToUpperInvariant();

        switch (command)
        {
            case "CHAT":
                await HandleChatAsync(sender, parts);
                break;

            case "PRIVATE":
                await HandlePrivateAsync(sender, parts);
                break;

            case "ONLINE_LIST":
                if (parts.Length != 1)
                {
                    await sender.SendAsync("ERROR|INVALID_MESSAGE");
                    return;
                }

                await sender.SendAsync(_registry.BuildOnlineListMessage());
                break;

            default:
                await sender.SendAsync("ERROR|UNKNOWN_COMMAND");
                break;
        }
    }

    // CHAT|content  ->  CHAT|sender|content (gửi cho người khác)
    private async Task HandleChatAsync(ClientSession sender, string[] parts)
    {
        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            !InputValidator.IsValidMessageContent(parts[1]))
        {
            await sender.SendAsync("ERROR|INVALID_MESSAGE");
            return;
        }

        string content = parts[1];
        string outgoing = $"CHAT|{sender.Username}|{content}";

        foreach (ClientSession recipient in _registry.Snapshot())
        {
            if (ReferenceEquals(recipient, sender))
            {
                continue;
            }

            await recipient.TrySendAsync(outgoing);
        }

        Console.WriteLine($"[BROADCAST] {sender.Username}: {content}");
    }

    // PRIVATE|recipient|content  ->  PRIVATE|sender|content
    private async Task HandlePrivateAsync(ClientSession sender, string[] parts)
    {
        if (parts.Length != 3 ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            string.IsNullOrWhiteSpace(parts[2]) ||
            !InputValidator.IsValidUsername(parts[1]) ||
            !InputValidator.IsValidMessageContent(parts[2]))
        {
            await sender.SendAsync("ERROR|INVALID_MESSAGE");
            return;
        }

        string recipientName = parts[1];
        string content = parts[2];

        if (!_registry.TryGet(recipientName, out ClientSession? recipient))
        {
            await sender.SendAsync("ERROR|USER_NOT_ONLINE");
            return;
        }

        bool delivered = await recipient.TrySendAsync(
            $"PRIVATE|{sender.Username}|{content}"
        );

        if (!delivered)
        {
            await sender.SendAsync("ERROR|USER_NOT_ONLINE");
            return;
        }

        await sender.SendAsync($"PRIVATE_SENT|{recipient.Username}");

        Console.WriteLine($"[PRIVATE] {sender.Username} -> {recipient.Username}");
    }
}