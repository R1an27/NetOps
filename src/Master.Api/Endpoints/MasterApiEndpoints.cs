using Master.Api.Options;
using Master.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

        // 2. Endpoint lấy danh sách các Agent (hỗ trợ lọc ?status=online|offline|all)
        app.MapGet("/api/agents", (IConnectionManager connectionManager, string? status) =>
        {
            // Nếu có tham số status (online, offline, all): Trả về danh sách chi tiết từ AgentRegistry
            if (!string.IsNullOrWhiteSpace(status))
            {
                AgentStatus? filter = status.ToLowerInvariant() switch
                {
                    "online" => AgentStatus.Online,
                    "offline" => AgentStatus.Offline,
                    "connecting" => AgentStatus.Connecting,
                    "reconnecting" => AgentStatus.Reconnecting,
                    _ => null
                };

                var records = connectionManager.GetAllAgentStatuses(filter);
                return Results.Ok(records);
            }

            // Mặc định: Trả về danh sách các phiên đang kết nối, tương thích ngược 100% với các test hiện có
            // Khi bạn nâng cấp hoặc thêm tính năng mới vào hệ thống, 
            // những code cũ, client cũ hoặc các Unit Test / Integration Test 
            // đã viết từ trước vẫn tiếp tục chạy đúng mà không bị lỗi
            var agents = connectionManager.GetAllSessions().Select(s => new
            {
                s.SessionId,
                s.AgentId,
                s.RemoteEndPoint,
                s.ConnectedAt,
                s.LastSeenAt,
                s.IsAuthenticated,
                Status = AgentStatus.Online.ToString(),
                IdleSeconds = Math.Round((DateTimeOffset.UtcNow - s.LastSeenAt).TotalSeconds, 1)
            });

            return Results.Ok(agents);
        })
        .WithName("GetConnectedAgents")
        .WithSummary("Lấy danh sách các Agent (mặc định Online hoặc lọc theo status: online, offline, all)");

        // 3. Endpoint tra cứu trạng thái vòng đời của một Agent theo AgentId
        app.MapGet("/api/agents/status/{agentId}", (string agentId, IConnectionManager connectionManager) =>
        {
            var record = connectionManager.GetAgentStatus(agentId);
            if (record == null)
            {
                return Results.NotFound(new { Message = $"Agent '{agentId}' status not found in registry" });
            }

            return Results.Ok(record);
        })
        .WithName("GetAgentLifecycleStatus")
        .WithSummary("Tra cứu trạng thái vòng đời kết nối chi tiết (Online/Offline/Reason) của Agent");

        // 4. Endpoint lấy chi tiết một phiên kết nối theo SessionId
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
                Status = AgentStatus.Online.ToString(),
                IdleSeconds = Math.Round((DateTimeOffset.UtcNow - session.LastSeenAt).TotalSeconds, 1)
            });
        })
        .WithName("GetAgentSessionById")
        .WithSummary("Lấy thông tin chi tiết của một phiên kết nối theo SessionId");

        // 4. Endpoint gửi lệnh Shell Unicast tới một Agent cụ thể
        // Shell Unicast là gửi lệnh tới 1 agent cụ thể 
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
        // NetOpsMessage Unicast là gửi thông điệp NetOpsMessage tới 1 agent cụ thể 
        // Không phải gửi lệnh Shell mà là gửi thông điệp NetOpsMessage bất kỳ
        // NetOpsMessage là một thông điệp NetOps Protocol v0
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

        // 8. Endpoint kiểm thử sự cố: Thực thi kịch bản Failure Test chuẩn hóa (T19.2)
        app.MapPost("/api/fault/simulate", async (
            FailureTestRequest request,
            IConnectionManager connectionManager,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken) =>
        {
            if (request == null)
            {
                return Results.BadRequest(new { Message = "FailureTestRequest is required." });
            }

            var failureService = serviceProvider.GetService<IFailureTestService>()
                ?? new FailureTestService(connectionManager);

            var report = await failureService.ExecuteScenarioAsync(request, cancellationToken);
            return Results.Ok(report);
        })
        .WithName("SimulateFailureScenario")
        .WithSummary("Thực thi một kịch bản kiểm thử sự cố mạng và phục hồi (Failure Test / Chaos Injection)");

        // 9. Endpoint kiểm thử sự cố: Cưỡng chế ngắt socket của một Agent cụ thể (T19.2)
        app.MapPost("/api/fault/drop-socket/{agentId}", (
            string agentId,
            IConnectionManager connectionManager,
            IServiceProvider serviceProvider) =>
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                return Results.BadRequest(new { Message = "AgentId is required." });
            }

            var failureService = serviceProvider.GetService<IFailureTestService>()
                ?? new FailureTestService(connectionManager);

            bool terminated = failureService.TerminateSessionAbruptly(agentId, DisconnectReason.NetworkFault);
            if (!terminated)
            {
                return Results.NotFound(new
                {
                    Success = false,
                    Message = $"Active session for Agent '{agentId}' not found to terminate.",
                    AgentId = agentId
                });
            }

            return Results.Ok(new
            {
                Success = true,
                AgentId = agentId,
                Message = $"Socket for Agent '{agentId}' terminated abruptly with TCP RST simulation."
            });
        })
        .WithName("DropAgentSocketAbruptly")
        .WithSummary("Cưỡng chế ngắt kết nối socket của Agent để kiểm thử khả năng tự động kết nối lại");

        // 10. Endpoint lấy danh sách các kịch bản kiểm thử sự cố được hỗ trợ (T19.2)
        app.MapGet("/api/fault/scenarios", (
            IConnectionManager connectionManager,
            IServiceProvider serviceProvider) =>
        {
            var failureService = serviceProvider.GetService<IFailureTestService>()
                ?? new FailureTestService(connectionManager);

            var scenarios = failureService.GetSupportedScenarios();
            return Results.Ok(scenarios);
        })
        .WithName("GetSupportedFailureScenarios")
        .WithSummary("Liệt kê danh sách các kịch bản kiểm thử sự cố mạng được hỗ trợ");

        return app;
    }
}
