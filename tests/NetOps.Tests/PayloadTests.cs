using System.Text.Json;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

public class PayloadTests
{
    [Fact]
    public void Heartbeat_va_HeartbeatAck_roundtrip_chuan_xac()
    {
        var heartbeat = new HeartbeatPayload("agent-007", 1728500000000);
        var request = NetOpsMessage.CreateRequest(MessageType.Heartbeat, heartbeat, "req-hb-1");

        Assert.Equal(MessageType.Heartbeat, request.MessageType);
        Assert.Equal("req-hb-1", request.RequestId);
        Assert.NotNull(request.Payload);
        Assert.Contains("\"agentId\":\"agent-007\"", request.Payload);
        Assert.Contains("\"timestamp\":1728500000000", request.Payload);

        var parsedHeartbeat = request.GetPayload<HeartbeatPayload>();
        Assert.NotNull(parsedHeartbeat);
        Assert.Equal("agent-007", parsedHeartbeat.AgentId);
        Assert.Equal(1728500000000, parsedHeartbeat.Timestamp);

        // Manager phản hồi HeartbeatAck
        var ackPayload = new HeartbeatAckPayload(1728500000000, true);
        var response = request.CreateResponse(MessageType.HeartbeatAck, ackPayload);

        Assert.Equal(MessageType.HeartbeatAck, response.MessageType);
        Assert.Equal(request.MessageId, response.InReplyTo);
        Assert.Equal("req-hb-1", response.RequestId);

        var parsedAck = response.GetPayload<HeartbeatAckPayload>();
        Assert.NotNull(parsedAck);
        Assert.Equal(1728500000000, parsedAck.Timestamp);
        Assert.True(parsedAck.Acknowledged);
    }

    [Fact]
    public void Auth_va_AuthOk_AuthFailed_dong_goi_chuan_spec()
    {
        // 1. Auth Payload đầy đủ
        var auth = new AuthPayload("agent-linux-01", "secret-token-xyz", "Linux 6.8", "ubuntu-node");
        var authMsg = NetOpsMessage.CreateRequest(MessageType.Auth, auth);

        var readAuth = authMsg.GetPayload<AuthPayload>();
        Assert.NotNull(readAuth);
        Assert.Equal("agent-linux-01", readAuth.AgentId);
        Assert.Equal("secret-token-xyz", readAuth.Token);
        Assert.Equal("Linux 6.8", readAuth.OsInfo);
        Assert.Equal("ubuntu-node", readAuth.Hostname);

        // 2. Auth Payload không có trường tùy chọn (null) -> Không xuất hiện trong JSON
        var minimalAuth = new AuthPayload("agent-minimal", "token-123");
        var minAuthMsg = NetOpsMessage.CreateRequest(MessageType.Auth, minimalAuth);
        Assert.DoesNotContain("\"osInfo\"", minAuthMsg.Payload!);
        Assert.DoesNotContain("\"hostname\"", minAuthMsg.Payload!);

        // 3. AuthOk
        var okMsg = authMsg.CreateResponse(MessageType.AuthOk, new AuthOkPayload("sess-999"));
        var readOk = okMsg.GetPayload<AuthOkPayload>();
        Assert.NotNull(readOk);
        Assert.Equal("sess-999", readOk.SessionId);
        Assert.Equal("Authenticated successfully", readOk.Message);

        // 4. AuthFailed
        var failedMsg = authMsg.CreateResponse(MessageType.AuthFailed, new AuthFailedPayload("AUTH_INVALID_TOKEN", "Bad key"));
        var readFailed = failedMsg.GetPayload<AuthFailedPayload>();
        Assert.NotNull(readFailed);
        Assert.Equal("AUTH_INVALID_TOKEN", readFailed.ErrorCode);
        Assert.Equal("Bad key", readFailed.Message);
    }

    [Fact]
    public async Task MetricsPayload_so_lieu_phan_cung_serialize_va_roundtrip_qua_frame_stream()
    {
        var metrics = new MetricsPayload
        {
            AgentId = "agent-linux-01",
            CpuUsagePercent = 14.8,
            MemoryTotalMb = 16384,
            MemoryUsedMb = 4096,
            DiskTotalMb = 256000,
            DiskUsedMb = 51200,
            UptimeSeconds = 86400,
            Hostname = "ubuntu-vps-01",
            OsVersion = "Linux 6.8.0-generic"
        };

        var message = NetOpsMessage.CreateRequest(MessageType.Metrics, metrics);

        // Kiểm tra roundtrip qua SafeFrameStream
        var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);
        await safeStream.WriteMessageAsync(message);

        ms.Position = 0;
        var receivedMsg = await safeStream.ReadMessageAsync();
        Assert.NotNull(receivedMsg);
        Assert.Equal(MessageType.Metrics, receivedMsg.MessageType);

        var readMetrics = receivedMsg.GetPayload<MetricsPayload>();
        Assert.NotNull(readMetrics);
        Assert.Equal("agent-linux-01", readMetrics.AgentId);
        Assert.Equal(14.8, readMetrics.CpuUsagePercent);
        Assert.Equal(16384, readMetrics.MemoryTotalMb);
        Assert.Equal(4096, readMetrics.MemoryUsedMb);
        Assert.Equal(256000, readMetrics.DiskTotalMb);
        Assert.Equal(51200, readMetrics.DiskUsedMb);
        Assert.Equal(86400, readMetrics.UptimeSeconds);
        Assert.Equal("ubuntu-vps-01", readMetrics.Hostname);
        Assert.Equal("Linux 6.8.0-generic", readMetrics.OsVersion);
    }

    [Fact]
    public void Command_chuoi_thuc_thi_dong_goi_dung_cac_loai_payload()
    {
        // 1. COMMAND
        var cmd = new CommandPayload("cmd-001", "uname -a", 45, "/tmp");
        var cmdMsg = NetOpsMessage.CreateRequest(MessageType.Command, cmd, "req-cmd-001");

        var readCmd = cmdMsg.GetPayload<CommandPayload>();
        Assert.NotNull(readCmd);
        Assert.Equal("cmd-001", readCmd.CommandId);
        Assert.Equal("uname -a", readCmd.CommandLine);
        Assert.Equal(45, readCmd.TimeoutSeconds);
        Assert.Equal("/tmp", readCmd.WorkingDir);

        // 2. COMMAND_OUTPUT
        var outputPayload = new CommandOutputPayload("cmd-001", "Linux ubuntu 6.8.0\n", false);
        var outputMsg = cmdMsg.CreateResponse(MessageType.CommandOutput, outputPayload);

        var readOutput = outputMsg.GetPayload<CommandOutputPayload>();
        Assert.NotNull(readOutput);
        Assert.Equal("cmd-001", readOutput.CommandId);
        Assert.Equal("Linux ubuntu 6.8.0\n", readOutput.Output);
        Assert.False(readOutput.IsError);

        // 3. COMMAND_DONE
        var donePayload = new CommandDonePayload("cmd-001", 0, 125);
        var doneMsg = cmdMsg.CreateResponse(MessageType.CommandDone, donePayload);

        var readDone = doneMsg.GetPayload<CommandDonePayload>();
        Assert.NotNull(readDone);
        Assert.Equal("cmd-001", readDone.CommandId);
        Assert.Equal(0, readDone.ExitCode);
        Assert.Equal(125, readDone.TotalExecutionTimeMs);
    }

    [Fact]
    public void ErrorPayload_tuong_thich_giua_CreateError_va_GetPayload()
    {
        var req = NetOpsMessage.CreateRequest("BAD_COMMAND");
        var errorMsg = req.CreateError("INVALID_MESSAGE_TYPE", "MessageType 'BAD_COMMAND' is not supported", "Check documentation");

        var errPayload = errorMsg.GetPayload<ErrorPayload>();
        Assert.NotNull(errPayload);
        Assert.Equal("INVALID_MESSAGE_TYPE", errPayload.ErrorCode);
        Assert.Equal("MessageType 'BAD_COMMAND' is not supported", errPayload.Message);
        Assert.Equal("Check documentation", errPayload.Details);

        // Khi details là null, trường details được bỏ qua khỏi JSON
        var simpleError = req.CreateError("TIMEOUT", "Request timed out");
        Assert.DoesNotContain("details", simpleError.Payload!);

        var readSimpleErr = simpleError.GetPayload<ErrorPayload>();
        Assert.NotNull(readSimpleErr);
        Assert.Equal("TIMEOUT", readSimpleErr.ErrorCode);
        Assert.Null(readSimpleErr.Details);
    }

    [Fact]
    public void GetPayload_tra_ve_default_khi_Payload_null_hoac_trang()
    {
        var msgNull = new NetOpsMessage { Payload = null };
        Assert.Null(msgNull.GetPayload<HeartbeatPayload>());

        var msgEmpty = new NetOpsMessage { Payload = "" };
        Assert.Null(msgEmpty.GetPayload<HeartbeatPayload>());

        var msgWhitespace = new NetOpsMessage { Payload = "    " };
        Assert.Null(msgWhitespace.GetPayload<HeartbeatPayload>());
    }

    [Fact]
    public void GetPayload_nem_loi_JsonException_khi_chuoi_JSON_khong_hop_le()
    {
        var msgBadJson = new NetOpsMessage { Payload = "{ this is not valid json }" };
        Assert.Throws<JsonException>(() => msgBadJson.GetPayload<HeartbeatPayload>());
    }

    [Fact]
    public void SetPayload_cap_nhat_payload_hoac_set_null_dung_chuan()
    {
        var msg = new NetOpsMessage { MessageType = MessageType.Ping };

        msg.SetPayload(new HeartbeatPayload("worker-99", 5000));
        Assert.NotNull(msg.Payload);
        Assert.Contains("\"agentId\":\"worker-99\"", msg.Payload);

        msg.SetPayload<HeartbeatPayload>(null);
        Assert.Null(msg.Payload);
    }

    [Fact]
    public void Static_overloads_CreateResponse_va_CreateRequest_voi_Typed_Payload()
    {
        var req = NetOpsMessage.CreateRequest(MessageType.Command, new CommandPayload("cmd-1", "ls"), "req-ls-1");
        Assert.Equal("req-ls-1", req.RequestId);

        var res = NetOpsMessage.CreateResponse(req, MessageType.CommandDone, new CommandDonePayload("cmd-1", 0, 50));
        Assert.Equal(req.MessageId, res.InReplyTo);
        Assert.Equal("req-ls-1", res.RequestId);

        var done = res.GetPayload<CommandDonePayload>();
        Assert.NotNull(done);
        Assert.Equal(0, done.ExitCode);
    }
}
