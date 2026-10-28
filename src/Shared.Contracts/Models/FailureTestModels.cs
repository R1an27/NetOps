namespace ServerManager.Shared.Contracts.Models;

/// <summary>
/// Giai đoạn T19.1: Khảo sát &amp; Thiết kế Hợp đồng dữ liệu cho 'Failure Test'.
/// Liệt kê các kịch bản kiểm thử sự cố mạng và khả năng chịu lỗi (Resilience &amp; Chaos Scenarios)
/// trong hệ sinh thái NetOps.
/// </summary>
public enum FailureScenarioType
{
    /// <summary>
    /// Kịch bản 1: Ngắt kết nối socket cưỡng chế đột ngột (Abrupt Socket Drop / TCP RST).
    /// Socket bị đóng ngang xương mà không có quá trình bắt tay đóng êm dịu (TCP FIN Handshake).
    /// </summary>
    AbruptSocketDrop = 0,

    /// <summary>
    /// Kịch bản 2: Agent im lặng không gửi PING (Silent Drop / Dead Agent / Dây mạng bị rút một chiều).
    /// Kiểm chứng cơ chế Watchdog trên Master phát hiện Half-Open Socket và thu hồi tài nguyên sau 15s.
    /// </summary>
    HeartbeatSilence = 1,

    /// <summary>
    /// Kịch bản 3: Tiến trình Agent bị dừng cưỡng chế đột ngột (Agent Process Crash / taskkill).
    /// Kiểm chứng Master dọn dẹp phiên làm việc sạch sẽ, không rò rỉ File Descriptor / Memory.
    /// </summary>
    AgentProcessCrash = 2,

    /// <summary>
    /// Kịch bản 4: Máy chủ trung tâm Master Server khởi động lại (Master Restart).
    /// Kiểm chứng các Agent tiếp tục kiên trì thử kết nối lại theo Exponential Backoff + Jitter
    /// và tự động phục hồi phiên khi Server hoạt động trở lại.
    /// </summary>
    MasterRestart = 3,

    /// <summary>
    /// Kịch bản 5: Tấn công gói tin dị dạng hoặc vượt ngưỡng 16MB DoS (Protocol Fuzzing / Buffer Overflow Attack).
    /// Kiểm chứng bộ giải mã SafeFrameStream từ chối an toàn mà không làm sập máy chủ vì Out-Of-Memory (OOM).
    /// </summary>
    MalformedPacketAttack = 4
}

/// <summary>
/// Giai đoạn T19.1: Hợp đồng ghi nhận từng bước kiểm toán thời gian thực (Failure Audit Step).
/// Lưu giữ timeline với độ chính xác mili-giây phục vụ việc đánh giá độ trễ phát hiện lỗi và thời gian tự phục hồi.
/// </summary>
public class FailureAuditStep
{
    /// <summary>
    /// Mốc thời gian ghi nhận bước kiểm toán (UTC).
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Thời gian trôi qua tính từ lúc kịch bản sự cố bắt đầu (mili-giây).
    /// </summary>
    public double ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Phân đoạn / Giai đoạn kiểm thử (ví dụ: "TRIGGER", "DETECTED", "BACKOFF_RETRY", "RECONNECTED").
    /// </summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết hành động hoặc trạng thái vừa xảy ra.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái kết nối tại thời điểm ghi nhận (nếu có).
    /// </summary>
    public string? StateSnapshot { get; set; }

    /// <summary>
    /// Khởi tạo bước kiểm toán mặc định.
    /// </summary>
    public FailureAuditStep() { }

    /// <summary>
    /// Khởi tạo bước kiểm toán với đầy đủ thông số.
    /// </summary>
    public FailureAuditStep(double elapsedMs, string phase, string message, string? stateSnapshot = null)
    {
        ElapsedMilliseconds = elapsedMs;
        Phase = phase;
        Message = message;
        StateSnapshot = stateSnapshot;
    }
}

/// <summary>
/// Giai đoạn T19.1: Hợp đồng yêu cầu thực thi kịch bản kiểm thử sự cố (Failure Test Request).
/// </summary>
public class FailureTestRequest
{
    /// <summary>
    /// Kịch bản sự cố cần thực thi.
    /// </summary>
    public FailureScenarioType Scenario { get; set; } = FailureScenarioType.AbruptSocketDrop;

    /// <summary>
    /// Mã Agent mục tiêu (tùy chọn, nếu kịch bản nhắm tới một Agent cụ thể).
    /// </summary>
    public string? TargetAgentId { get; set; }

    /// <summary>
    /// Thời gian giả lập im lặng (dành cho kịch bản HeartbeatSilence, mặc định 15 giây).
    /// </summary>
    public int DurationSeconds { get; set; } = 15;

    /// <summary>
    /// Tham số mở rộng tùy chọn cho các kịch bản kiểm thử đặc thù.
    /// </summary>
    public Dictionary<string, string>? Parameters { get; set; }
}

/// <summary>
/// Giai đoạn T19.1: Hợp đồng kết quả báo cáo kiểm thử sự cố (Failure Test Report).
/// Tổng hợp toàn bộ dữ liệu kiểm chứng: thời gian phát hiện lỗi, thời gian phục hồi và timeline từng bước.
/// </summary>
public class FailureTestReport
{
    /// <summary>
    /// Mã định danh phiên kiểm thử duy nhất.
    /// </summary>
    public string TestId { get; set; } = $"ft-{Guid.NewGuid():N}"[..12];

    /// <summary>
    /// Kịch bản sự cố đã được kiểm thử.
    /// </summary>
    public FailureScenarioType Scenario { get; set; }

    /// <summary>
    /// Đối tượng chịu tác động (ví dụ: "Client-Local", "Agent:agent-linux-01", "Master Server").
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm bắt đầu kích hoạt sự cố (UTC).
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Thời điểm hoàn tất kịch bản kiểm thử (UTC).
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Độ trễ từ khi sự cố xảy ra đến khi hệ thống phát hiện (mili-giây).
    /// </summary>
    public double? DetectionLatencyMs { get; set; }

    /// <summary>
    /// Thời gian hệ thống tự động kết nối lại và phục hồi hoàn toàn (mili-giây).
    /// </summary>
    public double? RecoveryLatencyMs { get; set; }

    /// <summary>
    /// Số lần đã thử kết nối lại trong chu kỳ Exponential Backoff.
    /// </summary>
    public int ReconnectAttempts { get; set; }

    /// <summary>
    /// Kết quả kịch bản kiểm thử: Đạt (True) hay Thất bại (False).
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Tóm tắt kết luận kỹ thuật về khả năng chịu lỗi và tự phục hồi của hệ thống.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách chi tiết các bước kiểm toán theo trình tự thời gian.
    /// </summary>
    public List<FailureAuditStep> AuditSteps { get; set; } = new();

    /// <summary>
    /// Tiện ích bổ sung một bước kiểm toán vào báo cáo.
    /// </summary>
    public void AddStep(double elapsedMs, string phase, string message, string? stateSnapshot = null)
    {
        AuditSteps.Add(new FailureAuditStep(elapsedMs, phase, message, stateSnapshot));
    }
}
