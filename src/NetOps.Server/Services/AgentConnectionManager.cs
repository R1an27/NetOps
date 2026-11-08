using Grpc.Core;
using NetOps.Shared;
using RabbitMQ.Client;
using System.Collections.Concurrent;
using static MassTransit.MessageHeaders;

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

    // _connections Kết nối đang hoạt động
    // _knownAgents Thông tin máy đã từng đăng ký
    private readonly ConcurrentDictionary<string, RegisterAgentRequest> _knownAgents = new();

    // Tạo biến lưu trữ công cụ ghi log.
    private readonly ILogger<AgentConnectionManager> _logger;

    // Constructor này nhận một đầu vào là một công cụ ghi log
    // Bên Program.cs đã có AddSingleTon, nó sẽ truyền công cụ ghi log vào giúp
    // Công cụ ghi log được truyền vào là công cụ khi ta khai báo ở [1:Program.cs]
    // ILogger<AgentConnectionManager> là một kiểu dữ liệu cho công cụ ghi log
    // Giống như kiểu int là kiểu dữ liệu cho số nguyên
    // Nói chính xác hơn thì ILogger<AgentConnectionManager> là một interface
    // Quy định các chức năng mà công cu ghi log phải cung cấp:
    // ILogger: cung cấp chứuc năng ghi log
    // <AgentConnectionManager>: xác định log thuộc nhóm của class AgentConnectionManager
    // Nhằm giúp nhận biệt nguồn phát sinh log.
    public AgentConnectionManager(ILogger<AgentConnectionManager> logger)
    {
        // Bắt buộc phải lưu lại vì hết khi chạy hết phạm vi của constructor này
        // thì đối tượng ghi log không được lưu lại sẽ coi như phí công cốc
        // Nói chính xác hơn thì sau phép gán cả 2 sẽ được trỏ vào một đối tượng logger
        // Cho nên sẽ không có đối tượng ghi log mới nào được tạo ra sau phép gán này cả
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

        // Dòng này lưu thông tin máy theo Agent ID; khi cùng Agent đăng ký lại, thông tin được cập nhật.
        _knownAgents[agentId] = info;

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

    // _knownAgents.Values: lấy thông tin các máy đã đăng ký.
    // Select(...): chuyển thông tin mỗi máy thành một AgentSummary.
    // ContainsKey(...): kiểm tra Server còn giữ kết nối của Agent đó hay không.
    // ToList(): gom kết quả thành danh sách.
    // Khi Agent ngắt kết nối, Unregister xóa nó khỏi _connections,
    // còn thông tin trong _knownAgents vẫn tồn tại. Vì vậy IsOnline sẽ trả về false.
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