using System.Text.Json;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ Unit Test chuyên sâu ProtocolDtoTests xác nhận việc tuần tự hóa (Serialize)
/// và giải tuần tự hóa (Deserialize) thành công cho từng loại payload trong NetOps Protocol v0.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Protocol")]
public class ProtocolDtoTests
{
    #region 1. AuthRequestPayload Tests

    [Fact]
    public void AuthRequestPayload_SerializeAndDeserialize_DayDuThuocTinh_BaoToanDuLieu()
    {
        // Arrange
        var original = new AuthRequestPayload(
            agentId: "agent-node-01",
            token: "secret-token-xyz-123",
            osInfo: "Linux 6.8.0-45-generic #45-Ubuntu SMP",
            hostname: "prod-worker-vm-01"
        );

        // Act - 1. Direct JSON Roundtrip
        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AuthRequestPayload>(json);

        // Assert - JSON format chuẩn camelCase
        Assert.Contains("\"agentId\":\"agent-node-01\"", json);
        Assert.Contains("\"token\":\"secret-token-xyz-123\"", json);
        Assert.Contains("\"osInfo\":\"Linux 6.8.0-45-generic #45-Ubuntu SMP\"", json);
        Assert.Contains("\"hostname\":\"prod-worker-vm-01\"", json);

        // Assert - Deserialized object fidelity
        Assert.NotNull(deserialized);
        Assert.Equal(original.AgentId, deserialized!.AgentId);
        Assert.Equal(original.Token, deserialized.Token);
        Assert.Equal(original.OsInfo, deserialized.OsInfo);
        Assert.Equal(original.Hostname, deserialized.Hostname);

        // Act & Assert - 2. NetOpsMessage Envelope Integration
        var msg = NetOpsMessage.Create(MessageType.Auth, original, "req-auth-001");
        Assert.Equal(MessageType.Auth, msg.MessageType);
        Assert.Equal("req-auth-001", msg.RequestId);

        var extracted = msg.GetPayload<AuthRequestPayload>();
        Assert.NotNull(extracted);
        Assert.Equal(original.AgentId, extracted!.AgentId);
        Assert.Equal(original.Token, extracted.Token);
        Assert.Equal(original.OsInfo, extracted.OsInfo);
        Assert.Equal(original.Hostname, extracted.Hostname);
    }

    [Fact]
    public void AuthRequestPayload_SerializeAndDeserialize_ToiGiaNull_LoaiBoCacTruongNullKhoiJson()
    {
        // Arrange - Chỉ có agentId và token, osInfo và hostname là null
        var minimal = new AuthRequestPayload("agent-node-02", "min-token-456");

        // Act
        string json = JsonSerializer.Serialize(minimal);
        var deserialized = JsonSerializer.Deserialize<AuthRequestPayload>(json);

        // Assert - Các trường null không xuất hiện trong JSON (tiết kiệm băng thông)
        Assert.Contains("\"agentId\":\"agent-node-02\"", json);
        Assert.Contains("\"token\":\"min-token-456\"", json);
        Assert.DoesNotContain("\"osInfo\"", json);
        Assert.DoesNotContain("\"hostname\"", json);

        // Assert - Deserialized object
        Assert.NotNull(deserialized);
        Assert.Equal("agent-node-02", deserialized!.AgentId);
        Assert.Equal("min-token-456", deserialized.Token);
        Assert.Null(deserialized.OsInfo);
        Assert.Null(deserialized.Hostname);
    }

    #endregion

    #region 2. AuthResponsePayload Tests

    [Fact]
    public void AuthResponsePayload_SerializeAndDeserialize_TrangThaiThanhCong()
    {
        // Arrange - Dùng factory method Ok()
        var successPayload = AuthResponsePayload.Ok("sess-guid-777", "Auth session granted");

        // Act
        string json = JsonSerializer.Serialize(successPayload);
        var deserialized = JsonSerializer.Deserialize<AuthResponsePayload>(json);

        // Assert - Thuộc tính JSON chuẩn camelCase
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"sessionId\":\"sess-guid-777\"", json);
        Assert.Contains("\"message\":\"Auth session granted\"", json);
        Assert.DoesNotContain("\"errorCode\"", json); // Bỏ qua errorCode khi null

        // Assert - Object roundtrip
        Assert.NotNull(deserialized);
        Assert.True(deserialized!.Success);
        Assert.Equal("sess-guid-777", deserialized.SessionId);
        Assert.Equal("Auth session granted", deserialized.Message);
        Assert.Null(deserialized.ErrorCode);

        // Act & Assert - NetOpsMessage Envelope
        var msg = NetOpsMessage.Create(MessageType.AuthOk, successPayload);
        Assert.True(msg.TryGetPayload<AuthResponsePayload>(out var fromEnvelope));
        Assert.NotNull(fromEnvelope);
        Assert.True(fromEnvelope!.Success);
        Assert.Equal("sess-guid-777", fromEnvelope.SessionId);
    }

    [Fact]
    public void AuthResponsePayload_SerializeAndDeserialize_TrangThaiThatBai()
    {
        // Arrange - Dùng factory method Failed()
        var failedPayload = AuthResponsePayload.Failed("AUTH_INVALID_TOKEN", "Invalid authentication token");

        // Act
        string json = JsonSerializer.Serialize(failedPayload);
        var deserialized = JsonSerializer.Deserialize<AuthResponsePayload>(json);

        // Assert - Thuộc tính JSON chuẩn camelCase
        Assert.Contains("\"success\":false", json);
        Assert.Contains("\"errorCode\":\"AUTH_INVALID_TOKEN\"", json);
        Assert.Contains("\"message\":\"Invalid authentication token\"", json);
        Assert.DoesNotContain("\"sessionId\"", json); // Bỏ qua sessionId khi null

        // Assert - Object roundtrip
        Assert.NotNull(deserialized);
        Assert.False(deserialized!.Success);
        Assert.Equal("AUTH_INVALID_TOKEN", deserialized.ErrorCode);
        Assert.Equal("Invalid authentication token", deserialized.Message);
        Assert.Null(deserialized.SessionId);
    }

    #endregion

    #region 3. HeartbeatPayload Tests

    [Fact]
    public void HeartbeatPayload_SerializeAndDeserialize_TimestampTuSinhVaChiDinh()
    {
        // Case 1: Tự sinh timestamp UTC hiện tại
        long beforeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 50;
        var autoHb = new HeartbeatPayload("worker-agent-hb");
        long afterMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 50;

        Assert.Equal("worker-agent-hb", autoHb.AgentId);
        Assert.InRange(autoHb.Timestamp, beforeMs, afterMs);

        string autoJson = JsonSerializer.Serialize(autoHb);
        Assert.Contains("\"agentId\":\"worker-agent-hb\"", autoJson);
        Assert.Contains("\"timestamp\":", autoJson);

        var deserializedAuto = JsonSerializer.Deserialize<HeartbeatPayload>(autoJson);
        Assert.NotNull(deserializedAuto);
        Assert.Equal("worker-agent-hb", deserializedAuto!.AgentId);
        Assert.Equal(autoHb.Timestamp, deserializedAuto.Timestamp);

        // Case 2: Chỉ định timestamp cụ thể
        const long customTimestamp = 1750000000000L;
        var customHb = new HeartbeatPayload("worker-agent-custom", customTimestamp);

        string customJson = JsonSerializer.Serialize(customHb);
        Assert.Contains("\"timestamp\":1750000000000", customJson);

        var deserializedCustom = JsonSerializer.Deserialize<HeartbeatPayload>(customJson);
        Assert.NotNull(deserializedCustom);
        Assert.Equal("worker-agent-custom", deserializedCustom!.AgentId);
        Assert.Equal(customTimestamp, deserializedCustom.Timestamp);

        // NetOpsMessage Envelope
        var msg = NetOpsMessage.Create(MessageType.Heartbeat, customHb);
        var envelopeHb = msg.GetPayload<HeartbeatPayload>();
        Assert.NotNull(envelopeHb);
        Assert.Equal(customTimestamp, envelopeHb!.Timestamp);
    }

    #endregion

    #region 4. HeartbeatAckPayload Tests

    [Fact]
    public void HeartbeatAckPayload_SerializeAndDeserialize_SuccessFactoryVaCustom()
    {
        // Case 1: Dùng factory Success()
        const long ackTime = 1750000000100L;
        var successAck = HeartbeatAckPayload.Success(ackTime);

        string successJson = JsonSerializer.Serialize(successAck);
        Assert.Contains("\"timestamp\":1750000000100", successJson);
        Assert.Contains("\"acknowledged\":true", successJson);

        var deserializedSuccess = JsonSerializer.Deserialize<HeartbeatAckPayload>(successJson);
        Assert.NotNull(deserializedSuccess);
        Assert.True(deserializedSuccess!.Acknowledged);
        Assert.Equal(ackTime, deserializedSuccess.Timestamp);

        // Case 2: Acknowledged = false
        var failedAck = new HeartbeatAckPayload(ackTime + 50, false);
        string failedJson = JsonSerializer.Serialize(failedAck);
        Assert.Contains("\"acknowledged\":false", failedJson);

        var deserializedFailed = JsonSerializer.Deserialize<HeartbeatAckPayload>(failedJson);
        Assert.NotNull(deserializedFailed);
        Assert.False(deserializedFailed!.Acknowledged);
        Assert.Equal(ackTime + 50, deserializedFailed.Timestamp);

        // NetOpsMessage Envelope
        var msg = NetOpsMessage.Create(MessageType.HeartbeatAck, successAck);
        Assert.True(msg.TryGetPayload<HeartbeatAckPayload>(out var envelopeAck));
        Assert.NotNull(envelopeAck);
        Assert.True(envelopeAck!.Acknowledged);
        Assert.Equal(ackTime, envelopeAck.Timestamp);
    }

    #endregion

    #region 5. MetricsPayload Tests

    [Fact]
    public void MetricsPayload_SerializeAndDeserialize_BaoToan9ChiSoVaComputedProperties()
    {
        // Arrange
        var original = new MetricsPayload(
            agentId: "agent-srv-01",
            cpuUsagePercent: 33.75,
            memoryTotalMb: 16384,
            memoryUsedMb: 8192,
            diskTotalMb: 1000000,
            diskUsedMb: 250000,
            uptimeSeconds: 86400,
            hostname: "db-node-01",
            osVersion: "Ubuntu 24.04 LTS"
        );

        // Act - Direct JSON Roundtrip
        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<MetricsPayload>(json);

        // Assert - JSON camelCase mapping
        Assert.Contains("\"agentId\":\"agent-srv-01\"", json);
        Assert.Contains("\"cpuUsagePercent\":33.75", json);
        Assert.Contains("\"memoryTotalMb\":16384", json);
        Assert.Contains("\"memoryUsedMb\":8192", json);
        Assert.Contains("\"diskTotalMb\":1000000", json);
        Assert.Contains("\"diskUsedMb\":250000", json);
        Assert.Contains("\"uptimeSeconds\":86400", json);
        Assert.Contains("\"hostname\":\"db-node-01\"", json);
        Assert.Contains("\"osVersion\":\"Ubuntu 24.04 LTS\"", json);

        // Assert - Object roundtrip fidelity
        Assert.NotNull(deserialized);
        Assert.Equal(original.AgentId, deserialized!.AgentId);
        Assert.Equal(original.CpuUsagePercent, deserialized.CpuUsagePercent);
        Assert.Equal(original.MemoryTotalMb, deserialized.MemoryTotalMb);
        Assert.Equal(original.MemoryUsedMb, deserialized.MemoryUsedMb);
        Assert.Equal(original.DiskTotalMb, deserialized.DiskTotalMb);
        Assert.Equal(original.DiskUsedMb, deserialized.DiskUsedMb);
        Assert.Equal(original.UptimeSeconds, deserialized.UptimeSeconds);
        Assert.Equal(original.Hostname, deserialized.Hostname);
        Assert.Equal(original.OsVersion, deserialized.OsVersion);

        // Assert - Computed properties tính toán chính xác
        Assert.Equal(50.0, deserialized.MemoryUsagePercent); // 8192 / 16384 * 100
        Assert.Equal(25.0, deserialized.DiskUsagePercent);   // 250000 / 1000000 * 100

        // NetOpsMessage Envelope
        var msg = NetOpsMessage.Create(MessageType.Metrics, original);
        var envelopeMetrics = msg.GetPayload<MetricsPayload>();
        Assert.NotNull(envelopeMetrics);
        Assert.Equal(33.75, envelopeMetrics!.CpuUsagePercent);
        Assert.Equal(50.0, envelopeMetrics.MemoryUsagePercent);
    }

    [Fact]
    public void MetricsPayload_ComputedProperties_XyLyBienKhiTotalBangKhong()
    {
        var zeroMetrics = new MetricsPayload("agent-zero", 0, 0, 0, 0, 0, 0, "srv", "os");

        Assert.Equal(0, zeroMetrics.MemoryUsagePercent);
        Assert.Equal(0, zeroMetrics.DiskUsagePercent);

        string json = JsonSerializer.Serialize(zeroMetrics);
        var deserialized = JsonSerializer.Deserialize<MetricsPayload>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized!.MemoryUsagePercent);
        Assert.Equal(0, deserialized.DiskUsagePercent);
    }

    #endregion

    #region 6. CommandRequestPayload Tests

    [Fact]
    public void CommandRequestPayload_SerializeAndDeserialize_CauLenhShellVaWorkingDirDacBiet()
    {
        // Case 1: Đầy đủ tham số, working directory chứa dấu backslash Windows
        var originalWithDir = new CommandRequestPayload(
            commandId: "cmd-trace-001",
            commandLine: "Get-Process | Where-Object { $_.CPU -gt 10 }",
            timeoutSeconds: 60,
            workingDir: "C:\\Program Files\\NetOps"
        );

        string jsonWithDir = JsonSerializer.Serialize(originalWithDir);
        Assert.Contains("\"commandId\":\"cmd-trace-001\"", jsonWithDir);
        Assert.Contains("\"timeoutSeconds\":60", jsonWithDir);
        Assert.Contains("\"workingDir\":\"C:\\\\Program Files\\\\NetOps\"", jsonWithDir);

        var deserializedWithDir = JsonSerializer.Deserialize<CommandRequestPayload>(jsonWithDir);
        Assert.NotNull(deserializedWithDir);
        Assert.Equal(originalWithDir.CommandId, deserializedWithDir!.CommandId);
        Assert.Equal(originalWithDir.CommandLine, deserializedWithDir.CommandLine);
        Assert.Equal(60, deserializedWithDir.TimeoutSeconds);
        Assert.Equal("C:\\Program Files\\NetOps", deserializedWithDir.WorkingDir);

        // Case 2: workingDir là null -> Bỏ qua trường khỏi JSON
        var originalNoDir = new CommandRequestPayload("cmd-trace-002", "uname -a", 15);
        string jsonNoDir = JsonSerializer.Serialize(originalNoDir);
        Assert.DoesNotContain("\"workingDir\"", jsonNoDir);

        var deserializedNoDir = JsonSerializer.Deserialize<CommandRequestPayload>(jsonNoDir);
        Assert.NotNull(deserializedNoDir);
        Assert.Null(deserializedNoDir!.WorkingDir);
        Assert.Equal(15, deserializedNoDir.TimeoutSeconds);

        // NetOpsMessage Envelope
        var msg = NetOpsMessage.Create(MessageType.Command, originalWithDir, "req-cor-cmd-001");
        Assert.Equal("req-cor-cmd-001", msg.RequestId);
        var envelopeCmd = msg.GetPayload<CommandRequestPayload>();
        Assert.NotNull(envelopeCmd);
        Assert.Equal("cmd-trace-001", envelopeCmd!.CommandId);
    }

    #endregion

    #region 7. CommandOutputPayload Tests

    [Fact]
    public void CommandOutputPayload_SerializeAndDeserialize_StdoutStderrVaUnicode()
    {
        // Case 1: Luồng stdout bình thường, có ký tự xuống dòng
        var stdoutPayload = new CommandOutputPayload("cmd-uuid-10", "Line 1: Hello NetOps\nLine 2: Server running ok\n", isError: false);
        string stdoutJson = JsonSerializer.Serialize(stdoutPayload);
        Assert.Contains("\"isError\":false", stdoutJson);
        Assert.Contains("\"commandId\":\"cmd-uuid-10\"", stdoutJson);

        var deserializedStdout = JsonSerializer.Deserialize<CommandOutputPayload>(stdoutJson);
        Assert.NotNull(deserializedStdout);
        Assert.False(deserializedStdout!.IsError);
        Assert.Equal("cmd-uuid-10", deserializedStdout.CommandId);
        Assert.Equal("Line 1: Hello NetOps\nLine 2: Server running ok\n", deserializedStdout.Output);

        // Case 2: Luồng stderr báo lỗi
        var stderrPayload = new CommandOutputPayload("cmd-uuid-10", "cat: /etc/shadow: Permission denied\n", isError: true);
        string stderrJson = JsonSerializer.Serialize(stderrPayload);
        Assert.Contains("\"isError\":true", stderrJson);

        var deserializedStderr = JsonSerializer.Deserialize<CommandOutputPayload>(stderrJson);
        Assert.NotNull(deserializedStderr);
        Assert.True(deserializedStderr!.IsError);
        Assert.Equal("cat: /etc/shadow: Permission denied\n", deserializedStderr.Output);

        // NetOpsMessage Envelope
        var msg = NetOpsMessage.Create(MessageType.CommandOutput, stdoutPayload);
        Assert.True(msg.TryGetPayload<CommandOutputPayload>(out var fromEnvelope));
        Assert.NotNull(fromEnvelope);
        Assert.False(fromEnvelope!.IsError);
    }

    #endregion

    #region 8. CommandDonePayload Tests

    [Fact]
    public void CommandDonePayload_SerializeAndDeserialize_ExitCodeVaSuccessProperty()
    {
        // Case 1: Lệnh thực thi thành công (ExitCode == 0, Success == true)
        var doneSuccess = new CommandDonePayload("cmd-task-88", exitCode: 0, totalExecutionTimeMs: 1420);
        Assert.True(doneSuccess.Success);

        string successJson = JsonSerializer.Serialize(doneSuccess);
        Assert.Contains("\"commandId\":\"cmd-task-88\"", successJson);
        Assert.Contains("\"exitCode\":0", successJson);
        Assert.Contains("\"totalExecutionTimeMs\":1420", successJson);

        var deserializedSuccess = JsonSerializer.Deserialize<CommandDonePayload>(successJson);
        Assert.NotNull(deserializedSuccess);
        Assert.Equal("cmd-task-88", deserializedSuccess!.CommandId);
        Assert.Equal(0, deserializedSuccess.ExitCode);
        Assert.Equal(1420, deserializedSuccess.TotalExecutionTimeMs);
        Assert.True(deserializedSuccess.Success);

        // Case 2: Lệnh thực thi thất bại (ExitCode != 0, Success == false)
        var doneFailed = new CommandDonePayload("cmd-task-89", exitCode: 127, totalExecutionTimeMs: 45);
        Assert.False(doneFailed.Success);

        string failedJson = JsonSerializer.Serialize(doneFailed);
        var deserializedFailed = JsonSerializer.Deserialize<CommandDonePayload>(failedJson);
        Assert.NotNull(deserializedFailed);
        Assert.Equal(127, deserializedFailed!.ExitCode);
        Assert.False(deserializedFailed.Success);

        // NetOpsMessage Envelope
        var msg = NetOpsMessage.Create(MessageType.CommandDone, doneSuccess);
        var envelopeDone = msg.GetPayload<CommandDonePayload>();
        Assert.NotNull(envelopeDone);
        Assert.True(envelopeDone!.Success);
        Assert.Equal(1420, envelopeDone.TotalExecutionTimeMs);
    }

    #endregion

    #region 9. ErrorMessagePayload Tests

    [Fact]
    public void ErrorMessagePayload_SerializeAndDeserialize_DayDuVaToiGiaDetails()
    {
        // Case 1: Có đầy đủ mã lỗi, thông báo và chi tiết
        var fullError = new ErrorMessagePayload(
            errorCode: "FRAME_SIZE_EXCEEDED",
            message: "Incoming frame size exceeds 16MB limit",
            details: "Payload length reported: 20971520 bytes"
        );

        string fullJson = JsonSerializer.Serialize(fullError);
        Assert.Contains("\"errorCode\":\"FRAME_SIZE_EXCEEDED\"", fullJson);
        Assert.Contains("\"message\":\"Incoming frame size exceeds 16MB limit\"", fullJson);
        Assert.Contains("\"details\":\"Payload length reported: 20971520 bytes\"", fullJson);

        var deserializedFull = JsonSerializer.Deserialize<ErrorMessagePayload>(fullJson);
        Assert.NotNull(deserializedFull);
        Assert.Equal(fullError.ErrorCode, deserializedFull!.ErrorCode);
        Assert.Equal(fullError.Message, deserializedFull.Message);
        Assert.Equal(fullError.Details, deserializedFull.Details);

        // Case 2: details là null -> Bỏ qua trường details trong JSON
        var minimalError = new ErrorMessagePayload("COMMAND_TIMEOUT", "Command execution timed out after 30s");
        string minJson = JsonSerializer.Serialize(minimalError);
        Assert.DoesNotContain("\"details\"", minJson);

        var deserializedMin = JsonSerializer.Deserialize<ErrorMessagePayload>(minJson);
        Assert.NotNull(deserializedMin);
        Assert.Equal("COMMAND_TIMEOUT", deserializedMin!.ErrorCode);
        Assert.Null(deserializedMin.Details);

        // NetOpsMessage Envelope Integration
        var msg = NetOpsMessage.Create(MessageType.Error, fullError);
        Assert.True(msg.TryGetPayload<ErrorMessagePayload>(out var fromEnvelope));
        Assert.NotNull(fromEnvelope);
        Assert.Equal("FRAME_SIZE_EXCEEDED", fromEnvelope!.ErrorCode);
        Assert.Equal(fullError.Details, fromEnvelope.Details);
    }

    #endregion

    #region 10. Backward Compatibility & Subtyping Tests

    [Fact]
    public void BackwardCompatibility_Subtyping_HaiChieu_AuthPayload_AuthRequestPayload()
    {
        // Khởi tạo đối tượng từ lớp cũ AuthPayload
        var oldAuth = new AuthPayload("agent-legacy-01", "token-legacy-999", "Windows Server 2022", "win-srv-01");

        // Serialize đối tượng cũ sang JSON
        string json = JsonSerializer.Serialize(oldAuth);

        // Deserialize ngược lại bằng kiểu chuẩn AuthRequestPayload
        var asStandard = JsonSerializer.Deserialize<AuthRequestPayload>(json);
        Assert.NotNull(asStandard);
        Assert.Equal("agent-legacy-01", asStandard!.AgentId);
        Assert.Equal("token-legacy-999", asStandard.Token);
        Assert.Equal("Windows Server 2022", asStandard.OsInfo);
        Assert.Equal("win-srv-01", asStandard.Hostname);

        // Deserialize ngược lại bằng kiểu cũ AuthPayload
        var asLegacy = JsonSerializer.Deserialize<AuthPayload>(json);
        Assert.NotNull(asLegacy);
        Assert.Equal("agent-legacy-01", asLegacy!.AgentId);
    }

    [Fact]
    public void BackwardCompatibility_Subtyping_HaiChieu_CommandPayload_CommandRequestPayload()
    {
        // Khởi tạo đối tượng từ lớp cũ CommandPayload
        var oldCmd = new CommandPayload("cmd-leg-01", "ipconfig /all", 30, "C:\\Windows\\System32");

        // Serialize sang JSON
        string json = JsonSerializer.Serialize(oldCmd);

        // Deserialize sang CommandRequestPayload
        var asStandard = JsonSerializer.Deserialize<CommandRequestPayload>(json);
        Assert.NotNull(asStandard);
        Assert.Equal("cmd-leg-01", asStandard!.CommandId);
        Assert.Equal("ipconfig /all", asStandard.CommandLine);
        Assert.Equal(30, asStandard.TimeoutSeconds);
        Assert.Equal("C:\\Windows\\System32", asStandard.WorkingDir);

        // Deserialize sang CommandPayload
        var asLegacy = JsonSerializer.Deserialize<CommandPayload>(json);
        Assert.NotNull(asLegacy);
        Assert.Equal("cmd-leg-01", asLegacy!.CommandId);
    }

    [Fact]
    public void BackwardCompatibility_Subtyping_HaiChieu_ErrorPayload_ErrorMessagePayload()
    {
        // Khởi tạo đối tượng từ lớp cũ ErrorPayload
        var oldError = new ErrorPayload("CONNECTION_LOST", "TCP socket connection was reset by peer", "Error code 10054");

        // Serialize sang JSON
        string json = JsonSerializer.Serialize(oldError);

        // Deserialize sang ErrorMessagePayload
        var asStandard = JsonSerializer.Deserialize<ErrorMessagePayload>(json);
        Assert.NotNull(asStandard);
        Assert.Equal("CONNECTION_LOST", asStandard!.ErrorCode);
        Assert.Equal("TCP socket connection was reset by peer", asStandard.Message);
        Assert.Equal("Error code 10054", asStandard.Details);

        // Deserialize sang ErrorPayload
        var asLegacy = JsonSerializer.Deserialize<ErrorPayload>(json);
        Assert.NotNull(asLegacy);
        Assert.Equal("CONNECTION_LOST", asLegacy!.ErrorCode);
    }

    [Fact]
    public void CaseInsensitive_Json_Deserialization_HoatDongThongSuotChoCacDTOs()
    {
        // Giả lập JSON có thuộc tính PascalCase gửi từ hệ thống bên thứ ba
        var pascalErrorJson = "{\"ErrorCode\":\"ERR_CASE_TEST\",\"Message\":\"PascalCase test\",\"Details\":\"Extra details\"}";
        var msgError = new NetOpsMessage { MessageType = MessageType.Error, Payload = pascalErrorJson };

        var parsedError = msgError.GetPayload<ErrorMessagePayload>();
        Assert.NotNull(parsedError);
        Assert.Equal("ERR_CASE_TEST", parsedError!.ErrorCode);
        Assert.Equal("PascalCase test", parsedError.Message);
        Assert.Equal("Extra details", parsedError.Details);

        // Giả lập JSON có thuộc tính UPPERCASE cho CommandRequestPayload
        var upperCmdJson = "{\"COMMANDID\":\"cmd-upper\",\"COMMANDLINE\":\"hostname\",\"TIMEOUTSECONDS\":10}";
        var msgCmd = new NetOpsMessage { MessageType = MessageType.Command, Payload = upperCmdJson };

        var parsedCmd = msgCmd.GetPayload<CommandRequestPayload>();
        Assert.NotNull(parsedCmd);
        Assert.Equal("cmd-upper", parsedCmd!.CommandId);
        Assert.Equal("hostname", parsedCmd.CommandLine);
        Assert.Equal(10, parsedCmd.TimeoutSeconds);
    }

    [Fact]
    public void TryGetPayload_AnToanTrenTatCaCacDTOs()
    {
        // 1. TryGetPayload thành công với chuỗi JSON hợp lệ
        var validHbMsg = NetOpsMessage.Create(MessageType.Heartbeat, new HeartbeatPayload("agent-try", 999999L));
        Assert.True(validHbMsg.TryGetPayload<HeartbeatPayload>(out var hb));
        Assert.NotNull(hb);
        Assert.Equal("agent-try", hb!.AgentId);

        // 2. TryGetPayload trả về false an toàn (không crash) với chuỗi JSON không hợp lệ
        var invalidMsg = new NetOpsMessage
        {
            MessageType = MessageType.Command,
            Payload = "NOT_A_VALID_JSON_OBJECT"
        };

        Assert.False(invalidMsg.TryGetPayload<CommandRequestPayload>(out var cmd));
        Assert.Null(cmd);

        Assert.False(invalidMsg.TryGetPayload<ErrorMessagePayload>(out var err));
        Assert.Null(err);
    }

    #endregion

    #region 11. Extreme Boundaries, Special Characters & Large Buffers

    [Fact]
    public void Boundary_Values_SoCucHan_LongMax_DoubleMax_NegativeExitCode()
    {
        // 1. Heartbeat với timestamp cực đại long.MaxValue và 0
        var maxHb = new HeartbeatPayload("agent-edge-max", long.MaxValue);
        string maxHbJson = JsonSerializer.Serialize(maxHb);
        var readMaxHb = JsonSerializer.Deserialize<HeartbeatPayload>(maxHbJson);
        Assert.NotNull(readMaxHb);
        Assert.Equal(long.MaxValue, readMaxHb!.Timestamp);

        var zeroHb = new HeartbeatPayload("agent-edge-zero", 0L);
        string zeroHbJson = JsonSerializer.Serialize(zeroHb);
        var readZeroHb = JsonSerializer.Deserialize<HeartbeatPayload>(zeroHbJson);
        Assert.NotNull(readZeroHb);
        Assert.Equal(0L, readZeroHb!.Timestamp);

        // 2. Metrics với double biên (0.0 và 100.0) và dung lượng RAM/Disk cực đại
        var edgeMetrics = new MetricsPayload(
            agentId: "agent-edge-metrics",
            cpuUsagePercent: 100.0,
            memoryTotalMb: long.MaxValue,
            memoryUsedMb: long.MaxValue,
            diskTotalMb: long.MaxValue,
            diskUsedMb: 0L,
            uptimeSeconds: long.MaxValue,
            hostname: "supercomputer-cluster-01",
            osVersion: "Edge Linux Kernel 6.12"
        );
        string metricsJson = JsonSerializer.Serialize(edgeMetrics);
        var readMetrics = JsonSerializer.Deserialize<MetricsPayload>(metricsJson);
        Assert.NotNull(readMetrics);
        Assert.Equal(100.0, readMetrics!.CpuUsagePercent);
        Assert.Equal(long.MaxValue, readMetrics.MemoryTotalMb);
        Assert.Equal(long.MaxValue, readMetrics.DiskTotalMb);
        Assert.Equal(0L, readMetrics.DiskUsedMb);
        Assert.Equal(100.0, readMetrics.MemoryUsagePercent);
        Assert.Equal(0.0, readMetrics.DiskUsagePercent);

        // 3. CommandDone với ExitCode âm (-1) và int.MaxValue
        var negativeDone = new CommandDonePayload("cmd-edge-neg", exitCode: -1, totalExecutionTimeMs: long.MaxValue);
        Assert.False(negativeDone.Success);
        string negJson = JsonSerializer.Serialize(negativeDone);
        var readNegDone = JsonSerializer.Deserialize<CommandDonePayload>(negJson);
        Assert.NotNull(readNegDone);
        Assert.Equal(-1, readNegDone!.ExitCode);
        Assert.Equal(long.MaxValue, readNegDone.TotalExecutionTimeMs);
        Assert.False(readNegDone.Success);

        var maxDone = new CommandDonePayload("cmd-edge-max", exitCode: int.MaxValue, totalExecutionTimeMs: 0);
        Assert.False(maxDone.Success);
        string maxDoneJson = JsonSerializer.Serialize(maxDone);
        var readMaxDone = JsonSerializer.Deserialize<CommandDonePayload>(maxDoneJson);
        Assert.NotNull(readMaxDone);
        Assert.Equal(int.MaxValue, readMaxDone!.ExitCode);
        Assert.False(readMaxDone.Success);
    }

    [Fact]
    public void SpecialCharacters_Unicode_Emoji_Va_JsonEscape_Trong_CommandVaError()
    {
        // 1. CommandRequestPayload chứa nháy kép, escape backslash, Unicode và Emoji
        const string complexShell = "echo \"Chào NetOps 🚀🔥\" && dir \"C:\\Users\\Admin\\Desktop\\Tài liệu\\\" --flag='test\\value' & whoami";
        var cmdReq = new CommandRequestPayload("cmd-unicode-01", complexShell, 120, "D:\\Dữ liệu\\Dự án");

        string cmdJson = JsonSerializer.Serialize(cmdReq);
        var readCmd = JsonSerializer.Deserialize<CommandRequestPayload>(cmdJson);

        Assert.NotNull(readCmd);
        Assert.Equal("cmd-unicode-01", readCmd!.CommandId);
        Assert.Equal(complexShell, readCmd.CommandLine);
        Assert.Equal("D:\\Dữ liệu\\Dự án", readCmd.WorkingDir);

        // 2. ErrorMessagePayload chứa đa ngôn ngữ Unicode (CJK + Tiếng Việt) và ký tự điều khiển
        const string multiLangMsg = "Lỗi xử lý tệp tin 🚀 [日本語 / 中文 / Tiếng Việt]";
        const string multilineDetails = "Chi tiết lỗi:\n\tFile: 'C:\\tmp\\test.txt'\r\n\tStatus: Đã dừng \u2705";
        var errPayload = new ErrorMessagePayload("ERR_UNICODE_MULTILANG", multiLangMsg, multilineDetails);

        string errJson = JsonSerializer.Serialize(errPayload);
        var readErr = JsonSerializer.Deserialize<ErrorMessagePayload>(errJson);

        Assert.NotNull(readErr);
        Assert.Equal("ERR_UNICODE_MULTILANG", readErr!.ErrorCode);
        Assert.Equal(multiLangMsg, readErr.Message);
        Assert.Equal(multilineDetails, readErr.Details);

        // 3. Tích hợp với NetOpsMessage envelope
        var envelope = NetOpsMessage.Create(MessageType.Error, errPayload, "req-cor-multilang");
        var extracted = envelope.GetPayload<ErrorMessagePayload>();
        Assert.NotNull(extracted);
        Assert.Equal(multiLangMsg, extracted!.Message);
        Assert.Equal(multilineDetails, extracted.Details);
    }

    [Fact]
    public void LargePayload_Buffer_Serialization_Over_64KB()
    {
        // Tạo chuỗi dung lượng lớn > 68KB mô phỏng terminal buffer output
        string largeLog = new string('A', 70000) + "\n=== End of large stream buffer ===";
        var largePayload = new CommandOutputPayload("cmd-large-buffer-99", largeLog, isError: false);

        // Serialize sang JSON
        string json = JsonSerializer.Serialize(largePayload);
        Assert.True(json.Length > 70000);

        // Đóng gói vào NetOpsMessage
        var msg = NetOpsMessage.Create(MessageType.CommandOutput, largePayload);
        Assert.NotNull(msg.Payload);
        Assert.True(msg.Payload!.Length > 70000);

        // Bóc tách lại qua GetPayload<T>
        var extracted = msg.GetPayload<CommandOutputPayload>();
        Assert.NotNull(extracted);
        Assert.Equal("cmd-large-buffer-99", extracted!.CommandId);
        Assert.False(extracted.IsError);
        Assert.Equal(largeLog.Length, extracted.Output.Length);
        Assert.Equal(largeLog, extracted.Output);
    }

    [Fact]
    public void AuthRequestPayload_SpecialCharacters_TokenVaHostname()
    {
        const string complexToken = "jwt.token.eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.special+chars/with=padding#hash_value?param=1";
        const string fqdnHostname = "ip-10-0-1-24.ap-southeast-1.compute.internal";
        var authReq = new AuthRequestPayload("agent-edge-token", complexToken, "Debian GNU/Linux 12 (bookworm)", fqdnHostname);

        string json = JsonSerializer.Serialize(authReq);
        var readAuth = JsonSerializer.Deserialize<AuthRequestPayload>(json);

        Assert.NotNull(readAuth);
        Assert.Equal("agent-edge-token", readAuth!.AgentId);
        Assert.Equal(complexToken, readAuth.Token);
        Assert.Equal(fqdnHostname, readAuth.Hostname);
    }

    #endregion
}
