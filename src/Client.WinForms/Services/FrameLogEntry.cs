using ServerManager.Shared.Contracts.Models;

namespace Client.WinForms.Services;

/// <summary>
/// Hướng truyền tải của khung dữ liệu.
/// </summary>
public enum FrameDirection
{
    /// <summary>
    /// Gói tin nhận từ máy chủ Master (IN).
    /// </summary>
    Inbound,

    /// <summary>
    /// Gói tin gửi đi từ Client tới Master (OUT).
    /// </summary>
    Outbound
}

/// <summary>
/// Đại diện cho một bản ghi log của khung dữ liệu nhị phân trên đường truyền NetOps.
/// Phục vụ hiển thị trên bảng Frame Inspector và Debug Monitor.
/// </summary>
public sealed class FrameLogEntry
{
    /// <summary>
    /// Thời điểm gửi hoặc nhận gói tin.
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.Now;

    /// <summary>
    /// Hướng truyền tải (Inbound hoặc Outbound).
    /// </summary>
    public FrameDirection Direction { get; init; }

    /// <summary>
    /// Loại thông điệp NetOpsMessage (ví dụ: PING, PONG, COMMAND, HEARTBEAT,...).
    /// </summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>
    /// Định danh duy nhất của gói tin (GUID).
    /// </summary>
    public string MessageId { get; init; } = string.Empty;

    /// <summary>
    /// Mã tương quan luồng tác vụ (RequestId).
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Mã định danh của gói tin gốc mà gói này đang phản hồi (InReplyTo).
    /// </summary>
    public string? InReplyTo { get; init; }

    /// <summary>
    /// Kích thước dữ liệu payload nhị phân (bytes).
    /// </summary>
    public int PayloadSize { get; init; }

    /// <summary>
    /// 4 bytes header biểu diễn độ dài theo thứ tự Big-Endian.
    /// </summary>
    public byte[] HeaderBytes { get; init; } = [];

    /// <summary>
    /// Chuỗi JSON đầy đủ của NetOpsMessage.
    /// </summary>
    public string RawJson { get; init; } = string.Empty;

    /// <summary>
    /// Chuỗi JSON đã được format thụt đầu dòng (Indented).
    /// </summary>
    public string FormattedJson { get; init; } = string.Empty;

    /// <summary>
    /// Đối tượng NetOpsMessage gốc.
    /// </summary>
    public NetOpsMessage Message { get; init; } = null!;
}
