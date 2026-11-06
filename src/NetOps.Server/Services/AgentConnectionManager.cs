using System.Collections.Concurrent;
using Grpc.Core;
using NetOps.Shared;

namespace NetOps.Server.Services;

/// <summary>
/// Đại diện cho một kết nối gRPC stream 2 chiều đang hoạt động của Agent
/// </summary>
public class AgentConnection
{
    public required string AgentId { get; init; }
    public required IServerStreamWriter<ServerMessage> ResponseStream { get; init; }
    public RegisterAgentRequest? Info { get; set; }
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public HeartbeatMessage? LatestHeartbeat { get; set; }

    // SemaphoreSlim bảo đảm Thread-Safety khi ghi vào gRPC ResponseStream.
    // Theo tài liệu Microsoft gRPC: IServerStreamWriter.WriteAsync KHÔNG an toàn đa luồng,
    // chỉ một tác vụ WriteAsync được phép thực thi tại một thời điểm.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Gửi thông điệp từ Server xuống Agent một cách an toàn luồng (Thread-safe)
    /// </summary>
    public async Task SendMessageAsync(ServerMessage message, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await ResponseStream.WriteAsync(message, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

/// <summary>
/// Quản lý danh sách kết nối active của toàn bộ Agent trong bộ nhớ (In-Memory Connection Hub)
/// </summary>
public class AgentConnectionManager
{
    // Nằm trong thư viện chuẩn System.Collections.Concurrent của Microsoft .NET.
    // Nó hoạt động giống như một cuốn từ điển Dictionary<Key, Value>
    // Key ở đây là 1 chuỗi chứa Id của Agent, Value là object chứa thông tin kết nối của Agent đó
    private readonly ConcurrentDictionary<string, AgentConnection> _connections = new();
    private readonly ILogger<AgentConnectionManager> _logger;

    public AgentConnectionManager(ILogger<AgentConnectionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Đăng ký Agent kết nối vào Dictionary khi máy chủ đó kết nối thành công
    /// </summary>
    public AgentConnection Register(string agentId, IServerStreamWriter<ServerMessage> stream, RegisterAgentRequest info)
    {
        var connection = new AgentConnection
        {
            AgentId = agentId,
            ResponseStream = stream,
            Info = info,
            LastHeartbeatUtc = DateTime.UtcNow
        };

        _connections.AddOrUpdate(agentId, connection, (_, existing) =>
        {
            _logger.LogWarning("Agent {AgentId} đã có kết nối trước đó, cập nhật luồng stream mới.", agentId);
            return connection;
        });

        _logger.LogInformation("Agent {AgentId} ({Hostname} - {IpAddress}) đã kết nối thành công.",
            agentId, info.Hostname, info.IpAddress);

        return connection;
    }

    /// <summary>
    /// Ghi nhận Agent vừa báo tới central server rằng "tôi vẫn còn sống lúc mấy giờ".
    /// Nghe hơi AI nhưng mà giải thích như này đúng bản chất nhất rồi.
    /// </summary>
    public bool UpdateHeartbeat(string agentId, HeartbeatMessage heartbeat)
    {
        if (_connections.TryGetValue(agentId, out var connection))
        {
            connection.LastHeartbeatUtc = DateTime.UtcNow;
            connection.LatestHeartbeat = heartbeat;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Xóa Agent khỏi Dictionary khi máy đó tắt nguồn hoặc mất mạng.
    /// </summary>
    public bool Unregister(string agentId)
    {
        if (_connections.TryRemove(agentId, out var connection))
        {
            _logger.LogInformation("Agent {AgentId} đã ngắt kết nối khỏi Server.", agentId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Giúp API tìm lại đường truyền của 1 Agent khi User muốn gửi lệnh.
    /// </summary>
    public bool TryGetConnection(string agentId, out AgentConnection? connection)
    {
        return _connections.TryGetValue(agentId, out connection);
    }

    /// <summary>
    /// Trả về luồng gRPC stream của Agent theo Id (dùng cho CommandConsumer của MassTransit)
    /// </summary>
    public IServerStreamWriter<ServerMessage>? GetConnection(string agentId)
    {
        if (_connections.TryGetValue(agentId, out var connection))
        {
            return connection.ResponseStream;
        }
        return null;
    }
    /// <summary>
    /// Lấy danh sách toàn bộ các Agent đang Online
    /// </summary>
    public IReadOnlyCollection<AgentConnection> GetAllConnections()
    {
        return _connections.Values.ToList().AsReadOnly();
    }
}