using System.Text.Json.Serialization;

namespace ServerManager.Shared.Contracts.Payloads;

/// <summary>
/// Payload cho thông điệp METRICS thu thập số liệu phần cứng định kỳ từ Agent (Mục 4.3 protocol-v0-spec).
/// Chứa thông tin về tải CPU, dung lượng RAM, dung lượng ổ đĩa, thời gian uptime và thông tin hệ điều hành.
/// </summary>
public class MetricsPayload
{
    /// <summary>
    /// Mã định danh của Agent đang báo cáo chỉ số tài nguyên.
    /// </summary>
    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Phần trăm tải sử dụng CPU hiện tại (0.0 đến 100.0).
    /// </summary>
    [JsonPropertyName("cpuUsagePercent")]
    public double CpuUsagePercent { get; set; }

    /// <summary>
    /// Tổng dung lượng bộ nhớ RAM vật lý (tính bằng Megabytes).
    /// </summary>
    [JsonPropertyName("memoryTotalMb")]
    public long MemoryTotalMb { get; set; }

    /// <summary>
    /// Dung lượng bộ nhớ RAM vật lý đang được sử dụng (tính bằng Megabytes).
    /// </summary>
    [JsonPropertyName("memoryUsedMb")]
    public long MemoryUsedMb { get; set; }

    /// <summary>
    /// Tổng dung lượng ổ đĩa lưu trữ chính (tính bằng Megabytes).
    /// </summary>
    [JsonPropertyName("diskTotalMb")]
    public long DiskTotalMb { get; set; }

    /// <summary>
    /// Dung lượng ổ đĩa lưu trữ chính đã sử dụng (tính bằng Megabytes).
    /// </summary>
    [JsonPropertyName("diskUsedMb")]
    public long DiskUsedMb { get; set; }

    /// <summary>
    /// Thời gian hệ thống máy chủ đã hoạt động liên tục (tính bằng giây).
    /// </summary>
    [JsonPropertyName("uptimeSeconds")]
    public long UptimeSeconds { get; set; }

    /// <summary>
    /// Tên máy chủ / hostname của máy Agent.
    /// </summary>
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// Phiên bản chi tiết của hệ điều hành trên máy Agent.
    /// </summary>
    [JsonPropertyName("osVersion")]
    public string OsVersion { get; set; } = string.Empty;

    /// <summary>
    /// Tỷ lệ sử dụng bộ nhớ RAM tính theo phần trăm (0.0 đến 100.0).
    /// Thuộc tính tiện ích bỏ qua khi tuần tự hóa JSON.
    /// </summary>
    [JsonIgnore]
    public double MemoryUsagePercent => MemoryTotalMb > 0 ? (double)MemoryUsedMb / MemoryTotalMb * 100.0 : 0.0;

    /// <summary>
    /// Tỷ lệ sử dụng ổ đĩa lưu trữ chính tính theo phần trăm (0.0 đến 100.0).
    /// Thuộc tính tiện ích bỏ qua khi tuần tự hóa JSON.
    /// </summary>
    [JsonIgnore]
    public double DiskUsagePercent => DiskTotalMb > 0 ? (double)DiskUsedMb / DiskTotalMb * 100.0 : 0.0;

    /// <summary>
    /// Khởi tạo một đối tượng MetricsPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public MetricsPayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng MetricsPayload với đầy đủ các thông số tài nguyên phần cứng.
    /// </summary>
    /// <param name="agentId">Mã định danh Agent</param>
    /// <param name="cpuUsagePercent">Tỷ lệ CPU (%)</param>
    /// <param name="memoryTotalMb">Tổng RAM (MB)</param>
    /// <param name="memoryUsedMb">RAM đã dùng (MB)</param>
    /// <param name="diskTotalMb">Tổng ổ đĩa (MB)</param>
    /// <param name="diskUsedMb">Ổ đĩa đã dùng (MB)</param>
    /// <param name="uptimeSeconds">Thời gian hoạt động (giây)</param>
    /// <param name="hostname">Tên máy chủ</param>
    /// <param name="osVersion">Phiên bản hệ điều hành</param>
    public MetricsPayload(
        string agentId,
        double cpuUsagePercent,
        long memoryTotalMb,
        long memoryUsedMb,
        long diskTotalMb,
        long diskUsedMb,
        long uptimeSeconds,
        string hostname,
        string osVersion)
    {
        AgentId = agentId;
        CpuUsagePercent = cpuUsagePercent;
        MemoryTotalMb = memoryTotalMb;
        MemoryUsedMb = memoryUsedMb;
        DiskTotalMb = diskTotalMb;
        DiskUsedMb = diskUsedMb;
        UptimeSeconds = uptimeSeconds;
        Hostname = hostname;
        OsVersion = osVersion;
    }
}
