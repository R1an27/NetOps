using System.Net.Sockets;
using System.Text.Json;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Protocol;

namespace Agent.Worker.Services;

/// <summary>
/// Client quản lý kết nối TCP từ Agent tới Master và thực hiện chu trình PING/PONG
/// </summary>
public class TcpAgentClient(ILogger<TcpAgentClient> logger)
{
    public async Task RunAsync(string host, int port, string agentId, TimeSpan pingInterval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("[Agent {AgentId}] Attempting to connect to Master at {Host}:{Port}...",
                    agentId, host, port);

                using var client = new TcpClient();
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

                logger.LogInformation("[Agent {AgentId}] Successfully connected to Master at {Host}:{Port}!",
                    agentId, host, port);

                await using (NetworkStream stream = client.GetStream())
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                    // Task 1: Luồng nhận phản hồi từ Master
                    var receiveTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!linkedCts.Token.IsCancellationRequested)
                            {
                                NetOpsMessage? msg = await FrameCodec.ReadFrameAsync(stream, linkedCts.Token).ConfigureAwait(false);
                                if (msg == null)
                                {
                                    logger.LogWarning("[Agent {AgentId}] Master closed the connection (EOF).", agentId);
                                    break;
                                }

                                if (msg.MessageType == MessageType.Pong)
                                {
                                    logger.LogInformation("[Agent {AgentId}] Received PONG (InReplyTo: {InReplyTo}, Payload: {Payload})",
                                        agentId, msg.InReplyTo, msg.Payload);
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

                    // Task 2: Luồng gửi PING định kỳ
                    try
                    {
                        while (!linkedCts.Token.IsCancellationRequested)
                        {
                            string payload = JsonSerializer.Serialize(new
                            {
                                agentId,
                                os = Environment.OSVersion.ToString(),
                                machineName = Environment.MachineName,
                                sentAt = DateTimeOffset.UtcNow
                            });

                            var ping = NetOpsMessage.CreatePing(payload);
                            await FrameCodec.WriteFrameAsync(stream, ping, linkedCts.Token).ConfigureAwait(false);

                            logger.LogInformation("[Agent {AgentId}] Sent PING (Id: {MessageId})",
                                agentId, ping.MessageId);

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
                logger.LogWarning("[Agent {AgentId}] Socket error connecting to Master: {Message}. Reconnecting in 3 seconds...",
                    agentId, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Agent {AgentId}] Unexpected error. Reconnecting in 3 seconds...", agentId);
            }

            // Tự động chờ 3s trước khi thử kết nối lại
            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}