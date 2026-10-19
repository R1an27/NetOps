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
    /// Sự kiện kích hoạt khi có một session mới được đăng ký thành công.
    /// </summary>
    event Action<AgentSession>? SessionRegistered;

    /// <summary>
    /// Sự kiện kích hoạt khi một session bị gỡ bỏ / ngắt kết nối.
    /// </summary>
    event Action<AgentSession>? SessionUnregistered;

    /// <summary>
    /// Đăng ký một session mới vào hệ thống quản lý.
    /// </summary>
    /// <param name="session">Phiên kết nối của Agent</param>
    /// <returns>True nếu đăng ký thành công, False nếu SessionId đã tồn tại</returns>
    bool TryRegister(AgentSession session);

    /// <summary>
    /// Gỡ bỏ một session khỏi hệ thống quản lý theo SessionId.
    /// </summary>
    /// <param name="sessionId">Mã định danh phiên</param>
    /// <param name="session">Phiên được gỡ bỏ (nếu tìm thấy)</param>
    /// <returns>True nếu gỡ bỏ thành công, False nếu không tìm thấy</returns>
    bool TryUnregister(string sessionId, out AgentSession? session);

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
