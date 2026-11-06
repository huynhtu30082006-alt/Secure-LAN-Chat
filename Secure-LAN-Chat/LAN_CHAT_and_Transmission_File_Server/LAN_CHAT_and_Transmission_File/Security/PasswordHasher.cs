using System.Security.Cryptography;

namespace LanChatServer;

// PBKDF2-HMAC-SHA256 với salt ngẫu nhiên cho từng tài khoản.
public static class PasswordHasher
{
    public const int DefaultIterations = 600_000; // khuyến nghị OWASP cho PBKDF2-HMAC-SHA256
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static (string Salt, string Hash) Hash(
        string password, int iterations = DefaultIterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, HashSize);

        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(
        string password, string saltBase64, string hashBase64, int iterations)
    {
        byte[] salt = Convert.FromBase64String(saltBase64);
        byte[] expected = Convert.FromBase64String(hashBase64);

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // So sánh thời gian cố định để không lộ thông tin qua độ trễ.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
