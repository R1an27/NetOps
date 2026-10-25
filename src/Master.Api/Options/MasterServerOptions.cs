namespace Master.Api.Options;

/// <summary>
/// Tùy chọn cấu hình tập trung cho Master Server (gồm TCP Socket Server và Session Watchdog).
/// </summary>
public class MasterServerOptions
{
    public const string SectionName = "MasterServer";

    /// <summary>
    /// Cổng TCP mà Master Server lắng nghe kết nối từ các Agent (mặc định 5050).
    /// </summary>
    public int Port { get; set; } = 5050;

    /// <summary>
    /// Chu kỳ quét danh sách session của Watchdog để phát hiện kết nối chết (tính bằng giây, mặc định 5s).
    /// </summary>
    public int WatchdogIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Thời gian tối đa một session không có hoạt động trước khi bị coi là Half-Open Socket và bị dọn dẹp (tính bằng giây, mặc định 15s).
    /// Thường được cấu hình gấp 3 lần chu kỳ gửi Heartbeat của Agent (5s x 3 = 15s).
    /// </summary>
    public int SessionTimeoutSeconds { get; set; } = 15;
}
