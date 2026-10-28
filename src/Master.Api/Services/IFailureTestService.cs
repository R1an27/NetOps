using ServerManager.Shared.Contracts.Models;

namespace Master.Api.Services;

/// <summary>
/// Giai đoạn T19.2: Hợp đồng dịch vụ kiểm thử sự cố mạng và phục hồi (Failure &amp; Resilience Testing Service).
/// Quản lý việc tiêm lỗi mạng (Fault Injection), ngắt socket đột ngột, giả lập mất kết nối
/// và đánh giá tính bền bỉ của hệ thống Master-Agent.
/// </summary>
public interface IFailureTestService
{
    /// <summary>
    /// Cưỡng chế ngắt kết nối socket của một Agent hoặc Session cụ thể mà không có bắt tay FIN êm dịu (TCP RST / Abrupt Drop).
    /// </summary>
    /// <param name="agentIdOrSessionId">Mã Agent hoặc SessionId của kết nối mục tiêu</param>
    /// <param name="reason">Lý do ngắt kết nối (mặc định NetworkFault)</param>
    /// <returns>True nếu tìm thấy session và đã ngắt thành công, False nếu không tìm thấy</returns>
    bool TerminateSessionAbruptly(string agentIdOrSessionId, DisconnectReason reason = DisconnectReason.NetworkFault);

    /// <summary>
    /// Thực thi một kịch bản kiểm thử sự cố chuẩn hóa theo yêu cầu và trả về báo cáo kết quả chi tiết kèm timeline.
    /// </summary>
    /// <param name="request">Yêu cầu kịch bản kiểm thử sự cố</param>
    /// <param name="cancellationToken">Mã hủy tác vụ</param>
    /// <returns>Đối tượng FailureTestReport chứa số liệu thời gian phát hiện và phục hồi</returns>
    Task<FailureTestReport> ExecuteScenarioAsync(FailureTestRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các kịch bản kiểm thử sự cố được hỗ trợ trong hệ thống.
    /// </summary>
    IReadOnlyCollection<string> GetSupportedScenarios();
}
