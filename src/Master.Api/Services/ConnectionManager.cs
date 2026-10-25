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
    private readonly ConcurrentDictionary<string, AgentStatusRecord> _agentRegistry = new(StringComparer.OrdinalIgnoreCase);
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
    /// Sự kiện kích hoạt khi trạng thái hoạt động của Agent thay đổi (Online <-> Offline).
    /// </summary>
    public event Action<AgentStatusRecord>? AgentStatusChanged;

    /// <summary>
    /// Sự kiện kích hoạt khi một Agent bị phát hiện đã ngắt kết nối hoặc timeout (chuyển sang Offline).
    /// </summary>
    public event Action<AgentStatusRecord>? AgentOfflineDetected;

    /// <summary>
    /// Khởi tạo ConnectionManager với bộ ghi log tùy chọn.
    /// </summary>
    public ConnectionManager(ILogger<ConnectionManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Đăng ký một session mới vào hệ thống quản lý.
    /// Tự động cập nhật Agent sang trạng thái Online nếu session đã có AgentId.
    /// trycatch để kích hoạt sự kiện để thông báo cho các module khác
    /// Nếu session đã có trong hệ thì báo lỗi
    /// </summary>
    public bool TryRegister(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_sessions.TryAdd(session.SessionId, session))
        {
            _logger?.LogInformation(
                "[ConnectionManager] Registered session {SessionId} (Endpoint: {EndPoint}, AgentId: {AgentId}). Total active: {Count}",
                session.SessionId, session.RemoteEndPoint, session.AgentId ?? "Anonymous", _sessions.Count);

            // Nếu Agent đã định danh rõ danh tính thì tự động đưa vào AgentRegistry với trạng thái Online
            if (!string.IsNullOrWhiteSpace(session.AgentId))
            {
                MarkAgentOnline(session.AgentId, session.SessionId, session.RemoteEndPoint, session.LastSeenAt);
            }

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
    /// Gỡ bỏ một session khỏi hệ thống quản lý theo SessionId (ngắt kết nối mặc định GracefulDisconnect).
    /// </summary>
    public bool TryUnregister(string sessionId, out AgentSession? session)
    {
        return TryUnregister(sessionId, out session, DisconnectReason.GracefulDisconnect);
    }

    /// <summary>
    /// Gỡ bỏ một session khỏi hệ thống quản lý kèm nguyên nhân ngắt kết nối.
    /// Tự động đánh dấu Agent chuyển sang Offline và kích hoạt các sự kiện giám sát.
    /// </summary>
    public bool TryUnregister(string sessionId, out AgentSession? session, DisconnectReason reason)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            session = null;
            return false;
        }

        if (_sessions.TryRemove(sessionId, out session))
        {
            _logger?.LogInformation(
                "[ConnectionManager] Unregistered session {SessionId} (Endpoint: {EndPoint}, AgentId: {AgentId}, Reason: {Reason}). Total active: {Count}",
                sessionId, session.RemoteEndPoint, session.AgentId ?? "Anonymous", reason, _sessions.Count);

            // Nếu session có AgentId, đánh dấu Agent đó là Offline trong AgentRegistry
            if (!string.IsNullOrWhiteSpace(session.AgentId))
            {
                MarkAgentOffline(session.AgentId, reason, DateTimeOffset.UtcNow);
            }

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
    /// Đánh dấu hoặc cập nhật Agent sang trạng thái Online.
    /// Kích hoạt sự kiện AgentStatusChanged nếu trạng thái có sự thay đổi.
    /// </summary>
    public void MarkAgentOnline(string agentId, string sessionId, string remoteEndPoint, DateTimeOffset? seenAt = null)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return;

        var now = seenAt ?? DateTimeOffset.UtcNow;
        bool statusChanged = false;

        var record = _agentRegistry.AddOrUpdate(
            agentId,
            addValueFactory: id =>
            {
                statusChanged = true;
                return new AgentStatusRecord(id, sessionId, remoteEndPoint, AgentStatus.Online, now, now);
            },
            updateValueFactory: (id, existing) =>
            {
                if (existing.Status != AgentStatus.Online)
                {
                    statusChanged = true;
                }

                existing.Status = AgentStatus.Online;
                existing.CurrentSessionId = sessionId;
                existing.RemoteEndPoint = remoteEndPoint;
                existing.LastSeenAt = now;
                existing.DisconnectedAt = null;
                existing.DisconnectReason = DisconnectReason.None;
                return existing;
            });

        _logger?.LogInformation(
            "[ConnectionManager] Agent '{AgentId}' is marked ONLINE (Session: {SessionId}, EndPoint: {EndPoint})",
            agentId, sessionId, remoteEndPoint);

        if (statusChanged)
        {
            try
            {
                AgentStatusChanged?.Invoke(record);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ConnectionManager] Error invoking AgentStatusChanged for {AgentId}", agentId);
            }
        }
    }

    /// <summary>
    /// Đánh dấu Agent sang trạng thái Offline kèm nguyên nhân ngắt kết nối (ví dụ: HeartbeatTimeout).
    /// Kích hoạt sự kiện AgentOfflineDetected và AgentStatusChanged.
    /// </summary>
    public void MarkAgentOffline(string agentId, DisconnectReason reason, DateTimeOffset? disconnectedAt = null)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return;

        var timestamp = disconnectedAt ?? DateTimeOffset.UtcNow;
        bool statusChanged = false;

        var record = _agentRegistry.AddOrUpdate(
            agentId,
            addValueFactory: id =>
            {
                statusChanged = true;
                return new AgentStatusRecord(id, null, "Unknown", AgentStatus.Offline, timestamp, timestamp)
                {
                    DisconnectedAt = timestamp,
                    DisconnectReason = reason
                };
            },
            updateValueFactory: (id, existing) =>
            {
                if (existing.Status != AgentStatus.Offline)
                {
                    statusChanged = true;
                }

                existing.Status = AgentStatus.Offline;
                existing.CurrentSessionId = null;
                existing.DisconnectedAt = timestamp;
                existing.DisconnectReason = reason;
                return existing;
            });

        _logger?.LogWarning(
            "[ConnectionManager] Agent '{AgentId}' is marked OFFLINE (Reason: {Reason}, DisconnectedAt: {DisconnectedAt:u})",
            agentId, reason, timestamp);

        if (statusChanged)
        {
            try
            {
                AgentStatusChanged?.Invoke(record);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ConnectionManager] Error invoking AgentStatusChanged for {AgentId}", agentId);
            }

            try
            {
                AgentOfflineDetected?.Invoke(record);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ConnectionManager] Error invoking AgentOfflineDetected for {AgentId}", agentId);
            }
        }
    }

    /// <summary>
    /// Lấy danh sách toàn bộ trạng thái Agent đã biết (hỗ trợ lọc theo trạng thái Online/Offline).
    /// </summary>
    public IReadOnlyCollection<AgentStatusRecord> GetAllAgentStatuses(AgentStatus? statusFilter = null)
    {
        var all = _agentRegistry.Values.ToArray();
        return statusFilter.HasValue
            ? all.Where(a => a.Status == statusFilter.Value).ToArray() 
            // nếu có bộ lọc thì trả về status phù hợp
            : all; //nếu k có bộ lọc thì trả về tất cả status
    }

    /// <summary>
    /// Tra cứu thông tin trạng thái của một Agent cụ thể.
    /// </summary>
    public AgentStatusRecord? GetAgentStatus(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return null;
        _agentRegistry.TryGetValue(agentId, out var record);
        return record;
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
    public async Task<BroadcastResult> BroadcastAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sessions = _sessions.Values.ToArray();
        if (sessions.Length == 0)
        {
            _logger?.LogDebug("[ConnectionManager] Broadcast skipped: No active sessions available.");
            return new BroadcastResult(0, 0, 0);
        }

        int successCount = 0;
        int failedCount = 0;

        ///khởi tạo các tác vụ gửi tin nhắn cùng một lúc cho tất cả Agent
        var tasks = sessions.Select(async session =>
        {
            try
            {
                await session.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref successCount);
                /// để đảm bảo khi hàng trăm luồng cùng tăng biến này một lúc thì 
                /// số đếm vẫn chuẩn xác, không bị xung đột bộ nhớ
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failedCount);
                _logger?.LogWarning(ex, "[ConnectionManager] Error broadcasting to session {SessionId} (AgentId: {AgentId})",
                    session.SessionId, session.AgentId ?? "Anonymous");
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        ///Tạm dừng thực thi hàm và đợi tất cả các tác vụ gửi tin nhắn song song trong 
        /// danh sách tasks hoàn thành (dù thành công hay thất bại) 
        /// mới cho code chạy tiếp xuống bên dưới
        _logger?.LogInformation(
            "[ConnectionManager] Broadcast message {MessageType} (Id: {MessageId}) completed. Targeted: {Total}, Succeeded: {Success}, Failed: {Failed}",
            message.MessageType, message.MessageId, sessions.Length, successCount, failedCount);

        return new BroadcastResult(sessions.Length, successCount, failedCount);
    }

    /// <summary>
    /// Gửi một NetOpsMessage dạng Unicast tới một Agent cụ thể theo AgentId.
    /// </summary>
    public async Task<bool> SendUnicastAsync(string agentId, NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(agentId))
        {
            _logger?.LogWarning("[ConnectionManager] SendUnicastAsync failed: agentId is null or whitespace");
            return false;
        }

        var session = GetSessionByAgentId(agentId);
        if (session == null)
        {
            _logger?.LogWarning("[ConnectionManager] SendUnicastAsync failed: Agent '{AgentId}' is not connected or not found", agentId);
            return false;
        }

        return await SendToSessionInternalAsync(session, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gửi một NetOpsMessage dạng Unicast tới một Agent cụ thể theo SessionId.
    /// </summary>
    public async Task<bool> SendUnicastBySessionIdAsync(string sessionId, NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger?.LogWarning("[ConnectionManager] SendUnicastBySessionIdAsync failed: sessionId is null or whitespace");
            return false;
        }

        var session = GetSession(sessionId);
        if (session == null)
        {
            _logger?.LogWarning("[ConnectionManager] SendUnicastBySessionIdAsync failed: Session '{SessionId}' is not found", sessionId);
            return false;
        }

        return await SendToSessionInternalAsync(session, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Hàm nội bộ thực thi việc gửi tin nhắn tới một session cụ thể một cách an toàn.
    /// </summary>
    private async Task<bool> SendToSessionInternalAsync(AgentSession session, NetOpsMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await session.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug(
                "[ConnectionManager] Sent Unicast message {MessageType} (Id: {MessageId}) to Agent '{AgentId}' (Session: {SessionId})",
                message.MessageType, message.MessageId, session.AgentId ?? "Anonymous", session.SessionId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ///Ném lại ngoại lệ nếu lý do dừng là do yêu cầu hủy thao tác
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[ConnectionManager] Error sending Unicast message to Agent '{AgentId}' (Session: {SessionId})",
                session.AgentId ?? "Anonymous", session.SessionId);
            return false;
        }
    }
}
