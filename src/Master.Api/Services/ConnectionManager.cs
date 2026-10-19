using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ServerManager.Shared.Contracts.Models;

namespace Master.Api.Services;

/// <summary>
/// Hiện thực quản lý danh sách phiên làm việc (AgentSession) an toàn đa luồng qua ConcurrentDictionary.
/// Cung cấp khả năng đăng ký, hủy đăng ký với độ phức tạp O(1), tra cứu theo SessionId/AgentId, và phát sóng (broadcast).
/// </summary>
public class ConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ILogger<ConnectionManager>? _logger;

    /// <summary>
    /// Số lượng phiên kết nối đang hoạt động.
    /// </summary>
    public int ActiveCount => _sessions.Count;

    /// <summary>
    /// Sự kiện kích hoạt khi có một session mới được đăng ký thành công.
    /// </summary>
    public event Action<AgentSession>? SessionRegistered;

    /// <summary>
    /// Sự kiện kích hoạt khi một session bị gỡ bỏ / ngắt kết nối.
    /// </summary>
    public event Action<AgentSession>? SessionUnregistered;

    /// <summary>
    /// Khởi tạo ConnectionManager với bộ ghi log tùy chọn.
    /// </summary>
    public ConnectionManager(ILogger<ConnectionManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Đăng ký một session mới vào hệ thống quản lý.
    /// </summary>
    public bool TryRegister(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_sessions.TryAdd(session.SessionId, session))
        {
            _logger?.LogInformation(
                "[ConnectionManager] Registered session {SessionId} (Endpoint: {EndPoint}, AgentId: {AgentId}). Total active: {Count}",
                session.SessionId, session.RemoteEndPoint, session.AgentId ?? "Anonymous", _sessions.Count);

            try
            {
                SessionRegistered?.Invoke(session);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ConnectionManager] Error invoking SessionRegistered event for {SessionId}", session.SessionId);
            }

            return true;
        }

        _logger?.LogWarning("[ConnectionManager] Failed to register session {SessionId} - already exists", session.SessionId);
        return false;
    }

    /// <summary>
    /// Gỡ bỏ một session khỏi hệ thống quản lý theo SessionId.
    /// </summary>
    public bool TryUnregister(string sessionId, out AgentSession? session)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            session = null;
            return false;
        }

        if (_sessions.TryRemove(sessionId, out session))
        {
            _logger?.LogInformation(
                "[ConnectionManager] Unregistered session {SessionId} (Endpoint: {EndPoint}, AgentId: {AgentId}). Total active: {Count}",
                sessionId, session.RemoteEndPoint, session.AgentId ?? "Anonymous", _sessions.Count);

            try
            {
                SessionUnregistered?.Invoke(session);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ConnectionManager] Error invoking SessionUnregistered event for {SessionId}", sessionId);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Lấy thông tin session theo SessionId.
    /// </summary>
    public AgentSession? GetSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Tìm kiếm session đầu tiên gắn với AgentId cụ thể.
    /// </summary>
    public AgentSession? GetSessionByAgentId(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return null;
        return _sessions.Values.FirstOrDefault(s => string.Equals(s.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Lấy danh sách toàn bộ các session đang hoạt động dưới dạng snapshot mảng.
    /// </summary>
    public IReadOnlyCollection<AgentSession> GetAllSessions()
    {
        return _sessions.Values.ToArray();
    }

    /// <summary>
    /// Phát sóng (broadcast) một NetOpsMessage tới tất cả các session đang hoạt động song song.
    /// </summary>
    public async Task BroadcastAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sessions = _sessions.Values.ToArray();
        if (sessions.Length == 0) return;

        var tasks = sessions.Select(async session =>
        {
            try
            {
                await session.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[ConnectionManager] Error broadcasting to session {SessionId}", session.SessionId);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
