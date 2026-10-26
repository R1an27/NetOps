using ServerManager.Shared.Contracts.Models;

namespace Master.Api.Services;

/// <summary>
/// Hợp đồng quản lý danh sách phiên làm việc (AgentSession) của các Agent kết nối tới Master Server.
/// Đảm bảo an toàn luồng (thread-safety) khi có nhiều kết nối đồng thời.
/// </summary>
public interface IConnectionManager
{
    /// <summary>
    /// Số lượng phiên kết nối đang hoạt động.
    /// </summary>
    int ActiveCount { get; }

    /// <summary>
    /// Đối tượng quản lý danh bạ và trạng thái sống còn IAgentRegistry (T16.1).
    /// </summary>
    IAgentRegistry Registry { get; }

    /// <summary>
    /// Sự kiện kích hoạt khi có một session mới được đăng ký thành công.
    /// </summary>
    event Action<AgentSession>? SessionRegistered;

    /// <summary>
    /// Sự kiện kích hoạt khi một session bị gỡ bỏ / ngắt kết nối.
    /// </summary>
    event Action<AgentSession>? SessionUnregistered;

    /// <summary>
    /// Sự kiện kích hoạt khi trạng thái hoạt động của Agent thay đổi (Online <-> Offline).
    /// </summary>
    event Action<AgentStatusRecord>? AgentStatusChanged;

    /// <summary>
    /// Sự kiện kích hoạt chuyên biệt khi một Agent bị phát hiện đã ngắt kết nối hoặc timeout (chuyển sang Offline).
    /// </summary>
    event Action<AgentStatusRecord>? AgentOfflineDetected;

    /// <summary>
    /// Đăng ký một session mới vào hệ thống quản lý.
    /// </summary>
    /// <param name="session">Phiên kết nối của Agent</param>
    /// <returns>True nếu đăng ký thành công, False nếu SessionId đã tồn tại</returns>
    bool TryRegister(AgentSession session);

    /// <summary>
    /// Gỡ bỏ một session khỏi hệ thống quản lý theo SessionId (mặc định ngắt kết nối thông thường).
    /// </summary>
    /// <param name="sessionId">Mã định danh phiên</param>
    /// <param name="session">Phiên được gỡ bỏ (nếu tìm thấy)</param>
    /// <returns>True nếu gỡ bỏ thành công, False nếu không tìm thấy</returns>
    bool TryUnregister(string sessionId, out AgentSession? session);

    /// <summary>
    /// Gỡ bỏ một session khỏi hệ thống quản lý kèm nguyên nhân ngắt kết nối (Graceful, HeartbeatTimeout, NetworkFault).
    /// </summary>
    /// <param name="sessionId">Mã định danh phiên</param>
    /// <param name="session">Phiên được gỡ bỏ (nếu tìm thấy)</param>
    /// <param name="reason">Nguyên nhân ngắt kết nối</param>
    /// <returns>True nếu gỡ bỏ thành công, False nếu không tìm thấy</returns>
    bool TryUnregister(string sessionId, out AgentSession? session, DisconnectReason reason);

    /// <summary>
    /// Đánh dấu hoặc cập nhật trạng thái của Agent sang Online khi nhận kết nối hoặc nhận tín hiệu nhịp tim.
    /// </summary>
    /// <param name="agentId">Mã định danh Agent</param>
    /// <param name="sessionId">Mã phiên socket hiện tại</param>
    /// <param name="remoteEndPoint">Địa chỉ kết nối từ xa</param>
    /// <param name="seenAt">Thời điểm nhận tín hiệu (mặc định UtcNow)</param>
    void MarkAgentOnline(string agentId, string sessionId, string remoteEndPoint, DateTimeOffset? seenAt = null);

    /// <summary>
    /// Đánh dấu Agent chuyển sang trạng thái Offline kèm nguyên nhân (ví dụ do Watchdog phát hiện mất nhịp tim).
    /// </summary>
    /// <param name="agentId">Mã định danh Agent</param>
    /// <param name="reason">Nguyên nhân ngắt kết nối</param>
    /// <param name="disconnectedAt">Thời điểm ngắt kết nối (mặc định UtcNow)</param>
    void MarkAgentOffline(string agentId, DisconnectReason reason, DateTimeOffset? disconnectedAt = null);

    /// <summary>
    /// Giai đoạn T18.2: Đánh dấu Agent chuyển sang trạng thái Reconnecting khi nhận tín hiệu hoặc bắt đầu chu kỳ thử lại.
    /// </summary>
    /// <param name="agentId">Mã định danh Agent</param>
    /// <param name="reason">Nguyên nhân ngắt kết nối (mặc định NetworkFault)</param>
    void MarkAgentReconnecting(string agentId, DisconnectReason reason = DisconnectReason.NetworkFault);

    /// <summary>
    /// Lấy danh sách toàn bộ các bản ghi trạng thái của tất cả Agent đã biết (kể cả Online lẫn Offline).
    /// </summary>
    /// <param name="statusFilter">Bộ lọc trạng thái tùy chọn (null: lấy toàn bộ)</param>
    IReadOnlyCollection<AgentStatusRecord> GetAllAgentStatuses(AgentStatus? statusFilter = null);

    /// <summary>
    /// Tra cứu bản ghi trạng thái của một Agent cụ thể theo AgentId.
    /// </summary>
    /// <param name="agentId">Mã định danh Agent</param>
    AgentStatusRecord? GetAgentStatus(string agentId);

    /// <summary>
    /// Lấy thông tin session theo SessionId.
    /// </summary>
    AgentSession? GetSession(string sessionId);

    /// <summary>
    /// Tìm kiếm session đầu tiên gắn với AgentId cụ thể.
    /// </summary>
    AgentSession? GetSessionByAgentId(string agentId);

    /// <summary>
    /// Lấy danh sách toàn bộ các session đang hoạt động.
    /// </summary>
    IReadOnlyCollection<AgentSession> GetAllSessions();

    /// <summary>
    /// Phát sóng (broadcast) một thông điệp tới tất cả các session đang hoạt động song song.
    /// </summary>
    /// <param name="message">Thông điệp cần phát sóng</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Đối tượng BroadcastResult tổng hợp số lượng phiên nhắm tới, thành công và thất bại</returns>
    Task<BroadcastResult> BroadcastAsync(NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi một NetOpsMessage dạng Unicast tới một Agent cụ thể theo AgentId.
    /// </summary>
    /// <param name="agentId">Mã định danh Agent đích</param>
    /// <param name="message">Thông điệp cần gửi</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>True nếu tìm thấy session và gửi thành công, False nếu không tìm thấy session hoặc gặp sự cố mạng</returns>
    Task<bool> SendUnicastAsync(string agentId, NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi một NetOpsMessage dạng Unicast tới một Agent cụ thể theo SessionId.
    /// </summary>
    /// <param name="sessionId">Mã định danh phiên kết nối đích</param>
    /// <param name="message">Thông điệp cần gửi</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>True nếu tìm thấy session và gửi thành công, False nếu không tìm thấy session hoặc gặp sự cố mạng</returns>
    Task<bool> SendUnicastBySessionIdAsync(string sessionId, NetOpsMessage message, CancellationToken cancellationToken = default);
}
