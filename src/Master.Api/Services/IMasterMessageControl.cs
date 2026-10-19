using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;

namespace Master.Api.Services;

/// <summary>
/// Hợp đồng điều khiển, phân loại và điều phối thông điệp chuẩn NetOps Protocol v0 trên Master Server.
/// Hỗ trợ cả 2 mô hình thực thi: Invoke (chờ kết quả phản hồi) và BeginInvoke (thực thi bất đồng bộ non-blocking).
/// </summary>
public interface IMasterMessageControl
{
    /// <summary>
    /// Sự kiện kích hoạt khi nhận được báo cáo tài nguyên phần cứng (Metrics) từ Agent.
    /// </summary>
    event Action<AgentSession, MetricsPayload>? MetricsReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được dòng dữ liệu output (stdout/stderr) từ một lệnh đang thực thi trên Agent.
    /// </summary>
    event Action<AgentSession, CommandOutputPayload>? CommandOutputReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi một lệnh thực thi trên Agent đã hoàn thành (CommandDone).
    /// </summary>
    event Action<AgentSession, CommandDonePayload>? CommandDoneReceived;

    /// <summary>
    /// Xử lý phân loại thông điệp đến một cách bất đồng bộ và trả về thông điệp phản hồi (nếu có).
    /// </summary>
    /// <param name="session">Phiên làm việc của Agent gửi thông điệp</param>
    /// <param name="message">Thông điệp NetOpsMessage nhận được</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Thông điệp phản hồi NetOpsMessage hoặc null nếu không cần phản hồi</returns>
    Task<NetOpsMessage?> InvokeAsync(AgentSession session, NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overload tiện ích của InvokeAsync cho phép gọi nhanh cú pháp Invoke.
    /// </summary>
    /// <param name="session">Phiên làm việc của Agent gửi thông điệp</param>
    /// <param name="message">Thông điệp NetOpsMessage nhận được</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Thông điệp phản hồi NetOpsMessage hoặc null nếu không cần phản hồi</returns>
    Task<NetOpsMessage?> Invoke(AgentSession session, NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xử lý thông điệp bất đồng bộ dạng không chặn (non-blocking dispatch).
    /// Tự động thực thi phân loại và truyền phát phản hồi (nếu có) qua session.SendMessageAsync.
    /// </summary>
    /// <param name="session">Phiên làm việc của Agent gửi thông điệp</param>
    /// <param name="message">Thông điệp NetOpsMessage nhận được</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Tác vụ Task đại diện cho quá trình dispatch ngầm</returns>
    Task BeginInvoke(AgentSession session, NetOpsMessage message, CancellationToken cancellationToken = default);
}
