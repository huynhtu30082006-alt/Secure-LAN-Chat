namespace LanChatServer;

public static class ServerConfig
{
    public const int TcpPort = 5000;
    public const int UdpDiscoveryPort = 5001;
    public const int FilePort = 5002;
    public const int MaxUsernameLength = 20;
    public const int MaxMessageLength = 1000;

    // ---- Mật khẩu ----
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 64;

    // ---- Chống brute-force đăng nhập ----
    public const int MaxFailedAttemptsPerUser = 5;   // sai 5 lần với cùng một tài khoản -> khóa tạm
    public const int MaxFailedAttemptsPerIp = 10;    // sai 10 lần từ cùng một địa chỉ IP -> khóa tạm
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    // ---- Giới hạn kết nối / tốc độ ----
    public const int MaxConnectionsPerIp = 10;       // số kết nối đồng thời tối đa từ một IP (mỗi cổng)
    public const int MessageBurst = 15;              // tối đa 15 tin / 10 giây / người dùng
    public static readonly TimeSpan MessageWindow = TimeSpan.FromSeconds(10);
    public const int MaxRateViolations = 30;         // vi phạm quá nhiều lần -> ngắt kết nối
    public const int MaxFilesPerMinute = 10;         // tối đa 10 lần gửi file / phút / người dùng
}
