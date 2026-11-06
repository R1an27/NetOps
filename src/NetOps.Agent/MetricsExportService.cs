using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources; // <-- CCCD của mọi metrics

namespace NetOps.Agent;

public static class MetricsExportService
{
    // 1. Tạo "Đồng hồ đo" (Meter) của riêng Agent
    private static readonly Meter AgentMeter = new("NetOps.Agent", "1.0.0");

    //-- Khai báo biến đo CPU --

    //Lưu mốc thời gian thực của lần đo trước để tính khoảng thời gian trôi qua
    private static DateTime _lastCpuCheck = DateTime.UtcNow;
    //Lưu tổng thời gian CPU đã thực sự làm việc ở lần đo trước để tính lượng CPU vừa tiêu thụ
    private static TimeSpan _lastTotalProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
    //Bộ nhớ đệm lưu kết quả % CPU gần nhất, giúp tránh tính toán dồn dập khi bị hỏi liên tục
    private static double _cachedCpuUsage = 0.0;

    public static MeterProvider InitMetrics(string agentId, string hostName)
    {
        // 1. Tạo công cụ đo tỷ lệ CPU(%) kiểu Gauge (cho phép giá trị tăng giảm liên tục như đồng hồ tốc độ)
        AgentMeter.CreateObservableGauge(
            name: "system_cpu_utilization_percent",
            observeValue: () => GetCpuUsagePercent(),
            unit: "%",
            description: "Ty le su dung CPU hien tai"
        );

        // 2. Đo dung lượng RAM đã sử dụng (đơn vị: Bytes)
        AgentMeter.CreateObservableGauge(
            name: "system_memory_used_bytes",
            observeValue: () => GetMemoryUsedBytes(),
            unit: "By",
            description: "Dung luong RAM he thong dang su dung"
        );

        // 3. Đo dung lượng Ổ đĩa còn trống (đơn vị: Bytes)
        AgentMeter.CreateObservableGauge(
            name: "system_disk_free_bytes",
            observeValue: () => GetSystemDriveFreeBytes(),
            unit: "By",
            description: "Dung luong o dia he dieu hanh con trong"
        );

        // 4. Cấu hình OTel MeterProvider để đẩy OTLP về Prometheus (Khởi tạo Pipeline OTel)
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            // --- THÊM ĐỊNH DANH MÁY CHỦ ---
            .ConfigureResource(resource => resource
                .AddService(serviceName: "NetOps.Agent")
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("host.id", agentId),
                    new KeyValuePair<string, object>("host.name", hostName)
                }))
            .AddMeter("NetOps.Agent") // Đăng ký Meter vừa tạo
            .AddOtlpExporter((options, readerOptions) =>
            {
                // Cổng tiếp nhận OTLP của Prometheus
                options.Endpoint = new Uri("http://localhost:9090/api/v1/otlp/v1/metrics");
                // Protobuf: Đóng gói nhị phân
                // HTTP: Giao thức truyền tải --> Nhẹ và nhanh hơn JSON
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                // Chu kỳ đẩy dữ liệu (mỗi 5 giây một lần)
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000;
            })
            .Build();

        return meterProvider;
    }
    // --- CÁC HÀM ĐO ĐẠC NGUYÊN BẢN TỪ .NET ---

    // Thuật toán đo CPU đơn giản: So sánh thời gian CPU chạy giữa 2 lần lấy mẫu
    private static double GetCpuUsagePercent()
    {
        var now = DateTime.UtcNow;
        var elapsedRealTime = (now - _lastCpuCheck).TotalMilliseconds;
        //-- Chỉ tính toán lại nếu đã trôi qua ít nhất 0.5s (500ms): --
        //    - Tránh lỗi chia cho 0 nếu bị gọi liên tiếp dồn dập
        //    - Lọc bỏ nhiễu giật cục tức thời
        //    - Giảm tải CPU khi không phải gọi System Call liên tục
        if (elapsedRealTime > 500) 
        {
            var currentTotalProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
            var elapsedCpuTime = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;

            // Tỷ lệ = (Thời gian CPU dùng / (Thời gian thực * số nhân)) * 100
            _cachedCpuUsage = Math.Round((elapsedCpuTime / (elapsedRealTime * Environment.ProcessorCount)) * 100, 2);

            _lastCpuCheck = now;
            _lastTotalProcessorTime = currentTotalProcessorTime;
        }

        return _cachedCpuUsage;
    }

    // Đo RAM: Dùng API chính thức của GC trong .NET
    private static long GetMemoryUsedBytes()
    {
        var memInfo = GC.GetGCMemoryInfo();
        return memInfo.MemoryLoadBytes; // Bộ nhớ OS đang chiếm dụng
    }

    // Đo Ổ đĩa: Dùng DriveInfo chính thức của System.IO
    private static long GetSystemDriveFreeBytes()
    {
        // Lấy ổ đĩa cài đặt thư mục hệ thống (thường là C:\ trên Windows hoặc / trên Linux)
        var systemPath = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var drive = new DriveInfo(systemPath);
        return drive.AvailableFreeSpace;
    }
}

