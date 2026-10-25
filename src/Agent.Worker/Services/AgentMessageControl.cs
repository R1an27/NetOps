using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;

namespace Agent.Worker.Services;

/// <summary>
/// Hiện thực bộ điều phối và phân loại thông điệp chuẩn NetOps Protocol v0 phía Agent.
/// Xử lý tự động:
/// - Ping -> Tự động phản hồi Pong (PONG_ACK), bảo toàn RequestId và liên kết InReplyTo.
/// - Heartbeat -> Phản hồi HeartbeatAck nếu Master chủ động gửi yêu cầu kiểm tra sống còn.
/// - HeartbeatAck -> Bóc tách HeartbeatAckPayload và kích hoạt sự kiện HeartbeatAckReceived.
/// - Command -> Bóc tách CommandRequestPayload, kích hoạt sự kiện CommandReceived (chuẩn bị khung sườn cho executor).
/// - Pong -> Kích hoạt sự kiện PongReceived khi nhận phản hồi kiểm tra độ trễ mạng từ Master.
/// - Error -> Ghi log cảnh báo khi nhận mã lỗi từ Master.
/// - Unsupported -> Phản hồi ErrorMessagePayload chuẩn hóa với mã lỗi UNSUPPORTED_MESSAGE_TYPE.
/// </summary>
public class AgentMessageControl : IAgentMessageControl
{
    private readonly ILogger<AgentMessageControl> _logger;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được yêu cầu thực thi lệnh shell từ xa (COMMAND) từ Master.
    /// </summary>
    public event Action<NetOpsMessage, CommandRequestPayload>? CommandReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được gói tin xác nhận nhịp tim (HEARTBEAT_ACK) từ Master.
    /// </summary>
    public event Action<NetOpsMessage, HeartbeatAckPayload>? HeartbeatAckReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được yêu cầu nhịp tim (HEARTBEAT) từ Master.
    /// </summary>
    public event Action<NetOpsMessage, HeartbeatPayload>? HeartbeatReceived;

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được phản hồi PONG từ Master cho một gói tin PING do Agent phát đi.
    /// </summary>
    public event Action<NetOpsMessage, string?>? PongReceived;

    /// <summary>
    /// Khởi tạo AgentMessageControl với ILogger tùy chọn.
    /// </summary>
    /// <param name="logger">Bộ ghi nhật ký ILogger (nếu null sẽ sử dụng NullLogger)</param>
    public AgentMessageControl(ILogger<AgentMessageControl>? logger = null)
    {
        _logger = logger ?? NullLogger<AgentMessageControl>.Instance;
    }

    /// <summary>
    /// Xử lý phân loại thông điệp đến một cách bất đồng bộ và trả về thông điệp phản hồi (nếu có).
    /// </summary>
    /// <param name="message">Thông điệp NetOpsMessage nhận được từ Master</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Thông điệp phản hồi NetOpsMessage hoặc null nếu không cần phản hồi</returns>
    public Task<NetOpsMessage?> InvokeAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogInformation(
            "[AgentMessageControl] Handling message '{Type}' (Id: {MessageId}, InReplyTo: {InReplyTo})",
            message.MessageType, message.MessageId, message.InReplyTo);

        NetOpsMessage? response = message.MessageType switch
        {
            MessageType.Ping => HandlePing(message),
            MessageType.Pong => HandlePong(message),
            MessageType.Heartbeat => HandleHeartbeat(message),
            MessageType.HeartbeatAck => HandleHeartbeatAck(message),
            MessageType.Command => HandleCommand(message),
            MessageType.Error => HandleError(message),
            _ => HandleUnsupported(message)
        };

        return Task.FromResult(response);
    }

    /// <summary>
    /// Overload tiện ích của InvokeAsync cho phép gọi nhanh cú pháp Invoke.
    /// </summary>
    /// <param name="message">Thông điệp NetOpsMessage nhận được từ Master</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Thông điệp phản hồi NetOpsMessage hoặc null nếu không cần phản hồi</returns>
    public Task<NetOpsMessage?> Invoke(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(message, cancellationToken);
    }

    /// <summary>
    /// Xử lý thông điệp bất đồng bộ dạng không chặn (non-blocking dispatch).
    /// Tự động thực thi phân loại và truyền phát phản hồi (nếu có) qua kênh channel.WriteMessageAsync.
    /// </summary>
    /// <param name="channel">Kênh truyền an toàn kết nối tới Master</param>
    /// <param name="message">Thông điệp NetOpsMessage nhận được từ Master</param>
    /// <param name="cancellationToken">Mã hủy thao tác bất đồng bộ</param>
    /// <returns>Tác vụ Task đại diện cho quá trình dispatch ngầm</returns>
    public async Task BeginInvoke(ISafeFrameChannel channel, NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var response = await InvokeAsync(message, cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                await channel.SendMessageAsync(response, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "[AgentMessageControl] Sent response '{Type}' (InReplyTo: {InReplyTo}, RequestId: {RequestId})",
                    response.MessageType, response.InReplyTo, response.RequestId);
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
                "[AgentMessageControl] Error in BeginInvoke dispatch for message '{Type}' (Id: {MessageId})",
                message.MessageType, message.MessageId);
        }
    }

    #region Message Handlers

    private NetOpsMessage HandlePing(NetOpsMessage message)
    {
        _logger.LogInformation(
            "[AgentMessageControl] Processed PING (Id: {MessageId}), generating PONG response",
            message.MessageId);

        return message.CreateResponse(MessageType.Pong, "PONG_ACK");
    }

    private NetOpsMessage? HandlePong(NetOpsMessage message)
    {
        _logger.LogInformation(
            "[AgentMessageControl] Received PONG (InReplyTo: {InReplyTo}, RequestId: {RequestId}, Payload: {Payload})",
            message.InReplyTo, message.RequestId, message.Payload);

        try
        {
            PongReceived?.Invoke(message, message.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentMessageControl] Error in PongReceived event handler");
        }

        return null;
    }

    private NetOpsMessage HandleHeartbeat(NetOpsMessage message)
    {
        if (message.TryGetPayload<HeartbeatPayload>(out var heartbeat) && heartbeat != null)
        {
            _logger.LogInformation(
                "[AgentMessageControl] Processed HEARTBEAT probe from Master (Timestamp: {Timestamp})",
                heartbeat.Timestamp);

            try
            {
                HeartbeatReceived?.Invoke(message, heartbeat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentMessageControl] Error in HeartbeatReceived event handler");
            }
        }
        else
        {
            _logger.LogWarning(
                "[AgentMessageControl] Received HEARTBEAT with empty or malformed payload (Id: {MessageId})",
                message.MessageId);
        }

        var ackPayload = new HeartbeatAckPayload(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true);
        return message.CreateResponse(MessageType.HeartbeatAck, ackPayload);
    }

    private NetOpsMessage? HandleHeartbeatAck(NetOpsMessage message)
    {
        if (message.TryGetPayload<HeartbeatAckPayload>(out var ack) && ack != null)
        {
            _logger.LogInformation(
                "[AgentMessageControl] Received HEARTBEAT_ACK from Master (Timestamp: {Timestamp}, Acknowledged: {Ack})",
                ack.Timestamp, ack.Acknowledged);

            try
            {
                HeartbeatAckReceived?.Invoke(message, ack);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentMessageControl] Error in HeartbeatAckReceived event handler");
            }
        }
        else
        {
            _logger.LogWarning(
                "[AgentMessageControl] Received HEARTBEAT_ACK with empty or unparseable payload (Id: {MessageId})",
                message.MessageId);
        }

        return null;
    }

    private NetOpsMessage? HandleCommand(NetOpsMessage message)
    {
        if (message.TryGetPayload<CommandRequestPayload>(out var command) && command != null)
        {
            _logger.LogInformation(
                "[AgentMessageControl] Received COMMAND '{CmdId}': CommandLine='{CommandLine}', Timeout={Timeout}s, WorkingDir='{WorkingDir}'",
                command.CommandId, command.CommandLine, command.TimeoutSeconds, command.WorkingDir ?? "Default");

            try
            {
                CommandReceived?.Invoke(message, command);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[AgentMessageControl] Error in CommandReceived event handler for command '{CmdId}'",
                    command.CommandId);
            }
        }
        else
        {
            _logger.LogWarning(
                "[AgentMessageControl] Received COMMAND with invalid or missing payload (Id: {MessageId})",
                message.MessageId);
        }

        // Trả về null vì quá trình thực thi lệnh được xử lý bất đồng bộ bởi command runner
        // và sẽ stream kết quả (CommandOutput/CommandDone) qua kênh truyền sau.
        return null;
    }

    private NetOpsMessage? HandleError(NetOpsMessage message)
    {
        if (message.TryGetPayload<ErrorPayload>(out var error) && error != null)
        {
            _logger.LogWarning(
                "[AgentMessageControl] Received ERROR from Master: [{Code}] {Message} (Details: {Details}, InReplyTo: {InReplyTo})",
                error.ErrorCode, error.Message, error.Details ?? "N/A", message.InReplyTo);
        }
        else
        {
            _logger.LogWarning(
                "[AgentMessageControl] Received ERROR with unparseable payload: {Payload}",
                message.Payload);
        }

        return null;
    }

    private NetOpsMessage HandleUnsupported(NetOpsMessage message)
    {
        _logger.LogWarning(
            "[AgentMessageControl] Unhandled message type '{Type}' (Id: {MessageId})",
            message.MessageType, message.MessageId);

        return message.CreateError(
            errorCode: "UNSUPPORTED_MESSAGE_TYPE",
            message: $"MessageType '{message.MessageType}' is not supported by Agent",
            details: $"Agent received unhandled message type '{message.MessageType}'");
    }

    #endregion
}
