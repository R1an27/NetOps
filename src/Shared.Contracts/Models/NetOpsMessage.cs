using System.Text.Json.Serialization;

namespace ServerManager.Shared.Contracts.Models;

/// <summary>
/// Cấu trúc khung thông điệp tiêu chuẩn trao đổi giữa Manager và Agent
/// </summary>
public class NetOpsMessage
{
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("inReplyTo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InReplyTo { get; set; }

    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Payload { get; set; }

    public static NetOpsMessage CreatePing(string? payload = null) => new()
    {
        MessageType = Models.MessageType.Ping,
        Payload = payload
    };

    public static NetOpsMessage CreatePong(string inReplyToMessageId, string? payload = null) => new()
    {
        MessageType = Models.MessageType.Pong,
        InReplyTo = inReplyToMessageId,
        Payload = payload
    };
}