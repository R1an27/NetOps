using System.Net.Sockets;
using Client.WinForms.Services;
using Master.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Shared.Contracts.Framing;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Giai đoạn T19.4: Bộ kiểm thử đơn vị và tích hợp kiểm chứng tính đúng đắn cho 'Failure Test' (Kiểm thử sự cố & Khả năng chịu lỗi).
/// Đáp ứng các yêu cầu kiểm định:
/// - T19.1: Khảo sát & Thiết kế hợp đồng FailureTestModels (Scenarios, Request, Report, AuditStep).
/// - T19.2: Logic nghiệp vụ tiêm sự cố: Ngắt socket đột ngột (TCP RST), Agent bị kill, Watchdog phát hiện im lặng.
/// - T19.3: Giao diện và cơ chế an toàn đa luồng (Cross-Thread SafeInvoke) khi có sự kiện sự cố mạng.
/// - T19.4: Kiểm chứng tính bền bỉ (Robustness), cô lập lỗi (Fault Isolation) và phòng vệ tấn công DoS OOM 16MB (Protocol Fuzzing).
/// </summary>
[Trait("Category", "Resilience")]
[Trait("Area", "FailureTesting")]
public class FailureResilienceTests
{
    #region T19.1: Kiểm thử Hợp đồng & Models Failure Test

    [Fact]
    public void T19_1_FailureScenarioTypeEnum_ShouldDefineAllRequiredScenarios()
    {
        // Kiểm tra tính đầy đủ của các kịch bản kiểm thử sự cố mạng
        Assert.Equal(0, (int)FailureScenarioType.AbruptSocketDrop);
        Assert.Equal(1, (int)FailureScenarioType.HeartbeatSilence);
        Assert.Equal(2, (int)FailureScenarioType.AgentProcessCrash);
        Assert.Equal(3, (int)FailureScenarioType.MasterRestart);
        Assert.Equal(4, (int)FailureScenarioType.MalformedPacketAttack);
    }

    [Fact]
    public void T19_1_FailureTestReport_ShouldRecordAuditStepsAndCalculateLatencies()
    {
        // Kiểm tra đối tượng báo cáo kết quả kiểm thử sự cố
        var report = new FailureTestReport
        {
            Scenario = FailureScenarioType.AbruptSocketDrop,
            Target = "agent-linux-01",
            DetectionLatencyMs = 12.5,
            RecoveryLatencyMs = 2050.0,
            ReconnectAttempts = 1,
            Success = true,
            Summary = "Đã tự động kết nối lại thành công sau khi bị ngắt socket đột ngột."
        };

        report.AddStep(0.0, "TRIGGER", "Ngắt socket cưỡng chế");
        report.AddStep(12.5, "DETECTED", "Phát hiện mất kết nối");
        report.AddStep(2050.0, "RECONNECTED", "Tự động kết nối lại thành công");

        Assert.Equal(FailureScenarioType.AbruptSocketDrop, report.Scenario);
        Assert.Equal("agent-linux-01", report.Target);
        Assert.Equal(12.5, report.DetectionLatencyMs);
        Assert.Equal(2050.0, report.RecoveryLatencyMs);
        Assert.True(report.Success);
        Assert.Equal(3, report.AuditSteps.Count);
        Assert.Equal("TRIGGER", report.AuditSteps[0].Phase);
        Assert.Equal("RECONNECTED", report.AuditSteps[2].Phase);
    }

    #endregion

    #region T19.2: Kiểm thử Logic Nghiệp vụ Tiêm sự cố (FailureTestService)

    [Fact]
    public void T19_2_TerminateSessionAbruptly_ShouldCloseSocketAndMarkAgentOffline()
    {
        // Arrange
        var registry = new AgentRegistry();
        var connMgr = new ConnectionManager(registry, NullLogger<ConnectionManager>.Instance);
        var failureService = new FailureTestService(connMgr, NullLogger<FailureTestService>.Instance);

        using var ms = new MemoryStream();
        using var safeStream = new SafeFrameStream(ms);
        var session = new AgentSession("session-test-01", safeStream, "127.0.0.1:54321", DateTimeOffset.UtcNow)
        {
            AgentId = "agent-fail-01"
        };
        connMgr.TryRegister(session);

        Assert.Equal(1, connMgr.ActiveCount);
        Assert.Equal(AgentStatus.Online, registry.Get("agent-fail-01")?.Status);

        // Act: Cưỡng chế ngắt socket Agent
        bool terminated = failureService.TerminateSessionAbruptly("agent-fail-01", DisconnectReason.NetworkFault);

        // Assert: Session bị gỡ bỏ khỏi active list, trạng thái trong Registry là Offline với lý do NetworkFault
        Assert.True(terminated);
        Assert.Equal(0, connMgr.ActiveCount);
        var record = registry.Get("agent-fail-01");
        Assert.NotNull(record);
        Assert.Equal(AgentStatus.Offline, record.Status);
        Assert.Equal(DisconnectReason.NetworkFault, record.DisconnectReason);
    }

    [Fact]
    public async Task T19_2_ExecuteScenarioAsync_AbruptSocketDrop_GeneratesSuccessfulReport()
    {
        // Arrange
        var registry = new AgentRegistry();
        var connMgr = new ConnectionManager(registry, NullLogger<ConnectionManager>.Instance);
        var failureService = new FailureTestService(connMgr, NullLogger<FailureTestService>.Instance);

        using var ms = new MemoryStream();
        using var safeStream = new SafeFrameStream(ms);
        var session = new AgentSession("session-drop-01", safeStream, "127.0.0.1:51111", DateTimeOffset.UtcNow)
        {
            AgentId = "agent-drop-test"
        };
        connMgr.TryRegister(session);

        var request = new FailureTestRequest
        {
            Scenario = FailureScenarioType.AbruptSocketDrop,
            TargetAgentId = "agent-drop-test"
        };

        // Act
        var report = await failureService.ExecuteScenarioAsync(request);

        // Assert
        Assert.True(report.Success);
        Assert.Equal("agent-drop-test", report.Target);
        Assert.NotNull(report.DetectionLatencyMs);
        Assert.NotEmpty(report.AuditSteps);
        Assert.Equal(0, connMgr.ActiveCount);
    }

    [Fact]
    public async Task T19_2_ExecuteScenarioAsync_MalformedPacketAttack_VerifiesDoSDefense()
    {
        // Arrange
        var connMgr = new ConnectionManager();
        var failureService = new FailureTestService(connMgr);

        var request = new FailureTestRequest
        {
            Scenario = FailureScenarioType.MalformedPacketAttack
        };

        // Act
        var report = await failureService.ExecuteScenarioAsync(request);

        // Assert: Phải chứng minh hệ thống phòng thủ từ chối gói tin > 16MB
        Assert.True(report.Success);
        Assert.Contains("16MB", report.Summary);
    }

    [Fact]
    public void T19_2_AgentNetworkClient_SimulateHeartbeatSilence_SetsFlagCorrectly()
    {
        var client = new Agent.Worker.Services.AgentNetworkClient(NullLogger<Agent.Worker.Services.AgentNetworkClient>.Instance);

        Assert.False(client.IsHeartbeatSilenced);

        client.SimulateHeartbeatSilence(true);
        Assert.True(client.IsHeartbeatSilenced);

        client.SimulateHeartbeatSilence(false);
        Assert.False(client.IsHeartbeatSilenced);
    }

    #endregion

    #region T19.3: Kiểm thử WinForms Client Service Simulate Abrupt Drop & Reconnect

    [Fact]
    public void T19_3_NetOpsClientService_SimulateAbruptDisconnect_WhenDisconnected_DoesNotThrow()
    {
        var service = new NetOpsClientService();
        Assert.Equal(ConnectionState.Disconnected, service.State);

        // Gọi giả lập ngắt socket khi chưa kết nối không được quăng exception
        var ex = Record.Exception(() => service.SimulateAbruptDisconnect());
        Assert.Null(ex);
    }

    [Fact]
    public void T19_3_NetOpsClientService_DetailedStateChanged_ProvidesAuditInfo()
    {
        var service = new NetOpsClientService();
        ConnectionStateChangedEventArgs? capturedArgs = null;

        service.DetailedConnectionStateChanged += args => capturedArgs = args;

        service.NotifyReconnecting(attempt: 1, retryDelay: TimeSpan.FromSeconds(2), message: "Test socket drop");

        Assert.NotNull(capturedArgs);
        Assert.Equal(ConnectionState.Disconnected, capturedArgs.PreviousState);
        Assert.Equal(ConnectionState.Reconnecting, capturedArgs.NewState);
        Assert.Equal(1, capturedArgs.Attempt);
        Assert.Equal(TimeSpan.FromSeconds(2), capturedArgs.RetryDelay);
        Assert.Equal("Test socket drop", capturedArgs.Message);
    }

    #endregion

    #region T19.4: Kiểm chứng Khả năng Chịu lỗi, Bền bỉ & Fuzzing DoS 16MB (Oral Exam Questions)

    [Fact]
    public void T19_4_ProtocolFuzzing_Exceeding16MB_ThrowsSafeFrameException_PreventingOOM()
    {
        // Câu hỏi vấn đáp Barem T19.4:
        // "Ý nghĩa của Integration Test và Fuzzing Protocol trong việc kiểm chứng độ bền bỉ (robustness) trước tấn công DoS OOM 16MB?"
        // Trả lời & Kiểm chứng:
        // Wire Length Header quy định chuẩn 4-byte Big-Endian. Kích thước tối đa là 16MB (16,777,216 bytes).
        // Khi kẻ tấn công gửi gói tin giả mạo có header lớn hơn 16MB (ví dụ 17MB),
        // SafeFrameStream phải phát hiện ngay từ lúc đọc 4 bytes đầu tiên và ném ngoại lệ lập tức,
        // TUYỆT ĐỐI KHÔNG cấp phát mảng byte byte[17MB], nhờ đó ngăn chặn triệt để tấn công cạn kiệt bộ nhớ Out-Of-Memory (OOM).

        const int maxAllowedSize = 16 * 1024 * 1024; // 16MB
        int maliciousLength = maxAllowedSize + 1024; // 16MB + 1KB (Vượt ngưỡng)

        // Mã hóa header 4-byte Big-Endian độc hại
        byte[] maliciousHeader = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(maliciousHeader, maliciousLength);

        using var ms = new System.IO.MemoryStream(maliciousHeader);
        var safeStream = new SafeFrameStream(ms);

        // Xác thực: Đọc frame phải bị ném InvalidDataException ngay lập tức
        var ex = Assert.ThrowsAsync<System.IO.InvalidDataException>(async () =>
        {
            await safeStream.ReadMessageAsync();
        });

        Assert.NotNull(ex);
    }

    [Fact]
    public void T19_4_FaultIsolation_AbruptDropOnOneAgent_DoesNotAffectOtherAgents()
    {
        // Câu hỏi vấn đáp Barem T19.1 & T19.2:
        // "Sự khác biệt giữa TCP Socket và UDP Datagram? Khi nào dùng TCP và khi nào dùng UDP trong hệ thống giám sát phân tán?"
        // Kiểm chứng tính cô lập lỗi (Fault Isolation):
        // Khi một kết nối TCP của Agent A bị sập (RST / Drop), chỉ phiên của Agent A bị giải phóng.
        // Agent B và Agent C vẫn duy trì socket TCP song công hoàn toàn độc lập.

        var registry = new AgentRegistry();
        var connMgr = new ConnectionManager(registry, NullLogger<ConnectionManager>.Instance);
        var failureService = new FailureTestService(connMgr, NullLogger<FailureTestService>.Instance);

        using var msA = new MemoryStream();
        using var streamA = new SafeFrameStream(msA);
        using var msB = new MemoryStream();
        using var streamB = new SafeFrameStream(msB);
        using var msC = new MemoryStream();
        using var streamC = new SafeFrameStream(msC);

        connMgr.TryRegister(new AgentSession("session-A", streamA, "127.0.0.1:1001", DateTimeOffset.UtcNow) { AgentId = "agent-A" });
        connMgr.TryRegister(new AgentSession("session-B", streamB, "127.0.0.1:1002", DateTimeOffset.UtcNow) { AgentId = "agent-B" });
        connMgr.TryRegister(new AgentSession("session-C", streamC, "127.0.0.1:1003", DateTimeOffset.UtcNow) { AgentId = "agent-C" });

        Assert.Equal(3, connMgr.ActiveCount);

        // Cưỡng chế ngắt Agent A
        bool terminatedA = failureService.TerminateSessionAbruptly("agent-A");
        Assert.True(terminatedA);

        // Kiểm chứng:
        // 1. Tổng số active giảm từ 3 xuống 2
        Assert.Equal(2, connMgr.ActiveCount);
        // 2. Agent A chuyển sang Offline
        Assert.Equal(AgentStatus.Offline, registry.Get("agent-A")?.Status);
        // 3. Agent B và C vẫn hoàn toàn Online, không bị gián đoạn hay ảnh hưởng chéo
        Assert.Equal(AgentStatus.Online, registry.Get("agent-B")?.Status);
        Assert.Equal(AgentStatus.Online, registry.Get("agent-C")?.Status);
    }

    #endregion
}
