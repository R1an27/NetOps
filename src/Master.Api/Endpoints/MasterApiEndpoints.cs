using Master.Api.Options;
using Master.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;

namespace Master.Api.Endpoints;

/// <summary>
/// Yêu cầu gửi lệnh Shell từ HTTP Client tới Agent qua Master Server.
/// </summary>
public record SendCommandRequest(
    string CommandLine,
    string? CommandId = null,
    int TimeoutSeconds = 30,
    string? WorkingDir = null
);

/// <summary>
/// Các endpoint Minimal API quản trị và giám sát trạng thái hệ thống Master Server.
/// </summary>
public static class MasterApiEndpoints
{
    /// <summary>
    /// Đăng ký các route Minimal API (/api/health, /api/agents, /api/agents/{sessionId}, /api/agents/{agentId}/command, /api/agents/{agentId}/message, /api/broadcast/command, /api/broadcast/message) vào pipeline.
    /// </summary>
    public static IEndpointRouteBuilder MapMasterApiEndpoints(this IEndpointRouteBuilder app)
    {
        // 1. Endpoint kiểm tra sức khỏe và thông số vận hành của Master Server
        app.MapGet("/api/health", (
            IConnectionManager connectionManager,
            IOptions<MasterServerOptions> options) =>
        {
            return Results.Ok(new
            {
                Status = "Healthy",
                Timestamp = DateTimeOffset.UtcNow,
                TcpPort = options.Value.Port,
                ActiveConnections = connectionManager.ActiveCount,
                Watchdog = new
                {
                    IntervalSeconds = options.Value.WatchdogIntervalSeconds,
                    TimeoutSeconds = options.Value.SessionTimeoutSeconds
                }
            });
        })
        .WithName("GetServerHealth")
        .WithSummary("Kiểm tra trạng thái sức khỏe và số lượng kết nối đang hoạt động của Master Server");

        // 2. Endpoint lấy danh sách toàn bộ các Agent đang kết nối trực tuyến
        app.MapGet("/api/agents", (IConnectionManager connectionManager) =>
        {
            var agents = connectionManager.GetAllSessions().Select(s => new
            {
                s.SessionId,
                s.AgentId,
                s.RemoteEndPoint,
                s.ConnectedAt,
                s.LastSeenAt,
                s.IsAuthenticated,
                IdleSeconds = Math.Round((DateTimeOffset.UtcNow - s.LastSeenAt).TotalSeconds, 1)
            });

            return Results.Ok(agents);
        })
        .WithName("GetConnectedAgents")
        .WithSummary("Lấy danh sách các Agent đang kết nối trực tuyến tới Master Server");

        // 3. Endpoint lấy chi tiết một phiên kết nối theo SessionId
        app.MapGet("/api/agents/{sessionId}", (string sessionId, IConnectionManager connectionManager) =>
        {
            var session = connectionManager.GetSession(sessionId);
            if (session == null)
            {
                return Results.NotFound(new { Message = $"Session '{sessionId}' not found" });
            }

            return Results.Ok(new
            {
                session.SessionId,
                session.AgentId,
                session.RemoteEndPoint,
                session.ConnectedAt,
                session.LastSeenAt,
                session.IsAuthenticated,
                IdleSeconds = Math.Round((DateTimeOffset.UtcNow - session.LastSeenAt).TotalSeconds, 1)
            });
        })
        .WithName("GetAgentSessionById")
        .WithSummary("Lấy thông tin chi tiết của một phiên kết nối theo SessionId");

        // 4. Endpoint gửi lệnh Shell Unicast tới một Agent cụ thể
        app.MapPost("/api/agents/{agentId}/command", async (
            string agentId,
            SendCommandRequest request,
            IConnectionManager connectionManager,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(request?.CommandLine))
            {
                return Results.BadRequest(new { Message = "AgentId and CommandLine are required." });
            }

            var commandId = string.IsNullOrWhiteSpace(request.CommandId)
                ? $"cmd-{Guid.NewGuid():N}"[..12]
                : request.CommandId;

            var payload = new CommandRequestPayload(
                commandId,
                request.CommandLine.Trim(),
                request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30,
                request.WorkingDir);

            var message = NetOpsMessage.CreateRequest(
                MessageType.Command,
                payload,
                requestId: $"req-{commandId}");

            bool sent = await connectionManager.SendUnicastAsync(agentId, message, cancellationToken);
            if (!sent)
            {
                return Results.NotFound(new
                {
                    Success = false,
                    Message = $"Agent '{agentId}' is not connected or not found.",
                    AgentId = agentId
                });
            }

            return Results.Ok(new
            {
                Success = true,
                AgentId = agentId,
                CommandId = commandId,
                CommandLine = request.CommandLine.Trim(),
                Message = "Command dispatched successfully"
            });
        })
        .WithName("SendCommandToAgent")
        .WithSummary("Gửi lệnh Shell Unicast tới một Agent cụ thể đang kết nối");

        // 5. Endpoint gửi thông điệp NetOpsMessage Unicast tùy biến tới một Agent cụ thể
        app.MapPost("/api/agents/{agentId}/message", async (
            string agentId,
            NetOpsMessage message,
            IConnectionManager connectionManager,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(agentId) || message == null)
            {
                return Results.BadRequest(new { Message = "AgentId and message are required." });
            }

            bool sent = await connectionManager.SendUnicastAsync(agentId, message, cancellationToken);
            if (!sent)
            {
                return Results.NotFound(new
                {
                    Success = false,
                    Message = $"Agent '{agentId}' is not connected or not found.",
                    AgentId = agentId
                });
            }

            return Results.Ok(new
            {
                Success = true,
                AgentId = agentId,
                MessageId = message.MessageId,
                MessageType = message.MessageType.ToString(),
                Message = "Unicast message dispatched successfully"
            });
        })
        .WithName("SendMessageToAgent")
        .WithSummary("Gửi thông điệp NetOpsMessage Unicast tùy biến tới một Agent cụ thể đang kết nối");

        // 6. Endpoint phát sóng (broadcast) lệnh Shell tới toàn bộ các Agent đang trực tuyến
        app.MapPost("/api/broadcast/command", async (
            SendCommandRequest request,
            IConnectionManager connectionManager,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request?.CommandLine))
            {
                return Results.BadRequest(new { Message = "CommandLine is required." });
            }

            var commandId = string.IsNullOrWhiteSpace(request.CommandId)
                ? $"cmd-bc-{Guid.NewGuid():N}"[..15]
                : request.CommandId;

            var payload = new CommandRequestPayload(
                commandId,
                request.CommandLine.Trim(),
                request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30,
                request.WorkingDir);

            var message = NetOpsMessage.CreateRequest(
                MessageType.Command,
                payload,
                requestId: $"req-{commandId}");

            var result = await connectionManager.BroadcastAsync(message, cancellationToken);

            return Results.Ok(new
            {
                Success = true,
                CommandId = commandId,
                CommandLine = request.CommandLine.Trim(),
                TotalTargeted = result.TotalTargeted,
                SuccessCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                Message = result.TotalTargeted > 0
                    ? $"Broadcast command dispatched to {result.SuccessCount}/{result.TotalTargeted} agent(s)."
                    : "No online agents found to broadcast."
            });
        })
        .WithName("BroadcastCommand")
        .WithSummary("Phát sóng lệnh Shell đồng loạt tới tất cả các Agent đang kết nối trực tuyến");

        // 7. Endpoint phát sóng (broadcast) thông điệp NetOpsMessage tùy biến tới toàn bộ các Agent đang trực tuyến
        app.MapPost("/api/broadcast/message", async (
            NetOpsMessage message,
            IConnectionManager connectionManager,
            CancellationToken cancellationToken) =>
        {
            if (message == null)
            {
                return Results.BadRequest(new { Message = "Message payload is required." });
            }

            var result = await connectionManager.BroadcastAsync(message, cancellationToken);

            return Results.Ok(new
            {
                Success = true,
                MessageId = message.MessageId,
                MessageType = message.MessageType.ToString(),
                TotalTargeted = result.TotalTargeted,
                SuccessCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                Message = result.TotalTargeted > 0
                    ? $"Broadcast message dispatched to {result.SuccessCount}/{result.TotalTargeted} agent(s)."
                    : "No online agents found to broadcast."
            });
        })
        .WithName("BroadcastMessage")
        .WithSummary("Phát sóng thông điệp NetOpsMessage tùy biến tới tất cả các Agent đang kết nối trực tuyến");

        return app;
    }
}
