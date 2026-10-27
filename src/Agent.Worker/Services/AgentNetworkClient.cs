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
/// Tự động tuần tự hóa ghi (Write Serialization) qua SafeFrameStream, xử lý Exponential Backoff kèm Jitter,
/// và bọc bắt lỗi mạng an toàn khi gửi/nhận thông điệp.
/// </summary>
public class AgentNetworkClient : IAgentNetworkClient
{
    private readonly ILogger _logger;
    private readonly IAgentMessageControl _messageControl;
    private readonly object _channelLock = new();
    private ISafeFrameChannel? _activeChannel;

    // Mốc thời gian (TickCount64, mili giây) của lần cuối Agent nghe thấy Master (nhận PONG hoặc vừa kết nối xong).
    private long _lastPongTickMs = Environment.TickCount64;

    // Trạng thái kết nối hiện tại, lưu dưới dạng int để đọc/ghi nguyên tử giữa các luồng.
    private int _state = (int)AgentConnectionState.Disconnected;

    /// <summary>
    /// Cho biết Agent có đang duy trì kết nối mạng ổn định tới Master hay không.
    /// </summary>
    public bool IsConnected => _activeChannel?.IsConnected ?? false;

    /// <summary>
    /// Trạng thái kết nối hiện tại của Agent (Disconnected, Connecting, Authenticating, Connected, Reconnecting, Unauthorized).
    /// </summary>
    public AgentConnectionState CurrentState => (AgentConnectionState)Volatile.Read(ref _state);

    /// <summary>
    /// Sự kiện kích hoạt mỗi khi trạng thái kết nối thay đổi. Tham số: (trạng thái trước, trạng thái mới).
    /// Lưu ý: sự kiện được gọi trên luồng đang xử lý kết nối (không phải luồng giao diện),
    /// nên bên đăng ký cập nhật WinForms phải chuyển luồng bằng Control.BeginInvoke.
    /// </summary>
    public event Action<AgentConnectionState, AgentConnectionState>? StateChanged;

    /// <summary>
    /// Bộ tính độ trễ kết nối lại (Exponential Backoff kèm Jitter). Mặc định: cơ sở 2s, tối đa 30s, jitter 50%.
    /// Có thể thay thế trước khi gọi RunAsync (ví dụ test dùng độ trễ ngắn).
    /// </summary>
    public ReconnectBackoff Backoff { get; set; } = new();

    /// <summary>
    /// Địa chỉ endpoint của Master mà Agent đang kết nối tới (dạng Host:Port), hoặc null nếu chưa kết nối.
    /// </summary>
    public string? ConnectedEndpoint { get; private set; }

    /// <summary>
    /// Số chu kỳ PING liên tiếp không nhận được PONG thì Agent coi kết nối đã chết
    /// (ví dụ Master mất điện hoặc NAT xóa phiên nên không có gói đóng kết nối, socket vẫn "mở" - half-open)
    /// và chủ động đóng để kết nối lại theo Exponential Backoff.
    /// Mặc định 3 (PING 5s thì timeout khoảng 15s, trùng với session timeout của Master). Đặt 0 để tắt cơ chế này.
    /// </summary>
    public int MissedPongLimit { get; set; } = 3;

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
    /// Đổi trạng thái kết nối. Chỉ ghi log và phát sự kiện StateChanged khi trạng thái thật sự thay đổi.
    /// </summary>
    private void SetState(AgentConnectionState next)
    {
        var previous = (AgentConnectionState)Interlocked.Exchange(ref _state, (int)next);
        if (previous == next)
        {
            return;
        }

        _logger.LogInformation("[AgentNetworkClient] State changed: {Previous} -> {Next}", previous, next);

        try
        {
            StateChanged?.Invoke(previous, next);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentNetworkClient] Error invoking StateChanged event ({Previous} -> {Next})", previous, next);
        }
    }

    /// <summary>
    /// Bắt đầu chu trình kết nối TCP, vòng lặp nhận thông điệp và gửi PING định kỳ tới Master.
    /// Tự động thực thi cơ chế Exponential Backoff kèm Jitter khi gặp sự cố mạng cho tới khi cancellationToken được kích hoạt.
    /// </summary>
    public async Task RunAsync(string host, int port, string agentId, TimeSpan pingInterval, CancellationToken cancellationToken)
    {
        Action<NetOpsMessage, string?> onPong = (msg, payload) =>
        {
            // Ghi nhận thời điểm nhận PONG gần nhất để phát hiện kết nối chết ngầm
            Interlocked.Exchange(ref _lastPongTickMs, Environment.TickCount64);

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
                    SetState(AgentConnectionState.Connecting);

                    _logger.LogInformation("[Agent {AgentId}] Attempting to connect to Master at {Host}:{Port}...",
                        agentId, host, port);

                    using var client = new TcpClient();
                    await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

                    // Khi kết nối thành công, đặt lại độ trễ backoff về mức cơ sở
                    Backoff.Reset();
                    ConnectedEndpoint = $"{host}:{port}";

                    // Mốc "vừa nghe thấy Master": tính từ lúc kết nối thành công
                    Interlocked.Exchange(ref _lastPongTickMs, Environment.TickCount64);

                    _logger.LogInformation("[Agent {AgentId}] Successfully connected to Master at {Host}:{Port}!",
                        agentId, host, port);

                    using (client)
                    await using (var safeStream = new SafeFrameStream(client.GetStream()))
                    {
                        lock (_channelLock)
                        {
                            _activeChannel = safeStream;
                        }

                        SetState(AgentConnectionState.Connected);

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
                                // Phát hiện kết nối chết ngầm: quá nhiều chu kỳ PING mà Master không trả PONG
                                if (MissedPongLimit > 0)
                                {
                                    long silentMs = Environment.TickCount64 - Interlocked.Read(ref _lastPongTickMs);
                                    double limitMs = pingInterval.TotalMilliseconds * MissedPongLimit;
                                    if (silentMs > limitMs)
                                    {
                                        _logger.LogWarning(
                                            "[Agent {AgentId}] No PONG for {Silent}ms (limit {Limit}ms). Closing connection to reconnect.",
                                            agentId, silentMs, limitMs);
                                        linkedCts.Cancel();
                                        break;
                                    }
                                }

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
                    _logger.LogWarning("[Agent {AgentId}] Socket error connecting to Master: {Message}. Will reconnect with exponential backoff...",
                        agentId, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Agent {AgentId}] Unexpected error. Will reconnect with exponential backoff...",
                        agentId);
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

                // Chờ theo cơ chế Exponential Backoff kèm Jitter trước khi kết nối lại
                if (!cancellationToken.IsCancellationRequested)
                {
                    SetState(AgentConnectionState.Reconnecting);

                    var delay = Backoff.NextDelay();
                    _logger.LogInformation("[Agent {AgentId}] Reconnecting in {Seconds:F1}s (attempt {Attempt})...",
                        agentId, delay.TotalSeconds, Backoff.Attempt);

                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
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
            SetState(AgentConnectionState.Disconnected);
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
