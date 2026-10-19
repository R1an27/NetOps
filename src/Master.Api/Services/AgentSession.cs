using System.Net.Sockets;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Protocol;

namespace Master.Api.Services;

/// <summary>
/// Đại diện cho một phiên làm việc (session) kết nối giữa Agent và Master Server.
/// Đóng gói thông tin định danh, địa chỉ kết nối, kênh truyền nhị phân SafeFrameStream và trạng thái sống còn.
/// </summary>
public class AgentSession
{
    /// <summary>
    /// Mã định danh phiên duy nhất (GUID hoặc chuỗi định danh).
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Địa chỉ IP và cổng mạng đầu xa (Remote EndPoint).
    /// </summary>
    public string RemoteEndPoint { get; }

    /// <summary>
    /// Thời điểm thiết lập kết nối ban đầu (UTC).
    /// </summary>
    public DateTimeOffset ConnectedAt { get; }

    /// <summary>
    /// Thời điểm nhận gói tin hợp lệ gần nhất từ Agent (UTC).
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// Mã định danh của Agent (nếu đã xác thực hoặc đã gửi gói tin Heartbeat/Auth).
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Cho biết Agent đã hoàn tất bước xác thực thành công hay chưa.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Kênh truyền thông điệp an toàn đa luồng trên nền TCP Full-Duplex.
    /// </summary>
    public ISafeFrameChannel SafeStream { get; }

    /// <summary>
    /// Đối tượng TcpClient của kết nối (nếu có).
    /// </summary>
    public TcpClient? Client { get; }

    /// <summary>
    /// Khởi tạo phiên làm việc mới cho Agent kết nối.
    /// </summary>
    public AgentSession(
        string sessionId,
        ISafeFrameChannel safeStream,
        string remoteEndPoint,
        DateTimeOffset connectedAt,
        TcpClient? client = null,
        string? agentId = null)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        SafeStream = safeStream ?? throw new ArgumentNullException(nameof(safeStream));
        RemoteEndPoint = remoteEndPoint ?? "Unknown";
        ConnectedAt = connectedAt;
        LastSeenAt = connectedAt;
        Client = client;
        AgentId = agentId;
    }

    /// <summary>
    /// Gửi một NetOpsMessage tới Agent qua kênh truyền an toàn đa luồng.
    /// </summary>
    /// <param name="message">Thông điệp cần gửi</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    public ValueTask SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return SafeStream.SendMessageAsync(message, cancellationToken);
    }
}
