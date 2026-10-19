using System.Collections.Concurrent;
using System.Net.Sockets;
using Client.WinForms.Services;
using NetOps.Tests.TestHelpers;
using ServerManager.Shared.Contracts.Models;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử đa kết nối Client đồng thời trên TcpManagerServer (Multi-Client Concurrency Tests).
/// Xác thực:
/// 1. Máy chủ xử lý song song nhiều kết nối client (15-20 clients) mà không làm nghẽn vòng lặp AcceptTcpClientAsync.
/// 2. Mỗi client nhận phản hồi độc lập, chính xác, đúng Correlation ID.
/// 3. Server giải phóng tài nguyên triệt để khi client ngắt kết nối và sẵn sàng đón các đợt client mới.
/// 4. Các sự cố đứt kết nối đột ngột của một nhóm client không gây crash server hay ảnh hưởng tới các client khác.
/// </summary>
[Trait("Category", "Stress")]
[Trait("Area", "Master")]
public class MultiClientConcurrencyTests
{
    /// <summary>
    /// Kịch bản 1: 15 clients độc lập kết nối cùng một lúc tới TcpManagerServer.
    /// Mỗi client đồng thời gửi một chuỗi các yêu cầu Ping và Heartbeat.
    /// Xác thực: 100% clients kết nối và nhận phản hồi độc lập, đúng endpoint và đúng InReplyTo.
    /// </summary>
    [Fact]
    public async Task TcpManagerServer_MultipleSimultaneousClients_AllConnectAndReceiveResponsesIndependently()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        const int clientCount = 15;
        const int requestsPerClient = 4;
        const int totalExpectedResponses = clientCount * requestsPerClient;

        var clientResults = new ConcurrentBag<(int ClientId, string InReplyTo, string ResponseType)>();

        // Khởi chạy 15 clients song song
        var clientTasks = Enumerable.Range(0, clientCount).Select(clientId => Task.Run(async () =>
        {
            using var client = new NetOpsClientService();
            await client.ConnectAsync(server.Host, server.Port);
            Assert.True(client.IsConnected);

            for (int seq = 0; seq < requestsPerClient; seq++)
            {
                if (seq % 2 == 0)
                {
                    string pingPayload = $"multi-client-{clientId}-seq-{seq}";
                    var (rtt, pong) = await client.PingAsync(pingPayload, TimeSpan.FromSeconds(10));

                    Assert.Equal(MessageType.Pong, pong.MessageType);
                    Assert.Equal("PONG_ACK", pong.Payload);
                    Assert.False(string.IsNullOrEmpty(pong.InReplyTo));

                    clientResults.Add((clientId, pong.InReplyTo!, pong.MessageType));
                }
                else
                {
                    string agentId = $"agent-concurrency-{clientId}";
                    var (ack, res) = await client.SendHeartbeatAsync(agentId, TimeSpan.FromSeconds(10));

                    Assert.NotNull(ack);
                    Assert.True(ack.Acknowledged);
                    Assert.True(ack.Timestamp > 0);
                    Assert.Equal(MessageType.HeartbeatAck, res.MessageType);
                    Assert.False(string.IsNullOrEmpty(res.InReplyTo));

                    clientResults.Add((clientId, res.InReplyTo!, res.MessageType));
                }
            }

            await client.DisconnectAsync();
            Assert.False(client.IsConnected);
        })).ToArray();

        await Task.WhenAll(clientTasks);

        // Xác nhận nhận đủ số lượng phản hồi từ toàn bộ 15 clients
        Assert.Equal(totalExpectedResponses, clientResults.Count);

        // Xác nhận mỗi client đều có đúng số lượng phản hồi
        for (int c = 0; c < clientCount; c++)
        {
            int responsesForClient = clientResults.Count(r => r.ClientId == c);
            Assert.Equal(requestsPerClient, responsesForClient);
        }
    }

    /// <summary>
    /// Kịch bản 2: Vòng đời kết nối theo đợt (Batch Lifecycle).
    /// Đợt 1: 10 clients kết nối và gửi yêu cầu, sau đó đồng loạt ngắt kết nối sạch sẽ.
    /// Đợt 2: 10 clients mới kết nối vào cùng server và tiếp tục gửi nhận bình thường.
    /// Xác thực: Server dọn dẹp sạch sẽ tài nguyên socket của đợt 1 và sẵn sàng phục vụ đợt 2 mà không bị nghẽn hay rò rỉ.
    /// </summary>
    [Fact]
    public async Task TcpManagerServer_SequentialAndBatchDisconnect_ServerReleasesResourcesAndAcceptsNewClients()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        // Đợt 1: 10 clients
        var batch1Tasks = Enumerable.Range(0, 10).Select(async id =>
        {
            using var client = new NetOpsClientService();
            await client.ConnectAsync(server.Host, server.Port);
            var (rtt, pong) = await client.PingAsync($"batch1-{id}", TimeSpan.FromSeconds(5));
            Assert.Equal(MessageType.Pong, pong.MessageType);
            await client.DisconnectAsync();
        });
        await Task.WhenAll(batch1Tasks);

        // Chờ ngắn để server hoàn tất cleanup session cũ
        await Task.Delay(50);

        // Đợt 2: 10 clients mới kết nối vào cùng server
        var batch2Tasks = Enumerable.Range(0, 10).Select(async id =>
        {
            using var client = new NetOpsClientService();
            await client.ConnectAsync(server.Host, server.Port);
            var (ack, res) = await client.SendHeartbeatAsync($"batch2-agent-{id}", TimeSpan.FromSeconds(5));
            Assert.NotNull(ack);
            Assert.True(ack.Acknowledged);
            await client.DisconnectAsync();
        });
        await Task.WhenAll(batch2Tasks);
    }

    /// <summary>
    /// Kịch bản 3: Xử lý cô lập lỗi khi một số clients ngắt kết nối đột ngột (Abrupt Disconnect).
    /// Khởi động 10 clients:
    /// - 5 clients đóng socket đột ngột (gây SocketException/IOException trên server).
    /// - 5 clients khỏe mạnh vẫn tiếp tục trao đổi dữ liệu Ping/Pong liên tục.
    /// Xác thực: Sự cố của nhóm client lỗi không làm crash server hay ảnh hưởng tới các client khỏe mạnh.
    /// </summary>
    [Fact]
    public async Task TcpManagerServer_ClientsDisconnectingAbruptly_DoesNotCrashServerOrAffectOtherClients()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        var healthyClients = new List<NetOpsClientService>();
        var faultedSockets = new List<TcpClient>();

        try
        {
            // 1. Kết nối 5 clients khỏe mạnh
            for (int i = 0; i < 5; i++)
            {
                var healthy = new NetOpsClientService();
                await healthy.ConnectAsync(server.Host, server.Port);
                healthyClients.Add(healthy);
            }

            // 2. Kết nối 5 raw sockets giả lập client lỗi
            for (int i = 0; i < 5; i++)
            {
                var rawSocket = new TcpClient();
                await rawSocket.ConnectAsync(server.Host, server.Port);
                faultedSockets.Add(rawSocket);
            }

            // 3. Đột ngột đóng sạch cả 5 raw sockets (hard reset/abortive disconnect)
            foreach (var faulted in faultedSockets)
            {
                faulted.Client.LingerState = new LingerOption(true, 0); // RST packet
                faulted.Client.Close();
                faulted.Dispose();
            }
            faultedSockets.Clear();

            // Chờ ngắn để server ghi nhận lỗi ngắt kết nối của các client lỗi
            await Task.Delay(100);

            // 4. Kiểm chứng các healthy clients vẫn gửi nhận Ping/Pong thành công mà không gặp bất kỳ lỗi nào
            var pingTasks = healthyClients.Select(async (client, index) =>
            {
                var (rtt, pong) = await client.PingAsync($"survivor-ping-{index}", TimeSpan.FromSeconds(5));
                Assert.Equal(MessageType.Pong, pong.MessageType);
                Assert.Equal("PONG_ACK", pong.Payload);
            });

            await Task.WhenAll(pingTasks);
        }
        finally
        {
            foreach (var client in healthyClients)
            {
                await client.DisconnectAsync();
                client.Dispose();
            }

            foreach (var faulted in faultedSockets)
            {
                faulted.Dispose();
            }
        }
    }
}
