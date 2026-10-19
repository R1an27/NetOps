using System.Text.Json.Serialization;

namespace ServerManager.Shared.Contracts.Payloads;

/// <summary>
/// Payload cho thông điệp HEARTBEAT gửi định kỳ từ Agent về Manager (Mục 4.1 protocol-v0-spec).
/// Báo hiệu cho Manager biết Agent vẫn đang duy trì hoạt động bình thường.
/// </summary>
public class HeartbeatPayload
{
    /// <summary>
    /// Mã định danh của Agent đang gửi nhịp tim.
    /// </summary>
    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Dấu thời gian Unix timestamp tính bằng mili-giây tại thời điểm Agent phát nhịp tim.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Khởi tạo một đối tượng HeartbeatPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public HeartbeatPayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng HeartbeatPayload với mã Agent và timestamp tùy chọn.
    /// </summary>
    /// <param name="agentId">Mã định danh của Agent</param>
    /// <param name="timestamp">Dấu thời gian gửi nhịp tim (mili-giây UTC)</param>
    public HeartbeatPayload(string agentId, long? timestamp = null)
    {
        AgentId = agentId;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

/// <summary>
/// Payload phản hồi xác nhận HEARTBEAT_ACK từ Manager về Agent (Mục 4.1 protocol-v0-spec).
/// Xác nhận Manager đã ghi nhận nhịp tim thành công.
/// </summary>
public class HeartbeatAckPayload
{
    /// <summary>
    /// Dấu thời gian Unix timestamp tính bằng mili-giây tại thời điểm Manager xác nhận.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Cờ xác nhận nhịp tim đã được ghi nhận thành công.
    /// </summary>
    [JsonPropertyName("acknowledged")]
    public bool Acknowledged { get; set; } = true;

    /// <summary>
    /// Khởi tạo một đối tượng HeartbeatAckPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public HeartbeatAckPayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng HeartbeatAckPayload với timestamp và trạng thái xác nhận.
    /// </summary>
    /// <param name="timestamp">Dấu thời gian xác nhận (mili-giây UTC)</param>
    /// <param name="acknowledged">Trạng thái xác nhận ghi nhận nhịp tim</param>
    public HeartbeatAckPayload(long? timestamp = null, bool acknowledged = true)
    {
        Timestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Acknowledged = acknowledged;
    }

    /// <summary>
    /// Factory helper tạo đối tượng xác nhận thành công với timestamp hiện tại.
    /// </summary>
    /// <param name="timestamp">Timestamp xác nhận tùy chọn</param>
    /// <returns>Đối tượng HeartbeatAckPayload với Acknowledged = true</returns>
    public static HeartbeatAckPayload Success(long? timestamp = null)
    {
        return new HeartbeatAckPayload(timestamp, acknowledged: true);
    }
}
