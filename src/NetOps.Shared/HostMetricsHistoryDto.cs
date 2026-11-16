using System;
using System.Collections.Generic;
using System.Text;

namespace NetOps.Shared
{
    /// <summary>
    /// Đại diện cho một điểm đo trên trục toạ độ thời gian (X = Thời gian UTC, Y = Giá trị số).
    /// </summary>
    public record MetricPointDto(DateTime Timestamp, double Value);

    /// <summary>
    /// Gói dữ liệu chuỗi thời gian hoàn chỉnh cho một máy trạm trong dải thời gian yêu cầu.
    /// </summary>
    public record HostMetricsHistoryDto(
        string AgentId,
        // Nhãn phân loại thời gian
        string Range,
        List<MetricPointDto> CpuHistory,
        List<MetricPointDto> MemoryHistory,
        List<MetricPointDto> DiskHistory,
        double MaxCpuPercent,
        double AvgCpuPercent,
        long PeakMemoryUsedBytes,
        DateTime StartTime,
        DateTime EndTime
    );
}
