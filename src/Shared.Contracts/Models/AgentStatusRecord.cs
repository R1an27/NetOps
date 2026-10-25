using System.Text.Json.Serialization;

namespace ServerManager.Shared.Contracts.Models;

/// <summary>
/// Bản ghi thông tin lưu trữ trạng thái định danh và lịch sử sống còn của Agent trên Master Server.
/// Cho phép Master lưu giữ vết của các Agent kể cả khi Agent đã chuyển sang Offline (do Timeout hoặc ngắt kết nối).
/// </summary>
public class AgentStatusRecord
{
    /// <summary>
    /// Mã định danh duy nhất của Agent (ví dụ: "agent-linux-01").
    /// </summary>
    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Mã phiên làm việc socket hiện tại (null nếu Agent đang Offline).
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? CurrentSessionId { get; set; }

    /// <summary>
    /// Địa chỉ IP và cổng mạng đầu xa của kết nối gần nhất (ví dụ: "192.168.1.10:45000").
    /// </summary>
    [JsonPropertyName("remoteEndPoint")]
    public string RemoteEndPoint { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái hoạt động hiện tại của Agent (Online, Offline, Connecting, Reconnecting).
    /// </summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentStatus Status { get; set; } = AgentStatus.Offline;

    /// <summary>
    /// Thời điểm thiết lập kết nối thành công gần nhất (UTC).
    /// </summary>
    [JsonPropertyName("connectedAt")]
    public DateTimeOffset? ConnectedAt { get; set; }

    /// <summary>
    /// Thời điểm nhận gói tin hợp lệ cuối cùng (Heartbeat/PING/Data) từ Agent (UTC).
    /// Dùng cho thuật toán Watchdog Timeout 15s.
    /// </summary>
    [JsonPropertyName("lastSeenAt")]
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// Thời điểm Agent bị ngắt kết nối hoặc bị Watchdog đánh dấu Offline (UTC).
    /// </summary>
    [JsonPropertyName("disconnectedAt")]
    public DateTimeOffset? DisconnectedAt { get; set; }

    /// <summary>
    /// Nguyên nhân chi tiết dẫn tới việc Agent bị ngắt kết nối hoặc Offline.
    /// </summary>
    [JsonPropertyName("disconnectReason")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DisconnectReason DisconnectReason { get; set; } = DisconnectReason.None;

    /// <summary>
    /// Khoảng thời gian tính bằng giây kể từ lần cuối nhận tín hiệu từ Agent đến thời điểm hiện tại.
    /// </summary>
    [JsonPropertyName("idleSeconds")]
    public double IdleSeconds => Math.Max(0, Math.Round((DateTimeOffset.UtcNow - LastSeenAt).TotalSeconds, 1));

    /// <summary>
    /// Khởi tạo bản ghi trạng thái Agent mặc định.
    /// </summary>
    public AgentStatusRecord() { }

    /// <summary>
    /// Khởi tạo bản ghi trạng thái cho một Agent cụ thể.
    /// </summary>
    public AgentStatusRecord(
        string agentId,
        string? currentSessionId,
        string remoteEndPoint,
        AgentStatus status,
        DateTimeOffset? connectedAt = null,
        DateTimeOffset? lastSeenAt = null)
    {
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        CurrentSessionId = currentSessionId;
        RemoteEndPoint = remoteEndPoint ?? "Unknown";
        Status = status;
        ConnectedAt = connectedAt ?? DateTimeOffset.UtcNow;
        LastSeenAt = lastSeenAt ?? ConnectedAt.Value;
        DisconnectedAt = null;
        DisconnectReason = DisconnectReason.None;
    }
}
