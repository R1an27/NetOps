using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;

namespace Master.Api.Services;

/// <summary>
/// Hiện thực bộ điều phối và phân loại thông điệp chuẩn NetOps Protocol v0 trên Master Server.
/// Xử lý tự động:
/// - Ping -> Phản hồi Pong (PONG_ACK).
/// - Heartbeat -> Cập nhật AgentId, LastSeenAt và phản hồi HeartbeatAck.
/// - Auth -> Xác thực, gán AgentId, IsAuthenticated = true và phản hồi AuthOk kèm SessionId.
/// - Metrics -> Bóc tách MetricsPayload, ghi log chi tiết chỉ số phần cứng và phát sự kiện MetricsReceived.
/// - Command / CommandOutput / CommandDone -> Ghi log tiến trình lệnh shell và phát các sự kiện tương ứng.
/// - Unsupported -> Phản hồi ErrorMessagePayload chuẩn hóa.
/// </summary>
public class MasterMessageControl : IMasterMessageControl
{
    private readonly IConnectionManager? _connectionManager;
    private readonly ILogger<MasterMessageControl> _logger;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được báo cáo tài nguyên phần cứng (Metrics) từ Agent.
    /// </summary>
    public event Action<AgentSession, MetricsPayload>? MetricsReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được dòng dữ liệu output (stdout/stderr) từ một lệnh đang thực thi trên Agent.
    /// </summary>
    public event Action<AgentSession, CommandOutputPayload>? CommandOutputReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi một lệnh thực thi trên Agent đã hoàn thành (CommandDone).
    /// </summary>
    public event Action<AgentSession, CommandDonePayload>? CommandDoneReceived;

    /// <summary>
    /// Khởi tạo MasterMessageControl với IConnectionManager và ILogger tùy chọn.
    /// </summary>
    public MasterMessageControl(
        IConnectionManager? connectionManager = null,
        ILogger<MasterMessageControl>? logger = null)
    {
        _connectionManager = connectionManager;
        _logger = logger ?? NullLogger<MasterMessageControl>.Instance;
    }

    /// <summary>
    /// Overload khởi tạo MasterMessageControl chỉ nhận ILogger (hỗ trợ kiểm thử đơn vị).
    /// </summary>
    public MasterMessageControl(ILogger<MasterMessageControl>? logger)
        : this(null, logger)
    {
    }

    /// <summary>
    /// Xử lý phân loại thông điệp đến một cách bất đồng bộ và trả về thông điệp phản hồi (nếu có).
    /// Tự động cập nhật mốc thời gian nhận gói tin (LastSeenAt) cho Agent để phục vụ Watchdog.
    /// </summary>
    public Task<NetOpsMessage?> InvokeAsync(AgentSession session, NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);

        // Cập nhật mốc thời gian nhận gói tin hợp lệ gần nhất của session
        session.LastSeenAt = DateTimeOffset.UtcNow;

        // Nếu session đã có AgentId thì đồng bộ cập nhật vào AgentRegistry
        if (!string.IsNullOrWhiteSpace(session.AgentId))
        {
            _connectionManager?.MarkAgentOnline(session.AgentId, session.SessionId, session.RemoteEndPoint, session.LastSeenAt);
        }

        _logger.LogInformation(
            "[MasterMessageControl] Handling message '{Type}' (Id: {MessageId}) from session {SessionId} (Endpoint: {EndPoint})",
            message.MessageType, message.MessageId, session.SessionId, session.RemoteEndPoint);

        NetOpsMessage? response = message.MessageType switch
        {
            MessageType.Ping => HandlePing(session, message),
            MessageType.Heartbeat => HandleHeartbeat(session, message),
            MessageType.Auth => HandleAuth(session, message),
            MessageType.Metrics => HandleMetrics(session, message),
            MessageType.Command => HandleCommand(session, message),
            MessageType.CommandOutput => HandleCommandOutput(session, message),
            MessageType.CommandDone => HandleCommandDone(session, message),
            _ => HandleUnsupported(session, message)
        };

        return Task.FromResult(response);
    }

    /// <summary>
    /// Overload tiện ích của InvokeAsync cho phép gọi nhanh cú pháp Invoke.
    /// </summary>
    public Task<NetOpsMessage?> Invoke(AgentSession session, NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(session, message, cancellationToken);
    }

    /// <summary>
    /// Xử lý thông điệp bất đồng bộ dạng không chặn (non-blocking dispatch).
    /// Tự động thực thi phân loại và truyền phát phản hồi (nếu có) qua session.SendMessageAsync.
    /// </summary>
    public async Task BeginInvoke(AgentSession session, NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var response = await InvokeAsync(session, message, cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                await session.SendMessageAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Thao tác hủy bỏ bình thường
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[MasterMessageControl] Error in BeginInvoke dispatch for message '{Type}' on session {SessionId}",
                message.MessageType, session.SessionId);
        }
    }

    #region Message Handlers
    /// <summary>
    /// Xử lý thông điệp Ping
    /// </summary>
    private NetOpsMessage HandlePing(AgentSession session, NetOpsMessage message)
    {
        // Tự động nhận diện và gán AgentId nếu gói tin PING chứa HeartbeatPayload và session chưa có AgentId
        if (string.IsNullOrEmpty(session.AgentId) &&
            message.TryGetPayload<HeartbeatPayload>(out var heartbeat) &&
            heartbeat != null &&
            !string.IsNullOrWhiteSpace(heartbeat.AgentId))
        {
            session.AgentId = heartbeat.AgentId;
            _connectionManager?.MarkAgentOnline(session.AgentId, session.SessionId, session.RemoteEndPoint, session.LastSeenAt);
            _logger.LogInformation(
                "[MasterMessageControl] Identified Agent '{AgentId}' from PING payload on session {SessionId}",
                session.AgentId, session.SessionId);
        }

        _logger.LogInformation(
            "[MasterMessageControl] Processed PING from session {SessionId}, generating PONG response",
            session.SessionId);

        return message.CreateResponse(MessageType.Pong, "PONG_ACK");
    }

    /// <summary>
    /// Xử lý thông điệp Heartbeat
    /// </summary>
    private NetOpsMessage HandleHeartbeat(AgentSession session, NetOpsMessage message)
    {
        if (message.TryGetPayload<HeartbeatPayload>(out var heartbeat) && heartbeat != null)
        {
            if (!string.IsNullOrEmpty(heartbeat.AgentId))
            {
                session.AgentId = heartbeat.AgentId;
            }

            if (!string.IsNullOrEmpty(session.AgentId))
            {
                _connectionManager?.MarkAgentOnline(session.AgentId, session.SessionId, session.RemoteEndPoint, session.LastSeenAt);
            }

            _logger.LogInformation(
                "[MasterMessageControl] Processed HEARTBEAT from Agent '{AgentId}' (Session: {SessionId}, Timestamp: {Timestamp})",
                session.AgentId ?? "Unknown", session.SessionId, heartbeat.Timestamp);
        }
        else
        {
            _logger.LogWarning(
                "[MasterMessageControl] Received HEARTBEAT with empty or unparseable payload on session {SessionId}",
                session.SessionId);
        }
        /// Tạo gói tin phản hồi HeartbeatAck
        var ackPayload = new HeartbeatAckPayload(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true);
        return message.CreateResponse(MessageType.HeartbeatAck, ackPayload);
    }

    /// <summary>
    /// Xử lý thông điệp Auth
    /// </summary>
    private NetOpsMessage HandleAuth(AgentSession session, NetOpsMessage message)
    {
        if (message.TryGetPayload<AuthRequestPayload>(out var auth) && auth != null)
        {
            if (!string.IsNullOrEmpty(auth.AgentId))
            {
                session.AgentId = auth.AgentId;
                _connectionManager?.MarkAgentOnline(session.AgentId, session.SessionId, session.RemoteEndPoint, session.LastSeenAt);
            }

            session.IsAuthenticated = true;

            _logger.LogInformation(
                "[MasterMessageControl] Authenticated Agent '{AgentId}' (OS: {OS}, Host: {Host}, Session: {SessionId})",
                session.AgentId ?? "Unknown", auth.OsInfo ?? "N/A", auth.Hostname ?? "N/A", session.SessionId);
            /// Tạo gói tin phản hồi AuthOk
            var authOkPayload = new AuthResponsePayload(true, session.SessionId, message: "Authentication successful");
            return message.CreateResponse(MessageType.AuthOk, authOkPayload);
        }

        _logger.LogWarning(
            "[MasterMessageControl] Authentication failed on session {SessionId} due to malformed payload",
            session.SessionId);
        /// Tạo gói tin phản hồi AuthFailed
        var authFailedPayload = new AuthResponsePayload(
            false,
            errorCode: "INVALID_AUTH_PAYLOAD",
            message: "Missing or malformed authentication request payload");

        return message.CreateResponse(MessageType.AuthFailed, authFailedPayload);
    }

    /// <summary>
    /// Xử lý thông điệp Metrics
    /// </summary>
    private NetOpsMessage? HandleMetrics(AgentSession session, NetOpsMessage message)
    {
        if (message.TryGetPayload<MetricsPayload>(out var metrics) && metrics != null)
        {
            _logger.LogInformation(
                "[MasterMessageControl] METRICS from Agent '{AgentId}' (Session: {SessionId}): CPU={Cpu:F1}%, RAM={MemUsedMB}/{MemTotalMB}MB, Disk={DiskUsedMB}/{DiskTotalMB}MB, Uptime={UptimeSec}s",
                session.AgentId ?? "Unknown",
                session.SessionId,
                metrics.CpuUsagePercent,
                metrics.MemoryUsedMb,
                metrics.MemoryTotalMb,
                metrics.DiskUsedMb,
                metrics.DiskTotalMb,
                metrics.UptimeSeconds);

            try
            {
                MetricsReceived?.Invoke(session, metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MasterMessageControl] Error invoking MetricsReceived event for session {SessionId}", session.SessionId);
            }
        }
        else
        {
            _logger.LogWarning("[MasterMessageControl] Unparseable METRICS payload from session {SessionId}", session.SessionId);
        }
        /// Không có gói tin phản hồi
        return null;
    }

    /// <summary>
    /// Xử lý thông điệp Command
    /// </summary>
    private NetOpsMessage? HandleCommand(AgentSession session, NetOpsMessage message)
    {
        if (message.TryGetPayload<CommandRequestPayload>(out var command) && command != null)
        {
            _logger.LogInformation(
                "[MasterMessageControl] Observed COMMAND '{CmdId}' on session {SessionId}: CommandLine='{CommandLine}', Timeout={Timeout}s, WorkingDir='{WorkingDir}'",
                command.CommandId, session.SessionId, command.CommandLine, command.TimeoutSeconds, command.WorkingDir ?? "Default");
        }
        /// Không có gói tin phản hồi
        return null;
    }

    /// <summary>
    /// Xử lý thông điệp CommandOutput
    /// </summary>
    private NetOpsMessage? HandleCommandOutput(AgentSession session, NetOpsMessage message)
    {
        if (message.TryGetPayload<CommandOutputPayload>(out var output) && output != null)
        {
            _logger.LogInformation(
                "[MasterMessageControl] COMMAND_OUTPUT for '{CmdId}' on session {SessionId}: IsError={IsError}, OutputLength={Length} chars",
                output.CommandId, session.SessionId, output.IsError, output.Output?.Length ?? 0);

            try
            {
                CommandOutputReceived?.Invoke(session, output);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MasterMessageControl] Error invoking CommandOutputReceived event for session {SessionId}", session.SessionId);
            }
        }

        return null;
    }

    /// <summary>
    /// Xử lý thông điệp CommandDone
    /// </summary>
    private NetOpsMessage? HandleCommandDone(AgentSession session, NetOpsMessage message)
    {
        if (message.TryGetPayload<CommandDonePayload>(out var done) && done != null)
        {
            _logger.LogInformation(
                "[MasterMessageControl] COMMAND_DONE for '{CmdId}' on session {SessionId}: ExitCode={ExitCode}, Success={Success}, ElapsedTime={ElapsedMs}ms",
                done.CommandId, session.SessionId, done.ExitCode, done.Success, done.TotalExecutionTimeMs);

            try
            {
                CommandDoneReceived?.Invoke(session, done);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MasterMessageControl] Error invoking CommandDoneReceived event for session {SessionId}", session.SessionId);
            }
        }

        return null;
    }

    /// <summary>
    /// Xử lý thông điệp Unsupported
    /// </summary>
    private NetOpsMessage HandleUnsupported(AgentSession session, NetOpsMessage message)
    {
        /// Ghi log Warning
        _logger.LogWarning(
            "[MasterMessageControl] Unhandled message type '{Type}' from session {SessionId} (Endpoint: {EndPoint})",
            message.MessageType, session.SessionId, session.RemoteEndPoint);
        /// Tạo gói tin phản hồi Error
        return message.CreateError(
            errorCode: "UNSUPPORTED_MESSAGE_TYPE",
            message: $"MessageType '{message.MessageType}' is not supported",
            details: $"Endpoint: {session.RemoteEndPoint}, SessionId: {session.SessionId}");
    }

    #endregion
}
