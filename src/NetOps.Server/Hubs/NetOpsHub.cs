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

        // Lưu route bằng chính ConnectionId hiện tại trước khi đẩy vào queue
        // để output trả về nhanh vẫn định tuyến đúng về Desktop này.
        var commandId = Guid.NewGuid().ToString("N");
        var cacheKey = $"command:{commandId}";
        _cache.Set(cacheKey, Context.ConnectionId);

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
