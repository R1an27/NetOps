namespace ServerManager.Shared.Contracts.Models;

/// <summary>
/// Giai đoạn T18.1: Thiết kế hợp đồng trạng thái kết nối (Connection State Contract).
/// Trạng thái kết nối TCP của Client tới Master Server theo chuẩn vòng đời NetOps Protocol v0.
/// Hỗ trợ phản ánh trạng thái trực quan trên giao diện Client.WinForms và điều phối tự động kết nối lại.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// Chưa kết nối hoặc đã ngắt kết nối an toàn (Graceful Disconnect / Idle).
    /// Giao diện hiển thị: Slate Gray (DISCONNECTED).
    /// </summary>
    Disconnected = 0,

    /// <summary>
    /// Đang trong quá trình thiết lập kết nối TCP socket hoặc thực hiện bắt tay mạng.
    /// Giao diện hiển thị: Amber / Vàng cam (CONNECTING).
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// Đã kết nối thành công và kênh truyền SafeFrameStream sẵn sàng trao đổi dữ liệu.
    /// Giao diện hiển thị: Emerald Green / Xanh lục (CONNECTED).
    /// </summary>
    Connected = 2,

    /// <summary>
    /// Đang trong chu kỳ tự động thử kết nối lại sau sự cố mất mạng hoặc đứt kết nối ngầm.
    /// Giao diện hiển thị: Deep Orange / Cam nổi bật (RECONNECTING).
    /// </summary>
    Reconnecting = 3
}

/// <summary>
/// Giai đoạn T18.1: Hợp đồng dữ liệu sự kiện thay đổi trạng thái kết nối (ConnectionStateChangedEventArgs).
/// Chứa đầy đủ thông tin bối cảnh để luồng mạng truyền an toàn sang luồng giao diện WinForms UI:
/// - Trạng thái trước và sau khi thay đổi.
/// - Số lần thử kết nối lại (Attempt).
/// - Thời gian chờ trước lần thử tiếp theo (RetryDelay).
/// - Lý do chi tiết hoặc thông điệp lỗi (Reason / Message).
/// - Endpoint máy chủ đích và mốc thời gian UTC.
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Trạng thái kết nối trước thời điểm thay đổi.
    /// </summary>
    public ConnectionState PreviousState { get; }

    /// <summary>
    /// Trạng thái kết nối mới vừa được áp dụng.
    /// </summary>
    public ConnectionState NewState { get; }

    /// <summary>
    /// Địa chỉ máy chủ đích (ví dụ: "127.0.0.1:5000").
    /// </summary>
    public string? RemoteEndPoint { get; }

    /// <summary>
    /// Lần thử kết nối lại thứ mấy (nếu đang trong chu kỳ Reconnecting).
    /// Mặc định là 0 nếu không phải hành động thử lại.
    /// </summary>
    public int Attempt { get; }

    /// <summary>
    /// Thời gian chờ (backoff delay) trước lần thử tiếp theo (nếu có).
    /// </summary>
    public TimeSpan? RetryDelay { get; }

    /// <summary>
    /// Thông điệp giải thích nguyên nhân hoặc chi tiết trạng thái (ví dụ: "Mất kết nối máy chủ", "Đứt cáp").
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Thời điểm diễn ra sự kiện thay đổi trạng thái (UTC).
    /// </summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Khởi tạo một đối tượng dữ liệu sự kiện thay đổi trạng thái kết nối.
    /// </summary>
    public ConnectionStateChangedEventArgs(
        ConnectionState previousState,
        ConnectionState newState,
        string? remoteEndPoint = null,
        int attempt = 0,
        TimeSpan? retryDelay = null,
        string? message = null)
    {
        PreviousState = previousState;
        NewState = newState;
        RemoteEndPoint = remoteEndPoint;
        Attempt = attempt;
        RetryDelay = retryDelay;
        Message = message;
    }
}
