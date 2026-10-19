using System.Buffers.Binary;
using System.Text.Json;
using System.Threading.Channels;
using Client.WinForms.Services;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử tự động toàn diện cho tầng dịch vụ mạng NetOpsClientService.
/// Kiểm tra vòng đời kết nối, truyền nhận request-response, đo RTT Ping/Pong,
/// xử lý Heartbeat, Command, kiểm soát timeout và ghi log khung dữ liệu nhị phân.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Client")]
public class NetOpsClientServiceTests
{
    [Fact]
    public void InitialState_ShouldBeDisconnected()
    {
        using var service = new NetOpsClientService();

        Assert.Equal(ConnectionState.Disconnected, service.State);
        Assert.False(service.IsConnected);
        Assert.True(string.IsNullOrEmpty(service.ConnectedEndpoint));
    }

    [Fact]
    public async Task AttachChannelAsync_ShouldSetConnectedState_AndFireEvent()
    {
        using var service = new NetOpsClientService();
        using var channel = new MockSafeFrameChannel();

        var stateChanges = new List<ConnectionState>();
        service.ConnectionStateChanged += s => stateChanges.Add(s);

        await service.AttachChannelAsync(channel, "127.0.0.1:5000");

        Assert.Equal(ConnectionState.Connected, service.State);
        Assert.True(service.IsConnected);
        Assert.Equal("127.0.0.1:5000", service.ConnectedEndpoint);
        Assert.Contains(ConnectionState.Connected, stateChanges);
    }

    [Fact]
    public async Task DisconnectAsync_ShouldTransitionToDisconnected_AndDisposeChannel()
    {
        using var service = new NetOpsClientService();
        var channel = new MockSafeFrameChannel();

        await service.AttachChannelAsync(channel, "127.0.0.1:5000");
        Assert.True(service.IsConnected);

        var stateChanges = new List<ConnectionState>();
        service.ConnectionStateChanged += s => stateChanges.Add(s);

        await service.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, service.State);
        Assert.False(service.IsConnected);
        Assert.Contains(ConnectionState.Disconnected, stateChanges);
        Assert.True(channel.IsDisposed);
    }

    [Fact]
    public async Task SendMessageAsync_WhenDisconnected_ShouldThrowInvalidOperationException()
    {
        using var service = new NetOpsClientService();
        var message = NetOpsMessage.CreateRequest(MessageType.Ping, "ping");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendMessageAsync(message));
    }

    [Fact]
    public async Task PingAsync_ShouldMeasureRtt_AndReturnPongMessage()
    {
        using var service = new NetOpsClientService();
        using var channel = new MockSafeFrameChannel();

        channel.OnMessageSent = async (sentMsg, ch) =>
        {
            if (sentMsg.MessageType == MessageType.Ping)
            {
                var pong = new NetOpsMessage
                {
                    MessageType = MessageType.Pong,
                    InReplyTo = sentMsg.MessageId,
                    RequestId = sentMsg.RequestId,
                    Payload = "pong-response-test"
                };
                await ch.EnqueueIncomingMessageAsync(pong);
            }
        };

        await service.AttachChannelAsync(channel, "MockHost:5000");

        var (rtt, response) = await service.PingAsync("ping-probe", TimeSpan.FromSeconds(2));

        Assert.True(rtt >= TimeSpan.Zero);
        Assert.Equal(MessageType.Pong, response.MessageType);
        Assert.Equal("pong-response-test", response.Payload);
    }

    [Fact]
    public async Task SendHeartbeatAsync_ShouldSendHeartbeat_AndParseHeartbeatAck()
    {
        using var service = new NetOpsClientService();
        using var channel = new MockSafeFrameChannel();

        long serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        channel.OnMessageSent = async (sentMsg, ch) =>
        {
            if (sentMsg.MessageType == MessageType.Heartbeat)
            {
                var ackPayload = new HeartbeatAckPayload(serverTime, true);
                var ackMsg = new NetOpsMessage
                {
                    MessageType = MessageType.HeartbeatAck,
                    InReplyTo = sentMsg.MessageId,
                    RequestId = sentMsg.RequestId,
                    Payload = JsonSerializer.Serialize(ackPayload)
                };
                await ch.EnqueueIncomingMessageAsync(ackMsg);
            }
        };

        await service.AttachChannelAsync(channel, "MockHost:5000");

        var (ack, response) = await service.SendHeartbeatAsync("agent-007", TimeSpan.FromSeconds(2));

        Assert.NotNull(ack);
        Assert.True(ack.Acknowledged);
        Assert.Equal(serverTime, ack.Timestamp);
        Assert.Equal(MessageType.HeartbeatAck, response.MessageType);
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShouldSendCorrectCommandPayload()
    {
        using var service = new NetOpsClientService();
        using var channel = new MockSafeFrameChannel();

        NetOpsMessage? receivedCmdMsg = null;

        channel.OnMessageSent = async (sentMsg, ch) =>
        {
            if (sentMsg.MessageType == MessageType.Command)
            {
                receivedCmdMsg = sentMsg;
                var donePayload = new CommandDonePayload("cmd-test", 0, 45);
                var doneMsg = new NetOpsMessage
                {
                    MessageType = MessageType.CommandDone,
                    InReplyTo = sentMsg.MessageId,
                    RequestId = sentMsg.RequestId,
                    Payload = JsonSerializer.Serialize(donePayload)
                };
                await ch.EnqueueIncomingMessageAsync(doneMsg);
            }
        };

        await service.AttachChannelAsync(channel, "MockHost:5000");

        var response = await service.ExecuteCommandAsync("dir /b", timeoutSeconds: 20, responseTimeout: TimeSpan.FromSeconds(2));

        Assert.NotNull(response);
        Assert.Equal(MessageType.CommandDone, response.MessageType);
        Assert.NotNull(receivedCmdMsg);

        var cmdPayload = receivedCmdMsg.GetPayload<CommandPayload>();
        Assert.NotNull(cmdPayload);
        Assert.Equal("dir /b", cmdPayload.CommandLine);
        Assert.Equal(20, cmdPayload.TimeoutSeconds);
    }

    [Fact]
    public async Task SendRequestAsync_WhenTimeoutOccurs_ShouldThrowTimeoutException()
    {
        using var service = new NetOpsClientService();
        using var channel = new MockSafeFrameChannel();

        // Kênh không trả lời bất cứ phản hồi nào
        await service.AttachChannelAsync(channel, "MockHost:5000");

        var req = NetOpsMessage.CreateRequest(MessageType.Ping, "no-response");

        await Assert.ThrowsAsync<TimeoutException>(() =>
            service.SendRequestAsync(req, timeout: TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task FrameLogged_ShouldFireOnOutboundAndInboundFrames_WithAccurateHeader()
    {
        using var service = new NetOpsClientService();
        using var channel = new MockSafeFrameChannel();

        var loggedEntries = new List<FrameLogEntry>();
        service.FrameLogged += entry => loggedEntries.Add(entry);

        channel.OnMessageSent = async (sentMsg, ch) =>
        {
            var pong = new NetOpsMessage
            {
                MessageType = MessageType.Pong,
                InReplyTo = sentMsg.MessageId,
                Payload = "pong-data"
            };
            await ch.EnqueueIncomingMessageAsync(pong);
        };

        await service.AttachChannelAsync(channel, "MockHost:5000");
        await service.PingAsync("ping-data", TimeSpan.FromSeconds(2));

        Assert.True(loggedEntries.Count >= 2);

        var outbound = loggedEntries.Find(e => e.Direction == FrameDirection.Outbound);
        Assert.NotNull(outbound);
        Assert.Equal(MessageType.Ping, outbound.MessageType);
        Assert.Equal(4, outbound.HeaderBytes.Length);
        int expectedOutboundLength = BinaryPrimitives.ReadInt32BigEndian(outbound.HeaderBytes);
        Assert.Equal(outbound.PayloadSize, expectedOutboundLength);
        Assert.False(string.IsNullOrEmpty(outbound.FormattedJson));

        var inbound = loggedEntries.Find(e => e.Direction == FrameDirection.Inbound);
        Assert.NotNull(inbound);
        Assert.Equal(MessageType.Pong, inbound.MessageType);
        Assert.Equal(4, inbound.HeaderBytes.Length);
        int expectedInboundLength = BinaryPrimitives.ReadInt32BigEndian(inbound.HeaderBytes);
        Assert.Equal(inbound.PayloadSize, expectedInboundLength);
    }

    [Fact]
    public async Task ServerGracefulEOF_ShouldTriggerDisconnection()
    {
        using var service = new NetOpsClientService();
        using var channel = new MockSafeFrameChannel();

        var tcsDisconnected = new TaskCompletionSource<bool>();
        service.ConnectionStateChanged += state =>
        {
            if (state == ConnectionState.Disconnected)
            {
                tcsDisconnected.TrySetResult(true);
            }
        };

        await service.AttachChannelAsync(channel, "MockHost:5000");
        Assert.True(service.IsConnected);

        // Kích hoạt Graceful EOF bằng cách gửi null
        await channel.EnqueueIncomingMessageAsync(null);

        // Chờ luồng background phát hiện ngắt kết nối
        var disconnected = await Task.WhenAny(tcsDisconnected.Task, Task.Delay(1000));
        Assert.Same(tcsDisconnected.Task, disconnected);
        Assert.False(service.IsConnected);
        Assert.Equal(ConnectionState.Disconnected, service.State);
    }
}

/// <summary>
/// Kênh truyền hai chiều giả lập (In-memory mock ISafeFrameChannel) phục vụ kiểm thử đơn vị độc lập.
/// </summary>
public sealed class MockSafeFrameChannel : ISafeFrameChannel
{
    private readonly Channel<NetOpsMessage?> _incomingChannel = Channel.CreateUnbounded<NetOpsMessage?>();
    private bool _isDisposed;

    public bool IsConnected => !_isDisposed;

    public Stream UnderlyingStream => Stream.Null;

    public bool IsDisposed => _isDisposed;

    public Func<NetOpsMessage, MockSafeFrameChannel, Task>? OnMessageSent { get; set; }

    public async ValueTask SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (OnMessageSent != null)
        {
            await OnMessageSent(message, this).ConfigureAwait(false);
        }
    }

    public async ValueTask<NetOpsMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return null;
        }

        try
        {
            return await _incomingChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public ValueTask SendRawFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return ValueTask.CompletedTask;
    }

    public async Task EnqueueIncomingMessageAsync(NetOpsMessage? message)
    {
        await _incomingChannel.Writer.WriteAsync(message).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _incomingChannel.Writer.TryComplete();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
