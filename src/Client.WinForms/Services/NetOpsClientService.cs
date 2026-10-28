using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Shared.Contracts.Framing;

namespace Client.WinForms.Services;

/// <summary>
/// Dịch vụ kết nối mạng trung tâm của Client.WinForms theo chuẩn NetOps Protocol v0.
/// Bọc quanh TcpClient, ISafeFrameChannel và SafeFrameStream.
/// Cung cấp Request-Response correlation, background message loop, và các tiện ích nghiệp vụ (Ping, Heartbeat, Command).
/// </summary>
public sealed class NetOpsClientService : IAsyncDisposable, IDisposable
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private TcpClient? _tcpClient;
    private ISafeFrameChannel? _channel;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<NetOpsMessage>> _pendingRequests = new();
    private bool _disposed;

    // Giai đoạn T18.2: Quản lý thông tin kết nối lại (Auto-Reconnect context)
    private string? _lastHost;
    private int _lastPort;
    private bool _isUserDisconnecting;
    private CancellationTokenSource? _reconnectCts;

    /// <summary>
    /// Trạng thái kết nối hiện tại của dịch vụ (Disconnected, Connecting, Connected, Reconnecting).
    /// </summary>
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>
    /// Cho biết client đã kết nối và kênh truyền an toàn sẵn sàng hay không.
    /// </summary>
    public bool IsConnected => State == ConnectionState.Connected && (_channel?.IsConnected ?? false);

    /// <summary>
    /// Địa chỉ endpoint đang kết nối (Host:Port).
    /// </summary>
    public string? ConnectedEndpoint { get; private set; }

    /// <summary>
    /// Sự kiện thông báo khi trạng thái kết nối mạng thay đổi (chuẩn đơn giản).
    /// </summary>
    public event Action<ConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// Giai đoạn T18.2: Sự kiện thông báo chi tiết khi trạng thái kết nối thay đổi
    /// (cung cấp PreviousState, NewState, Attempt, RetryDelay, Message cho WinForms UI).
    /// </summary>
    public event Action<ConnectionStateChangedEventArgs>? DetailedConnectionStateChanged;

    /// <summary>
    /// Bật/Tắt tính năng tự động kết nối lại khi bị ngắt kết nối mạng bất ngờ (T18.2).
    /// Mặc định: false (để các unit test ngắt kết nối dứt khoát; giao diện UI có thể kích hoạt).
    /// </summary>
    public bool AutoReconnectEnabled { get; set; } = false;

    /// <summary>
    /// Số lần thử kết nối lại tối đa khi tự động reconnect (mặc định: 5).
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;

    /// <summary>
    /// Chủ động thông báo trạng thái Reconnecting (T18.2) kèm thông tin lần thử và độ trễ chờ.
    /// Dùng cho kịch bản điều phối kết nối lại từ ngoài hoặc kiểm thử.
    /// </summary>
    public void NotifyReconnecting(int attempt = 1, TimeSpan? retryDelay = null, string? message = null)
    {
        SetState(ConnectionState.Reconnecting, message ?? "Đang thử kết nối lại...", attempt, retryDelay);
    }

    /// <summary>
    /// Sự kiện thông báo khi có một khung dữ liệu được gửi đi hoặc nhận về (phục vụ Frame Inspector).
    /// </summary>
    public event Action<FrameLogEntry>? FrameLogged;

    /// <summary>
    /// Sự kiện thông báo khi nhận được một thông điệp NetOpsMessage bất kỳ từ server.
    /// </summary>
    public event Action<NetOpsMessage>? MessageReceived;

    /// <summary>
    /// Khởi tạo một đối tượng NetOpsClientService mới.
    /// </summary>
    public NetOpsClientService()
    {
    }

    /// <summary>
    /// Thiết lập kết nối TCP socket tới Master và khởi tạo kênh truyền an toàn SafeFrameStream.
    /// </summary>
    /// <param name="host">Địa chỉ IP hoặc Hostname của Master</param>
    /// <param name="port">Cổng dịch vụ TCP của Master</param>
    /// <param name="cancellationToken">Mã hủy tác vụ kết nối</param>
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsConnected)
        {
            return;
        }

        // Ghi nhận bối cảnh kết nối phục vụ tự động thử lại
        _isUserDisconnecting = false;
        _lastHost = host;
        _lastPort = port;

        SetState(ConnectionState.Connecting, $"Đang kết nối tới {host}:{port}...");
        ConnectedEndpoint = $"{host}:{port}";

        try
        {
            var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            await tcpClient.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);

            _tcpClient = tcpClient;
            var safeStream = new SafeFrameStream(tcpClient.GetStream());
            await AttachChannelInternalAsync(safeStream, ConnectedEndpoint).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await CleanupResourcesAsync().ConfigureAwait(false);
            SetState(ConnectionState.Disconnected, $"Kết nối thất bại: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gắn trực tiếp một ISafeFrameChannel có sẵn (dùng cho Mock/Testing trong bộ nhớ mà không cần mở socket TCP).
    /// </summary>
    /// <param name="channel">Kênh truyền hai chiều an toàn</param>
    /// <param name="endpointName">Tên định danh của endpoint (tùy chọn)</param>
    public async Task AttachChannelAsync(ISafeFrameChannel channel, string endpointName = "InMemoryChannel")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(channel);

        await AttachChannelInternalAsync(channel, endpointName).ConfigureAwait(false);
    }

    private Task AttachChannelInternalAsync(ISafeFrameChannel channel, string endpoint)
    {
        _channel = channel;
        ConnectedEndpoint = endpoint;
        _readCts = new CancellationTokenSource();

        SetState(ConnectionState.Connected, $"Đã kết nối thành công tới {endpoint}");

        // Khởi động luồng đọc nền bất đồng bộ
        _readTask = Task.Run(() => BackgroundReadLoopAsync(_channel, _readCts.Token));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ngắt kết nối sạch sẽ khỏi máy chủ, hủy các yêu cầu đang chờ và giải phóng socket.
    /// Hủy bỏ toàn bộ chu kỳ Reconnect tự động nếu đang diễn ra.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (State == ConnectionState.Disconnected)
        {
            return;
        }

        // Đánh dấu người dùng chủ động ngắt, không kích hoạt tự động kết nối lại
        _isUserDisconnecting = true;
        try { _reconnectCts?.Cancel(); } catch { }
        try { _readCts?.Cancel(); } catch { }

        var readTask = _readTask;
        _readTask = null;
        if (readTask != null)
        {
            try
            {
                await readTask.ConfigureAwait(false);
            }
            catch
            {
                // Bỏ qua ngoại lệ đọc khi ngắt kết nối
            }
        }

        await CleanupResourcesAsync().ConfigureAwait(false);
        SetState(ConnectionState.Disconnected, "Người dùng chủ động ngắt kết nối.");
    }

    /// <summary>
    /// Giai đoạn T19.2: Giả lập ngắt kết nối socket đột ngột từ phía WinForms Client (Abrupt Socket Drop / RST).
    /// Đóng cưỡng chế socket TCP bên dưới mà không bật cờ _isUserDisconnecting,
    /// kích hoạt cơ chế phát hiện lỗi mạng và quy trình tự động kết nối lại (Auto-Reconnect).
    /// </summary>
    public void SimulateAbruptDisconnect()
    {
        if (State == ConnectionState.Disconnected)
        {
            return;
        }

        try
        {
            _channel?.Dispose();
        }
        catch { }

        try
        {
            _tcpClient?.Client?.Close(0);
            _tcpClient?.Dispose();
        }
        catch { }
    }

    /// <summary>
    /// Gửi một thông điệp NetOpsMessage và chờ nhận thông điệp phản hồi tương ứng dựa trên InReplyTo / RequestId.
    /// </summary>
    /// <param name="request">Thông điệp yêu cầu cần gửi</param>
    /// <param name="timeout">Thời gian tối đa chờ phản hồi (mặc định 10 giây)</param>
    /// <param name="cancellationToken">Mã hủy tác vụ</param>
    /// <returns>Thông điệp phản hồi từ server</returns>
    public async Task<NetOpsMessage> SendRequestAsync(
        NetOpsMessage request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        if (!IsConnected || _channel == null)
        {
            throw new InvalidOperationException("Client hiện chưa kết nối đến server.");
        }

        // Đảm bảo RequestId luôn được định danh
        if (string.IsNullOrEmpty(request.RequestId))
        {
            request.RequestId = request.MessageId;
        }

        var tcs = new TaskCompletionSource<NetOpsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        string correlationKey = request.RequestId;
        _pendingRequests[correlationKey] = tcs;

        // Ghi log khung gửi đi
        LogFrame(request, FrameDirection.Outbound);

        // Gửi qua ISafeFrameChannel
        await _channel.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);

        // Chờ phản hồi với timeout
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(10);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(actualTimeout);

        try
        {
            return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Hết thời gian chờ phản hồi ({actualTimeout.TotalSeconds}s) cho yêu cầu '{request.MessageType}' (RequestId: {request.RequestId}).");
        }
        finally
        {
            _pendingRequests.TryRemove(correlationKey, out _);
        }
    }

    /// <summary>
    /// Gửi một thông điệp NetOpsMessage một chiều mà không cần chờ phản hồi (Fire-and-forget).
    /// </summary>
    /// <param name="message">Thông điệp cần gửi</param>
    /// <param name="cancellationToken">Mã hủy tác vụ</param>
    public async Task SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);

        if (!IsConnected || _channel == null)
        {
            throw new InvalidOperationException("Client hiện chưa kết nối đến server.");
        }

        LogFrame(message, FrameDirection.Outbound);
        await _channel.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gửi yêu cầu Ping tới server và tính toán thời gian phản hồi Round-Trip Time (RTT).
    /// </summary>
    /// <param name="payload">Nội dung text đính kèm (tùy chọn)</param>
    /// <param name="timeout">Thời gian timeout (mặc định 5 giây)</param>
    /// <param name="cancellationToken">Mã hủy tác vụ</param>
    /// <returns>Cặp giá trị: RTT độ trễ và thông điệp Pong nhận được</returns>
    public async Task<(TimeSpan Rtt, NetOpsMessage Response)> PingAsync(
        string? payload = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var pingRequest = NetOpsMessage.CreateRequest(MessageType.Ping, payload ?? $"ping-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        var sw = Stopwatch.StartNew();

        var response = await SendRequestAsync(pingRequest, timeout ?? TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        sw.Stop();

        return (sw.Elapsed, response);
    }

    /// <summary>
    /// Gửi nhịp tim Heartbeat tới server và nhận kết quả xác nhận HeartbeatAck.
    /// </summary>
    /// <param name="agentId">Mã định danh Client/Agent gửi nhịp tim</param>
    /// <param name="timeout">Thời gian timeout (mặc định 5 giây)</param>
    /// <param name="cancellationToken">Mã hủy tác vụ</param>
    /// <returns>Cặp giá trị: đối tượng HeartbeatAckPayload và thông điệp phản hồi gốc</returns>
    public async Task<(HeartbeatAckPayload? Ack, NetOpsMessage Response)> SendHeartbeatAsync(
        string agentId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var heartbeatPayload = new HeartbeatPayload(agentId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var heartbeatRequest = NetOpsMessage.CreateRequest(MessageType.Heartbeat, heartbeatPayload);

        var response = await SendRequestAsync(heartbeatRequest, timeout ?? TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        var ack = response.GetPayload<HeartbeatAckPayload>();

        return (ack, response);
    }

    /// <summary>
    /// Gửi yêu cầu thực thi lệnh Command từ xa tới server và chờ kết quả CommandResult.
    /// </summary>
    /// <param name="commandLine">Chuỗi câu lệnh cần thực thi</param>
    /// <param name="timeoutSeconds">Thời gian giới hạn lệnh chạy (giây)</param>
    /// <param name="responseTimeout">Thời gian timeout chờ phản hồi từ server</param>
    /// <param name="cancellationToken">Mã hủy tác vụ</param>
    /// <returns>Thông điệp phản hồi chứa kết quả CommandResult</returns>
    public async Task<NetOpsMessage> ExecuteCommandAsync(
        string commandLine,
        int timeoutSeconds = 30,
        TimeSpan? responseTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var commandPayload = new CommandPayload(Guid.NewGuid().ToString("N"), commandLine, timeoutSeconds);
        var commandRequest = NetOpsMessage.CreateRequest(MessageType.Command, commandPayload);

        var waitTime = responseTimeout ?? TimeSpan.FromSeconds(timeoutSeconds + 10);
        return await SendRequestAsync(commandRequest, waitTime, cancellationToken).ConfigureAwait(false);
    }

    private async Task BackgroundReadLoopAsync(ISafeFrameChannel channel, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && channel.IsConnected)
            {
                NetOpsMessage? message = await channel.ReceiveMessageAsync(ct).ConfigureAwait(false);
                if (message == null)
                {
                    // Graceful EOF: Máy chủ đóng kết nối sạch sẽ
                    break;
                }

                // Ghi log khung nhận được
                LogFrame(message, FrameDirection.Inbound);

                // Phát sự kiện nhận thông điệp
                MessageReceived?.Invoke(message);

                // Kiểm tra khớp Request-Response
                if (!string.IsNullOrEmpty(message.InReplyTo) && _pendingRequests.TryGetValue(message.InReplyTo, out var tcs))
                {
                    tcs.TrySetResult(message);
                }
                else if (!string.IsNullOrEmpty(message.RequestId) && _pendingRequests.TryGetValue(message.RequestId, out var tcsByReq))
                {
                    tcsByReq.TrySetResult(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Tác vụ đọc bị hủy có chủ đích khi ngắt kết nối
        }
        catch (Exception)
        {
            // Lỗi socket hoặc đứt mạng đột ngột
        }
        finally
        {
            if (ReferenceEquals(_channel, channel) && State == ConnectionState.Connected)
            {
                try
                {
                    await CleanupResourcesAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Bỏ qua lỗi dọn dẹp khi loop kết thúc
                }

                // Giai đoạn T18.2: Nếu bật AutoReconnect và không phải do người dùng ngắt kết nối thì tự động thử lại
                if (AutoReconnectEnabled && !_isUserDisconnecting && !string.IsNullOrEmpty(_lastHost) && _lastPort > 0)
                {
                    _ = Task.Run(StartAutoReconnectLoopAsync);
                }
                else
                {
                    SetState(ConnectionState.Disconnected, "Kênh truyền mạng đã bị đóng.");
                }
            }
        }
    }

    private void LogFrame(NetOpsMessage message, FrameDirection direction)
    {
        try
        {
            string rawJson = JsonSerializer.Serialize(message);
            string formattedJson = JsonSerializer.Serialize(message, IndentedJsonOptions);
            byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(rawJson);

            byte[] headerBytes = new byte[FrameConstants.LengthPrefixSize];
            BinaryPrimitives.WriteInt32BigEndian(headerBytes, payloadBytes.Length);

            var entry = new FrameLogEntry
            {
                Direction = direction,
                MessageType = message.MessageType,
                MessageId = message.MessageId,
                RequestId = message.RequestId,
                InReplyTo = message.InReplyTo,
                PayloadSize = payloadBytes.Length,
                HeaderBytes = headerBytes,
                RawJson = rawJson,
                FormattedJson = formattedJson,
                Message = message
            };

            FrameLogged?.Invoke(entry);
        }
        catch
        {
            // Bỏ qua lỗi ghi log để không làm gián đoạn luồng mạng chính
        }
    }

    /// <summary>
    /// Giai đoạn T18.2: Thiết lập trạng thái kết nối mới và bắn đồng thời hai sự kiện:
    /// - ConnectionStateChanged (chuẩn enum đơn giản).
    /// - DetailedConnectionStateChanged (cung cấp đầy đủ bối cảnh, lần thử, độ trễ và lý do cho WinForms UI).
    /// </summary>
    private void SetState(
        ConnectionState newState,
        string? message = null,
        int attempt = 0,
        TimeSpan? retryDelay = null)
    {
        ConnectionState previousState = State;
        if (previousState != newState || (newState == ConnectionState.Reconnecting && attempt > 0))
        {
            State = newState;
            var args = new ConnectionStateChangedEventArgs(
                previousState,
                newState,
                ConnectedEndpoint,
                attempt,
                retryDelay,
                message);

            ConnectionStateChanged?.Invoke(newState);
            DetailedConnectionStateChanged?.Invoke(args);
        }
    }

    /// <summary>
    /// Giai đoạn T18.2: Vòng lặp tự động kết nối lại chạy ngầm với thuật toán Exponential Backoff và gửi trạng thái lên WinForms UI.
    /// </summary>
    private async Task StartAutoReconnectLoopAsync()
    {
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;

        int attempt = 0;
        int delaySeconds = 2;
        const int maxDelaySeconds = 30;

        while (!token.IsCancellationRequested && (MaxReconnectAttempts <= 0 || attempt < MaxReconnectAttempts))
        {
            attempt++;
            var retryDelay = TimeSpan.FromSeconds(delaySeconds);

            SetState(
                ConnectionState.Reconnecting,
                $"Mất kết nối máy chủ. Đang tự động kết nối lại lần {attempt}...",
                attempt,
                retryDelay);

            try
            {
                await Task.Delay(retryDelay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (token.IsCancellationRequested || _isUserDisconnecting)
            {
                break;
            }

            try
            {
                SetState(ConnectionState.Connecting, $"Đang thử kết nối lại tới {_lastHost}:{_lastPort}...", attempt);

                var tcpClient = new TcpClient();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                await tcpClient.ConnectAsync(_lastHost!, _lastPort, timeoutCts.Token).ConfigureAwait(false);

                _tcpClient = tcpClient;
                var safeStream = new SafeFrameStream(tcpClient.GetStream());
                await AttachChannelInternalAsync(safeStream, $"{_lastHost}:{_lastPort}").ConfigureAwait(false);
                return; // Reconnect thành công!
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                delaySeconds = Math.Min(delaySeconds * 2, maxDelaySeconds);
            }
        }

        if (!_isUserDisconnecting && State != ConnectionState.Connected)
        {
            SetState(ConnectionState.Disconnected, "Đã vượt quá số lần thử kết nối lại tự động.");
        }
    }

    private async Task CleanupResourcesAsync()
    {
        _readCts?.Cancel();

        // Hủy các yêu cầu đang chờ phản hồi
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetException(new IOException("Kết nối đã bị ngắt trong khi đang chờ phản hồi từ server."));
        }
        _pendingRequests.Clear();

        if (_channel != null)
        {
            try
            {
                await _channel.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Bỏ qua lỗi dispose channel
            }
            _channel = null;
        }

        if (_tcpClient != null)
        {
            try
            {
                _tcpClient.Close();
                _tcpClient.Dispose();
            }
            catch
            {
                // Bỏ qua lỗi dispose tcpClient
            }
            _tcpClient = null;
        }

        _readCts?.Dispose();
        _readCts = null;
    }

    /// <summary>
    /// Giải phóng các tài nguyên đồng bộ của dịch vụ.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Giải phóng các tài nguyên bất đồng bộ của dịch vụ.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await DisconnectAsync().ConfigureAwait(false);
        }
    }
}
