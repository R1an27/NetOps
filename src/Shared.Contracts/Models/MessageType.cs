namespace ServerManager.Shared.Contracts.Models;

/// <summary>
/// Định nghĩa các loại thông điệp chuẩn trong giao thức mạng NetOps Protocol v0.
/// </summary>
public static class MessageType
{
    /// <summary>
    /// Thông điệp kiểm tra kết nối tầng ứng dụng (Ping).
    /// </summary>
    public const string Ping = "PING";

    /// <summary>
    /// Thông điệp phản hồi kiểm tra kết nối tầng ứng dụng (Pong).
    /// </summary>
    public const string Pong = "PONG";

    /// <summary>
    /// Thông điệp yêu cầu xác thực phiên từ Agent lên Manager.
    /// </summary>
    public const string Auth = "AUTH";

    /// <summary>
    /// Thông điệp phản hồi chấp thuận xác thực phiên từ Manager về Agent.
    /// </summary>
    public const string AuthOk = "AUTH_OK";

    /// <summary>
    /// Thông điệp phản hồi từ chối xác thực phiên từ Manager về Agent.
    /// </summary>
    public const string AuthFailed = "AUTH_FAILED";

    /// <summary>
    /// Thông điệp nhịp tim định kỳ từ Agent gửi về Manager.
    /// </summary>
    public const string Heartbeat = "HEARTBEAT";

    /// <summary>
    /// Thông điệp xác nhận đã nhận nhịp tim từ Manager gửi về Agent.
    /// </summary>
    public const string HeartbeatAck = "HEARTBEAT_ACK";

    /// <summary>
    /// Thông điệp báo cáo chỉ số tài nguyên phần cứng định kỳ từ Agent.
    /// </summary>
    public const string Metrics = "METRICS";

    /// <summary>
    /// Thông điệp gửi lệnh điều khiển từ Manager xuống Agent để thực thi shell.
    /// </summary>
    public const string Command = "COMMAND";

    /// <summary>
    /// Thông điệp truyền phát dòng dữ liệu đầu ra (stdout/stderr) của lệnh theo thời gian thực.
    /// </summary>
    public const string CommandOutput = "COMMAND_OUTPUT";

    /// <summary>
    /// Thông điệp thông báo đã hoàn tất thực thi lệnh cùng mã thoát (exit code).
    /// </summary>
    public const string CommandDone = "COMMAND_DONE";

    /// <summary>
    /// Thông điệp phản hồi lỗi chuẩn hóa khi xảy ra vi phạm giao thức hoặc sự cố xử lý.
    /// </summary>
    public const string Error = "ERROR";
}