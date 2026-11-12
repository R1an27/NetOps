using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Runtime.InteropServices;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;

namespace NetOps.Agent;

public static class MetricsExportService
{
    // 1. Tạo "Đồng hồ đo" (Meter) của riêng Agent
    private static readonly Meter AgentMeter = new("NetOps.Agent", "1.0.0");

    // Khai báo biến đo CPU
    private static ulong _prevIdleTime;
    private static ulong _prevKernelTime;
    private static ulong _prevUserTime;
    private static bool _firstCpuCheck = true;
    private static double _cachedCpuUsage = 0.0;
    private static DateTime _lastCpuCheck = DateTime.MinValue;

    public static MeterProvider InitMetrics(string agentId, string hostName)
    {
        // 1. Tạo công cụ đo tỷ lệ CPU(%) kiểu Gauge kèm nhãn host_id
        AgentMeter.CreateObservableGauge(
            name: "system_cpu_utilization_percent",
            observeValue: () => new Measurement<double>(GetCpuUsagePercent(), new KeyValuePair<string, object?>("host_id", agentId)),
            unit: "%",
            description: "Ty le su dung CPU hien tai"
        );

        // 2. Đo dung lượng RAM đã sử dụng (đơn vị: Bytes) kèm nhãn host_id
        AgentMeter.CreateObservableGauge(
            name: "system_memory_used_bytes",
            observeValue: () => new Measurement<long>(GetMemoryUsedBytes(), new KeyValuePair<string, object?>("host_id", agentId)),
            unit: "By",
            description: "Dung luong RAM he thong dang su dung"
        );

        // 3. Đo dung lượng Ổ đĩa còn trống (đơn vị: Bytes) kèm nhãn host_id
        AgentMeter.CreateObservableGauge(
            name: "system_disk_free_bytes",
            observeValue: () => new Measurement<long>(GetSystemDriveFreeBytes(), new KeyValuePair<string, object?>("host_id", agentId)),
            unit: "By",
            description: "Dung luong o dia he dieu hanh con trong"
        );

        // 4. Cấu hình OTel MeterProvider để đẩy OTLP về Prometheus
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "NetOps.Agent")
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("host.id", agentId),
                    new KeyValuePair<string, object>("host.name", hostName)
                }))
            .AddMeter("NetOps.Agent")
            .AddOtlpExporter((options, readerOptions) =>
            {
                options.Endpoint = new Uri("http://localhost:9090/api/v1/otlp/v1/metrics");
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000;
            })
            .Build();

        return meterProvider;
    }

    // --- CÁC HÀM ĐO ĐẠC HỆ THỐNG CHUẨN XÁC ---

    // 1. Đo CPU toàn hệ thống (Windows: GetSystemTimes API)
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    private static double GetCpuUsagePercent()
    {
        var now = DateTime.UtcNow;
        // Nếu chưa đủ 500ms kể từ lần đo trước, trả về giá trị cũ để tránh nhiễu
        if ((now - _lastCpuCheck).TotalMilliseconds < 500 && !_firstCpuCheck)
        {
            return _cachedCpuUsage;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            {
                var idle = ToUInt64(idleFt);
                var kernel = ToUInt64(kernelFt);
                var user = ToUInt64(userFt);

                if (_firstCpuCheck)
                {
                    _prevIdleTime = idle;
                    _prevKernelTime = kernel;
                    _prevUserTime = user;
                    _firstCpuCheck = false;
                    _lastCpuCheck = now;
                    return 5.0; // Giá trị lấy mẫu ban đầu
                }

                var idleDelta = idle - _prevIdleTime;
                var kernelDelta = kernel - _prevKernelTime;
                var userDelta = user - _prevUserTime;

                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                _lastCpuCheck = now;

                var totalSystem = kernelDelta + userDelta;
                if (totalSystem > 0)
                {
                    var busy = totalSystem > idleDelta ? totalSystem - idleDelta : 0;
                    _cachedCpuUsage = Math.Round((double)busy * 100.0 / totalSystem, 1);
                }
                return _cachedCpuUsage;
            }
        }

        return _cachedCpuUsage;
    }

    // 2. Đo RAM thực tế đang dùng của toàn hệ thống (Windows: GlobalMemoryStatusEx)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;  // Tổng RAM vật lý (Bytes)
        public ulong ullAvailPhys;  // RAM vật lý còn trống (Bytes)
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private static long GetMemoryUsedBytes()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                // RAM đã dùng = Tổng RAM vật lý - RAM còn trống
                return (long)(mem.ullTotalPhys - mem.ullAvailPhys);
            }
        }

        // Fallback cho Linux / môi trường khác
        var memInfo = GC.GetGCMemoryInfo();
        return memInfo.MemoryLoadBytes > 0
            ? memInfo.MemoryLoadBytes
            : Math.Max(0, memInfo.TotalAvailableMemoryBytes - memInfo.TotalCommittedBytes);
    }

    // 3. Đo dung lượng ổ đĩa còn trống
    private static long GetSystemDriveFreeBytes()
    {
        var systemPath = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var drive = new DriveInfo(systemPath);
        return drive.AvailableFreeSpace;
    }
}
