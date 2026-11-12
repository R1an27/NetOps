using System;
using System.Collections.Generic;
using System.Text;

namespace NetOps.Shared
{
    /// <summary>
    /// Gói dữ liệu tóm tắt chỉ số phần cứng mới nhất của một máy trạm.
    /// </summary>
    public record HostMetricsSummaryDto(
        string AgentId,
        double CpuPercent,
        long MemoryUsedBytes,
        long DiskFreeBytes,
        DateTime Timestamp
    );
}
