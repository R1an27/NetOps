using System.Text;
using System.Text.Json;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Shared.Contracts.Framing;
using Xunit;

namespace NetOps.Tests;

public class NetOpsMessageTests
{
    [Fact]
    public void Khoi_tao_mac_dinh_co_MessageId_GUID_va_Timestamp_hop_le()
    {
        var beforeTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 50;
        var message = new NetOpsMessage();
        var afterTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 50;

        Assert.False(string.IsNullOrWhiteSpace(message.MessageId));
        Assert.Equal(32, message.MessageId.Length); // Guid.NewGuid().ToString("N") có độ dài 32 ký tự
        Assert.InRange(message.Timestamp, beforeTime, afterTime);
        Assert.Null(message.RequestId);
        Assert.Null(message.InReplyTo);
        Assert.Null(message.Payload);
        Assert.Equal(string.Empty, message.MessageType);
    }

    [Fact]
    public void CreateResponse_tu_instance_ke_thua_InReplyTo_va_RequestId()
    {
        var request = new NetOpsMessage
        {
            MessageId = "req-msg-001",
            RequestId = "correlation-trace-123",
            MessageType = MessageType.Ping,
            Payload = "PING_PAYLOAD"
        };

        var response = request.CreateResponse(MessageType.Pong, "PONG_PAYLOAD");

        Assert.Equal(MessageType.Pong, response.MessageType);
        Assert.Equal("req-msg-001", response.InReplyTo);
        Assert.Equal("correlation-trace-123", response.RequestId);
        Assert.Equal("PONG_PAYLOAD", response.Payload);
        Assert.NotEqual(request.MessageId, response.MessageId);
        Assert.Equal(32, response.MessageId.Length);
    }

    [Fact]
    public void CreateResponse_fallback_RequestId_ve_MessageId_khi_request_chua_co_RequestId()
    {
        var request = new NetOpsMessage
        {
            MessageId = "msg-standalone-999",
            RequestId = null,
            MessageType = MessageType.Ping
        };

        var response = request.CreateResponse(MessageType.Pong);

        Assert.Equal("msg-standalone-999", response.InReplyTo);
        Assert.Equal("msg-standalone-999", response.RequestId);
        Assert.Null(response.Payload);
    }

    [Fact]
    public void CreateResponse_static_overload_nem_loi_khi_tham_so_khong_hop_le()
    {
        Assert.Throws<ArgumentNullException>(() => NetOpsMessage.CreateResponse(null!, MessageType.Pong));

        var validRequest = new NetOpsMessage { MessageType = MessageType.Ping };
        Assert.Throws<ArgumentNullException>(() => validRequest.CreateResponse(null!));
        Assert.Throws<ArgumentException>(() => validRequest.CreateResponse(""));
        Assert.Throws<ArgumentException>(() => validRequest.CreateResponse("   "));

        // Static method gọi hợp lệ
        var res = NetOpsMessage.CreateResponse(validRequest, MessageType.Pong, "OK");
        Assert.Equal(MessageType.Pong, res.MessageType);
        Assert.Equal(validRequest.MessageId, res.InReplyTo);
    }

    [Fact]
    public void CreateError_tu_request_dong_goi_dung_chuan_protocol_v0()
    {
        var request = new NetOpsMessage
        {
            MessageId = "msg-client-42",
            RequestId = "req-flow-77",
            MessageType = "UNKNOWN_OP"
        };

        var errorMsg = request.CreateError("INVALID_MESSAGE_TYPE", "MessageType 'UNKNOWN_OP' is not supported", "Check protocol docs");

        Assert.Equal(MessageType.Error, errorMsg.MessageType);
        Assert.Equal("msg-client-42", errorMsg.InReplyTo);
        Assert.Equal("req-flow-77", errorMsg.RequestId);
        Assert.NotNull(errorMsg.Payload);

        using var jsonDoc = JsonDocument.Parse(errorMsg.Payload!);
        var root = jsonDoc.RootElement;
        Assert.Equal("INVALID_MESSAGE_TYPE", root.GetProperty("errorCode").GetString());
        Assert.Equal("MessageType 'UNKNOWN_OP' is not supported", root.GetProperty("message").GetString());
        Assert.Equal("Check protocol docs", root.GetProperty("details").GetString());
    }

    [Fact]
    public void CreateError_khong_co_details_thi_loai_bo_truong_details_khoi_JSON()
    {
        var request = new NetOpsMessage { MessageType = "BAD_REQ" };

        var errorMsg = request.CreateError("BAD_SYNTAX", "Payload could not be parsed");

        Assert.NotNull(errorMsg.Payload);
        Assert.DoesNotContain("details", errorMsg.Payload!);

        using var jsonDoc = JsonDocument.Parse(errorMsg.Payload!);
        Assert.True(jsonDoc.RootElement.TryGetProperty("errorCode", out _));
        Assert.True(jsonDoc.RootElement.TryGetProperty("message", out _));
        Assert.False(jsonDoc.RootElement.TryGetProperty("details", out _));
    }

    [Fact]
    public void CreateError_static_overload_doc_lap_khi_khong_co_request_hop_le()
    {
        var standaloneError = NetOpsMessage.CreateError(
            errorCode: "FRAME_TOO_LARGE",
            message: "Incoming frame exceeds 16 MB limit",
            details: "Length: 20000000",
            requestId: "trace-99",
            inReplyTo: "last-msg-11");

        Assert.Equal(MessageType.Error, standaloneError.MessageType);
        Assert.Equal("trace-99", standaloneError.RequestId);
        Assert.Equal("last-msg-11", standaloneError.InReplyTo);
        Assert.Contains("FRAME_TOO_LARGE", standaloneError.Payload!);

        Assert.Throws<ArgumentNullException>(() => NetOpsMessage.CreateError((NetOpsMessage)null!, "ERR", "msg"));
        Assert.Throws<ArgumentNullException>(() => NetOpsMessage.CreateError((string)null!, "msg"));
        Assert.Throws<ArgumentException>(() => NetOpsMessage.CreateError("", "msg"));
        Assert.Throws<ArgumentException>(() => NetOpsMessage.CreateError("ERR", ""));
    }

    [Fact]
    public void CreateRequest_sinh_ban_tin_yeu_cau_voi_RequestId_chi_dinh_hoac_tu_sinh()
    {
        // 1. Tự sinh RequestId theo MessageId
        var req1 = NetOpsMessage.CreateRequest(MessageType.Ping);
        Assert.Equal(MessageType.Ping, req1.MessageType);
        Assert.Equal(req1.MessageId, req1.RequestId);
        Assert.Null(req1.Payload);

        // 2. Chỉ định RequestId cụ thể
        var req2 = NetOpsMessage.CreateRequest(MessageType.Command, "uptime", "custom-req-007");
        Assert.Equal(MessageType.Command, req2.MessageType);
        Assert.Equal("custom-req-007", req2.RequestId);
        Assert.Equal("uptime", req2.Payload);

        Assert.Throws<ArgumentException>(() => NetOpsMessage.CreateRequest(""));
    }

    [Fact]
    public async Task FrameCodec_Roundtrip_bao_toan_RequestId_va_loai_bo_khi_null()
    {
        // Case 1: Có RequestId -> FrameCodec mã hóa và giải mã bảo toàn RequestId
        var msgWithReqId = new NetOpsMessage
        {
            MessageType = MessageType.Command,
            RequestId = "cor-id-555",
            Payload = "{\"cmd\":\"whoami\"}"
        };

        byte[] frameBytes1 = FrameCodec.EncodeFrame(msgWithReqId);
        string jsonUtf8_1 = Encoding.UTF8.GetString(frameBytes1, FrameCodec.HeaderSize, frameBytes1.Length - FrameCodec.HeaderSize);
        Assert.Contains("\"requestId\":\"cor-id-555\"", jsonUtf8_1);

        using var ms1 = new MemoryStream(frameBytes1);
        var readMsg1 = await FrameCodec.ReadFrameAsync(ms1);
        Assert.NotNull(readMsg1);
        Assert.Equal("cor-id-555", readMsg1!.RequestId);
        Assert.Equal(msgWithReqId.MessageId, readMsg1.MessageId);

        // Case 2: RequestId = null -> Không xuất hiện trong JSON (JsonIgnoreCondition.WhenWritingNull)
        var msgWithoutReqId = new NetOpsMessage
        {
            MessageType = MessageType.Ping,
            RequestId = null
        };

        byte[] frameBytes2 = FrameCodec.EncodeFrame(msgWithoutReqId);
        string jsonUtf8_2 = Encoding.UTF8.GetString(frameBytes2, FrameCodec.HeaderSize, frameBytes2.Length - FrameCodec.HeaderSize);
        Assert.DoesNotContain("\"requestId\"", jsonUtf8_2);

        using var ms2 = new MemoryStream(frameBytes2);
        var readMsg2 = await FrameCodec.ReadFrameAsync(ms2);
        Assert.NotNull(readMsg2);
        Assert.Null(readMsg2!.RequestId);
    }

    [Fact]
    public void Luồng_giao_tiếp_Ping_Pong_va_Error_giua_Agent_va_Master_dong_bo_RequestId()
    {
        // 1. Agent khởi tạo Ping thông qua CreateRequest
        var agentPing = NetOpsMessage.CreateRequest(MessageType.Ping, "{\"agentId\":\"worker-01\"}");
        Assert.NotNull(agentPing.RequestId);
        Assert.Equal(agentPing.MessageId, agentPing.RequestId);

        // 2. Master nhận Ping và phản hồi Pong bằng CreateResponse
        var masterPong = agentPing.CreateResponse(MessageType.Pong, "PONG_ACK");
        Assert.Equal(MessageType.Pong, masterPong.MessageType);
        Assert.Equal(agentPing.MessageId, masterPong.InReplyTo); // Đối chiếu cặp gói 1:1
        Assert.Equal(agentPing.RequestId, masterPong.RequestId); // Đối chiếu luồng tác vụ logic

        // 3. Nếu Agent gửi thông điệp chưa được hỗ trợ, Master phản hồi Error bằng CreateError
        var unknownCommand = NetOpsMessage.CreateRequest("REBOOT_SYSTEM", null, "req-reboot-999");
        var masterError = unknownCommand.CreateError("UNSUPPORTED_MESSAGE_TYPE", "MessageType 'REBOOT_SYSTEM' is not supported");
        Assert.Equal(MessageType.Error, masterError.MessageType);
        Assert.Equal(unknownCommand.MessageId, masterError.InReplyTo);
        Assert.Equal("req-reboot-999", masterError.RequestId);
        Assert.Contains("UNSUPPORTED_MESSAGE_TYPE", masterError.Payload!);
    }

    [Fact]
    public async Task FrameCodec_WriteFrameAsync_voi_SemaphoreSlim_hoat_dong_an_toan()
    {
        var ms = new MemoryStream();
        var writeLock = new SemaphoreSlim(1, 1);
        var msg = NetOpsMessage.CreateRequest(MessageType.Ping, "PAYLOAD_TEST");

        await FrameCodec.WriteFrameAsync(ms, msg, writeLock);

        Assert.Equal(1, writeLock.CurrentCount); // Semaphore phải được release sau khi ghi

        ms.Position = 0;
        var readMsg = await FrameCodec.ReadFrameAsync(ms);
        Assert.NotNull(readMsg);
        Assert.Equal(MessageType.Ping, readMsg!.MessageType);
        Assert.Equal("PAYLOAD_TEST", readMsg.Payload);
    }

    [Fact]
    public async Task SafeFrameStream_Doc_va_ghi_thong_diep_chuan_xac()
    {
        var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);

        var msg1 = NetOpsMessage.CreateRequest(MessageType.Ping, "SAFE_PING");
        var msg2 = msg1.CreateResponse(MessageType.Pong, "SAFE_PONG");

        await safeStream.WriteMessageAsync(msg1);
        await safeStream.WriteMessageAsync(msg2);

        ms.Position = 0;
        var read1 = await safeStream.ReadMessageAsync();
        Assert.NotNull(read1);
        Assert.Equal(MessageType.Ping, read1!.MessageType);
        Assert.Equal("SAFE_PING", read1.Payload);

        var read2 = await safeStream.ReadMessageAsync();
        Assert.NotNull(read2);
        Assert.Equal(MessageType.Pong, read2!.MessageType);
        Assert.Equal("SAFE_PONG", read2.Payload);
        Assert.Equal(msg1.MessageId, read2.InReplyTo);

        var read3 = await safeStream.ReadMessageAsync();
        Assert.Null(read3); // Graceful EOF
    }

    [Fact]
    public async Task SafeFrameStream_Ghi_dong_thoi_nhieu_task_khong_bi_tron_byte()
    {
        var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);

        const int taskCount = 30;
        var tasks = new Task[taskCount];

        for (int i = 0; i < taskCount; i++)
        {
            int index = i;
            tasks[i] = Task.Run(async () =>
            {
                var msg = NetOpsMessage.CreateRequest("TASK_OP", $"Data-{index}", $"req-{index}");
                await safeStream.WriteMessageAsync(msg);
            });
        }

        await Task.WhenAll(tasks);

        // Đọc lại toàn bộ frame từ stream để đảm bảo tất cả frame đều nguyên vẹn
        ms.Position = 0;
        var receivedRequestIds = new HashSet<string>();

        for (int i = 0; i < taskCount; i++)
        {
            var msg = await safeStream.ReadMessageAsync();
            Assert.NotNull(msg);
            Assert.Equal("TASK_OP", msg!.MessageType);
            Assert.NotNull(msg.RequestId);
            receivedRequestIds.Add(msg.RequestId);
        }

        Assert.Equal(taskCount, receivedRequestIds.Count);
        Assert.Null(await safeStream.ReadMessageAsync()); // Hết dữ liệu sạch sẽ
    }

    [Fact]
    public async Task SafeFrameStream_Ghi_raw_frame_thanh_cong()
    {
        var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);

        byte[] rawBytes = [1, 2, 3, 4, 5];
        await safeStream.WriteRawFrameAsync(rawBytes);

        ms.Position = 0;
        var frame = await Shared.Contracts.Framing.FrameReader.ReadAsync(ms);
        Assert.NotNull(frame);
        Assert.Equal(rawBytes, frame!.Value.Payload);
    }

    [Fact]
    public void FrameCodec_DecodeMessage_nem_InvalidDataException_khi_JSON_sai_cu_phap()
    {
        byte[] malformedJson = Encoding.UTF8.GetBytes("{ \"invalidJson\": definitely not json ... }");
        var ex = Assert.Throws<InvalidDataException>(() => FrameCodec.DecodeMessage(malformedJson));
        Assert.Contains("không hợp lệ", ex.Message);
    }

    [Fact]
    public void FrameCodec_DecodeMessage_nem_InvalidDataException_khi_payload_rong()
    {
        Assert.Throws<InvalidDataException>(() => FrameCodec.DecodeMessage(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public async Task DefaultFrameCodec_va_FrameCodec_dong_bo_ma_hoa_va_giai_ma()
    {
        var msg = NetOpsMessage.CreateRequest(MessageType.Heartbeat, "heartbeat-test", "req-hb-999");

        byte[] frame1 = FrameCodec.EncodeFrame(msg);
        byte[] frame2 = DefaultFrameCodec.Instance.EncodeMessage(msg);
        Assert.Equal(frame1, frame2);

        var decoded1 = FrameCodec.DecodeMessage(frame1.AsSpan(FrameCodec.HeaderSize));
        var decoded2 = DefaultFrameCodec.Instance.DecodeMessage(frame2.AsSpan(FrameConstants.LengthPrefixSize));

        Assert.Equal(decoded1.MessageId, decoded2.MessageId);
        Assert.Equal(decoded1.MessageType, decoded2.MessageType);
        Assert.Equal(decoded1.RequestId, decoded2.RequestId);

        // Test Write / Read Roundtrip
        using var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, msg);

        ms.Position = 0;
        var readMsg = await FrameCodec.ReadFrameAsync(ms);
        Assert.NotNull(readMsg);
        Assert.Equal(msg.MessageId, readMsg.MessageId);
    }

    [Fact]
    public async Task DefaultFrameCodec_tich_hop_custom_IFrameReader_va_IFrameWriter()
    {
        var customOptions = new Shared.Contracts.Framing.FramingOptions { MinPayloadSize = 10, MaxPayloadSize = 500 };
        var customWriter = new Shared.Contracts.Framing.DefaultFrameWriter(customOptions);
        var customReader = new Shared.Contracts.Framing.DefaultFrameReader(customOptions);
        var customCodec = new DefaultFrameCodec(customReader, customWriter);

        var msg = NetOpsMessage.CreateRequest(MessageType.Ping, "custom-codec-test");

        using var ms = new MemoryStream();
        await customCodec.WriteMessageAsync(ms, msg);

        ms.Position = 0;
        var readMsg = await customCodec.ReadMessageAsync(ms);
        Assert.NotNull(readMsg);
        Assert.Equal(msg.MessageId, readMsg.MessageId);
    }

    [Fact]
    public void Create_Typed_Generic_Voi_Cac_DTOs_Chuan_Dong_Goi_Chinh_Xac()
    {
        // 1. Tạo message với HeartbeatPayload không truyền requestId (RequestId phải là null)
        var hbPayload = new HeartbeatPayload("agent-beta-01", 1700000000000L);
        var hbMsg = NetOpsMessage.Create(MessageType.Heartbeat, hbPayload);

        Assert.Equal(MessageType.Heartbeat, hbMsg.MessageType);
        Assert.Null(hbMsg.RequestId);
        Assert.Null(hbMsg.InReplyTo);
        Assert.NotNull(hbMsg.Payload);
        Assert.Contains("\"agentId\":\"agent-beta-01\"", hbMsg.Payload!);
        Assert.Contains("\"timestamp\":1700000000000", hbMsg.Payload!);

        var parsedHb = hbMsg.GetPayload<HeartbeatPayload>();
        Assert.NotNull(parsedHb);
        Assert.Equal("agent-beta-01", parsedHb!.AgentId);
        Assert.Equal(1700000000000L, parsedHb.Timestamp);

        // 2. Tạo message với CommandRequestPayload có truyền requestId cụ thể
        var cmdPayload = new CommandRequestPayload("cmd-uuid-007", "dir /b", 45, "C:\\Temp");
        var cmdMsg = NetOpsMessage.Create(MessageType.Command, cmdPayload, "req-cor-999");

        Assert.Equal(MessageType.Command, cmdMsg.MessageType);
        Assert.Equal("req-cor-999", cmdMsg.RequestId);
        Assert.NotNull(cmdMsg.Payload);
        Assert.Contains("\"commandId\":\"cmd-uuid-007\"", cmdMsg.Payload!);
        Assert.Contains("\"workingDir\":\"C:\\\\Temp\"", cmdMsg.Payload!);

        var parsedCmd = cmdMsg.GetPayload<CommandRequestPayload>();
        Assert.NotNull(parsedCmd);
        Assert.Equal("cmd-uuid-007", parsedCmd!.CommandId);
        Assert.Equal("dir /b", parsedCmd.CommandLine);
        Assert.Equal(45, parsedCmd.TimeoutSeconds);
        Assert.Equal("C:\\Temp", parsedCmd.WorkingDir);
    }

    [Fact]
    public void Create_NonGeneric_Voi_Chuoi_Raw_Hoac_Null_Dong_Goi_Chinh_Xac()
    {
        // 1. Tạo message với raw string và không truyền requestId
        var rawMsg = NetOpsMessage.Create(MessageType.Ping, "raw_ping_content");
        Assert.Equal(MessageType.Ping, rawMsg.MessageType);
        Assert.Equal("raw_ping_content", rawMsg.Payload);
        Assert.Null(rawMsg.RequestId);

        // 2. Tạo message với null payload và có truyền requestId
        var emptyMsg = NetOpsMessage.Create(MessageType.Ping, (string?)null, "req-standalone-01");
        Assert.Equal(MessageType.Ping, emptyMsg.MessageType);
        Assert.Null(emptyMsg.Payload);
        Assert.Equal("req-standalone-01", emptyMsg.RequestId);

        // 3. Generic Create<T> với null payload
        var genericNullMsg = NetOpsMessage.Create<HeartbeatPayload>(MessageType.Heartbeat, null, "req-hb-null");
        Assert.Equal(MessageType.Heartbeat, genericNullMsg.MessageType);
        Assert.Null(genericNullMsg.Payload);
        Assert.Equal("req-hb-null", genericNullMsg.RequestId);
    }

    [Fact]
    public void Create_Nem_Ngoai_Le_Khi_MessageType_Null_Hoac_Whitespace()
    {
        // Non-generic Create
        Assert.Throws<ArgumentNullException>(() => NetOpsMessage.Create(null!));
        Assert.Throws<ArgumentException>(() => NetOpsMessage.Create(""));
        Assert.Throws<ArgumentException>(() => NetOpsMessage.Create("   "));

        // Generic Create<T>
        Assert.Throws<ArgumentNullException>(() => NetOpsMessage.Create<HeartbeatPayload>(null!, new HeartbeatPayload("a", 1)));
        Assert.Throws<ArgumentException>(() => NetOpsMessage.Create<HeartbeatPayload>("", new HeartbeatPayload("a", 1)));
        Assert.Throws<ArgumentException>(() => NetOpsMessage.Create<HeartbeatPayload>("   ", new HeartbeatPayload("a", 1)));
    }

    [Fact]
    public void GetPayload_Ho_Tro_PropertyNameCaseInsensitive()
    {
        // Giả lập message chứa JSON thuộc tính PascalCase thay vì camelCase chuẩn
        var pascalJson = "{\"AgentId\":\"worker-pascal-99\",\"Timestamp\":1712345678900}";
        var msg = new NetOpsMessage
        {
            MessageType = MessageType.Heartbeat,
            Payload = pascalJson
        };

        var parsed = msg.GetPayload<HeartbeatPayload>();
        Assert.NotNull(parsed);
        Assert.Equal("worker-pascal-99", parsed!.AgentId);
        Assert.Equal(1712345678900L, parsed.Timestamp);
    }

    [Fact]
    public void GetPayload_Ho_Tro_String_An_Toan_Voi_Ca_Raw_Text_Va_Quoted_Json()
    {
        // 1. Raw plain text (không có nháy kép bao quanh)
        var rawMsg = new NetOpsMessage
        {
            MessageType = MessageType.Ping,
            Payload = "PING_PLAIN_TEXT_123"
        };
        var strRaw = rawMsg.GetPayload<string>();
        Assert.Equal("PING_PLAIN_TEXT_123", strRaw);

        // 2. Quoted JSON string
        var quotedMsg = new NetOpsMessage
        {
            MessageType = MessageType.Ping,
            Payload = "\"PING_QUOTED_TEXT_456\""
        };
        var strQuoted = quotedMsg.GetPayload<string>();
        Assert.Equal("PING_QUOTED_TEXT_456", strQuoted);

        // 3. Null hoặc whitespace payload
        var nullMsg = new NetOpsMessage { MessageType = MessageType.Ping, Payload = null };
        Assert.Null(nullMsg.GetPayload<string>());

        var emptyMsg = new NetOpsMessage { MessageType = MessageType.Ping, Payload = "" };
        Assert.Null(emptyMsg.GetPayload<string>());
    }

    [Fact]
    public void TryGetPayload_Tra_Ve_True_Khi_Hop_Le_Va_False_Khi_Loi_Hoac_Rong()
    {
        // 1. JSON hợp lệ
        var validMsg = NetOpsMessage.Create(MessageType.Heartbeat, new HeartbeatPayload("agent-valid", 99999L));
        var validSuccess = validMsg.TryGetPayload<HeartbeatPayload>(out var validHb);
        Assert.True(validSuccess);
        Assert.NotNull(validHb);
        Assert.Equal("agent-valid", validHb!.AgentId);

        // 2. JSON hỏng (malformed syntax) -> TryGetPayload không ném exception mà trả về false
        var brokenMsg = new NetOpsMessage
        {
            MessageType = MessageType.Heartbeat,
            Payload = "{ \"agentId\": \"broken\", timestamp: INVALID_JSON }"
        };
        var brokenSuccess = brokenMsg.TryGetPayload<HeartbeatPayload>(out var brokenHb);
        Assert.False(brokenSuccess);
        Assert.Null(brokenHb);

        // 3. Payload là null / rỗng
        var nullMsg = new NetOpsMessage { MessageType = MessageType.Heartbeat, Payload = null };
        var nullSuccess = nullMsg.TryGetPayload<HeartbeatPayload>(out var nullHb);
        Assert.False(nullSuccess);
        Assert.Null(nullHb);

        var emptyMsg = new NetOpsMessage { MessageType = MessageType.Heartbeat, Payload = "   " };
        var emptySuccess = emptyMsg.TryGetPayload<HeartbeatPayload>(out var emptyHb);
        Assert.False(emptySuccess);
        Assert.Null(emptyHb);
    }

    [Fact]
    public void SetPayload_Va_GetPayload_Voi_Custom_JsonSerializerOptions()
    {
        var msg = new NetOpsMessage { MessageType = MessageType.Metrics };
        var customOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var metrics = new MetricsPayload("worker-custom", 25.5, 8192, 4096, 500000, 200000, 3600, "srv-01", "Ubuntu 24.04");
        msg.SetPayload(metrics, customOptions);

        Assert.NotNull(msg.Payload);
        Assert.Contains("\"cpuUsagePercent\":25.5", msg.Payload!);

        var parsed = msg.GetPayload<MetricsPayload>(customOptions);
        Assert.NotNull(parsed);
        Assert.Equal("worker-custom", parsed!.AgentId);
        Assert.Equal(25.5, parsed.CpuUsagePercent);
    }
}
