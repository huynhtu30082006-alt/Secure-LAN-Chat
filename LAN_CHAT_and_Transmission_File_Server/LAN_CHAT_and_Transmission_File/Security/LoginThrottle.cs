namespace LanChatServer;

// Đếm số lần thất bại theo "key" (ví dụ "ip:1.2.3.4" hoặc "user:tam") và khóa tạm khi vượt ngưỡng.
public sealed class LoginThrottle
{
    private sealed class Entry
    {
        public int Failures;
        public DateTime WindowStartUtc;
        public DateTime LockedUntilUtc;
    }

    private readonly Dictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();
    private DateTime _lastCleanupUtc = DateTime.UtcNow;

    public bool IsLocked(string key, out int secondsRemaining)
    {
        lock (_lock)
        {
            Cleanup();

            secondsRemaining = 0;

            if (_entries.TryGetValue(key, out Entry? entry) &&
                entry.LockedUntilUtc > DateTime.UtcNow)
            {
                secondsRemaining = (int)Math.Ceiling(
                    (entry.LockedUntilUtc - DateTime.UtcNow).TotalSeconds);
                return true;
            }

            return false;
        }
    }

    public void RegisterFailure(string key, int maxFailures)
    {
        lock (_lock)
        {
            DateTime now = DateTime.UtcNow;

            if (!_entries.TryGetValue(key, out Entry? entry))
            {
                entry = new Entry { WindowStartUtc = now };
                _entries[key] = entry;
            }

            if (now - entry.WindowStartUtc > ServerConfig.FailureWindow)
            {
                entry.Failures = 0;
                entry.WindowStartUtc = now;
            }

            entry.Failures++;

            if (entry.Failures >= maxFailures)
            {
                entry.LockedUntilUtc = now + ServerConfig.LockoutDuration;
                entry.Failures = 0;
                entry.WindowStartUtc = now;
            }
        }
    }

    public void Reset(string key)
    {
        lock (_lock)
        {
            _entries.Remove(key);
        }
    }

    // Gọi bên trong lock: thỉnh thoảng dọn các mục đã hết hạn để dictionary không phình mãi.
    private void Cleanup()
    {
        DateTime now = DateTime.UtcNow;

        if (now - _lastCleanupUtc < TimeSpan.FromMinutes(1))
            return;

        _lastCleanupUtc = now;

        var expired = _entries
            .Where(p => p.Value.LockedUntilUtc < now &&
                        now - p.Value.WindowStartUtc > ServerConfig.FailureWindow)
            .Select(p => p.Key)
            .ToList();

        foreach (string key in expired)
            _entries.Remove(key);
    }
}
