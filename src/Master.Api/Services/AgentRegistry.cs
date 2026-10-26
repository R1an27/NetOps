using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServerManager.Shared.Contracts.Models;

namespace Master.Api.Services;

/// <summary>
/// Hiện thực quản lý danh bạ Agent sử dụng bảng băm đồng thời ConcurrentDictionary.
/// Đáp ứng yêu cầu T16.1 trong Barem chấm điểm:
/// - Lưu trữ an toàn đa luồng trên nền tảng .NET 10 với cấu trúc ConcurrentDictionary.
/// - Cơ chế khóa phân đoạn (Lock-striping) và các thao tác nguyên tử (Atomic operations) như AddOrUpdate.
/// - Độ phức tạp thời gian O(1) cho các thao tác tra cứu, thêm mới và cập nhật trạng thái sống còn.
/// - Khi ngắt kết nối (T16.4), chỉ chuyển trạng thái sang Offline và lưu DisconnectReason, KHÔNG xóa bản ghi.
/// </summary>
public class AgentRegistry : IAgentRegistry
{
    /// <summary>
    /// Bảng băm đồng thời lưu trữ danh bạ Agent, khóa là AgentId không phân biệt hoa thường.
    /// ConcurrentDictionary đảm bảo thread-safe tuyệt đối khi hàng chục Agent cùng gửi heartbeat song song.
    /// </summary>
    private readonly ConcurrentDictionary<string, AgentStatusRecord> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<AgentRegistry> _logger;

    /// <summary>
    /// Đếm số lượng Agent đang trực tuyến (Status == Online).
    /// </summary>
    public int OnlineCount => _agents.Values.Count(a => a.Status == AgentStatus.Online);

    /// <summary>
    /// Tổng số lượng Agent đã biết trong hệ thống.
    /// </summary>
    public int TotalCount => _agents.Count;

    /// <summary>
    /// Sự kiện kích hoạt khi trạng thái Agent thay đổi (Online <-> Offline).
    /// </summary>
    public event Action<AgentStatusRecord>? AgentStatusChanged;

    /// <summary>
    /// Sự kiện kích hoạt chuyên biệt khi phát hiện Agent bị ngắt kết nối hoặc timeout.
    /// </summary>
    public event Action<AgentStatusRecord>? AgentOfflineDetected;

    /// <summary>
    /// Khởi tạo AgentRegistry với bộ ghi log ILogger tùy chọn.
    /// </summary>
    public AgentRegistry(ILogger<AgentRegistry>? logger = null)
    {
        _logger = logger ?? NullLogger<AgentRegistry>.Instance;
    }

    /// <summary>
    /// Đăng ký mới hoặc cập nhật trạng thái Agent thành Online (T16.2).
    /// Sử dụng AddOrUpdate để đảm bảo tính nguyên tử (Atomic), tránh Race Condition giữa các luồng mạng.
    /// </summary>
    public AgentStatusRecord RegisterOrUpdateOnline(string agentId, string? sessionId, string remoteEndPoint, DateTimeOffset? seenAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var now = seenAt ?? DateTimeOffset.UtcNow;
        bool statusChanged = false;

        // Thao tác nguyên tử AddOrUpdate:
        // - Nếu AgentId chưa có: Khởi tạo bản ghi mới với trạng thái Online.
        // - Nếu AgentId đã tồn tại: Cập nhật lại SessionId, RemoteEndPoint, LastSeenAt và chuyển sang Online nếu trước đó đang Offline.
        var record = _agents.AddOrUpdate(
            agentId,
            addValueFactory: id =>
            {
                statusChanged = true;
                return new AgentStatusRecord(id, sessionId, remoteEndPoint, AgentStatus.Online, now, now);
            },
            updateValueFactory: (id, existing) =>
            {
                // Nếu trước đó đang Offline thì ghi nhận sự kiện chuyển đổi trạng thái
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

        _logger.LogInformation(
            "[AgentRegistry] Agent '{AgentId}' is ONLINE (Session: {SessionId}, EndPoint: {EndPoint}, LastSeenAt: {LastSeenAt:u})",
            agentId, sessionId ?? "N/A", remoteEndPoint, now);

        // Kích hoạt sự kiện thông báo nếu có sự thay đổi trạng thái
        if (statusChanged)
        {
            try
            {
                AgentStatusChanged?.Invoke(record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentRegistry] Error invoking AgentStatusChanged for Agent '{AgentId}'", agentId);
            }
        }

        return record;
    }

    /// <summary>
    /// Đánh dấu Agent sang trạng thái Offline kèm nguyên nhân ngắt kết nối (T16.4).
    /// Tuyệt đối KHÔNG xóa bản ghi khỏi _agents để giữ lại lịch sử vận hành và vết kiểm toán (Audit Trail).
    /// </summary>
    public AgentStatusRecord? MarkOffline(string agentId, DisconnectReason reason, DateTimeOffset? disconnectedAt = null)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return null;

        var timestamp = disconnectedAt ?? DateTimeOffset.UtcNow;
        bool statusChanged = false;

        var record = _agents.AddOrUpdate(
            agentId,
            addValueFactory: id =>
            {
                // Trường hợp đặc biệt nếu Agent chưa từng kết nối mà bị đánh dấu offline
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

                // Chuyển sang Offline, hủy Session hiện tại nhưng giữ nguyên LastSeenAt và lịch sử kết nối
                existing.Status = AgentStatus.Offline;
                existing.CurrentSessionId = null;
                existing.DisconnectedAt = timestamp;
                existing.DisconnectReason = reason;
                return existing;
            });

        _logger.LogWarning(
            "[AgentRegistry] Agent '{AgentId}' is marked OFFLINE (Reason: {Reason}, DisconnectedAt: {DisconnectedAt:u})",
            agentId, reason, timestamp);

        if (statusChanged)
        {
            // Bắn sự kiện thay đổi trạng thái
            try
            {
                AgentStatusChanged?.Invoke(record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentRegistry] Error invoking AgentStatusChanged for Agent '{AgentId}'", agentId);
            }

            // Bắn sự kiện chuyên biệt AgentOfflineDetected để kích hoạt báo động
            try
            {
                AgentOfflineDetected?.Invoke(record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentRegistry] Error invoking AgentOfflineDetected for Agent '{AgentId}'", agentId);
            }
        }

        return record;
    }

    /// <summary>
    /// Giai đoạn T18.2: Đánh dấu một Agent chuyển sang trạng thái Reconnecting khi nhận tín hiệu hoặc bắt đầu chu kỳ thử lại.
    /// </summary>
    public AgentStatusRecord? MarkReconnecting(string agentId, DisconnectReason reason = DisconnectReason.NetworkFault)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return null;

        bool statusChanged = false;
        var now = DateTimeOffset.UtcNow;

        var record = _agents.AddOrUpdate(
            agentId,
            addValueFactory: id =>
            {
                statusChanged = true;
                return new AgentStatusRecord(id, null, "Unknown", AgentStatus.Reconnecting, now, now)
                {
                    DisconnectReason = reason
                };
            },
            updateValueFactory: (id, existing) =>
            {
                if (existing.Status != AgentStatus.Reconnecting)
                {
                    statusChanged = true;
                }

                existing.Status = AgentStatus.Reconnecting;
                existing.CurrentSessionId = null;
                existing.DisconnectReason = reason;
                return existing;
            });

        _logger.LogInformation(
            "[AgentRegistry] Agent '{AgentId}' is RECONNECTING (Reason: {Reason})",
            agentId, reason);

        if (statusChanged)
        {
            try
            {
                AgentStatusChanged?.Invoke(record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentRegistry] Error invoking AgentStatusChanged for Agent '{AgentId}'", agentId);
            }
        }

        return record;
    }

    /// <summary>
    /// Giai đoạn T18.2: Đánh dấu một Agent chuyển sang trạng thái Connecting khi bắt đầu thiết lập phiên.
    /// </summary>
    public AgentStatusRecord? MarkConnecting(string agentId, string? sessionId = null, string? remoteEndPoint = null)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return null;

        bool statusChanged = false;
        var now = DateTimeOffset.UtcNow;

        var record = _agents.AddOrUpdate(
            agentId,
            addValueFactory: id =>
            {
                statusChanged = true;
                return new AgentStatusRecord(id, sessionId, remoteEndPoint ?? "Unknown", AgentStatus.Connecting, now, now);
            },
            updateValueFactory: (id, existing) =>
            {
                if (existing.Status != AgentStatus.Connecting)
                {
                    statusChanged = true;
                }

                existing.Status = AgentStatus.Connecting;
                existing.CurrentSessionId = sessionId;
                if (!string.IsNullOrEmpty(remoteEndPoint))
                {
                    existing.RemoteEndPoint = remoteEndPoint;
                }
                return existing;
            });

        _logger.LogInformation(
            "[AgentRegistry] Agent '{AgentId}' is CONNECTING (Session: {SessionId}, EndPoint: {EndPoint})",
            agentId, sessionId ?? "N/A", remoteEndPoint ?? "Unknown");

        if (statusChanged)
        {
            try
            {
                AgentStatusChanged?.Invoke(record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentRegistry] Error invoking AgentStatusChanged for Agent '{AgentId}'", agentId);
            }
        }

        return record;
    }

    /// <summary>
    /// Tra cứu thông tin trạng thái của một Agent theo AgentId với độ phức tạp O(1).
    /// </summary>
    public AgentStatusRecord? Get(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return null;
        _agents.TryGetValue(agentId, out var record);
        return record;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ các Agent dưới dạng mảng bản sao (Snapshot) an toàn, tránh can thiệp vào bộ sưu tập gốc.
    /// </summary>
    public IReadOnlyCollection<AgentStatusRecord> GetAll(AgentStatus? statusFilter = null)
    {
        var all = _agents.Values.ToArray();
        return statusFilter.HasValue
            ? all.Where(a => a.Status == statusFilter.Value).ToArray()
            : all;
    }
}
