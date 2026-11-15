using System.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using NetOps.Server.Hubs;
using NetOps.Shared;

namespace NetOps.Server.Services;

public class AgentHubService : Hub
{
    private readonly AgentConnectionManager _connectionManager;
    private readonly IHubContext<NetOpsHub> _desktopHubContext;
    private readonly IMemoryCache _commandRoutes;
    private readonly ILogger<AgentHubService> _logger;

    public AgentHubService(
        AgentConnectionManager connectionManager,
        IHubContext<NetOpsHub> desktopHubContext,
        IMemoryCache commandRoutes,
        ILogger<AgentHubService> logger)
    {
        _connectionManager = connectionManager;
        _desktopHubContext = desktopHubContext;
        _commandRoutes = commandRoutes;
        _logger = logger;
    }

    public RegisterAgentResponse Register(RegisterAgentRequest? info)
    {
        if (info is null
            || string.IsNullOrWhiteSpace(info.AgentId) || info.AgentId.Length > 128
            || string.IsNullOrWhiteSpace(info.Hostname) || info.Hostname.Length > 253
            || string.IsNullOrWhiteSpace(info.OsDescription) || info.OsDescription.Length > 512
            || !IPAddress.TryParse(info.IpAddress, out _)
            || info.CpuCores <= 0 || info.TotalMemoryBytes <= 0)
        {
            throw new HubException("Thong tin dang ky Agent khong hop le.");
        }

        if (Context.Items["AgentId"] is string agentId && agentId != info.AgentId)
        {
            throw new HubException("Mot ket noi chi duoc dang ky mot AgentId.");
        }

        _connectionManager.Register(Context.ConnectionId, info);
        Context.Items["AgentId"] = info.AgentId;
        return new RegisterAgentResponse { Success = true, Message = "Server da nhan dang ky thanh cong!" };
    }

    public void Heartbeat(HeartbeatMessage? heartbeat)
    {
        if (heartbeat is null
            || Context.Items["AgentId"] is not string agentId
            || heartbeat.AgentId != agentId
            || !_connectionManager.UpdateHeartbeat(agentId, Context.ConnectionId, heartbeat))
        {
            throw new HubException("Agent chua dang ky hoac phien ket noi da het hieu luc.");
        }

        _logger.LogInformation("Nhan Heartbeat tu {AgentId}: CPU {CpuUsagePercent}%.",
            agentId, heartbeat.CpuUsagePercent);
    }

    public async Task SendCommandOutput(CommandOutput? output)
    {
        if (output is null
            || string.IsNullOrWhiteSpace(output.CommandId)
            || Context.Items["AgentId"] is not string agentId
            || !_connectionManager.TryGetConnection(agentId, out var active)
            || active?.ConnectionId != Context.ConnectionId)
        {
            throw new HubException("Agent chua dang ky hoac phien ket noi da het hieu luc.");
        }

        if (!string.IsNullOrWhiteSpace(output.AgentId) && output.AgentId != agentId)
        {
            throw new HubException("Khong duoc phep gia danh AgentId khac.");
        }

        output.AgentId = agentId;

        var cacheKey = $"command:{output.CommandId}";
        if (!_commandRoutes.TryGetValue<string>(cacheKey, out var desktopConnectionId)
            || string.IsNullOrWhiteSpace(desktopConnectionId))
        {
            return;
        }

        try
        {
            await _desktopHubContext.Clients
                .Client(desktopConnectionId)
                .SendAsync("CommandOutputReceived", output);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Khong gui duoc output lenh {CommandId} toi Desktop.", output.CommandId);
        }

        if (output.IsCompleted)
        {
            _commandRoutes.Remove(cacheKey);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items["AgentId"] is string agentId)
        {
            _connectionManager.Unregister(agentId, Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }
}
