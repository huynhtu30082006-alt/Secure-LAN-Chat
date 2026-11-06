using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace LanChatServer;

public class ClientRegistry
{
    private readonly ConcurrentDictionary<string, ClientSession> _clients =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _clients.Count;

    public bool TryAdd(ClientSession session) =>
        _clients.TryAdd(session.Username, session);

    public bool TryGet(string username,
        [NotNullWhen(true)] out ClientSession? session) =>
        _clients.TryGetValue(username, out session);

    // Chỉ xóa nếu dictionary vẫn chứa đúng session này.
    public bool Remove(ClientSession session) =>
        ((ICollection<KeyValuePair<string, ClientSession>>)_clients)
            .Remove(new KeyValuePair<string, ClientSession>(
                session.Username, session));

    public ClientSession[] Snapshot() => _clients.Values.ToArray();

    public string BuildOnlineListMessage()
    {
        var names = _clients.Values
            .Select(c => c.Username)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        return $"ONLINE_LIST|{string.Join(",", names)}";
    }

    public async Task BroadcastOnlineListAsync()
    {
        string message = BuildOnlineListMessage();

        foreach (ClientSession client in Snapshot())
        {
            await client.TrySendAsync(message);
        }
    }
}