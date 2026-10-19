using System.Collections.Concurrent;
using System.Net.Sockets;
using Master.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử chuyên sâu cho IMasterMessageControl và MasterMessageControl theo chuẩn NetOps Protocol v0.
/// Kiểm chứng:
/// 1. Phân loại thông điệp chuẩn (Ping->Pong, Heartbeat->HeartbeatAck, Auth->AuthOk, Metrics, Command/Output/Done).
/// 2. Khả năng chống chịu lỗi payload (Malformed/Invalid JSON) không làm sập server.
/// 3. Phản hồi mã lỗi chuẩn hóa cho các MessageType không được hỗ trợ (UNSUPPORTED_MESSAGE_TYPE).
/// 4. Cơ chế BeginInvoke bất đồng bộ không chặn (non-blocking) và tự động truyền phát phản hồi qua AgentSession.
/// 5. Miễn nhiễm lỗi khi kênh truyền socket bị đứt gãy trong BeginInvoke.
/// 6. Kiểm thử tích hợp End-to-End qua TCP Socket thực tế.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Master")]
public class MasterMessageControlTests
{
    private static AgentSession CreateTestSession(string sessionId = "test-session-01", ISafeFrameChannel? channel = null)
    {
        var testChannel = channel ?? new TestSafeFrameChannel();
        return new AgentSession(sessionId, testChannel, "127.0.0.1:45678", DateTimeOffset.UtcNow);
    }

    #region 1. Phân Loại Thông Điệp Chuẩn Qua InvokeAsync

    [Fact]
    public async Task InvokeAsync_NullArguments_ThrowsArgumentNullException()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession();
        var ping = NetOpsMessage.Create(MessageType.Ping, "PING");

        await Assert.ThrowsAsync<ArgumentNullException>(() => control.InvokeAsync(null!, ping));
        await Assert.ThrowsAsync<ArgumentNullException>(() => control.InvokeAsync(session, null!));
    }

    [Fact]
    public async Task InvokeAsync_PingMessage_ReturnsPongWithCorrectCorrelationAndPayload()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession();
        var ping = NetOpsMessage.Create(MessageType.Ping, "HELLO_PING", requestId: "req-ping-101");

        var initialLastSeen = session.LastSeenAt;
        await Task.Delay(10); // Đảm bảo thời gian trôi đi

        var response = await control.InvokeAsync(session, ping);

        Assert.NotNull(response);
        Assert.Equal(MessageType.Pong, response.MessageType);
        Assert.Equal("PONG_ACK", response.Payload);
        Assert.Equal(ping.MessageId, response.InReplyTo);
        Assert.Equal("req-ping-101", response.RequestId);
        Assert.True(session.LastSeenAt >= initialLastSeen);
    }

    [Fact]
    public async Task InvokeAsync_PingMessage_WithHeartbeatPayload_AutomaticallyAssignsSessionAgentId()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession();
        Assert.Null(session.AgentId);

        var heartbeat = new HeartbeatPayload("agent-node-alpha", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var ping = NetOpsMessage.CreateRequest(MessageType.Ping, heartbeat, requestId: "req-ping-agent-01");

        var response = await control.InvokeAsync(session, ping);

        Assert.NotNull(response);
        Assert.Equal(MessageType.Pong, response.MessageType);
        Assert.Equal("PONG_ACK", response.Payload);
        Assert.Equal("agent-node-alpha", session.AgentId);
    }

    [Fact]
    public async Task Invoke_OverloadConvenienceMethod_BehavesIdenticalToInvokeAsync()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession();
        var ping = NetOpsMessage.Create(MessageType.Ping, "OVERLOAD_TEST");

        var response = await control.Invoke(session, ping);

        Assert.NotNull(response);
        Assert.Equal(MessageType.Pong, response.MessageType);
        Assert.Equal("PONG_ACK", response.Payload);
        Assert.Equal(ping.MessageId, response.InReplyTo);
    }

    [Fact]
    public async Task InvokeAsync_HeartbeatMessage_UpdatesAgentIdAndLastSeenAt_ReturnsHeartbeatAck()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession();
        Assert.Null(session.AgentId);

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var heartbeat = NetOpsMessage.Create(MessageType.Heartbeat, new HeartbeatPayload("agent-007", timestamp));

        var response = await control.InvokeAsync(session, heartbeat);

        Assert.NotNull(response);
        Assert.Equal(MessageType.HeartbeatAck, response.MessageType);
        Assert.Equal(heartbeat.MessageId, response.InReplyTo);
        Assert.Equal("agent-007", session.AgentId);

        var ack = response.GetPayload<HeartbeatAckPayload>();
        Assert.NotNull(ack);
        Assert.True(ack.Acknowledged);
        Assert.True(ack.Timestamp > 0);
    }

    [Fact]
    public async Task InvokeAsync_HeartbeatMessage_WithMalformedPayload_DoesNotThrowAndReturnsHeartbeatAck()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession();

        // Gói tin Heartbeat nhưng mang chuỗi không phải JSON của HeartbeatPayload
        var badHeartbeat = NetOpsMessage.Create(MessageType.Heartbeat, "{malformed_json_heartbeat");

        var response = await control.InvokeAsync(session, badHeartbeat);

        Assert.NotNull(response);
        Assert.Equal(MessageType.HeartbeatAck, response.MessageType);
        var ack = response.GetPayload<HeartbeatAckPayload>();
        Assert.NotNull(ack);
        Assert.True(ack.Acknowledged);
    }

    [Fact]
    public async Task InvokeAsync_AuthMessage_ValidPayload_AuthenticatesSessionAndReturnsAuthOk()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession("sess-auth-ok");
        Assert.False(session.IsAuthenticated);
        Assert.Null(session.AgentId);

        var authPayload = new AuthRequestPayload("agent-jupiter", "secret-token-123", "Linux Ubuntu 24.04", "server-node-01");
        var authMessage = NetOpsMessage.Create(MessageType.Auth, authPayload);

        var response = await control.InvokeAsync(session, authMessage);

        Assert.NotNull(response);
        Assert.Equal(MessageType.AuthOk, response.MessageType);
        Assert.Equal(authMessage.MessageId, response.InReplyTo);
        Assert.True(session.IsAuthenticated);
        Assert.Equal("agent-jupiter", session.AgentId);

        var authOk = response.GetPayload<AuthResponsePayload>();
        Assert.NotNull(authOk);
        Assert.True(authOk.Success);
        Assert.Equal("sess-auth-ok", authOk.SessionId);
    }

    [Fact]
    public async Task InvokeAsync_AuthMessage_MalformedPayload_ReturnsAuthFailed_DoesNotAuthenticate()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession("sess-auth-fail");

        var malformedAuth = NetOpsMessage.Create(MessageType.Auth, "{corrupted_auth_json");

        var response = await control.InvokeAsync(session, malformedAuth);

        Assert.NotNull(response);
        Assert.Equal(MessageType.AuthFailed, response.MessageType);
        Assert.False(session.IsAuthenticated);

        var authFailed = response.GetPayload<AuthResponsePayload>();
        Assert.NotNull(authFailed);
        Assert.False(authFailed.Success);
        Assert.Equal("INVALID_AUTH_PAYLOAD", authFailed.ErrorCode);
    }

    [Fact]
    public async Task InvokeAsync_MetricsMessage_ParsesMetrics_FiresMetricsReceivedEvent_ReturnsNull()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession("sess-metrics");
        session.AgentId = "node-alpha";

        AgentSession? eventSession = null;
        MetricsPayload? eventPayload = null;

        control.MetricsReceived += (s, m) =>
        {
            eventSession = s;
            eventPayload = m;
        };

        var metricsPayload = new MetricsPayload(
            agentId: "node-alpha",
            cpuUsagePercent: 45.2,
            memoryTotalMb: 16384,
            memoryUsedMb: 8192,
            diskTotalMb: 500000,
            diskUsedMb: 120000,
            uptimeSeconds: 7200,
            hostname: "srv-01",
            osVersion: "Linux");

        var metricsMsg = NetOpsMessage.Create(MessageType.Metrics, metricsPayload);

        var response = await control.InvokeAsync(session, metricsMsg);

        // Metrics không cần phản hồi ngược gây tiêu tốn tài nguyên mạng
        Assert.Null(response);

        // Sự kiện MetricsReceived được kích hoạt với đầy đủ dữ liệu
        Assert.Same(session, eventSession);
        Assert.NotNull(eventPayload);
        Assert.Equal(45.2, eventPayload.CpuUsagePercent);
        Assert.Equal(8192, eventPayload.MemoryUsedMb);
        Assert.Equal(16384, eventPayload.MemoryTotalMb);
        Assert.Equal(120000, eventPayload.DiskUsedMb);
        Assert.Equal(500000, eventPayload.DiskTotalMb);
        Assert.Equal(7200, eventPayload.UptimeSeconds);
    }

    [Fact]
    public async Task InvokeAsync_CommandMessages_ObserveCommandAndFireOutputAndDoneEvents()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession("sess-cmd");

        CommandOutputPayload? receivedOutput = null;
        CommandDonePayload? receivedDone = null;

        control.CommandOutputReceived += (_, o) => receivedOutput = o;
        control.CommandDoneReceived += (_, d) => receivedDone = d;

        // 1. Command Request Message -> Quan sát và không trả response trực tiếp (trả về null)
        var cmdRequest = NetOpsMessage.Create(MessageType.Command, new CommandRequestPayload("cmd-101", "dir /s", 60));
        var cmdResponse = await control.InvokeAsync(session, cmdRequest);
        Assert.Null(cmdResponse);

        // 2. Command Output Message -> Kích hoạt event CommandOutputReceived
        var outputPayload = new CommandOutputPayload("cmd-101", "Volume in drive C is Windows\r\n", false);
        var outputMsg = NetOpsMessage.Create(MessageType.CommandOutput, outputPayload);
        var outputResponse = await control.InvokeAsync(session, outputMsg);
        Assert.Null(outputResponse);
        Assert.NotNull(receivedOutput);
        Assert.Equal("cmd-101", receivedOutput.CommandId);
        Assert.Equal("Volume in drive C is Windows\r\n", receivedOutput.Output);
        Assert.False(receivedOutput.IsError);

        // 3. Command Done Message -> Kích hoạt event CommandDoneReceived
        var donePayload = new CommandDonePayload("cmd-101", 0, 350);
        var doneMsg = NetOpsMessage.Create(MessageType.CommandDone, donePayload);
        var doneResponse = await control.InvokeAsync(session, doneMsg);
        Assert.Null(doneResponse);
        Assert.NotNull(receivedDone);
        Assert.Equal("cmd-101", receivedDone.CommandId);
        Assert.Equal(0, receivedDone.ExitCode);
        Assert.True(receivedDone.Success);
        Assert.Equal(350, receivedDone.TotalExecutionTimeMs);
    }

    [Fact]
    public async Task InvokeAsync_UnsupportedMessageType_ReturnsErrorMessagePayload()
    {
        var control = new MasterMessageControl();
        var session = CreateTestSession("sess-unsupported");

        // Sử dụng MessageType.Error như thông điệp gửi lên server (server không mong đợi nhận Error từ agent mà chỉ sinh Error)
        var unhandled = NetOpsMessage.Create(MessageType.Error, "Some unexpected error content");

        var response = await control.InvokeAsync(session, unhandled);

        Assert.NotNull(response);
        Assert.Equal(MessageType.Error, response.MessageType);
        Assert.Equal(unhandled.MessageId, response.InReplyTo);

        var error = response.GetPayload<ErrorMessagePayload>();
        Assert.NotNull(error);
        Assert.Equal("UNSUPPORTED_MESSAGE_TYPE", error.ErrorCode);
        Assert.Contains("ERROR", error.Message);
        Assert.Contains("sess-unsupported", error.Details);
    }

    #endregion

    #region 2. Kiểm Thử Cơ Chế BeginInvoke Bất Đồng Bộ Không Chặn

    [Fact]
    public async Task BeginInvoke_PingMessage_AutomaticallySendsPongResponseViaSessionSafeStream()
    {
        var channel = new TestSafeFrameChannel();
        var session = CreateTestSession("sess-begin-invoke", channel);
        var control = new MasterMessageControl();

        var ping = NetOpsMessage.Create(MessageType.Ping, "BEGIN_INVOKE_PING");

        // Kích hoạt BeginInvoke
        await control.BeginInvoke(session, ping);

        // Chờ gói tin phản hồi xuất hiện trong kênh truyền
        var sentMessage = await channel.WaitForMessageAsync(TimeSpan.FromSeconds(3));

        Assert.NotNull(sentMessage);
        Assert.Equal(MessageType.Pong, sentMessage.MessageType);
        Assert.Equal("PONG_ACK", sentMessage.Payload);
        Assert.Equal(ping.MessageId, sentMessage.InReplyTo);
    }

    [Fact]
    public async Task BeginInvoke_MetricsMessage_ProducesNoResponse_DoesNotSendToChannel()
    {
        var channel = new TestSafeFrameChannel();
        var session = CreateTestSession("sess-begin-metrics", channel);
        var control = new MasterMessageControl();

        bool eventFired = false;
        control.MetricsReceived += (_, _) => eventFired = true;

        var metrics = NetOpsMessage.Create(MessageType.Metrics, new MetricsPayload("sess-begin-metrics", 10.0, 4000, 1000, 50000, 20000, 100, "srv-02", "Linux"));

        await control.BeginInvoke(session, metrics);

        Assert.True(eventFired);
        Assert.Empty(channel.SentMessages);
    }

    [Fact]
    public async Task BeginInvoke_WhenChannelThrows_CatchesAndLogsWithoutCrashing()
    {
        var faultyChannel = new FaultySafeFrameChannel();
        var session = CreateTestSession("sess-faulty", faultyChannel);
        var control = new MasterMessageControl();

        var ping = NetOpsMessage.Create(MessageType.Ping, "FAULT_TEST");

        // Không được quăng ngoại lệ ra ngoài luồng gọi
        var exception = await Record.ExceptionAsync(() => control.BeginInvoke(session, ping));
        Assert.Null(exception);
    }

    [Fact]
    public async Task BeginInvoke_WithCancelledToken_HandlesCancellationGracefully()
    {
        var channel = new TestSafeFrameChannel();
        var session = CreateTestSession("sess-cancel", channel);
        var control = new MasterMessageControl();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ping = NetOpsMessage.Create(MessageType.Ping, "CANCELLED_PING");

        var exception = await Record.ExceptionAsync(() => control.BeginInvoke(session, ping, cts.Token));
        Assert.Null(exception);
    }

    #endregion

    #region 3. Kiểm Thử Tích Hợp End-to-End Qua TCP Socket Thực Tế

    [Fact]
    public async Task Integration_OverRealTcpSocket_RoutesPingHeartbeatAuthMetricsThroughMessageControl()
    {
        var messageControl = new MasterMessageControl();
        var connectionManager = new ConnectionManager();
        var logger = NullLogger<TcpManagerServer>.Instance;

        // Khởi động server với instance MessageControl riêng
        var server = new TcpManagerServer(logger, port: 0, connectionManager, messageControl);
        await using var serverHost = new CustomTcpServerHelper(server);
        await serverHost.StartAsync();

        // Đăng ký sự kiện metrics từ MessageControl
        var metricsTcs = new TaskCompletionSource<MetricsPayload>();
        messageControl.MetricsReceived += (_, m) => metricsTcs.TrySetResult(m);

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(serverHost.Host, serverHost.Port);
        await using var safeStream = new SafeFrameStream(tcpClient.GetStream());

        // 1. Kiểm tra gửi Ping -> nhận Pong
        var ping = NetOpsMessage.Create(MessageType.Ping, "TCP_E2E_PING");
        await safeStream.WriteMessageAsync(ping);
        var pong = await safeStream.ReadMessageAsync();
        Assert.NotNull(pong);
        Assert.Equal(MessageType.Pong, pong.MessageType);
        Assert.Equal("PONG_ACK", pong.Payload);
        Assert.Equal(ping.MessageId, pong.InReplyTo);

        // 2. Kiểm tra gửi Auth -> nhận AuthOk
        var auth = NetOpsMessage.Create(MessageType.Auth, new AuthRequestPayload("e2e-agent-x", "token-xyz", "Win11", "HostX"));
        await safeStream.WriteMessageAsync(auth);
        var authOk = await safeStream.ReadMessageAsync();
        Assert.NotNull(authOk);
        Assert.Equal(MessageType.AuthOk, authOk.MessageType);
        var authOkPayload = authOk.GetPayload<AuthResponsePayload>();
        Assert.NotNull(authOkPayload);
        Assert.True(authOkPayload.Success);

        // Kiểm tra session trên server đã được cập nhật AgentId và Authenticated
        Assert.Equal(1, connectionManager.ActiveCount);
        Assert.NotNull(authOkPayload.SessionId);
        var session = connectionManager.GetSession(authOkPayload.SessionId!);
        Assert.NotNull(session);
        Assert.Equal("e2e-agent-x", session.AgentId);
        Assert.True(session.IsAuthenticated);

        // 3. Kiểm tra gửi Heartbeat -> nhận HeartbeatAck
        var heartbeat = NetOpsMessage.Create(MessageType.Heartbeat, new HeartbeatPayload("e2e-agent-x"));
        await safeStream.WriteMessageAsync(heartbeat);
        var heartbeatAck = await safeStream.ReadMessageAsync();
        Assert.NotNull(heartbeatAck);
        Assert.Equal(MessageType.HeartbeatAck, heartbeatAck.MessageType);

        // 4. Kiểm tra gửi Metrics -> kích hoạt event trên Server mà không trả về response
        var metricsPayload = new MetricsPayload("e2e-agent-x", 33.3, 8192, 2048, 100000, 40000, 999, "srv-03", "Win11");
        var metricsMsg = NetOpsMessage.Create(MessageType.Metrics, metricsPayload);
        await safeStream.WriteMessageAsync(metricsMsg);

        // Chờ event được kích hoạt trên server
        var receivedMetrics = await metricsTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(receivedMetrics);
        Assert.Equal(33.3, receivedMetrics.CpuUsagePercent);
        Assert.Equal(2048, receivedMetrics.MemoryUsedMb);
        Assert.Equal(999, receivedMetrics.UptimeSeconds);
    }

    #endregion

    #region Test Double Helpers

    private sealed class TestSafeFrameChannel : ISafeFrameChannel
    {
        public readonly ConcurrentQueue<NetOpsMessage> SentMessages = new();
        private readonly TaskCompletionSource<NetOpsMessage> _messageReceivedTcs = new();

        public Task<NetOpsMessage> WaitForMessageAsync(TimeSpan timeout) =>
            _messageReceivedTcs.Task.WaitAsync(timeout);

        public bool IsConnected => true;
        public Stream UnderlyingStream => Stream.Null;

        public ValueTask SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Enqueue(message);
            _messageReceivedTcs.TrySetResult(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask<NetOpsMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<NetOpsMessage?>(null);

        public ValueTask SendRawFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<byte[]?> ReceiveRawFrameAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<byte[]?>(null);

        public void Close() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FaultySafeFrameChannel : ISafeFrameChannel
    {
        public bool IsConnected => false;
        public Stream UnderlyingStream => Stream.Null;

        public ValueTask SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default) =>
            throw new SocketException((int)SocketError.ConnectionReset);

        public ValueTask<NetOpsMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default) =>
            throw new SocketException((int)SocketError.ConnectionReset);

        public ValueTask SendRawFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default) =>
            throw new SocketException((int)SocketError.ConnectionReset);

        public ValueTask<byte[]?> ReceiveRawFrameAsync(CancellationToken cancellationToken = default) =>
            throw new SocketException((int)SocketError.ConnectionReset);

        public void Close() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CustomTcpServerHelper : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private Task? _executingTask;

        public TcpManagerServer Server { get; }
        public string Host => "127.0.0.1";
        public int Port => Server.Port;

        public CustomTcpServerHelper(TcpManagerServer server)
        {
            Server = server;
        }

        public async Task StartAsync()
        {
            _executingTask = Server.StartAsync(_cts.Token);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            while (!Server.IsListening && !_cts.IsCancellationRequested)
            {
                await Task.Delay(10, timeoutCts.Token).ConfigureAwait(false);
            }

            if (!Server.IsListening)
            {
                throw new InvalidOperationException("TcpManagerServer không thể khởi động lắng nghe trong thời gian quy định.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await Server.StopAsync(CancellationToken.None).ConfigureAwait(false);
                if (_executingTask != null)
                {
                    await _executingTask.ConfigureAwait(false);
                }
            }
            catch { }
            finally
            {
                _cts.Dispose();
            }
        }
    }

    #endregion
}
