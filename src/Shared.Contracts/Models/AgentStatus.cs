namespace ServerManager.Shared.Contracts.Models;

/// <summary>
/// Trạng thái hoạt động và vòng đời kết nối của Agent đối với Master Server.
/// Dùng để quản lý vòng đời (Connection Lifecycle) và hiển thị trực quan trên giao diện Client.
/// </summary>
public enum AgentStatus
{
    /// <summary>
    /// Agent đang kết nối trực tuyến và phản hồi nhịp tim/gói tin bình thường.
    /// </summary>
    Online = 0,

    /// <summary>
    /// Agent đã ngắt kết nối hoặc mất tín hiệu nhịp tim quá thời gian chờ Watchdog (15s).
    /// </summary>
    Offline = 1,

    /// <summary>
    /// Agent đang trong quá trình thiết lập kết nối TCP socket tới Master.
    /// </summary>
    Connecting = 2,

    /// <summary>
    /// Agent bị mất mạng và đang trong chu kỳ thử kết nối lại tự động (Exponential Backoff).
    /// </summary>
    Reconnecting = 3
}

/// <summary>
/// Nguyên nhân chi tiết khiến Agent bị chuyển sang trạng thái Offline hoặc ngắt kết nối.
/// Phục vụ cho việc kiểm toán (Audit Trail), chẩn đoán lỗi mạng và hiển thị trên giao diện.
/// </summary>
public enum DisconnectReason
{
    /// <summary>
    /// Chưa xác định nguyên nhân hoặc Agent đang trực tuyến bình thường.
    /// </summary>
    None = 0,

    /// <summary>
    /// Mất tín hiệu nhịp tim (Heartbeat/PING) vượt quá ngưỡng thời gian Watchdog (mặc định 15s).
    /// Đây là dấu hiệu nhận diện kết nối chết ngầm (Half-Open Socket) do rút cáp, sập nguồn.
    /// </summary>
    HeartbeatTimeout = 1,

    /// <summary>
    /// Ngắt kết nối êm ái có chủ đích từ phía Agent (Graceful EOF / Clean Shutdown).
    /// </summary>
    GracefulDisconnect = 2,

    /// <summary>
    /// Lỗi mạng bất thường tầng Socket (Socket RST, lỗi đường truyền hoặc ngắt đột ngột).
    /// </summary>
    NetworkFault = 3,

    /// <summary>
    /// Bị từ chối xác thực danh tính (token không hợp lệ, phiên hết hạn).
    /// </summary>
    AuthenticationFailed = 4,

    /// <summary>
    /// Master Server chủ động tắt hoặc dừng dịch vụ (Host Graceful Shutdown).
    /// </summary>
    ServerShutdown = 5
}
