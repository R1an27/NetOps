using System.Collections.Concurrent;
using NetOps.Shared;

namespace NetOps.Server.Services;

/// <summary>
/// Đại diện cho một kết nối SignalR đang hoạt động của Agent.
/// </summary>
public sealed record AgentConnection
{
    public required string AgentId { get; init; }
    public required string ConnectionId { get; init; }
    public required RegisterAgentRequest Info { get; init; }
    public DateTime LastHeartbeatUtc { get; init; } = DateTime.UtcNow;
    public HeartbeatMessage? LatestHeartbeat { get; init; }
}

// Thông tin định tuyến cho 1 lệnh đang chạy, lưu trong RAM (IMemoryCache) theo key "command:<CommandId>".
// Lưu cả 2 giá trị (DesktopConnectionId + AgentId) để khi Agent trả output về:
//   1. Biết chính xác phải gửi kết quả về phiên Desktop nào (DesktopConnectionId).
//   2. Kiểm tra đúng Agent được giao lệnh mới được gửi output, chặn Agent khác giả mạo kết quả (AgentId).
public sealed record CommandRoute(string DesktopConnectionId, string AgentId);

/// <summary>
/// Quản lý danh sách kết nối active và danh bạ các Agent đã biết trong bộ nhớ.
/// </summary>
public class AgentConnectionManager
{
    private readonly ConcurrentDictionary<string, AgentConnection> _connections = new();
    private readonly ConcurrentDictionary<string, RegisterAgentRequest> _knownAgents = new();
    private readonly ILogger<AgentConnectionManager> _logger;

    public AgentConnectionManager(ILogger<AgentConnectionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Đăng ký Agent kết nối vào Dictionary khi Agent mở phiên SignalR thành công.
    /// </summary>
    public AgentConnection Register(string connectionId, RegisterAgentRequest info)
    {
        var connection = new AgentConnection
        {
            AgentId = info.AgentId,
            ConnectionId = connectionId,
            Info = info
        };

        _connections[info.AgentId] = connection;
        _knownAgents[info.AgentId] = info;

        _logger.LogInformation("Agent {AgentId} ({Hostname} - {IpAddress}) da dang ky phien {ConnectionId}.",
            info.AgentId, info.Hostname, info.IpAddress, connectionId);

        return connection;
    }

    /// <summary>
    /// Cập nhật thời điểm Heartbeat nếu AgentId vẫn đang gắn với đúng ConnectionId hiện hành.
    /// </summary>
    public bool UpdateHeartbeat(string agentId, string connectionId, HeartbeatMessage heartbeat)
    {
        if (!_connections.TryGetValue(agentId, out var connection) || connection.ConnectionId != connectionId)
        {
            return false;
        }

        return _connections.TryUpdate(agentId, connection with
        {
            LastHeartbeatUtc = DateTime.UtcNow,
            LatestHeartbeat = heartbeat
        }, connection);
    }

    /// <summary>
    /// Xóa kết nối đang hoạt động nếu đúng ConnectionId, giữ lại thông tin trong _knownAgents.
    /// </summary>
    public bool Unregister(string agentId, string connectionId)
    {
        if (!_connections.TryGetValue(agentId, out var connection) || connection.ConnectionId != connectionId)
        {
            return false;
        }

        // Chỉ xóa đúng phiên đã đọc; phiên mới có thể đăng ký trong lúc phiên cũ đang đóng.
        if (!_connections.TryRemove(new KeyValuePair<string, AgentConnection>(agentId, connection)))
        {
            return false;
        }

        _logger.LogInformation("Agent {AgentId} da ngat ket noi.", agentId);
        return true;
    }

    /// <summary>
    /// Tìm phiên kết nối đang hoạt động của một Agent theo AgentId.
    /// </summary>
    public bool TryGetConnection(string agentId, out AgentConnection? connection)
    {
        return _connections.TryGetValue(agentId, out connection);
    }

    /// <summary>
    /// Lấy snapshot các Agent đang kết nối để xuất metrics sang Prometheus.
    /// </summary>
    public IReadOnlyCollection<AgentConnection> GetActiveConnections() => _connections.Values.ToArray();

    /// <summary>
    /// Trả về danh bạ tất cả Agent từng đăng ký kèm trạng thái online/offline cho Desktop.
    /// </summary>
    public List<AgentSummary> GetAgents()
    {
        return _knownAgents.Values
            .Select(info => new AgentSummary
            {
                AgentId = info.AgentId,
                Hostname = info.Hostname,
                IpAddress = info.IpAddress,
                OsDescription = info.OsDescription,
                IsOnline = _connections.ContainsKey(info.AgentId)
            })
            .ToList();
    }
}
