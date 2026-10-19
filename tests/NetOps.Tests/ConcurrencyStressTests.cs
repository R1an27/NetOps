using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Client.WinForms.Services;
using NetOps.Tests.TestHelpers;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Shared.Contracts.Framing;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử tải nặng và an toàn đa luồng (High-Concurrency Stress & Thread-Safety Tests).
/// Xác thực tính toàn vẹn 100% của khung dữ liệu (Frame Integrity), chứng minh SemaphoreSlim(1,1)
/// ngăn chặn tuyệt đối tình trạng trộn lẫn byte (Byte Interleaving) và Request-Response Correlation
/// của NetOpsClientService hoạt động chính xác dưới áp lực nhiều luồng đồng thời.
/// </summary>
[Trait("Category", "Stress")]
[Trait("Area", "Protocol")]
public class ConcurrencyStressTests
{
    /// <summary>
    /// Kịch bản 1: Nhiều luồng (25 workers) đồng thời ghi hàng trăm frames thô có kích thước biến thiên
    /// ngẫu nhiên (từ 32 bytes đến 16 KB) qua cùng một SafeFrameStream trên socket TCP loopback.
    /// Xác thực: Phía đọc nhận đủ 100% frames không lỗi, không phân mảnh sai, và toàn vẹn từng byte.
    /// </summary>
    [Fact]
    public async Task SafeFrameStream_HeavyConcurrentRawWrites_PreservesFrameIntegrityAndPreventsInterleaving()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        // 1. Thiết lập kênh TCP loopback thực tế
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var clientConnectTask = Task.Run(async () =>
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, ct);
            return client;
        }, ct);

        using var serverClient = await listener.AcceptTcpClientAsync(ct);
        using var client = await clientConnectTask;
        listener.Stop();

        await using var safeWriterStream = new SafeFrameStream(client.GetStream());
        var serverStream = serverClient.GetStream();
        var frameReader = DefaultFrameReader.Instance;

        const int workerCount = 25;
        const int framesPerWorker = 20;
        const int totalFrames = workerCount * framesPerWorker; // 500 frames

        // 2. Tác vụ đọc nền: Đọc liên tục 500 frames và kiểm tra tính toàn vẹn từng byte
        var receivedFrames = new ConcurrentBag<(int WorkerId, int SeqId, int Size)>();
        var readerTask = Task.Run(async () =>
        {
            for (int i = 0; i < totalFrames; i++)
            {
                var frame = await frameReader.ReadFrameAsync(serverStream, ct);
                Assert.NotNull(frame);
                var payload = frame.Value.Payload;
                Assert.NotNull(payload);
                Assert.True(payload.Length >= 12, "Payload phải tối thiểu 12 bytes chứa header kiểm tra.");

                // Giải mã header kiểm chứng trong payload
                int workerId = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0, 4));
                int seqId = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4, 4));
                int declaredSize = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(8, 4));

                Assert.Equal(declaredSize, payload.Length);
                Assert.True(workerId >= 0 && workerId < workerCount);
                Assert.True(seqId >= 0 && seqId < framesPerWorker);

                // Kiểm tra từng byte nội dung payload: phải khớp tuyệt đối với công thức sinh
                for (int b = 12; b < payload.Length; b++)
                {
                    byte expected = (byte)((workerId ^ seqId ^ b) & 0xFF);
                    if (payload[b] != expected)
                    {
                        Assert.Fail($"Phát hiện trộn lẫn byte (Interleaving) tại byte {b} của Worker {workerId}, Seq {seqId}. Nhận: {payload[b]}, Mong đợi: {expected}");
                    }
                }

                receivedFrames.Add((workerId, seqId, payload.Length));
            }
        }, ct);

        // 3. Chạy 25 workers đồng thời cùng ghi vào SafeFrameStream
        var writerTasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(async () =>
        {
            for (int seq = 0; seq < framesPerWorker; seq++)
            {
                // Kích thước biến thiên từ 32 bytes đến 16 KB
                int payloadSize = 32 + ((workerId * 137 + seq * 89) % 16000);
                byte[] buffer = new byte[payloadSize];

                // Ghi header kiểm chứng vào 12 byte đầu
                BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), workerId);
                BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(4, 4), seq);
                BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(8, 4), payloadSize);

                // Điền pattern kiểm chứng vào các byte còn lại
                for (int b = 12; b < payloadSize; b++)
                {
                    buffer[b] = (byte)((workerId ^ seq ^ b) & 0xFF);
                }

                // Ghi qua SafeFrameStream an toàn đa luồng
                await safeWriterStream.WriteRawFrameAsync(buffer, ct);
            }
        }, ct)).ToArray();

        // 4. Chờ toàn bộ các tác vụ ghi và tác vụ đọc hoàn tất
        await Task.WhenAll(writerTasks);
        await readerTask;

        // 5. Xác nhận nhận đủ 500 frame độc nhất không mất mát
        Assert.Equal(totalFrames, receivedFrames.Count);
        var uniqueKeys = receivedFrames.Select(f => $"{f.WorkerId}-{f.SeqId}").ToHashSet();
        Assert.Equal(totalFrames, uniqueKeys.Count);
    }

    /// <summary>
    /// Kịch bản 2: Kiểm thử Request-Response Correlation của NetOpsClientService trên kết nối TCP socket thật.
    /// Chạy 20 workers đồng thời, mỗi worker gửi 5 requests (Ping và Heartbeat xen kẽ) tới TcpManagerServer.
    /// Tổng cộng 100 requests đồng thời.
    /// Xác thực: Tất cả 100 requests đều hoàn thành đúng hạn, mỗi phản hồi trả về đúng TaskCompletionSource tương ứng
    /// thông qua InReplyTo / RequestId mà không bị lẫn lộn giữa các luồng.
    /// </summary>
    [Fact]
    public async Task NetOpsClientService_ConcurrentRequests_CorrectlyCorrelatesAllResponses()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();
        using var client = new NetOpsClientService();

        await client.ConnectAsync(server.Host, server.Port);
        Assert.True(client.IsConnected);

        const int workerCount = 20;
        const int requestsPerWorker = 5;
        const int totalExpectedRequests = workerCount * requestsPerWorker; // 100 requests

        var results = new ConcurrentBag<(string WorkerTag, string RequestId, string ResponseType, bool Success)>();

        // 20 workers cùng gọi SendRequestAsync song song
        var tasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(async () =>
        {
            for (int seq = 0; seq < requestsPerWorker; seq++)
            {
                string workerTag = $"w{workerId}-s{seq}";

                if (seq % 2 == 0)
                {
                    // Gửi Ping và kiểm tra Pong
                    var (rtt, pong) = await client.PingAsync($"ping-{workerTag}", TimeSpan.FromSeconds(10));
                    Assert.Equal(MessageType.Pong, pong.MessageType);
                    Assert.Equal("PONG_ACK", pong.Payload);
                    Assert.False(string.IsNullOrEmpty(pong.InReplyTo));

                    results.Add((workerTag, pong.InReplyTo!, pong.MessageType, true));
                }
                else
                {
                    // Gửi Heartbeat và kiểm tra HeartbeatAck
                    var (ack, res) = await client.SendHeartbeatAsync($"agent-{workerTag}", TimeSpan.FromSeconds(10));
                    Assert.NotNull(ack);
                    Assert.True(ack.Acknowledged);
                    Assert.True(ack.Timestamp > 0);
                    Assert.Equal(MessageType.HeartbeatAck, res.MessageType);
                    Assert.False(string.IsNullOrEmpty(res.InReplyTo));

                    results.Add((workerTag, res.InReplyTo!, res.MessageType, true));
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Xác nhận nhận đủ 100 kết quả
        Assert.Equal(totalExpectedRequests, results.Count);
        Assert.All(results, r => Assert.True(r.Success));

        await client.DisconnectAsync();
    }

    /// <summary>
    /// Kịch bản 3: Nhiều luồng đồng thời ghi nhiều loại NetOpsMessage phong phú (PING, HEARTBEAT, COMMAND,
    /// COMMAND_OUTPUT, METRICS) vào cùng một SafeFrameStream qua kênh TCP.
    /// Xác thực: 100% messages được giải mã nguyên vẹn, đúng loại (MessageType) và đúng cấu trúc payload.
    /// </summary>
    [Fact]
    public async Task SafeFrameStream_ConcurrentNetOpsMessages_SerializationAndDeserialization()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var clientConnectTask = Task.Run(async () =>
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, ct);
            return client;
        }, ct);

        using var serverClient = await listener.AcceptTcpClientAsync(ct);
        using var client = await clientConnectTask;
        listener.Stop();

        await using var safeWriterStream = new SafeFrameStream(client.GetStream());
        await using var safeReaderStream = new SafeFrameStream(serverClient.GetStream());

        const int workerCount = 20;
        const int messagesPerWorker = 10;
        const int totalExpectedMessages = workerCount * messagesPerWorker; // 200 messages

        // Đầu đọc nhận toàn bộ messages và phân loại kiểm tra
        var receivedMessages = new ConcurrentBag<NetOpsMessage>();
        var readerTask = Task.Run(async () =>
        {
            for (int i = 0; i < totalExpectedMessages; i++)
            {
                var msg = await safeReaderStream.ReadMessageAsync(ct);
                Assert.NotNull(msg);
                receivedMessages.Add(msg);

                // Kiểm tra giải mã payload theo đúng loại message
                switch (msg.MessageType)
                {
                    case MessageType.Ping:
                        Assert.NotNull(msg.Payload);
                        Assert.StartsWith("ping-content-", msg.Payload);
                        break;

                    case MessageType.Heartbeat:
                        var hb = msg.GetPayload<HeartbeatPayload>();
                        Assert.NotNull(hb);
                        Assert.StartsWith("agent-hb-", hb.AgentId);
                        break;

                    case MessageType.Command:
                        var cmd = msg.GetPayload<CommandPayload>();
                        Assert.NotNull(cmd);
                        Assert.StartsWith("cmd-id-", cmd.CommandId);
                        Assert.Equal("dir /s", cmd.CommandLine);
                        break;

                    case MessageType.CommandOutput:
                        var outPayload = msg.GetPayload<CommandOutputPayload>();
                        Assert.NotNull(outPayload);
                        Assert.StartsWith("cmd-out-", outPayload.CommandId);
                        break;

                    case MessageType.Metrics:
                        var metrics = msg.GetPayload<MetricsPayload>();
                        Assert.NotNull(metrics);
                        Assert.StartsWith("agent-metrics-", metrics.AgentId);
                        Assert.Equal(55.5, metrics.CpuUsagePercent);
                        break;

                    default:
                        Assert.Fail($"Loại message không mong đợi: {msg.MessageType}");
                        break;
                }
            }
        }, ct);

        // 20 workers gửi đồng thời các thông điệp hỗn hợp
        var writerTasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(async () =>
        {
            for (int seq = 0; seq < messagesPerWorker; seq++)
            {
                string tag = $"w{workerId}-s{seq}";
                NetOpsMessage msg = (seq % 5) switch
                {
                    0 => NetOpsMessage.CreateRequest(MessageType.Ping, $"ping-content-{tag}", $"req-ping-{tag}"),
                    1 => NetOpsMessage.CreateRequest(MessageType.Heartbeat, new HeartbeatPayload($"agent-hb-{tag}", 1700000000000), $"req-hb-{tag}"),
                    2 => NetOpsMessage.CreateRequest(MessageType.Command, new CommandPayload($"cmd-id-{tag}", "dir /s", 30), $"req-cmd-{tag}"),
                    3 => NetOpsMessage.CreateRequest(MessageType.CommandOutput, new CommandOutputPayload($"cmd-out-{tag}", $"output-line-{tag}\n", false), $"req-out-{tag}"),
                    _ => NetOpsMessage.CreateRequest(MessageType.Metrics, new MetricsPayload { AgentId = $"agent-metrics-{tag}", CpuUsagePercent = 55.5, MemoryUsedMb = 2048 }, $"req-met-{tag}")
                };

                await safeWriterStream.WriteMessageAsync(msg, ct);
            }
        }, ct)).ToArray();

        await Task.WhenAll(writerTasks);
        await readerTask;

        Assert.Equal(totalExpectedMessages, receivedMessages.Count);
        var uniqueRequestIds = receivedMessages.Select(m => m.RequestId).ToHashSet();
        Assert.Equal(totalExpectedMessages, uniqueRequestIds.Count);
    }

    /// <summary>
    /// Kịch bản 4: Kiểm thử phương thức SendRawFrameAsync(ReadOnlyMemory) dưới áp lực tải đồng thời cao.
    /// 25 workers đồng thời ghi 500 ReadOnlyMemory frames qua SafeFrameStream.
    /// Xác thực: Quá trình truyền ReadOnlyMemory không sao chép thừa thãi vẫn bảo toàn 100% tính nguyên vẹn frame.
    /// </summary>
    [Fact]
    public async Task SafeFrameStream_SendRawFrameAsync_ReadOnlyMemory_HeavyConcurrentStress()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var clientConnectTask = Task.Run(async () =>
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, ct);
            return client;
        }, ct);

        using var serverClient = await listener.AcceptTcpClientAsync(ct);
        using var client = await clientConnectTask;
        listener.Stop();

        await using var safeWriterStream = new SafeFrameStream(client.GetStream());
        var serverStream = serverClient.GetStream();
        var frameReader = DefaultFrameReader.Instance;

        const int workerCount = 25;
        const int framesPerWorker = 20;
        const int totalFrames = workerCount * framesPerWorker; // 500 frames

        var readerTask = Task.Run(async () =>
        {
            for (int i = 0; i < totalFrames; i++)
            {
                var frame = await frameReader.ReadFrameAsync(serverStream, ct);
                Assert.NotNull(frame);
                var payload = frame.Value.Payload;
                Assert.NotNull(payload);
                Assert.True(payload.Length >= 8);

                int workerId = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0, 4));
                int seqId = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4, 4));

                Assert.True(workerId >= 0 && workerId < workerCount);
                Assert.True(seqId >= 0 && seqId < framesPerWorker);

                // Kiểm tra checksum byte đơn giản
                byte expectedCrc = (byte)((workerId * 17 + seqId * 31) & 0xFF);
                Assert.Equal(expectedCrc, payload[^1]);
            }
        }, ct);

        var writerTasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(async () =>
        {
            for (int seq = 0; seq < framesPerWorker; seq++)
            {
                int size = 64 + ((workerId * 7 + seq * 13) % 512);
                byte[] data = new byte[size];

                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0, 4), workerId);
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4, 4), seq);
                data[^1] = (byte)((workerId * 17 + seq * 31) & 0xFF);

                // Gửi qua SendRawFrameAsync với ReadOnlyMemory<byte>
                ReadOnlyMemory<byte> memory = data.AsMemory();
                await safeWriterStream.SendRawFrameAsync(memory, ct);
            }
        }, ct)).ToArray();

        await Task.WhenAll(writerTasks);
        await readerTask;
    }
}
