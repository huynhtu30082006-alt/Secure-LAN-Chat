using System.Text.Json;

namespace LanChatServer;

public enum RegisterResult
{
    Ok,
    UsernameExists
}

// Lưu tài khoản (username + salt + hash) vào users.json. KHÔNG lưu mật khẩu gốc.
public sealed class UserStore
{
    private sealed class UserRecord
    {
        public string Username { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<string, UserRecord> _users =
        new(StringComparer.OrdinalIgnoreCase);

    // Dùng khi username không tồn tại: vẫn tính PBKDF2 để thời gian phản hồi
    // không tiết lộ tài khoản nào tồn tại.
    private readonly UserRecord _dummy;

    public UserStore(string path)
    {
        _path = path;

        var (salt, hash) = PasswordHasher.Hash("dummy-password-for-timing");
        _dummy = new UserRecord
        {
            Salt = salt,
            Hash = hash,
            Iterations = PasswordHasher.DefaultIterations
        };

        Load();
    }

    public int Count
    {
        get { lock (_lock) return _users.Count; }
    }

    public RegisterResult TryRegister(string username, string password)
    {
        lock (_lock)
        {
            if (_users.ContainsKey(username))
                return RegisterResult.UsernameExists;
        }

        // Tính hash ngoài lock (tốn CPU) để không chặn các đăng nhập khác.
        var (salt, hash) = PasswordHasher.Hash(password);

        lock (_lock)
        {
            if (_users.ContainsKey(username))
                return RegisterResult.UsernameExists;

            _users[username] = new UserRecord
            {
                Username = username,
                Salt = salt,
                Hash = hash,
                Iterations = PasswordHasher.DefaultIterations,
                CreatedUtc = DateTime.UtcNow
            };

            Save();
        }

        return RegisterResult.Ok;
    }

    // Trả về true nếu đúng mật khẩu; canonicalName là tên đã đăng ký (đúng chữ hoa/thường).
    public bool Verify(string username, string password, out string canonicalName)
    {
        UserRecord? record;

        lock (_lock)
        {
            _users.TryGetValue(username, out record);
        }

        if (record == null)
        {
            PasswordHasher.Verify(password, _dummy.Salt, _dummy.Hash, _dummy.Iterations);
            canonicalName = string.Empty;
            return false;
        }

        bool ok = PasswordHasher.Verify(
            password, record.Salt, record.Hash, record.Iterations);

        canonicalName = ok ? record.Username : string.Empty;
        return ok;
    }

    private void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            string json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<UserRecord>>(json);

            if (list == null)
                return;

            foreach (UserRecord record in list)
            {
                if (!string.IsNullOrWhiteSpace(record.Username))
                    _users[record.Username] = record;
            }
        }
        catch (JsonException ex)
        {
            // Dừng hẳn thay vì khởi động với DB rỗng rồi ghi đè mất toàn bộ tài khoản.
            throw new InvalidOperationException(
                $"The user database '{_path}' is corrupted: {ex.Message}", ex);
        }
    }

    // Gọi bên trong lock. Ghi file tạm rồi thay thế để không bị hỏng nếu mất điện giữa chừng.
    private void Save()
    {
        string json = JsonSerializer.Serialize(
            _users.Values.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase).ToList(),
            new JsonSerializerOptions { WriteIndented = true });

        string temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _path, overwrite: true);
    }
}
