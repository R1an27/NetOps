using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;

namespace Agent.Worker.Services;

/// <summary>
/// Client quản trị kết nối TCP từ Agent tới Master, thực thi chu trình PING/PONG định kỳ
/// và điều phối thông điệp hai chiều theo chuẩn NetOps Protocol v0.
/// Tự động tuần tự hóa ghi (Write Serialization) qua SafeFrameStream, xử lý Exponential Backoff,
/// và bọc bắt lỗi mạng an toàn khi gửi/nhận thông điệp.
/// </summary>
public class AgentNetworkClient : IAgentNetworkClient
{
    private readonly ILogger _logger;
    private readonly IAgentMessageControl _messageControl;
    private readonly object _channelLock = new();
    private ISafeFrameChannel? _activeChannel;

    /// <summary>
    /// Cho biết Agent có đang duy trì kết nối mạng ổn định tới Master hay không.
    /// </summary>
    public bool IsConnected => _activeChannel?.IsConnected ?? false;

    /// <summary>
    /// Địa chỉ endpoint của Master mà Agent đang kết nối tới (dạng Host:Port), hoặc null nếu chưa kết nối.
    /// </summary>
    public string? ConnectedEndpoint { get; private set; }

    /// <summary>
    /// Bộ điều phối và phân loại thông điệp chuẩn NetOps Protocol v0 gắn liền với client mạng này.
    /// </summary>
    public IAgentMessageControl MessageControl => _messageControl;

    /// <summary>
    /// Sự kiện kích hoạt khi Agent thiết lập kết nối TCP socket thành công tới Master.
    /// </summary>
    public event Action? Connected;

    /// <summary>
    /// Sự kiện kích hoạt khi kết nối giữa Agent và Master bị đứt gãy hoặc đóng socket.
    /// </summary>
    public event Action? Disconnected;

    /// <summary>
    /// Khởi tạo AgentNetworkClient với ILogger{AgentNetworkClient} và IAgentMessageControl tùy chọn cho DI.
    /// </summary>
    /// <param name="logger">Bộ ghi nhật ký ILogger{AgentNetworkClient}</param>
    /// <param name="messageControl">Bộ điều phối thông điệp (nếu null tự khởi tạo AgentMessageControl)</param>
    public AgentNetworkClient(ILogger<AgentNetworkClient> logger, IAgentMessageControl? messageControl = null)
        : this((ILogger?)logger, messageControl)
    {
    }

    /// <summary>
    /// Khởi tạo AgentNetworkClient với ILogger và IAgentMessageControl tùy chọn.
    /// </summary>
    /// <param name="logger">Bộ ghi nhật ký ILogger (nếu null sử dụng NullLogger)</param>
    /// <param name="messageControl">Bộ điều phối thông điệp (nếu null tự khởi tạo AgentMessageControl)</param>
    public AgentNetworkClient(ILogger? logger = null, IAgentMessageControl? messageControl = null)
    {
        _logger = logger ?? NullLogger<AgentNetworkClient>.Instance;
        _messageControl = messageControl ?? new AgentMessageControl();
    }

    /// <summary>
    /// Bắt đầu chu trình kết nối TCP, vòng lặp nhận thông điệp và gửi PING định kỳ tới Master.
    /// Tự động thực thi cơ chế Exponential Backoff khi gặp sự cố mạng cho tới khi cancellationToken được kích hoạt.
    /// </summary>
    public async Task RunAsync(string host, int port, string agentId, TimeSpan pingInterval, CancellationToken cancellationToken)
    {
        int reconnectDelaySeconds = 3;
        const int maxReconnectDelaySeconds = 30;

        Action<NetOpsMessage, string?> onPong = (msg, payload) =>
        {
            _logger.LogInformation("[Agent {AgentId}] Received PONG (InReplyTo: {InReplyTo}, RequestId: {RequestId}, Payload: {Payload})",
                agentId, msg.InReplyTo, msg.RequestId, payload);
        };

        _messageControl.PongReceived += onPong;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("[Agent {AgentId}] Attempting to connect to Master at {Host}:{Port}...",
                        agentId, host, port);

                    using var client = new TcpClient();
                    await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

                    // Khi kết nối thành công, đặt lại độ trễ backoff về mức cơ sở 3s
                    reconnectDelaySeconds = 3;
                    ConnectedEndpoint = $"{host}:{port}";

                    _logger.LogInformation("[Agent {AgentId}] Successfully connected to Master at {Host}:{Port}!",
                        agentId, host, port);

                    using (client)
                    await using (var safeStream = new SafeFrameStream(client.GetStream()))
                    {
                        lock (_channelLock)
                        {
                            _activeChannel = safeStream;
                        }

                        try
                        {
                            Connected?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[Agent {AgentId}] Error invoking Connected event", agentId);
                        }

                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                        // Task 1: Luồng nhận và phân phối thông điệp từ Master qua IAgentMessageControl
                        var receiveTask = Task.Run(async () =>
                        {
                            try
                            {
                                while (!linkedCts.Token.IsCancellationRequested)
                                {
                                    NetOpsMessage? msg = await safeStream.ReadMessageAsync(linkedCts.Token).ConfigureAwait(false);
                                    if (msg == null)
                                    {
                                        _logger.LogWarning("[Agent {AgentId}] Master closed the connection (EOF).", agentId);
                                        break;
                                    }

                                    // Phân loại và điều phối xử lý nghiệp vụ thông qua IAgentMessageControl
                                    await _messageControl.BeginInvoke(safeStream, msg, linkedCts.Token).ConfigureAwait(false);
                                }
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[Agent {AgentId}] Error in receive loop", agentId);
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

                                _logger.LogInformation("[Agent {AgentId}] Sent PING (Id: {MessageId}, RequestId: {RequestId})",
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
                    _logger.LogInformation("[Agent {AgentId}] TCP Agent client stopping gracefully.", agentId);
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogWarning("[Agent {AgentId}] Socket error connecting to Master: {Message}. Reconnecting in {Seconds}s...",
                        agentId, ex.Message, reconnectDelaySeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Agent {AgentId}] Unexpected error. Reconnecting in {Seconds}s...",
                        agentId, reconnectDelaySeconds);
                }
                finally
                {
                    lock (_channelLock)
                    {
                        _activeChannel = null;
                    }
                    ConnectedEndpoint = null;

                    try
                    {
                        Disconnected?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Agent {AgentId}] Error invoking Disconnected event", agentId);
                    }
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
        finally
        {
            _messageControl.PongReceived -= onPong;
        }
    }

    /// <summary>
    /// Chủ động gửi một NetOpsMessage ra kênh truyền mạng an toàn tới Master.
    /// Tự động tuần tự hóa ghi (Write Serialization) qua SafeFrameStream
    /// để chống hiện tượng trộn lẫn byte khi gửi đồng thời với luồng PING định kỳ.
    /// </summary>
    public async Task SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        ISafeFrameChannel? channel;
        lock (_channelLock)
        {
            channel = _activeChannel;
        }

        if (channel == null || !channel.IsConnected)
        {
            throw new InvalidOperationException("AgentNetworkClient is not currently connected to Master.");
        }

        await channel.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "[AgentNetworkClient] Actively sent message '{Type}' (Id: {MessageId}) to {Endpoint}",
            message.MessageType, message.MessageId, ConnectedEndpoint ?? "Unknown");
    }
}
