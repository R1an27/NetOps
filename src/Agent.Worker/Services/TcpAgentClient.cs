using System.Net.Sockets;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;

namespace Agent.Worker.Services;

/// <summary>
/// Client quản lý kết nối TCP từ Agent tới Master và thực hiện chu trình PING/PONG
/// Tuân thủ NetOps Protocol v0 (Length-Prefixed Framing, Write Serialization, Exponential Backoff)
/// </summary>
public class TcpAgentClient(ILogger<TcpAgentClient> logger)
{
    public async Task RunAsync(string host, int port, string agentId, TimeSpan pingInterval, CancellationToken cancellationToken)
    {
        int reconnectDelaySeconds = 3;
        const int maxReconnectDelaySeconds = 30;

        // Cơ chế Backoff theo mục 5.4 protocol-v0-spec:
        // Khi mất kết nối hoặc gặp lỗi, Agent tự động thử kết nối lại với độ trễ tăng dần
        // (Exponential Backoff: 3s -> 6s -> 12s -> tối đa 30s) tránh gây bão kết nối (connection storm).
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("[Agent {AgentId}] Attempting to connect to Master at {Host}:{Port}...",
                    agentId, host, port);

                using var client = new TcpClient();
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

                // Khi kết nối thành công, đặt lại độ trễ backoff về mức cơ sở 3s
                reconnectDelaySeconds = 3;

                logger.LogInformation("[Agent {AgentId}] Successfully connected to Master at {Host}:{Port}!",
                    agentId, host, port);

                using (client)
                await using (var safeStream = new SafeFrameStream(client.GetStream()))
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                    // Task 1: Luồng nhận phản hồi từ Master
                    var receiveTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!linkedCts.Token.IsCancellationRequested)
                            {
                                NetOpsMessage? msg = await safeStream.ReadMessageAsync(linkedCts.Token).ConfigureAwait(false);
                                if (msg == null)
                                {
                                    logger.LogWarning("[Agent {AgentId}] Master closed the connection (EOF).", agentId);
                                    break;
                                }

                                if (msg.MessageType == MessageType.Pong)
                                {
                                    logger.LogInformation("[Agent {AgentId}] Received PONG (InReplyTo: {InReplyTo}, RequestId: {RequestId}, Payload: {Payload})",
                                        agentId, msg.InReplyTo, msg.RequestId, msg.Payload);
                                }
                                else if (msg.MessageType == MessageType.HeartbeatAck)
                                {
                                    var ack = msg.GetPayload<HeartbeatAckPayload>();
                                    logger.LogInformation("[Agent {AgentId}] Received HEARTBEAT_ACK (Timestamp: {Timestamp}, Acknowledged: {Ack})",
                                        agentId, ack?.Timestamp, ack?.Acknowledged);
                                }
                                else if (msg.MessageType == MessageType.Error)
                                {
                                    var err = msg.GetPayload<ErrorPayload>();
                                    logger.LogWarning("[Agent {AgentId}] Received ERROR [{Code}] {Message} (InReplyTo: {InReplyTo})",
                                        agentId, err?.ErrorCode ?? "UNKNOWN", err?.Message, msg.InReplyTo);
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "[Agent {AgentId}] Error in receive loop", agentId);
                        }
                        finally
                        {
                            linkedCts.Cancel();
                        }
                    }, linkedCts.Token);

                    // Task 2: Luồng gửi PING định kỳ với Write Serialization
                    try
                    {
                        while (!linkedCts.Token.IsCancellationRequested)
                        {
                            // Đóng gói Payload chuẩn hóa theo DTO HeartbeatPayload (Mục 4.1 protocol-v0-spec)
                            var heartbeatPayload = new HeartbeatPayload(agentId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                            var ping = NetOpsMessage.CreateRequest(MessageType.Ping, heartbeatPayload);

                            // Tuần tự hóa ghi qua SafeFrameStream với SemaphoreSlim(1, 1)
                            await safeStream.WriteMessageAsync(ping, linkedCts.Token).ConfigureAwait(false);

                            logger.LogInformation("[Agent {AgentId}] Sent PING (Id: {MessageId}, RequestId: {RequestId})",
                                agentId, ping.MessageId, ping.RequestId);

                            await Task.Delay(pingInterval, linkedCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { }

                    await receiveTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("[Agent {AgentId}] TCP Agent client stopping gracefully.", agentId);
                break;
            }
            catch (SocketException ex)
            {
                logger.LogWarning("[Agent {AgentId}] Socket error connecting to Master: {Message}. Reconnecting in {Seconds}s...",
                    agentId, ex.Message, reconnectDelaySeconds);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Agent {AgentId}] Unexpected error. Reconnecting in {Seconds}s...",
                    agentId, reconnectDelaySeconds);
            }

            // Chờ theo cơ chế Exponential Backoff trước khi kết nối lại
            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(reconnectDelaySeconds), cancellationToken).ConfigureAwait(false);
                    reconnectDelaySeconds = Math.Min(reconnectDelaySeconds * 2, maxReconnectDelaySeconds);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}