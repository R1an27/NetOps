using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using NetOps.Server.Services;
using NetOps.Shared;
using NetOps.Shared.Messages;

namespace NetOps.Server.Hubs;

public class NetOpsHub : Hub
{
    private static readonly Uri CommandQueueUri = new("queue:agent-commands-queue");

    private readonly AgentConnectionManager _connections;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<NetOpsHub> _logger;

    public NetOpsHub(
        AgentConnectionManager connections,
        ISendEndpointProvider sendEndpointProvider,
        IMemoryCache cache,
        ILogger<NetOpsHub> logger)
    {
        _connections = connections;
        _sendEndpointProvider = sendEndpointProvider;
        _cache = cache;
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        // Từ chối kết nối chưa xác định được danh tính người dùng từ token.
        if (string.IsNullOrWhiteSpace(Context.UserIdentifier))
        {
            Context.Abort();
            return Task.CompletedTask;
        }

        return base.OnConnectedAsync();
    }

    // Bắt buộc người gọi phải có ít nhất 1 trong 3 role NetOps (Admin / Operator / Viewer) mới xem được danh sách Agent.
    // Nếu bỏ dòng này, tài khoản mới đăng ký chưa được cấp role vẫn có thể gọi GetAgents() vì Hub chỉ yêu cầu đã đăng nhập.
    [Authorize(Policy = "ReadMetrics")]
    public List<AgentSummary> GetAgents()
    {
        return _connections.GetAgents();
    }

    [Authorize(Policy = "RunCommands")]
    public async Task<SubmitCommandResponse> SubmitCommand(SubmitCommandRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId))
            throw new HubException("AgentId không được để trống.");

        if (string.IsNullOrWhiteSpace(request.CommandText))
            throw new HubException("Nội dung lệnh không được để trống.");

        if (request.TimeoutSeconds is < 1 or > 3600)
            throw new HubException("TimeoutSeconds phải nằm trong khoảng 1–3600.");

        if (string.IsNullOrWhiteSpace(Context.UserIdentifier))
            throw new HubException("Không tìm thấy định danh người gửi lệnh.");

        var agentId = request.AgentId.Trim();
        if (!_connections.TryGetConnection(agentId, out var connection) || connection is null)
            throw new HubException("Không tìm thấy kết nối của Agent.");

        // Lưu tuyến trả kết quả vào RAM (IMemoryCache): "command:<CommandId>" -> (DesktopConnectionId, AgentId).
        // Phân biệt rõ:
        // - RabbitMQ (bên dưới) giữ NỘI DUNG LỆNH để xếp hàng gửi xuống Agent.
        // - IMemoryCache (dòng này) giữ cả ĐỊA CHỈ DESKTOP nhận output lẫn AGENT ĐƯỢC GIAO LỆNH,
        //   giúp AgentHubService vừa trả output về đúng cửa sổ Desktop vừa chặn Agent khác gửi output mạo danh.
        // Phải lưu TRƯỚC khi đẩy vào RabbitMQ phòng trường hợp Agent chạy quá nhanh trả kết quả về ngay lập tức.
        // Đặt thời hạn tự xóa (TTL) = TimeoutSeconds + 60 giây dự phòng:
        //   - Đủ dài để không bao giờ bị xóa nhầm khi lệnh hợp lệ còn đang chạy.
        //   - Đảm bảo tự dọn sạch RAM nếu Agent sập nguồn/mất mạng giữa chừng mà không kịp gửi gói IsCompleted = true.
        var commandId = Guid.NewGuid().ToString("N");
        var cacheKey = $"command:{commandId}";
        _cache.Set(cacheKey, new CommandRoute(Context.ConnectionId, agentId), TimeSpan.FromSeconds(request.TimeoutSeconds + 60));

        var message = new RunCommandMessage
        {
            AgentId = agentId,
            CommandId = commandId,
            CommandText = request.CommandText,
            TimeoutSeconds = request.TimeoutSeconds
        };

        try
        {
            var endpoint = await _sendEndpointProvider.GetSendEndpoint(CommandQueueUri);
            await endpoint.Send(message, Context.ConnectionAborted);
        }
        catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
        {
            _cache.Remove(cacheKey);
            throw;
        }
        catch (Exception ex)
        {
            _cache.Remove(cacheKey);
            _logger.LogError(ex, "Lỗi gửi lệnh {CommandId} vào RabbitMQ.", commandId);
            throw new HubException("Không xác nhận được việc gửi lệnh vào hàng đợi.");
        }

        return new SubmitCommandResponse { CommandId = commandId };
    }
}
