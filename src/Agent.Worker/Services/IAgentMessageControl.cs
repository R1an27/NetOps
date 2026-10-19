using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;

namespace Agent.Worker.Services;

/// <summary>
/// Hợp đồng điều khiển, phân loại và điều phối thông điệp chuẩn NetOps Protocol v0 phía Agent.
/// Hỗ trợ cả 2 mô hình thực thi: Invoke (chờ kết quả phản hồi) và BeginInvoke (thực thi bất đồng bộ non-blocking).
/// </summary>
public interface IAgentMessageControl
{
    /// <summary>
    /// Sự kiện kích hoạt khi nhận được yêu cầu thực thi lệnh shell từ xa (COMMAND) từ Master.
    /// </summary>
    event Action<NetOpsMessage, CommandRequestPayload>? CommandReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được gói tin xác nhận nhịp tim (HEARTBEAT_ACK) từ Master.
    /// </summary>
    event Action<NetOpsMessage, HeartbeatAckPayload>? HeartbeatAckReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được yêu cầu nhịp tim (HEARTBEAT) từ Master (nếu Master chủ động thăm dò Agent).
    /// </summary>
    event Action<NetOpsMessage, HeartbeatPayload>? HeartbeatReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được phản hồi PONG từ Master cho một gói tin PING do Agent phát đi.
    /// </summary>
    event Action<NetOpsMessage, string?>? PongReceived;

    /// <summary>
    /// Xử lý phân loại thông điệp đến một cách bất đồng bộ và trả về thông điệp phản hồi (nếu có).
    /// </summary>
    /// <param name="message">Thông điệp NetOpsMessage nhận được từ Master</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Thông điệp phản hồi NetOpsMessage hoặc null nếu không cần phản hồi</returns>
    Task<NetOpsMessage?> InvokeAsync(NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overload tiện ích của InvokeAsync cho phép gọi nhanh cú pháp Invoke.
    /// </summary>
    /// <param name="message">Thông điệp NetOpsMessage nhận được từ Master</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Thông điệp phản hồi NetOpsMessage hoặc null nếu không cần phản hồi</returns>
    Task<NetOpsMessage?> Invoke(NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xử lý thông điệp bất đồng bộ dạng không chặn (non-blocking dispatch).
    /// Tự động thực thi phân loại và truyền phát phản hồi (nếu có) qua kênh channel.SendMessageAsync.
    /// </summary>
    /// <param name="channel">Kênh truyền an toàn kết nối tới Master</param>
    /// <param name="message">Thông điệp NetOpsMessage nhận được từ Master</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Tác vụ Task đại diện cho quá trình dispatch ngầm</returns>
    Task BeginInvoke(ISafeFrameChannel channel, NetOpsMessage message, CancellationToken cancellationToken = default);
}
