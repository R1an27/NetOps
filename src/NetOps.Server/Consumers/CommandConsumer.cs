using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using NetOps.Server.Hubs;
using NetOps.Server.Services;
using NetOps.Shared;
using NetOps.Shared.Messages;

namespace NetOps.Server.Consumers;

/// <summary>
/// Nhận lệnh từ RabbitMQ và đẩy xuống Agent qua SignalR Hub.
/// </summary>
public class CommandConsumer(
    AgentConnectionManager connections,
    IHubContext<AgentHubService> agentHub,
    IHubContext<NetOpsHub> desktopHub,
    IMemoryCache commandRoutes,
    ILogger<CommandConsumer> logger) : IConsumer<RunCommandMessage>
{
    public async Task Consume(ConsumeContext<RunCommandMessage> context)
    {
        var msg = context.Message;
        logger.LogInformation("Đã nhận lệnh {CommandId} cho Agent {AgentId}", msg.CommandId, msg.AgentId);

        if (!connections.TryGetConnection(msg.AgentId, out var conn) || conn is null)
        {
            logger.LogWarning("Agent {AgentId} đang offline, hủy lệnh {CommandId}.", msg.AgentId, msg.CommandId);
            await NotifyOfflineAsync(msg, context.CancellationToken);
            return;
        }

        var request = new CommandRequest
        {
            CommandId = msg.CommandId,
            CommandText = msg.CommandText,
            TimeoutSeconds = msg.TimeoutSeconds
        };

        try
        {
            await agentHub.Clients.Client(conn.ConnectionId)
                .SendAsync("ReceiveCommand", request, context.CancellationToken);

            logger.LogInformation("Đã đẩy lệnh {CommandId} xuống Agent {AgentId} (phiên {ConnectionId})",
                msg.CommandId, msg.AgentId, conn.ConnectionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Lỗi SignalR khi đẩy lệnh {CommandId} xuống Agent {AgentId}", msg.CommandId, msg.AgentId);
            throw; // Để MassTransit retry hoặc đưa vào error queue
        }
    }

    private async Task NotifyOfflineAsync(RunCommandMessage msg, CancellationToken ct)
    {
        var cacheKey = $"command:{msg.CommandId}";
        if (!commandRoutes.TryGetValue<string>(cacheKey, out var desktopId) || string.IsNullOrWhiteSpace(desktopId))
            return;

        commandRoutes.Remove(cacheKey);

        try
        {
            await desktopHub.Clients.Client(desktopId).SendAsync("CommandOutputReceived", new CommandOutput
            {
                AgentId = msg.AgentId,
                CommandId = msg.CommandId,
                OutputLine = $"[ERROR] Agent {msg.AgentId} đang offline, không thể thực thi lệnh.",
                IsError = true,
                IsCompleted = true,
                ExitCode = -1
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Không gửi được thông báo offline cho lệnh {CommandId}.", msg.CommandId);
        }
    }
}
