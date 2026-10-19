using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Shared.Contracts.Framing;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử giao thức cấp cao (Protocol Regression & Specification Suite).
/// Xác thực tính tương thích ngược (Backward Compatibility) và sự đúng đắn của toàn bộ luồng NetOps Protocol v0.
/// </summary>
public class ProtocolTests
{
    #region Phase 1: Backward Compatibility Tests

    [Fact]
    public async Task Goi_tin_legacy_khong_co_requestId_van_deserialize_va_hoat_dong_binh_thuong()
    {
        // 1. Giả lập một client phiên bản cũ chỉ gửi cấu trúc JSON nguyên thủy (không có trường requestId)
        string legacyJson = """
        {
          "messageId": "legacy-msg-12345",
          "messageType": "PING",
          "timestamp": 1728500000000,
          "payload": "legacy-plain-payload"
        }
        """;

        byte[] payloadBytes = Encoding.UTF8.GetBytes(legacyJson);
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, payloadBytes);

        // 2. Phía nhận giải mã gói tin qua FrameCodec
        ms.Position = 0;
        var receivedMsg = await FrameCodec.ReadFrameAsync(ms);

        Assert.NotNull(receivedMsg);
        Assert.Equal("legacy-msg-12345", receivedMsg.MessageId);
        Assert.Equal("PING", receivedMsg.MessageType);
        Assert.Equal(1728500000000, receivedMsg.Timestamp);
        Assert.Equal("legacy-plain-payload", receivedMsg.Payload);
        Assert.Null(receivedMsg.RequestId); // Khách hàng cũ không gửi requestId -> nhận về null
        Assert.Null(receivedMsg.InReplyTo);

        // 3. Phía nhận gọi CreateResponse: Hệ thống tự động fallback gán RequestId = MessageId
        var response = receivedMsg.CreateResponse("PONG", "legacy-pong-ack");

        Assert.Equal("PONG", response.MessageType);
        Assert.Equal("legacy-msg-12345", response.InReplyTo);
        Assert.Equal("legacy-msg-12345", response.RequestId); // Fallback chuẩn xác để đảm bảo correlation
        Assert.Equal("legacy-pong-ack", response.Payload);
    }

    [Fact]
    public async Task Goi_tin_JSON_chua_truong_mo_rong_khong_xac_dinh_khong_nem_ngoai_le()
    {
        // Giả lập một client đời mới hơn hoặc client tùy biến gửi kèm các metadata lạ ngoài spec
        string extendedJson = """
        {
          "messageId": "msg-with-extra-fields",
          "requestId": "req-ext-001",
          "messageType": "COMMAND",
          "timestamp": 1728500000000,
          "payload": "{\"commandId\":\"cmd-999\"}",
          "futureExtensionField": 99999,
          "customMetadata": {
            "clientVersion": "1.2.0-beta",
            "environment": "staging"
          }
        }
        """;

        byte[] payloadBytes = Encoding.UTF8.GetBytes(extendedJson);
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, payloadBytes);

        ms.Position = 0;
        var msg = await FrameCodec.ReadFrameAsync(ms);

        Assert.NotNull(msg);
        Assert.Equal("msg-with-extra-fields", msg.MessageId);
        Assert.Equal("req-ext-001", msg.RequestId);
        Assert.Equal("COMMAND", msg.MessageType);
        Assert.Equal("{\"commandId\":\"cmd-999\"}", msg.Payload);
    }

    [Fact]
    public async Task Goi_tin_co_payload_la_chuoi_text_thuan_tuy_khong_phai_json_object()
    {
        // Kiểm tra tương thích với các thông điệp mang chuỗi text tự do (vd: log lines, plain string)
        string plainTextPayload = "Raw output line with special symbols: \r\n \t \"quoted\" and Tiếng Việt có dấu UTF-8.";

        var msg = NetOpsMessage.CreateRequest("LOG_STREAM", plainTextPayload);
        byte[] frameBytes = FrameCodec.EncodeFrame(msg);

        using var ms = new MemoryStream(frameBytes);
        var readMsg = await FrameCodec.ReadFrameAsync(ms);

        Assert.NotNull(readMsg);
        Assert.Equal("LOG_STREAM", readMsg.MessageType);
        Assert.Equal(plainTextPayload, readMsg.Payload);
    }

    [Fact]
    public async Task Goi_tin_keep_alive_khong_co_payload_van_truyen_nhan_tron_tru()
    {
        // Client chỉ gửi Ping rỗng (Payload = null)
        var ping = new NetOpsMessage
        {
            MessageType = MessageType.Ping,
            Payload = null
        };

        var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);
        await safeStream.WriteMessageAsync(ping);

        ms.Position = 0;
        var received = await safeStream.ReadMessageAsync();

        Assert.NotNull(received);
        Assert.Equal(MessageType.Ping, received.MessageType);
        Assert.Null(received.Payload);
        Assert.Null(received.RequestId);
        Assert.Null(received.InReplyTo);
    }

    [Fact]
    public async Task Goi_tin_legacy_co_inReplyTo_nhung_khong_co_requestId_van_bao_toan()
    {
        // Server cũ phản hồi chỉ có inReplyTo mà không có requestId
        string legacyResponseJson = """
        {
          "messageId": "resp-legacy-01",
          "inReplyTo": "req-client-99",
          "messageType": "PONG",
          "timestamp": 1728500000000
        }
        """;

        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, Encoding.UTF8.GetBytes(legacyResponseJson));

        ms.Position = 0;
        var received = await FrameCodec.ReadFrameAsync(ms);

        Assert.NotNull(received);
        Assert.Equal("resp-legacy-01", received.MessageId);
        Assert.Equal("req-client-99", received.InReplyTo);
        Assert.Null(received.RequestId);
        Assert.Equal("PONG", received.MessageType);
    }

    #endregion

    #region Phase 2: Protocol v0 Message Flows

    [Fact]
    public async Task Luong_Ping_Pong_ghep_doi_chinh_xac_MessageId_va_RequestId()
    {
        var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);

        // 1. Client gửi PING có RequestId tường minh
        var ping = NetOpsMessage.CreateRequest(MessageType.Ping, "PING_PAYLOAD", "trace-ping-42");
        await safeStream.WriteMessageAsync(ping);

        // 2. Server nhận PING và phản hồi PONG qua CreateResponse
        ms.Position = 0;
        var receivedPing = await safeStream.ReadMessageAsync();
        Assert.NotNull(receivedPing);
        Assert.Equal(MessageType.Ping, receivedPing.MessageType);
        Assert.Equal("trace-ping-42", receivedPing.RequestId);

        var pong = receivedPing.CreateResponse(MessageType.Pong, "PONG_ACK");

        // Ghi phản hồi ra stream mới để client đọc
        var msResponse = new MemoryStream();
        await using var safeStreamResp = new SafeFrameStream(msResponse);
        await safeStreamResp.WriteMessageAsync(pong);

        // 3. Client nhận PONG và đối chiếu
        msResponse.Position = 0;
        var receivedPong = await safeStreamResp.ReadMessageAsync();
        Assert.NotNull(receivedPong);
        Assert.Equal(MessageType.Pong, receivedPong.MessageType);
        Assert.Equal(receivedPing.MessageId, receivedPong.InReplyTo); // Quan hệ 1:1 gói tin
        Assert.Equal("trace-ping-42", receivedPong.RequestId);        // Đối chiếu luồng tác vụ logic
        Assert.Equal("PONG_ACK", receivedPong.Payload);
    }

    [Fact]
    public async Task Luong_Heartbeat_va_HeartbeatAck_trao_doi_dung_DTO_va_timestamp()
    {
        var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);

        // 1. Agent gửi HEARTBEAT định kỳ
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var heartbeatPayload = new HeartbeatPayload("agent-node-01", now);
        var heartbeat = NetOpsMessage.CreateRequest(MessageType.Heartbeat, heartbeatPayload, "hb-seq-101");

        await safeStream.WriteMessageAsync(heartbeat);

        // 2. Manager nhận HEARTBEAT, xác thực Agent và phản hồi HEARTBEAT_ACK
        ms.Position = 0;
        var receivedHb = await safeStream.ReadMessageAsync();
        Assert.NotNull(receivedHb);
        Assert.Equal(MessageType.Heartbeat, receivedHb.MessageType);

        var parsedHb = receivedHb.GetPayload<HeartbeatPayload>();
        Assert.NotNull(parsedHb);
        Assert.Equal("agent-node-01", parsedHb.AgentId);
        Assert.Equal(now, parsedHb.Timestamp);

        var ackPayload = new HeartbeatAckPayload(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true);
        var ack = receivedHb.CreateResponse(MessageType.HeartbeatAck, ackPayload);

        var msAck = new MemoryStream();
        await using var safeAckStream = new SafeFrameStream(msAck);
        await safeAckStream.WriteMessageAsync(ack);

        // 3. Agent nhận HEARTBEAT_ACK
        msAck.Position = 0;
        var receivedAck = await safeAckStream.ReadMessageAsync();
        Assert.NotNull(receivedAck);
        Assert.Equal(MessageType.HeartbeatAck, receivedAck.MessageType);
        Assert.Equal(heartbeat.MessageId, receivedAck.InReplyTo);
        Assert.Equal("hb-seq-101", receivedAck.RequestId);

        var parsedAck = receivedAck.GetPayload<HeartbeatAckPayload>();
        Assert.NotNull(parsedAck);
        Assert.True(parsedAck.Acknowledged);
    }

    [Fact]
    public async Task Luong_Auth_thanh_cong_AUTH_OK_va_that_bai_AUTH_FAILED()
    {
        // Case 1: Xác thực thành công (Token hợp lệ)
        var msSuccess = new MemoryStream();
        await using var safeStreamSuccess = new SafeFrameStream(msSuccess);

        var authPayload = new AuthPayload("agent-vps-01", "valid-secret-token", "Linux 6.8", "vps-01");
        var authMsg = NetOpsMessage.CreateRequest(MessageType.Auth, authPayload, "auth-flow-01");
        await safeStreamSuccess.WriteMessageAsync(authMsg);

        msSuccess.Position = 0;
        var receivedAuth = await safeStreamSuccess.ReadMessageAsync();
        Assert.NotNull(receivedAuth);
        var parsedAuth = receivedAuth.GetPayload<AuthPayload>();
        Assert.Equal("agent-vps-01", parsedAuth!.AgentId);

        var authOkMsg = receivedAuth.CreateResponse(MessageType.AuthOk, new AuthOkPayload("session-uuid-777", "Welcome"));
        var msOk = new MemoryStream();
        await using var safeOkStream = new SafeFrameStream(msOk);
        await safeOkStream.WriteMessageAsync(authOkMsg);

        msOk.Position = 0;
        var receivedOk = await safeOkStream.ReadMessageAsync();
        Assert.NotNull(receivedOk);
        Assert.Equal(MessageType.AuthOk, receivedOk.MessageType);
        Assert.Equal("session-uuid-777", receivedOk.GetPayload<AuthOkPayload>()!.SessionId);

        // Case 2: Xác thực thất bại (Token sai)
        var failedMsg = receivedAuth.CreateResponse(MessageType.AuthFailed, new AuthFailedPayload("AUTH_INVALID_TOKEN", "Bad agent secret key"));
        var msFail = new MemoryStream();
        await using var safeFailStream = new SafeFrameStream(msFail);
        await safeFailStream.WriteMessageAsync(failedMsg);

        msFail.Position = 0;
        var receivedFail = await safeFailStream.ReadMessageAsync();
        Assert.NotNull(receivedFail);
        Assert.Equal(MessageType.AuthFailed, receivedFail.MessageType);
        var parsedFail = receivedFail.GetPayload<AuthFailedPayload>();
        Assert.Equal("AUTH_INVALID_TOKEN", parsedFail!.ErrorCode);
    }

    [Fact]
    public async Task Luong_Command_Execution_streaming_nhieu_doan_duy_tri_cung_RequestId()
    {
        // Manager gửi lệnh và Agent streaming phản hồi nhiều phần
        var msManagerToAgent = new MemoryStream();
        await using var safeM2A = new SafeFrameStream(msManagerToAgent);

        var cmdPayload = new CommandPayload("cmd-uuid-001", "docker ps -a", 60, "/home/deploy");
        var cmdRequest = NetOpsMessage.CreateRequest(MessageType.Command, cmdPayload, "cor-task-flow-99");
        await safeM2A.WriteMessageAsync(cmdRequest);

        msManagerToAgent.Position = 0;
        var receivedCmd = await safeM2A.ReadMessageAsync();
        Assert.NotNull(receivedCmd);
        Assert.Equal(MessageType.Command, receivedCmd.MessageType);

        // Agent streaming phản hồi 3 đoạn output rồi kết thúc bằng COMMAND_DONE
        var msAgentToManager = new MemoryStream();
        await using var safeA2M = new SafeFrameStream(msAgentToManager);

        var chunk1 = receivedCmd.CreateResponse(MessageType.CommandOutput, new CommandOutputPayload("cmd-uuid-001", "CONTAINER ID   IMAGE\n", false));
        var chunk2 = receivedCmd.CreateResponse(MessageType.CommandOutput, new CommandOutputPayload("cmd-uuid-001", "a1b2c3d4e5f6   nginx:alpine\n", false));
        var chunk3 = receivedCmd.CreateResponse(MessageType.CommandOutput, new CommandOutputPayload("cmd-uuid-001", "Warning: 1 container exited\n", true));
        var done = receivedCmd.CreateResponse(MessageType.CommandDone, new CommandDonePayload("cmd-uuid-001", 0, 180));

        await safeA2M.WriteMessageAsync(chunk1);
        await safeA2M.WriteMessageAsync(chunk2);
        await safeA2M.WriteMessageAsync(chunk3);
        await safeA2M.WriteMessageAsync(done);

        // Manager đọc toàn bộ stream phản hồi
        msAgentToManager.Position = 0;

        // Đọc chunk 1
        var msg1 = await safeA2M.ReadMessageAsync();
        Assert.NotNull(msg1);
        Assert.Equal(MessageType.CommandOutput, msg1.MessageType);
        Assert.Equal("cor-task-flow-99", msg1.RequestId);
        Assert.Equal(receivedCmd.MessageId, msg1.InReplyTo);
        Assert.Equal("CONTAINER ID   IMAGE\n", msg1.GetPayload<CommandOutputPayload>()!.Output);

        // Đọc chunk 2
        var msg2 = await safeA2M.ReadMessageAsync();
        Assert.NotNull(msg2);
        Assert.Equal(MessageType.CommandOutput, msg2.MessageType);
        Assert.Equal("cor-task-flow-99", msg2.RequestId);
        Assert.False(msg2.GetPayload<CommandOutputPayload>()!.IsError);

        // Đọc chunk 3
        var msg3 = await safeA2M.ReadMessageAsync();
        Assert.NotNull(msg3);
        Assert.Equal(MessageType.CommandOutput, msg3.MessageType);
        Assert.Equal("cor-task-flow-99", msg3.RequestId);
        Assert.True(msg3.GetPayload<CommandOutputPayload>()!.IsError);

        // Đọc DONE
        var msgDone = await safeA2M.ReadMessageAsync();
        Assert.NotNull(msgDone);
        Assert.Equal(MessageType.CommandDone, msgDone.MessageType);
        Assert.Equal("cor-task-flow-99", msgDone.RequestId);
        Assert.Equal(receivedCmd.MessageId, msgDone.InReplyTo);
        var doneParsed = msgDone.GetPayload<CommandDonePayload>();
        Assert.Equal(0, doneParsed!.ExitCode);
        Assert.Equal(180, doneParsed.TotalExecutionTimeMs);
    }

    [Fact]
    public async Task Luong_Error_Handling_khi_nhan_messageType_khong_hop_le()
    {
        var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);

        // Client gửi messageType không được hệ thống hỗ trợ
        var invalidMsg = NetOpsMessage.CreateRequest("UNSUPPORTED_ACTION", "raw_payload", "req-err-flow-01");
        await safeStream.WriteMessageAsync(invalidMsg);

        ms.Position = 0;
        var received = await safeStream.ReadMessageAsync();
        Assert.NotNull(received);

        // Server phát hiện loại thông điệp lạ, phản hồi ERROR envelope
        var errorResponse = received.CreateError("INVALID_MESSAGE_TYPE", "MessageType 'UNSUPPORTED_ACTION' is not supported", "Endpoint: 127.0.0.1");

        var msResp = new MemoryStream();
        await using var safeRespStream = new SafeFrameStream(msResp);
        await safeRespStream.WriteMessageAsync(errorResponse);

        msResp.Position = 0;
        var clientReceivedError = await safeRespStream.ReadMessageAsync();
        Assert.NotNull(clientReceivedError);
        Assert.Equal(MessageType.Error, clientReceivedError.MessageType);
        Assert.Equal("req-err-flow-01", clientReceivedError.RequestId);
        Assert.Equal(invalidMsg.MessageId, clientReceivedError.InReplyTo);

        var errPayload = clientReceivedError.GetPayload<ErrorPayload>();
        Assert.NotNull(errPayload);
        Assert.Equal("INVALID_MESSAGE_TYPE", errPayload.ErrorCode);
        Assert.Equal("MessageType 'UNSUPPORTED_ACTION' is not supported", errPayload.Message);
        Assert.Equal("Endpoint: 127.0.0.1", errPayload.Details);
    }

    #endregion

    #region Phase 3: Resilience & Concurrency Tests

    [Fact]
    public async Task SafeFrameStream_Write_Serialization_bao_ve_du_lieu_khi_nhieu_luong_ghi_dong_thoi()
    {
        // 1. Chuẩn bị MemoryStream và bọc qua SafeFrameStream
        var ms = new MemoryStream();
        var safeStream = new SafeFrameStream(ms);

        int workerCount = 10;
        int messagesPerWorker = 10;
        int totalExpectedMessages = workerCount * messagesPerWorker;

        // 2. Chạy 10 Task đồng thời, mỗi Task ghi 10 message khác nhau
        var tasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(async () =>
        {
            for (int i = 0; i < messagesPerWorker; i++)
            {
                var msg = NetOpsMessage.CreateRequest(
                    "CONCURRENT_EVENT",
                    $"payload-from-worker-{workerId}-seq-{i}",
                    $"req-worker-{workerId}"
                );
                await safeStream.WriteMessageAsync(msg);
            }
        }));

        await Task.WhenAll(tasks);

        // 3. Đọc tuần tự từ MemoryStream để kiểm tra tính toàn vẹn của tất cả các frame
        ms.Position = 0;
        var readMessages = new List<NetOpsMessage>();

        for (int i = 0; i < totalExpectedMessages; i++)
        {
            var msg = await safeStream.ReadMessageAsync();
            Assert.NotNull(msg);
            Assert.Equal("CONCURRENT_EVENT", msg.MessageType);
            readMessages.Add(msg);
        }

        // Sau khi đọc hết 100 gói, lần đọc tiếp theo phải là Graceful EOF (trả về null)
        var eofMsg = await safeStream.ReadMessageAsync();
        Assert.Null(eofMsg);

        // Đảm bảo không bị mất gói tin nào và nhận đủ 100 message
        Assert.Equal(totalExpectedMessages, readMessages.Count);
        var payloads = readMessages.Select(m => m.Payload).ToHashSet();
        Assert.Equal(totalExpectedMessages, payloads.Count);
    }

    [Fact]
    public async Task Mo_phong_kenh_FullDuplex_hai_chieu_dong_thoi_qua_TCP_loopback()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        // 1. Khởi tạo TCP Listener trên IP Loopback với dynamic port
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // 2. Client và Server kết nối
        var serverAcceptTask = listener.AcceptTcpClientAsync(ct);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, ct);
        using var serverClient = await serverAcceptTask;

        await using var clientStream = new SafeFrameStream(client.GetStream());
        await using var serverStream = new SafeFrameStream(serverClient.GetStream());

        int messageCount = 20;

        // 3. Chạy 2 chiều Full-Duplex đồng thời:
        // Chiều 1 (Client -> Server): Agent gửi Heartbeat liên tục
        var clientSenderTask = Task.Run(async () =>
        {
            for (int i = 0; i < messageCount; i++)
            {
                var hb = NetOpsMessage.CreateRequest(
                    MessageType.Heartbeat,
                    new HeartbeatPayload($"agent-{i}", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                    $"hb-req-{i}"
                );
                await clientStream.WriteMessageAsync(hb, ct);
            }
        }, ct);

        var serverReceiverTask = Task.Run(async () =>
        {
            var received = new List<NetOpsMessage>();
            for (int i = 0; i < messageCount; i++)
            {
                var msg = await serverStream.ReadMessageAsync(ct);
                Assert.NotNull(msg);
                Assert.Equal(MessageType.Heartbeat, msg.MessageType);
                received.Add(msg);
            }
            return received;
        }, ct);

        // Chiều 2 (Server -> Client): Server gửi CommandOutput streaming liên tục
        var serverSenderTask = Task.Run(async () =>
        {
            for (int i = 0; i < messageCount; i++)
            {
                var output = NetOpsMessage.CreateRequest(
                    MessageType.CommandOutput,
                    new CommandOutputPayload("cmd-full-duplex", $"streaming-line-{i}\n", false),
                    $"cmd-req-{i}"
                );
                await serverStream.WriteMessageAsync(output, ct);
            }
        }, ct);

        var clientReceiverTask = Task.Run(async () =>
        {
            var received = new List<NetOpsMessage>();
            for (int i = 0; i < messageCount; i++)
            {
                var msg = await clientStream.ReadMessageAsync(ct);
                Assert.NotNull(msg);
                Assert.Equal(MessageType.CommandOutput, msg.MessageType);
                received.Add(msg);
            }
            return received;
        }, ct);

        // 4. Chờ cả 4 luồng hoàn tất mà không gặp deadlock
        await Task.WhenAll(clientSenderTask, serverReceiverTask, serverSenderTask, clientReceiverTask);

        var serverReceived = await serverReceiverTask;
        var clientReceived = await clientReceiverTask;

        Assert.Equal(messageCount, serverReceived.Count);
        Assert.Equal(messageCount, clientReceived.Count);

        listener.Stop();
    }

    [Fact]
    public async Task Xu_ly_ngat_ket_noi_sach_Graceful_EOF_tra_ve_null()
    {
        // Case 1: Stream rỗng hoàn toàn -> trả về null ngay lập tức
        using var emptyStream = new MemoryStream();
        await using var safeEmpty = new SafeFrameStream(emptyStream);
        var resultEmpty = await safeEmpty.ReadMessageAsync();
        Assert.Null(resultEmpty);

        // Case 2: Stream có dữ liệu 1 frame, sau khi đọc xong đến EOF -> lần đọc tiếp theo trả về null
        var ms = new MemoryStream();
        var safeStream = new SafeFrameStream(ms);
        await safeStream.WriteMessageAsync(NetOpsMessage.CreateRequest(MessageType.Ping, "single-packet"));

        ms.Position = 0;
        var firstRead = await safeStream.ReadMessageAsync();
        Assert.NotNull(firstRead);
        Assert.Equal(MessageType.Ping, firstRead.MessageType);

        // Đọc tiếp sau khi đã cạn stream
        var secondRead = await safeStream.ReadMessageAsync();
        Assert.Null(secondRead);
    }

    [Fact]
    public async Task Phat_hien_dut_ket_noi_giua_chung_nem_EndOfStreamException()
    {
        // Case 1: Đứt kết nối khi đang đọc header độ dài (Length Header chỉ có 2 byte thay vì 4 byte)
        byte[] truncatedHeader = [0x00, 0x01]; // Thiếu 2 bytes
        using var msTruncatedHeader = new MemoryStream(truncatedHeader);
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await FrameCodec.ReadFrameAsync(msTruncatedHeader);
        });

        // Case 2: Đứt kết nối khi đang đọc payload (Header khai báo 20 byte, nhưng chỉ có 5 byte payload)
        byte[] truncatedPayload = new byte[4 + 5];
        BinaryPrimitives.WriteInt32BigEndian(truncatedPayload.AsSpan(0, 4), 20); // Length = 20
        Encoding.UTF8.GetBytes("hello").CopyTo(truncatedPayload, 4); // Chỉ ghi 5 bytes

        using var msTruncatedPayload = new MemoryStream(truncatedPayload);
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await FrameCodec.ReadFrameAsync(msTruncatedPayload);
        });
    }

    [Fact]
    public async Task Vi_pham_gioi_han_kich_thuoc_frame_nem_InvalidDataException()
    {
        // Case 1: Chiều dài frame = 0 (nhỏ hơn MinPayloadSize = 1)
        byte[] zeroLengthFrame = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(zeroLengthFrame, 0);

        using var msZero = new MemoryStream(zeroLengthFrame);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await FrameCodec.ReadFrameAsync(msZero);
        });

        // Case 2: Chiều dài frame vượt quá 16MB (MaxPayloadSize = 16 * 1024 * 1024)
        byte[] oversizedFrame = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(oversizedFrame, 17 * 1024 * 1024);

        using var msOversized = new MemoryStream(oversizedFrame);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await FrameCodec.ReadFrameAsync(msOversized);
        });
    }

    #endregion
}

