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

    /// <summary>
    /// Trạng thái kết nối hiện tại của dịch vụ.
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
    /// Sự kiện thông báo khi trạng thái kết nối mạng thay đổi.
    /// </summary>
    public event Action<ConnectionState>? ConnectionStateChanged;

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

        SetState(ConnectionState.Connecting);
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
        catch
        {
            await CleanupResourcesAsync().ConfigureAwait(false);
            SetState(ConnectionState.Disconnected);
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

        SetState(ConnectionState.Connected);

        // Khởi động luồng đọc nền bất đồng bộ
        _readTask = Task.Run(() => BackgroundReadLoopAsync(_channel, _readCts.Token));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ngắt kết nối sạch sẽ khỏi máy chủ, hủy các yêu cầu đang chờ và giải phóng socket.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (State == ConnectionState.Disconnected)
        {
            return;
        }

        _readCts?.Cancel();

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
        SetState(ConnectionState.Disconnected);
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
                SetState(ConnectionState.Disconnected);
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

    private void SetState(ConnectionState newState)
    {
        if (State != newState)
        {
            State = newState;
            ConnectionStateChanged?.Invoke(newState);
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
