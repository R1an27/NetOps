namespace Agent.Worker.Services;

/// <summary>
/// Trạng thái kết nối của Agent tới Master trong suốt vòng đời kết nối (Connection Lifecycle).
/// </summary>
public enum AgentConnectionState
{
    /// <summary>Chưa chạy, hoặc đã dừng hẳn (RunAsync kết thúc).</summary>
    Disconnected = 0,

    /// <summary>Đang mở socket TCP tới Master.</summary>
    Connecting = 1,

    /// <summary>Đã nối TCP, đang gửi AUTH và chờ kết quả xác thực.</summary>
    Authenticating = 2,

    /// <summary>Đã sẵn sàng: kết nối ổn định, chạy chu trình PING/PONG.</summary>
    Connected = 3,

    /// <summary>Mất kết nối, đang chờ theo Exponential Backoff trước khi thử lại.</summary>
    Reconnecting = 4,

    /// <summary>Master từ chối xác thực (AUTH_FAILED). Dừng thử lại, chờ người can thiệp.</summary>
    Unauthorized = 5
}
