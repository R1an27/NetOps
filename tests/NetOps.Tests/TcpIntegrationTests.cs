using System.Diagnostics;
using Client.WinForms.Services;
using NetOps.Tests.TestHelpers;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử tích hợp End-to-End kiểm chứng giao tiếp qua Socket TCP thực tế trên localhost (127.0.0.1)
/// giữa NetOpsClientService và TcpManagerServer theo chuẩn NetOps Protocol v0.
/// </summary>
public class TcpIntegrationTests
{
    [Fact]
    public async Task Client_CanConnectToMaster_OverRealTcpSocket()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();
        using var client = new NetOpsClientService();

        var stateChanges = new List<ConnectionState>();
        client.ConnectionStateChanged += stateChanges.Add;

        await client.ConnectAsync(server.Host, server.Port);

        Assert.True(client.IsConnected);
        Assert.Equal(ConnectionState.Connected, client.State);
        Assert.Contains(server.Port.ToString(), client.ConnectedEndpoint);
        Assert.Contains(ConnectionState.Connected, stateChanges);

        await client.DisconnectAsync();

        Assert.False(client.IsConnected);
        Assert.Equal(ConnectionState.Disconnected, client.State);
        Assert.Contains(ConnectionState.Disconnected, stateChanges);
    }

    [Fact]
    public async Task PingPong_OverRealTcpSocket_MeasuresRttAndMatchesCorrelation()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();
        using var client = new NetOpsClientService();

        await client.ConnectAsync(server.Host, server.Port);

        var (rtt, pong) = await client.PingAsync("e2e-probe-payload", TimeSpan.FromSeconds(5));

        Assert.True(rtt >= TimeSpan.Zero);
        Assert.Equal(MessageType.Pong, pong.MessageType);
        Assert.Equal("PONG_ACK", pong.Payload);
        Assert.False(string.IsNullOrEmpty(pong.InReplyTo));

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task Heartbeat_OverRealTcpSocket_ReceivesHeartbeatAckWithServerTimestamp()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();
        using var client = new NetOpsClientService();

        await client.ConnectAsync(server.Host, server.Port);

        long beforeTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var (ack, response) = await client.SendHeartbeatAsync("client-agent-e2e", TimeSpan.FromSeconds(5));
        long afterTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Equal(MessageType.HeartbeatAck, response.MessageType);
        Assert.NotNull(ack);
        Assert.True(ack.Acknowledged);
        Assert.InRange(ack.Timestamp, beforeTimestamp - 1000, afterTimestamp + 1000);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task UnsupportedMessageType_OverRealTcpSocket_ReturnsErrorMessage()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();
        using var client = new NetOpsClientService();

        await client.ConnectAsync(server.Host, server.Port);

        var customRequest = NetOpsMessage.CreateRequest("SUPER_CUSTOM_UNKNOWN_TYPE", "payload-data");
        var response = await client.SendRequestAsync(customRequest, TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.Error, response.MessageType);
        Assert.Equal(customRequest.MessageId, response.InReplyTo);

        var error = response.GetPayload<ErrorPayload>();
        Assert.NotNull(error);
        Assert.Equal("UNSUPPORTED_MESSAGE_TYPE", error.ErrorCode);
        Assert.Contains("SUPER_CUSTOM_UNKNOWN_TYPE", error.Message);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task DisconnectAndReconnect_OverRealTcpSocket_OperatesReliably()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();
        using var client = new NetOpsClientService();

        // Phiên 1: Kết nối -> Ping -> Ngắt
        await client.ConnectAsync(server.Host, server.Port);
        Assert.True(client.IsConnected);
        var (rtt1, pong1) = await client.PingAsync("ping-1", TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.Pong, pong1.MessageType);
        await client.DisconnectAsync();
        Assert.False(client.IsConnected);

        // Phiên 2: Kết nối lại -> Ping tiếp -> Ngắt
        await client.ConnectAsync(server.Host, server.Port);
        Assert.True(client.IsConnected);
        var (rtt2, pong2) = await client.PingAsync("ping-2", TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.Pong, pong2.MessageType);
        await client.DisconnectAsync();
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task MultipleSubsequentRequests_OverSameTcpSession_AllSucceed()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();
        using var client = new NetOpsClientService();

        await client.ConnectAsync(server.Host, server.Port);

        // Gửi liên tiếp 5 yêu cầu trên cùng 1 kết nối TCP
        for (int i = 1; i <= 5; i++)
        {
            var (rtt, pong) = await client.PingAsync($"ping-seq-{i}", TimeSpan.FromSeconds(5));
            Assert.Equal(MessageType.Pong, pong.MessageType);
            Assert.Equal("PONG_ACK", pong.Payload);
        }

        var (ack, _) = await client.SendHeartbeatAsync("client-agent-seq", TimeSpan.FromSeconds(5));
        Assert.NotNull(ack);
        Assert.True(ack.Acknowledged);

        await client.DisconnectAsync();
    }
}
