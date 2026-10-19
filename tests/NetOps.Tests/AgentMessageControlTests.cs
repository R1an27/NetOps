using System.Collections.Concurrent;
using System.Net.Sockets;
using Agent.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử chuyên sâu cho IAgentMessageControl và AgentMessageControl theo chuẩn NetOps Protocol v0.
/// Kiểm chứng:
/// 1. Kiểm tra phòng vệ tham số null (ArgumentNullException).
/// 2. Phân loại thông điệp chuẩn (Ping->Pong, Heartbeat->HeartbeatAck, HeartbeatAck, Command, Pong, Error).
/// 3. Khung sườn Command bóc tách an toàn payload và kích hoạt sự kiện CommandReceived.
/// 4. Phản hồi mã lỗi chuẩn hóa cho các MessageType không được hỗ trợ (UNSUPPORTED_MESSAGE_TYPE).
/// 5. Cơ chế BeginInvoke bất đồng bộ tự động truyền phát phản hồi qua ISafeFrameChannel.
/// 6. Miễn nhiễm lỗi khi kênh truyền socket bị đứt gãy hoặc ném SocketException trong BeginInvoke.
/// 7. Cô lập ngoại lệ khi event subscriber ném exception (không làm sập tiến trình).
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Agent")]
public class AgentMessageControlTests
{
    #region 1. Kiểm Tra Phòng Vệ Tham Số Null

    [Fact]
    public async Task InvokeAsync_NullMessage_ThrowsArgumentNullException()
    {
        var control = new AgentMessageControl();
        await Assert.ThrowsAsync<ArgumentNullException>(() => control.InvokeAsync(null!));
    }

    [Fact]
    public async Task BeginInvoke_NullChannel_ThrowsArgumentNullException()
    {
        var control = new AgentMessageControl();
        var ping = NetOpsMessage.Create(MessageType.Ping, "TEST");

        await Assert.ThrowsAsync<ArgumentNullException>(() => control.BeginInvoke(null!, ping));
    }

    [Fact]
    public async Task BeginInvoke_NullMessage_ThrowsArgumentNullException()
    {
        var control = new AgentMessageControl();
        var channel = new TestSafeFrameChannel();

        await Assert.ThrowsAsync<ArgumentNullException>(() => control.BeginInvoke(channel, null!));
    }

    #endregion

    #region 2. Xử Lý PING -> PONG Tự Động & Tiện Ích Invoke

    [Fact]
    public async Task InvokeAsync_PingMessage_ReturnsPongWithCorrectPayloadAndCorrelation()
    {
        var control = new AgentMessageControl();
        var ping = NetOpsMessage.Create(MessageType.Ping, "PROBE_PING", requestId: "req-ping-42");

        var response = await control.InvokeAsync(ping);

        Assert.NotNull(response);
        Assert.Equal(MessageType.Pong, response.MessageType);
        Assert.Equal("PONG_ACK", response.Payload);
        Assert.Equal(ping.MessageId, response.InReplyTo);
        Assert.Equal("req-ping-42", response.RequestId);
    }

    [Fact]
    public async Task Invoke_OverloadConvenienceMethod_BehavesIdenticalToInvokeAsync()
    {
        var control = new AgentMessageControl();
        var ping = NetOpsMessage.Create(MessageType.Ping, "OVERLOAD_TEST");

        var response = await control.Invoke(ping);

        Assert.NotNull(response);
        Assert.Equal(MessageType.Pong, response.MessageType);
        Assert.Equal("PONG_ACK", response.Payload);
        Assert.Equal(ping.MessageId, response.InReplyTo);
    }

    #endregion

    #region 3. Khung Sườn Xử Lý Lệnh Shell COMMAND

    [Fact]
    public async Task InvokeAsync_CommandMessage_TriggersCommandReceivedEvent_ReturnsNull()
    {
        var control = new AgentMessageControl();
        var cmdPayload = new CommandRequestPayload("cmd-uuid-101", "whoami /all", timeoutSeconds: 30, workingDir: "C:\\Temp");
        var cmdMsg = NetOpsMessage.Create(MessageType.Command, cmdPayload, requestId: "req-cmd-99");

        NetOpsMessage? receivedMsg = null;
        CommandRequestPayload? receivedPayload = null;

        control.CommandReceived += (msg, payload) =>
        {
            receivedMsg = msg;
            receivedPayload = payload;
        };

        var response = await control.InvokeAsync(cmdMsg);

        // Khung sườn trả về null vì lệnh shell chạy bất đồng bộ và sẽ stream kết quả sau
        Assert.Null(response);
        Assert.NotNull(receivedMsg);
        Assert.NotNull(receivedPayload);
        Assert.Equal("cmd-uuid-101", receivedPayload.CommandId);
        Assert.Equal("whoami /all", receivedPayload.CommandLine);
        Assert.Equal(30, receivedPayload.TimeoutSeconds);
        Assert.Equal("C:\\Temp", receivedPayload.WorkingDir);
        Assert.Equal(cmdMsg.MessageId, receivedMsg.MessageId);
    }

    [Fact]
    public async Task InvokeAsync_CommandMessage_MalformedPayload_DoesNotThrow_ReturnsNull()
    {
        var control = new AgentMessageControl();
        var malformedCmd = new NetOpsMessage
        {
            MessageType = MessageType.Command,
            Payload = "{ broken json: [??? "
        };

        var response = await control.InvokeAsync(malformedCmd);

        Assert.Null(response);
    }

    #endregion

    #region 4. Xử Lý HEARTBEAT & HEARTBEAT_ACK

    [Fact]
    public async Task InvokeAsync_HeartbeatProbeFromMaster_TriggersHeartbeatReceived_ReturnsHeartbeatAck()
    {
        var control = new AgentMessageControl();
        long testTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var heartbeatMsg = NetOpsMessage.Create(MessageType.Heartbeat, new HeartbeatPayload("master-node", testTimestamp), requestId: "req-hb-1");

        bool eventFired = false;
        control.HeartbeatReceived += (msg, payload) =>
        {
            eventFired = true;
            Assert.Equal("master-node", payload.AgentId);
        };

        var response = await control.InvokeAsync(heartbeatMsg);

        Assert.True(eventFired);
        Assert.NotNull(response);
        Assert.Equal(MessageType.HeartbeatAck, response.MessageType);
        Assert.Equal(heartbeatMsg.MessageId, response.InReplyTo);
        Assert.Equal("req-hb-1", response.RequestId);

        var ack = response.GetPayload<HeartbeatAckPayload>();
        Assert.NotNull(ack);
        Assert.True(ack.Acknowledged);
        Assert.True(ack.Timestamp > 0);
    }

    [Fact]
    public async Task InvokeAsync_HeartbeatAckMessage_TriggersHeartbeatAckReceived_ReturnsNull()
    {
        var control = new AgentMessageControl();
        long ackTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ackMsg = NetOpsMessage.Create(MessageType.HeartbeatAck, new HeartbeatAckPayload(ackTime, true));

        HeartbeatAckPayload? receivedAck = null;
        control.HeartbeatAckReceived += (_, ack) =>
        {
            receivedAck = ack;
        };

        var response = await control.InvokeAsync(ackMsg);

        Assert.Null(response);
        Assert.NotNull(receivedAck);
        Assert.True(receivedAck.Acknowledged);
        Assert.Equal(ackTime, receivedAck.Timestamp);
    }

    #endregion

    #region 5. Xử Lý PONG & ERROR Từ Master

    [Fact]
    public async Task InvokeAsync_PongMessage_TriggersPongReceivedEvent_ReturnsNull()
    {
        var control = new AgentMessageControl();
        var pong = NetOpsMessage.Create(MessageType.Pong, "PONG_ACK_DATA", requestId: "req-ping-88");

        string? receivedPayload = null;
        control.PongReceived += (_, payload) =>
        {
            receivedPayload = payload;
        };

        var response = await control.InvokeAsync(pong);

        Assert.Null(response);
        Assert.Equal("PONG_ACK_DATA", receivedPayload);
    }

    [Fact]
    public async Task InvokeAsync_ErrorMessage_LogsWarningWithoutThrowing_ReturnsNull()
    {
        var control = new AgentMessageControl();
        var errorMsg = NetOpsMessage.Create(MessageType.Error, new ErrorPayload("SESSION_EXPIRED", "Session timed out on server"));

        var response = await control.InvokeAsync(errorMsg);

        Assert.Null(response);
    }

    #endregion

    #region 6. Xử Lý MessageType Không Được Hỗ Trợ (Unsupported)

    [Fact]
    public async Task InvokeAsync_UnsupportedMessageType_ReturnsStandardErrorMessage()
    {
        var control = new AgentMessageControl();
        var unknownMsg = NetOpsMessage.Create("UNKNOWN_CUSTOM_OP", "some-data", requestId: "req-err-404");

        var response = await control.InvokeAsync(unknownMsg);

        Assert.NotNull(response);
        Assert.Equal(MessageType.Error, response.MessageType);
        Assert.Equal(unknownMsg.MessageId, response.InReplyTo);
        Assert.Equal("req-err-404", response.RequestId);

        var err = response.GetPayload<ErrorPayload>();
        Assert.NotNull(err);
        Assert.Equal("UNSUPPORTED_MESSAGE_TYPE", err.ErrorCode);
        Assert.Contains("UNKNOWN_CUSTOM_OP", err.Message);
    }

    #endregion

    #region 7. Điều Phối Bất Đồng Bộ Non-Blocking Qua BeginInvoke

    [Fact]
    public async Task BeginInvoke_PingMessage_DispatchesPongViaChannel()
    {
        var control = new AgentMessageControl();
        var channel = new TestSafeFrameChannel();
        var ping = NetOpsMessage.Create(MessageType.Ping, "TEST_PING", requestId: "req-begin-1");

        await control.BeginInvoke(channel, ping);

        Assert.True(channel.SentMessages.TryDequeue(out var sentResponse));
        Assert.NotNull(sentResponse);
        Assert.Equal(MessageType.Pong, sentResponse.MessageType);
        Assert.Equal("PONG_ACK", sentResponse.Payload);
        Assert.Equal(ping.MessageId, sentResponse.InReplyTo);
        Assert.Equal("req-begin-1", sentResponse.RequestId);
    }

    [Fact]
    public async Task BeginInvoke_CommandMessage_TriggersEvent_DoesNotSendImmediateMessage()
    {
        var control = new AgentMessageControl();
        var channel = new TestSafeFrameChannel();
        var cmd = NetOpsMessage.Create(MessageType.Command, new CommandRequestPayload("cmd-async-1", "dir"));

        bool eventFired = false;
        control.CommandReceived += (_, _) => eventFired = true;

        await control.BeginInvoke(channel, cmd);

        Assert.True(eventFired);
        Assert.True(channel.SentMessages.IsEmpty);
    }

    [Fact]
    public async Task BeginInvoke_WhenChannelThrowsSocketException_CatchesExceptionSafelyWithoutCrashing()
    {
        var control = new AgentMessageControl();
        var faultyChannel = new FaultySafeFrameChannel();
        var ping = NetOpsMessage.Create(MessageType.Ping, "PING_ON_BROKEN_SOCKET");

        // Đảm bảo BeginInvoke không ném SocketException ra ngoài
        var exception = await Record.ExceptionAsync(() => control.BeginInvoke(faultyChannel, ping));
        Assert.Null(exception);
    }

    [Fact]
    public async Task BeginInvoke_WhenCancellationTokenCancelled_HandlesCancellationGracefully()
    {
        var control = new AgentMessageControl();
        var channel = new TestSafeFrameChannel();
        var ping = NetOpsMessage.Create(MessageType.Ping, "CANCEL_TEST");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = await Record.ExceptionAsync(() => control.BeginInvoke(channel, ping, cts.Token));
        Assert.Null(exception);
    }

    #endregion

    #region 8. Cách Ly Lỗi Khi Event Subscriber Ném Ngoại Lệ

    [Fact]
    public async Task InvokeAsync_WhenEventSubscriberThrowsException_DoesNotCrashCaller()
    {
        var control = new AgentMessageControl();
        var cmd = NetOpsMessage.Create(MessageType.Command, new CommandRequestPayload("cmd-fail-1", "dir"));

        // Subscriber ném lỗi cố ý
        control.CommandReceived += (_, _) => throw new InvalidOperationException("Subscriber exploded!");

        var exception = await Record.ExceptionAsync(() => control.InvokeAsync(cmd));
        Assert.Null(exception);
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

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion
}
