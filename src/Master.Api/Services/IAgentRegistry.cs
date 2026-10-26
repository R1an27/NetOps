using ServerManager.Shared.Contracts.Models;

namespace Master.Api.Services;

/// <summary>
/// Giao diện (Interface) quản lý danh bạ và trạng thái sống còn của các Agent trong hệ thống NetOps.
/// Phục vụ yêu cầu T16.1 trong Barem chấm điểm:
/// - Lưu trữ thông tin định danh và trạng thái (Online/Offline) của Agent.
/// - Đảm bảo an toàn đa luồng (Thread-safe) khi có hàng chục Agent kết nối và gửi nhịp tim đồng thời.
/// - Lưu giữ lịch sử máy chủ (không xóa hẳn thông tin khi socket ngắt kết nối).
/// </summary>
public interface IAgentRegistry
{
    /// <summary>
    /// Số lượng Agent đang ở trạng thái Online (kết nối trực tuyến).
    /// </summary>
    int OnlineCount { get; }

    /// <summary>
    /// Tổng số lượng Agent đã biết trong hệ thống (bao gồm cả Online và Offline).
    /// </summary>
    int TotalCount { get; }

    /// <summary>
    /// Sự kiện kích hoạt khi trạng thái của một Agent thay đổi (Online <-> Offline).
    /// Các module khác (WinForms UI, Audit Log, Notifier) có thể lắng nghe sự kiện này để cập nhật.
    /// </summary>
    event Action<AgentStatusRecord>? AgentStatusChanged;

    /// <summary>
    /// Sự kiện kích hoạt chuyên biệt khi một Agent bị mất kết nối hoặc bị Watchdog phát hiện Offline.
    /// Phục vụ cho tính năng kích hoạt cảnh báo (Alert) tới người quản trị.
    /// </summary>
    event Action<AgentStatusRecord>? AgentOfflineDetected;

    /// <summary>
    /// Đăng ký mới hoặc cập nhật trạng thái của Agent thành Online khi nhận được gói tin (PING, HEARTBEAT, AUTH).
    /// </summary>
    /// <param name="agentId">Mã định danh duy nhất của Agent (ví dụ: "agent-linux-01")</param>
    /// <param name="sessionId">Mã phiên socket hiện tại</param>
    /// <param name="remoteEndPoint">Địa chỉ IP và cổng mạng đầu xa</param>
    /// <param name="seenAt">Thời điểm nhận gói tin (mặc định UtcNow)</param>
    /// <returns>Bản ghi trạng thái Agent đã được cập nhật</returns>
    AgentStatusRecord RegisterOrUpdateOnline(string agentId, string? sessionId, string remoteEndPoint, DateTimeOffset? seenAt = null);

    /// <summary>
    /// Đánh dấu một Agent chuyển sang trạng thái Offline kèm nguyên nhân ngắt kết nối (T16.4: không xóa hẳn thông tin máy chủ).
    /// </summary>
    /// <param name="agentId">Mã định danh của Agent</param>
    /// <param name="reason">Nguyên nhân (HeartbeatTimeout, GracefulDisconnect, NetworkFault)</param>
    /// <param name="disconnectedAt">Thời điểm ngắt kết nối (mặc định UtcNow)</param>
    /// <returns>Bản ghi trạng thái sau khi đã chuyển sang Offline (hoặc null nếu không tìm thấy)</returns>
    AgentStatusRecord? MarkOffline(string agentId, DisconnectReason reason, DateTimeOffset? disconnectedAt = null);

    /// <summary>
    /// Tra cứu thông tin trạng thái của một Agent theo AgentId.
    /// </summary>
    /// <param name="agentId">Mã định danh của Agent</param>
    /// <returns>Bản ghi trạng thái hoặc null nếu Agent chưa từng kết nối</returns>
    AgentStatusRecord? Get(string agentId);

    /// <summary>
    /// Lấy danh sách toàn bộ các Agent trong hệ thống (hỗ trợ lọc theo trạng thái Online hoặc Offline).
    /// </summary>
    /// <param name="statusFilter">Bộ lọc trạng thái tùy chọn (null: lấy toàn bộ)</param>
    /// <returns>Danh sách snapshot các bản ghi AgentStatusRecord</returns>
    IReadOnlyCollection<AgentStatusRecord> GetAll(AgentStatus? statusFilter = null);
}
