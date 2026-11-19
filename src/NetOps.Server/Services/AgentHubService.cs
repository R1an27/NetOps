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

        // AgentId trong yêu cầu do máy tự gửi. client_id trong token đã được Keycloak ký.
        // So sánh trước khi ghi vào danh bạ để mỗi máy chỉ có thể cập nhật kết nối của chính mình.
        if (info.AgentId != Context.User?.FindFirst("client_id")?.Value)
        {
            throw new HubException("AgentId phải trùng với Client ID đã cấp cho máy này.");
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
            || !_connectionManager.UpdateHeartbeat(agentId, Context.ConnectionId, SanitizeMetrics(heartbeat)))
        {
            throw new HubException("Agent chua dang ky hoac phien ket noi da het hieu luc.");
        }

        _logger.LogInformation("Nhan Heartbeat tu {AgentId}: CPU {CpuUsagePercent}%, RAM {MemoryUsedMb} MB.",
            agentId, heartbeat.CpuUsagePercent, heartbeat.MemoryUsedBytes / (1024 * 1024));
    }

    private static HeartbeatMessage SanitizeMetrics(HeartbeatMessage heartbeat)
    {
        heartbeat.CpuUsagePercent = double.IsFinite(heartbeat.CpuUsagePercent)
            ? Math.Clamp(heartbeat.CpuUsagePercent, 0, 100)
            : 0;
        heartbeat.MemoryUsedBytes = Math.Max(0, heartbeat.MemoryUsedBytes);
        heartbeat.DiskFreeBytes = Math.Max(0, heartbeat.DiskFreeBytes);
        return heartbeat;
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
        if (!_commandRoutes.TryGetValue<CommandRoute>(cacheKey, out var route)
            || route is null
            || string.IsNullOrWhiteSpace(route.DesktopConnectionId))
        {
            return;
        }

        // Ràng buộc CommandId <-> AgentId: chỉ đúng Agent đã được giao lệnh này mới được gửi output về Desktop.
        if (route.AgentId != agentId)
        {
            throw new HubException("Agent khong duoc phep gui ket qua cho lenh cua Agent khac.");
        }

        try
        {
            await _desktopHubContext.Clients
                .Client(route.DesktopConnectionId)
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
