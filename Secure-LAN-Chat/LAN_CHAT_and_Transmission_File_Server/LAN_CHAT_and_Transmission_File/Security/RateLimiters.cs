namespace LanChatServer;

// Cho phép tối đa `limit` sự kiện trong mỗi cửa sổ thời gian `window`.
public sealed class SlidingWindowRateLimiter
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly Queue<DateTime> _times = new();
    private readonly object _lock = new();

    public SlidingWindowRateLimiter(int limit, TimeSpan window)
    {
        _limit = limit;
        _window = window;
    }

    public bool TryAcquire()
    {
        lock (_lock)
        {
            DateTime now = DateTime.UtcNow;

            while (_times.Count > 0 && now - _times.Peek() > _window)
                _times.Dequeue();

            if (_times.Count >= _limit)
                return false;

            _times.Enqueue(now);
            return true;
        }
    }
}

// Giới hạn số kết nối đồng thời từ một địa chỉ IP.
public sealed class ConnectionLimiter
{
    private readonly int _maxPerIp;
    private readonly Dictionary<string, int> _counts = new();
    private readonly object _lock = new();

    public ConnectionLimiter(int maxPerIp)
    {
        _maxPerIp = maxPerIp;
    }

    public bool TryAcquire(string ip)
    {
        lock (_lock)
        {
            int current = _counts.GetValueOrDefault(ip);

            if (current >= _maxPerIp)
                return false;

            _counts[ip] = current + 1;
            return true;
        }
    }

    public void Release(string ip)
    {
        lock (_lock)
        {
            if (!_counts.TryGetValue(ip, out int current))
                return;

            if (current <= 1)
                _counts.Remove(ip);
            else
                _counts[ip] = current - 1;
        }
    }
}
