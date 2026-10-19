using System.Text.Json;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử đơn vị chuyên sâu cho hệ thống 9 Data Transfer Objects (DTOs) theo chuẩn NetOps Protocol v0:
/// 1. AuthRequestPayload
/// 2. AuthResponsePayload
/// 3. HeartbeatPayload
/// 4. HeartbeatAckPayload
/// 5. MetricsPayload
/// 6. CommandRequestPayload
/// 7. CommandOutputPayload
/// 8. CommandDonePayload
/// 9. ErrorMessagePayload
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Protocol")]
public class DtoPayloadTests
{
    #region 1. AuthRequestPayload Tests

    [Fact]
    public void AuthRequestPayload_Serialization_RoundtripAndCamelCaseAndNullIgnoring()
    {
        // 1. Đầy đủ tham số
        var payload = new AuthRequestPayload("agent-node-01", "secret-token-123", "Ubuntu 24.04 LTS", "srv-linux");
        string json = JsonSerializer.Serialize(payload);

        Assert.Contains("\"agentId\":\"agent-node-01\"", json);
        Assert.Contains("\"token\":\"secret-token-123\"", json);
        Assert.Contains("\"osInfo\":\"Ubuntu 24.04 LTS\"", json);
        Assert.Contains("\"hostname\":\"srv-linux\"", json);

        var deserialized = JsonSerializer.Deserialize<AuthRequestPayload>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("agent-node-01", deserialized.AgentId);
        Assert.Equal("secret-token-123", deserialized.Token);
        Assert.Equal("Ubuntu 24.04 LTS", deserialized.OsInfo);
        Assert.Equal("srv-linux", deserialized.Hostname);

        // 2. Không có các trường tùy chọn (null) -> Không xuất hiện trong JSON
        var minimal = new AuthRequestPayload("agent-node-02", "tok-456");
        string minJson = JsonSerializer.Serialize(minimal);
        Assert.DoesNotContain("\"osInfo\"", minJson);
        Assert.DoesNotContain("\"hostname\"", minJson);

        var minDeserialized = JsonSerializer.Deserialize<AuthRequestPayload>(minJson);
        Assert.NotNull(minDeserialized);
        Assert.Null(minDeserialized.OsInfo);
        Assert.Null(minDeserialized.Hostname);

        // 3. Tương thích ngược: AuthPayload kế thừa từ AuthRequestPayload
        AuthRequestPayload compat = new AuthPayload("agent-compat", "tok-compat", "Debian", "node-deb");
        Assert.IsAssignableFrom<AuthRequestPayload>(compat);
        Assert.Equal("agent-compat", compat.AgentId);
    }

    #endregion

    #region 2. AuthResponsePayload Tests

    [Fact]
    public void AuthResponsePayload_SuccessAndFailed_RoundtripAndFactoryMethods()
    {
        // 1. Kịch bản thành công qua factory helper Ok()
        var okPayload = AuthResponsePayload.Ok("sess-uuid-777", "Welcome to Master");
        Assert.True(okPayload.Success);
        Assert.Equal("sess-uuid-777", okPayload.SessionId);
        Assert.Equal("Welcome to Master", okPayload.Message);
        Assert.Null(okPayload.ErrorCode);

        string okJson = JsonSerializer.Serialize(okPayload);
        Assert.Contains("\"success\":true", okJson);
        Assert.Contains("\"sessionId\":\"sess-uuid-777\"", okJson);
        Assert.DoesNotContain("\"errorCode\"", okJson);

        var readOk = JsonSerializer.Deserialize<AuthResponsePayload>(okJson);
        Assert.NotNull(readOk);
        Assert.True(readOk.Success);
        Assert.Equal("sess-uuid-777", readOk.SessionId);
        Assert.Null(readOk.ErrorCode);

        // 2. Kịch bản thất bại qua factory helper Failed()
        var failedPayload = AuthResponsePayload.Failed("AUTH_INVALID_TOKEN", "Mã token không hợp lệ");
        Assert.False(failedPayload.Success);
        Assert.Equal("AUTH_INVALID_TOKEN", failedPayload.ErrorCode);
        Assert.Equal("Mã token không hợp lệ", failedPayload.Message);
        Assert.Null(failedPayload.SessionId);

        string failedJson = JsonSerializer.Serialize(failedPayload);
        Assert.Contains("\"success\":false", failedJson);
        Assert.Contains("\"errorCode\":\"AUTH_INVALID_TOKEN\"", failedJson);
        Assert.DoesNotContain("\"sessionId\"", failedJson);

        var readFailed = JsonSerializer.Deserialize<AuthResponsePayload>(failedJson);
        Assert.NotNull(readFailed);
        Assert.False(readFailed.Success);
        Assert.Equal("AUTH_INVALID_TOKEN", readFailed.ErrorCode);
        Assert.Null(readFailed.SessionId);

        // 3. Tương thích ngược: AuthOkPayload và AuthFailedPayload kế thừa từ AuthResponsePayload
        AuthResponsePayload okSubclass = new AuthOkPayload("sess-legacy", "Legacy OK");
        Assert.True(okSubclass.Success);
        Assert.Equal("sess-legacy", okSubclass.SessionId);

        AuthResponsePayload failSubclass = new AuthFailedPayload("LEGACY_ERR", "Legacy Error");
        Assert.False(failSubclass.Success);
        Assert.Equal("LEGACY_ERR", failSubclass.ErrorCode);
    }

    #endregion

    #region 3. HeartbeatPayload Tests

    [Fact]
    public void HeartbeatPayload_AutoTimestampAndCustomTimestamp_Roundtrip()
    {
        // 1. Tự sinh timestamp
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var autoHb = new HeartbeatPayload("agent-hb-01");
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Equal("agent-hb-01", autoHb.AgentId);
        Assert.True(autoHb.Timestamp >= before && autoHb.Timestamp <= after);

        // 2. Custom timestamp cố định
        const long fixedTime = 1728500000000;
        var fixedHb = new HeartbeatPayload("agent-hb-02", fixedTime);
        Assert.Equal(fixedTime, fixedHb.Timestamp);

        string json = JsonSerializer.Serialize(fixedHb);
        Assert.Contains("\"agentId\":\"agent-hb-02\"", json);
        Assert.Contains("\"timestamp\":1728500000000", json);

        var deserialized = JsonSerializer.Deserialize<HeartbeatPayload>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("agent-hb-02", deserialized.AgentId);
        Assert.Equal(fixedTime, deserialized.Timestamp);
    }

    #endregion

    #region 4. HeartbeatAckPayload Tests

    [Fact]
    public void HeartbeatAckPayload_SuccessFactoryAndRoundtrip()
    {
        const long ackTime = 1728500001000;
        var ack = HeartbeatAckPayload.Success(ackTime);

        Assert.True(ack.Acknowledged);
        Assert.Equal(ackTime, ack.Timestamp);

        string json = JsonSerializer.Serialize(ack);
        Assert.Contains("\"acknowledged\":true", json);
        Assert.Contains("\"timestamp\":1728500001000", json);

        var deserialized = JsonSerializer.Deserialize<HeartbeatAckPayload>(json);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Acknowledged);
        Assert.Equal(ackTime, deserialized.Timestamp);
    }

    #endregion

    #region 5. MetricsPayload Tests

    [Fact]
    public void MetricsPayload_AllNineFields_AndCalculatedProperties_Roundtrip()
    {
        var metrics = new MetricsPayload(
            agentId: "agent-metrics-01",
            cpuUsagePercent: 42.5,
            memoryTotalMb: 16384,
            memoryUsedMb: 8192,
            diskTotalMb: 500000,
            diskUsedMb: 250000,
            uptimeSeconds: 86400,
            hostname: "ubuntu-server",
            osVersion: "Linux 6.8.0-generic"
        );

        // 1. Kiểm tra 9 trường chính
        Assert.Equal("agent-metrics-01", metrics.AgentId);
        Assert.Equal(42.5, metrics.CpuUsagePercent);
        Assert.Equal(16384, metrics.MemoryTotalMb);
        Assert.Equal(8192, metrics.MemoryUsedMb);
        Assert.Equal(500000, metrics.DiskTotalMb);
        Assert.Equal(250000, metrics.DiskUsedMb);
        Assert.Equal(86400, metrics.UptimeSeconds);
        Assert.Equal("ubuntu-server", metrics.Hostname);
        Assert.Equal("Linux 6.8.0-generic", metrics.OsVersion);

        // 2. Kiểm tra thuộc tính tính toán tiện ích
        Assert.Equal(50.0, metrics.MemoryUsagePercent);
        Assert.Equal(50.0, metrics.DiskUsagePercent);

        // 3. Serialize và xác nhận các thuộc tính tính toán không xuất hiện trong JSON
        string json = JsonSerializer.Serialize(metrics);
        Assert.Contains("\"agentId\":\"agent-metrics-01\"", json);
        Assert.Contains("\"cpuUsagePercent\":42.5", json);
        Assert.Contains("\"memoryTotalMb\":16384", json);
        Assert.Contains("\"memoryUsedMb\":8192", json);
        Assert.Contains("\"diskTotalMb\":500000", json);
        Assert.Contains("\"diskUsedMb\":250000", json);
        Assert.Contains("\"uptimeSeconds\":86400", json);
        Assert.Contains("\"hostname\":\"ubuntu-server\"", json);
        Assert.Contains("\"osVersion\":\"Linux 6.8.0-generic\"", json);
        Assert.DoesNotContain("\"memoryUsagePercent\"", json);
        Assert.DoesNotContain("\"diskUsagePercent\"", json);

        // 4. Deserialize roundtrip
        var deserialized = JsonSerializer.Deserialize<MetricsPayload>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(metrics.AgentId, deserialized.AgentId);
        Assert.Equal(metrics.CpuUsagePercent, deserialized.CpuUsagePercent);
        Assert.Equal(metrics.MemoryTotalMb, deserialized.MemoryTotalMb);
        Assert.Equal(metrics.MemoryUsedMb, deserialized.MemoryUsedMb);
        Assert.Equal(metrics.DiskTotalMb, deserialized.DiskTotalMb);
        Assert.Equal(metrics.DiskUsedMb, deserialized.DiskUsedMb);
        Assert.Equal(metrics.UptimeSeconds, deserialized.UptimeSeconds);
        Assert.Equal(metrics.Hostname, deserialized.Hostname);
        Assert.Equal(metrics.OsVersion, deserialized.OsVersion);
    }

    #endregion

    #region 6. CommandRequestPayload Tests

    [Fact]
    public void CommandRequestPayload_CommandLineAndOptionalWorkingDir_Roundtrip()
    {
        // 1. Đầy đủ tham số bao gồm workingDir
        var req = new CommandRequestPayload("cmd-uuid-001", "systemctl status nginx", 45, "/etc/nginx");
        string json = JsonSerializer.Serialize(req);

        Assert.Contains("\"commandId\":\"cmd-uuid-001\"", json);
        Assert.Contains("\"commandLine\":\"systemctl status nginx\"", json);
        Assert.Contains("\"timeoutSeconds\":45", json);
        Assert.Contains("\"workingDir\":\"/etc/nginx\"", json);

        var deserialized = JsonSerializer.Deserialize<CommandRequestPayload>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("cmd-uuid-001", deserialized.CommandId);
        Assert.Equal("systemctl status nginx", deserialized.CommandLine);
        Assert.Equal(45, deserialized.TimeoutSeconds);
        Assert.Equal("/etc/nginx", deserialized.WorkingDir);

        // 2. Không có workingDir (null) -> Bỏ qua trong JSON
        var minimal = new CommandRequestPayload("cmd-uuid-002", "whoami");
        string minJson = JsonSerializer.Serialize(minimal);
        Assert.DoesNotContain("\"workingDir\"", minJson);
        Assert.Contains("\"timeoutSeconds\":30", minJson); // Mặc định 30s

        // 3. Tương thích ngược: CommandPayload kế thừa CommandRequestPayload
        CommandRequestPayload legacy = new CommandPayload("cmd-leg-01", "dir /s", 15);
        Assert.IsAssignableFrom<CommandRequestPayload>(legacy);
        Assert.Equal("cmd-leg-01", legacy.CommandId);
        Assert.Equal("dir /s", legacy.CommandLine);
    }

    #endregion

    #region 7. CommandOutputPayload Tests

    [Fact]
    public void CommandOutputPayload_StdoutAndStderr_Roundtrip()
    {
        // 1. Stdout
        var stdout = new CommandOutputPayload("cmd-out-001", "nginx is running OK\n", false);
        string stdoutJson = JsonSerializer.Serialize(stdout);
        Assert.Contains("\"commandId\":\"cmd-out-001\"", stdoutJson);
        Assert.Contains("\"output\":\"nginx is running OK\\n\"", stdoutJson);
        Assert.Contains("\"isError\":false", stdoutJson);

        var readStdout = JsonSerializer.Deserialize<CommandOutputPayload>(stdoutJson);
        Assert.NotNull(readStdout);
        Assert.False(readStdout.IsError);
        Assert.Equal("nginx is running OK\n", readStdout.Output);

        // 2. Stderr
        var stderr = new CommandOutputPayload("cmd-out-001", "nginx: configuration syntax error\n", true);
        string stderrJson = JsonSerializer.Serialize(stderr);
        Assert.Contains("\"isError\":true", stderrJson);

        var readStderr = JsonSerializer.Deserialize<CommandOutputPayload>(stderrJson);
        Assert.NotNull(readStderr);
        Assert.True(readStderr.IsError);
    }

    #endregion

    #region 8. CommandDonePayload Tests

    [Fact]
    public void CommandDonePayload_ExitCodeAndSuccessHelper_Roundtrip()
    {
        // 1. Thành công: ExitCode = 0 -> Success == true
        var doneSuccess = new CommandDonePayload("cmd-done-01", 0, 125);
        Assert.True(doneSuccess.Success);
        Assert.Equal(0, doneSuccess.ExitCode);
        Assert.Equal(125, doneSuccess.TotalExecutionTimeMs);

        string successJson = JsonSerializer.Serialize(doneSuccess);
        Assert.Contains("\"commandId\":\"cmd-done-01\"", successJson);
        Assert.Contains("\"exitCode\":0", successJson);
        Assert.Contains("\"totalExecutionTimeMs\":125", successJson);
        Assert.DoesNotContain("\"success\"", successJson); // [JsonIgnore]

        var readSuccess = JsonSerializer.Deserialize<CommandDonePayload>(successJson);
        Assert.NotNull(readSuccess);
        Assert.True(readSuccess.Success);
        Assert.Equal(0, readSuccess.ExitCode);

        // 2. Thất bại: ExitCode != 0 -> Success == false
        var doneFail = new CommandDonePayload("cmd-done-02", 127, 45);
        Assert.False(doneFail.Success);
        Assert.Equal(127, doneFail.ExitCode);

        string failJson = JsonSerializer.Serialize(doneFail);
        var readFail = JsonSerializer.Deserialize<CommandDonePayload>(failJson);
        Assert.NotNull(readFail);
        Assert.False(readFail.Success);
        Assert.Equal(127, readFail.ExitCode);
    }

    #endregion

    #region 9. ErrorMessagePayload Tests

    [Fact]
    public void ErrorMessagePayload_ErrorCodeMessageAndDetails_Roundtrip()
    {
        // 1. Đầy đủ details
        var error = new ErrorMessagePayload("UNSUPPORTED_TYPE", "Loại thông điệp không hỗ trợ", "Endpoint: 127.0.0.1:5050");
        string json = JsonSerializer.Serialize(error);

        Assert.Contains("\"errorCode\":\"UNSUPPORTED_TYPE\"", json);
        Assert.Contains("\"message\":", json);
        Assert.Contains("\"details\":\"Endpoint: 127.0.0.1:5050\"", json);

        var deserialized = JsonSerializer.Deserialize<ErrorMessagePayload>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("UNSUPPORTED_TYPE", deserialized.ErrorCode);
        Assert.Equal("Loại thông điệp không hỗ trợ", deserialized.Message);
        Assert.Equal("Endpoint: 127.0.0.1:5050", deserialized.Details);

        // 2. Không có details (null) -> Bỏ qua trong JSON
        var minimal = new ErrorMessagePayload("TIMEOUT", "Quá thời gian phản hồi");
        string minJson = JsonSerializer.Serialize(minimal);
        Assert.DoesNotContain("\"details\"", minJson);

        // 3. Tương thích ngược: ErrorPayload kế thừa ErrorMessagePayload
        ErrorMessagePayload legacy = new ErrorPayload("LEGACY_ERR", "Legacy Error Message");
        Assert.IsAssignableFrom<ErrorMessagePayload>(legacy);
        Assert.Equal("LEGACY_ERR", legacy.ErrorCode);
    }

    #endregion

    #region 10. NetOpsMessage Envelope Integration Tests (All 9 DTOs)

    [Fact]
    public void NetOpsMessage_Integration_AllNineDtos_ViaSetPayloadAndGetPayload()
    {
        // 1. AuthRequestPayload
        var authReq = new AuthRequestPayload("agent-integ", "tok-abc");
        var msgAuth = NetOpsMessage.CreateRequest(MessageType.Auth, authReq);
        var readAuth = msgAuth.GetPayload<AuthRequestPayload>();
        Assert.NotNull(readAuth);
        Assert.Equal("agent-integ", readAuth.AgentId);

        // 2. AuthResponsePayload
        var authRes = AuthResponsePayload.Ok("sess-integ");
        var msgAuthRes = msgAuth.CreateResponse(MessageType.AuthOk, authRes);
        var readAuthRes = msgAuthRes.GetPayload<AuthResponsePayload>();
        Assert.NotNull(readAuthRes);
        Assert.True(readAuthRes.Success);
        Assert.Equal("sess-integ", readAuthRes.SessionId);

        // 3. HeartbeatPayload
        var hb = new HeartbeatPayload("agent-integ", 1700000000000);
        var msgHb = NetOpsMessage.CreateRequest(MessageType.Heartbeat, hb);
        var readHb = msgHb.GetPayload<HeartbeatPayload>();
        Assert.NotNull(readHb);
        Assert.Equal(1700000000000, readHb.Timestamp);

        // 4. HeartbeatAckPayload
        var ack = HeartbeatAckPayload.Success(1700000000100);
        var msgAck = msgHb.CreateResponse(MessageType.HeartbeatAck, ack);
        var readAck = msgAck.GetPayload<HeartbeatAckPayload>();
        Assert.NotNull(readAck);
        Assert.True(readAck.Acknowledged);

        // 5. MetricsPayload
        var metrics = new MetricsPayload("agent-integ", 25.0, 8192, 4096, 200000, 100000, 3600, "node-1", "Win11");
        var msgMetrics = NetOpsMessage.CreateRequest(MessageType.Metrics, metrics);
        var readMetrics = msgMetrics.GetPayload<MetricsPayload>();
        Assert.NotNull(readMetrics);
        Assert.Equal(25.0, readMetrics.CpuUsagePercent);

        // 6. CommandRequestPayload
        var cmdReq = new CommandRequestPayload("cmd-integ", "cat /etc/os-release", 15);
        var msgCmd = NetOpsMessage.CreateRequest(MessageType.Command, cmdReq);
        var readCmd = msgCmd.GetPayload<CommandRequestPayload>();
        Assert.NotNull(readCmd);
        Assert.Equal("cmd-integ", readCmd.CommandId);

        // 7. CommandOutputPayload
        var cmdOut = new CommandOutputPayload("cmd-integ", "NAME=\"Ubuntu\"\n", false);
        var msgCmdOut = msgCmd.CreateResponse(MessageType.CommandOutput, cmdOut);
        var readCmdOut = msgCmdOut.GetPayload<CommandOutputPayload>();
        Assert.NotNull(readCmdOut);
        Assert.Equal("cmd-integ", readCmdOut.CommandId);

        // 8. CommandDonePayload
        var cmdDone = new CommandDonePayload("cmd-integ", 0, 50);
        var msgCmdDone = msgCmd.CreateResponse(MessageType.CommandDone, cmdDone);
        var readCmdDone = msgCmdDone.GetPayload<CommandDonePayload>();
        Assert.NotNull(readCmdDone);
        Assert.True(readCmdDone.Success);

        // 9. ErrorMessagePayload qua NetOpsMessage.CreateError
        var errPayload = new ErrorMessagePayload("COMMAND_FAILED", "Command timed out", "Execution exceeded 15s");
        var msgErr = msgCmd.CreateError(errPayload);
        Assert.Equal(MessageType.Error, msgErr.MessageType);
        var readErr = msgErr.GetPayload<ErrorMessagePayload>();
        Assert.NotNull(readErr);
        Assert.Equal("COMMAND_FAILED", readErr.ErrorCode);
        Assert.Equal("Execution exceeded 15s", readErr.Details);
    }

    #endregion
}
