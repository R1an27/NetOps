# HƯỚNG DẪN TỪNG BƯỚC: GIÁM SÁT HIỆU NĂNG VỚI OPENTELEMETRY VÀ PROMETHEUS (NETOPS V2)

> **Mục tiêu:**
> 1. **NetOps.AppHost:** Cấu hình Prometheus Container tiếp nhận dữ liệu đẩy từ chuẩn OpenTelemetry (OTLP Receiver).
> 2. **NetOps.Agent:** Tự động đo đạc định kỳ tài nguyên (CPU %, RAM, Ổ đĩa Disk), gắn định danh máy (`host.id`, `host.name`) và xuất dữ liệu (OTLP Push) về Prometheus.
> 3. **NetOps.Server:** Xây dựng API tiếp nhận yêu cầu từ Dashboard/Desktop và truy vấn dữ liệu mới nhất từ Prometheus bằng PromQL.

---

## MỤC LỤC
* [Bức tranh tổng thể: Luồng hoạt động của hệ thống](#bức-tranh-tổng-thể-luồng-hoạt-động-của-hệ-thống)
* [BƯỚC 1: Cấu hình Prometheus tiếp nhận dữ liệu OTLP trên AppHost](#bước-1-cấu-hình-prometheus-tiếp-nhận-dữ-liệu-otlp-trên-apphost)
* [BƯỚC 2: Cài đặt thư viện OpenTelemetry cho NetOps.Agent (Thao tác GUI)](#bước-2-cài-đặt-thư-viện-opentelemetry-cho-netopsagent-thao-tác-gui)
* [BƯỚC 3: Xây dựng bộ thu thập & đẩy chỉ số tại Agent (Phương pháp Cộng dồn)](#bước-3-xây-dựng-bộ-thu-thập--đẩy-chỉ-số-tại-agent-phương-pháp-cộng-dồn)
  * [Chặng 1: Code tối giản nhất (Đo CPU & Đẩy OTLP)](#chặng-1-code-tối-giản-nhất-đo-cpu--đẩy-otlp)
  * [Chặng 2: Nguồn gốc từng Class & Hàm từ Tài liệu chính thức](#chặng-2-nguồn-gốc-từng-class--hàm-từ-tài-liệu-chính-thức)
  * [Chặng 3: Lắp thêm định danh máy chủ (Resource Attributes)](#chặng-3-lắp-thêm-định-danh-máy-chủ-resource-attributes)
  * [Chặng 4: Lắp thêm đo đạc RAM và Dung lượng Ổ đĩa (Disk)](#chặng-4-lắp-thêm-đo-đạc-ram-và-dung-lượng-ổ-đĩa-disk)
* [BƯỚC 4: Kiểm tra dữ liệu trực tiếp trên Giao diện Prometheus (GUI)](#bước-4-kiểm-tra-dữ-liệu-trực-tiếp-trên-giao-diện-prometheus-gui)
* [BƯỚC 5: Xây dựng API trên Server để truy vấn chỉ số mới nhất](#bước-5-xây-dựng-api-trên-server-để-truy-vấn-chỉ-số-mới-nhất)
  * [Chặng 1: Hiểu cách Prometheus trả lời qua câu lệnh PromQL](#chặng-1-hiểu-cách-prometheus-trả-lời-qua-câu-lệnh-promql)
  * [Chặng 2: Code tối giản Endpoint API trên Server](#chặng-2-code-tối-giản-endpoint-api-trên-server)
  * [Chặng 3: Đóng gói dữ liệu DTO hoàn chỉnh trả lời cho Desktop](#chặng-3-đóng-gói-dữ-liệu-dto-hoàn-chỉnh-trả-lời-cho-desktop)

---

## BỨC TRANH TỔNG THỂ: LUỒNG HOẠT ĐỘNG CỦA HỆ THỐNG

Trước khi bắt tay vào từng dòng lệnh, hãy hình dung dữ liệu sẽ đi như thế nào:

```
[Máy chủ mục tiêu (Host)]
          │
     (NetOps.Agent)
     - Đo CPU %, RAM, Disk định kỳ (mỗi 5 giây)
     - Gắn thẻ căn cước: host.id = "agent-01", host.name = "srv-lab-01"
     - Đóng gói chuẩn OTLP đẩy qua HTTP
          │
          ▼  POST http://localhost:9090/api/v1/otlp/v1/metrics
[Prometheus TSDB] (Cổng 9090)
     - Tiếp nhận bản tin OTLP
     - Lưu trữ chuỗi thời gian (Time-series)
          ▲
          │  GET /api/v1/query?query=system_cpu_utilization{host_id="agent-01"}
[NetOps.Server (Web API)]
     - Nhận yêu cầu từ Desktop: GET /api/metrics/agent-01/latest
     - Gửi câu truy vấn PromQL sang Prometheus
     - Bóc tách JSON và trả về kết quả tinh gọn
          │
          ▼
[Giao diện Desktop (WinForms)]
```

---

## BƯỚC 1: CẤU HÌNH PROMETHEUS TIẾP NHẬN DỮ LIỆU OTLP TRÊN APPHOST

> [!NOTE]
> Mặc định trong lịch sử, Prometheus sử dụng cơ chế **PULL (Kéo dữ liệu)**: Prometheus tự chạy đi cào dữ liệu từ các máy.
> Nhưng hệ thống NetOps v2 có các Agent nằm sau mạng NAT/Firewall, do đó Agent cần chủ động **PUSH (Đẩy dữ liệu)** về Prometheus theo chuẩn OpenTelemetry (OTLP).
> Từ phiên bản 2.47+ (và bản v3.0 đang dùng trong dự án), Prometheus đã tích hợp sẵn tính năng tiếp nhận OTLP qua cờ cấu hình `--web.enable-otlp-receiver`.

### Thao tác code trong `src/NetOps.AppHost/AppHost.cs`

Mở file `AppHost.cs`, tìm đến dòng cấu hình container `prometheus` và bổ sung thêm tham số chạy `.WithArgs()`:

```csharp
// Thêm Prometheus container để thu thập và lưu trữ time-series metrics
var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.0.0")
                        .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "http")
                        .WithArgs(
                            "--config.file=/etc/prometheus/prometheus.yml",
                            "--storage.tsdb.path=/prometheus",
                            "--web.enable-otlp-receiver" // <-- Cờ cốt lõi mở cổng nhận OTLP
                        );
```

#### Nguồn gốc và Ý nghĩa từ Tài liệu Prometheus:
* **`--web.enable-otlp-receiver`**: Được quy định trong *Tài liệu chính thức Prometheus (mục OpenTelemetry receiver)*. Khi bật cờ này, Prometheus tự động mở endpoint HTTP tại đường dẫn `/api/v1/otlp/v1/metrics` để các Agent đẩy dữ liệu metric chuẩn OTLP trực tiếp vào.

---

## BƯỚC 2: CÀI ĐẶT THƯ VIỆN OPENTELEMETRY CHO NETOPS.AGENT (THAO TÁC GUI)

Chúng ta sẽ cài đặt 2 thư viện chính thức của OpenTelemetry .NET bằng giao diện chuột trên Visual Studio.

1. Trong cửa sổ **Solution Explorer**, tìm đến project **`NetOps.Agent`**.
2. Chuột phải vào project **`NetOps.Agent`** $\rightarrow$ Chọn **`Manage NuGet Packages...`**.
3. Trong giao diện NuGet Package Manager:
   * Chuyển sang tab **`Browse`**.
   * Nhập vào ô tìm kiếm: `OpenTelemetry`.
   * Chọn gói **`OpenTelemetry`** (tác giả: *OpenTelemetry Authors*) $\rightarrow$ Bấm nút **`Install`** bên phải.
   * Tiếp tục nhập ô tìm kiếm: `OpenTelemetry.Exporter.OpenTelemetryProtocol`.
   * Chọn gói **`OpenTelemetry.Exporter.OpenTelemetryProtocol`** $\rightarrow$ Bấm nút **`Install`**.
4. Hộp thoại xác nhận hiện lên $\rightarrow$ Nhấn **`OK`** / **`I Accept`**.

*(Visual Studio sẽ tự động tải và cấu hình thư viện vào project của bạn mà không cần gõ lệnh CLI).*

---

## BƯỚC 3: XÂY DỰNG BỘ THU THẬP & ĐẨY CHỈ SỐ TẠI AGENT (PHƯƠNG PHÁP CỘNG DỒN)

Chúng ta sẽ tiếp cận từ mức **tối giản nhất** để bạn nắm được khung xương, sau đó mới lắp thêm cơ bắp!

---

### Chặng 1: Code tối giản nhất (Đo CPU & Đẩy OTLP)

Mục tiêu chặng này: Tạo ra 1 bộ phát metric đơn giản nhất, đo 1 giá trị CPU và gửi về Prometheus.

Tạo một class mới tên là `MetricsExportService.cs` trong project `NetOps.Agent`:
1. Trong Solution Explorer, chuột phải vào project **`NetOps.Agent`** $\rightarrow$ **`Add`** $\rightarrow$ **`Class...`**.
2. Đặt tên: `MetricsExportService.cs` $\rightarrow$ Nhấn **`Add`**.
3. Dán đúng đoạn code tối giản sau:

```csharp
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;

namespace NetOps.Agent;

public static class MetricsExportService
{
    // 1. Tạo "Đồng hồ đo" (Meter) của riêng Agent
    private static readonly Meter AgentMeter = new("NetOps.Agent", "1.0.0");

    // Giả lập giá trị CPU ban đầu để kiểm tra
    private static double _currentCpu = 15.5;

    public static MeterProvider InitMetrics()
    {
        // 2. Tạo công cụ đo kiểu Gauge (cho phép giá trị tăng giảm liên tục như đồng hồ tốc độ)
        AgentMeter.CreateObservableGauge(
            name: "system_cpu_utilization",
            observeValue: () => _currentCpu,
            unit: "%",
            description: "Ty le su dung CPU hien tai"
        );

        // 3. Cấu hình OTel MeterProvider để đẩy OTLP về Prometheus
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("NetOps.Agent") // Đăng ký Meter vừa tạo
            .AddOtlpExporter((options, readerOptions) =>
            {
                // Cổng tiếp nhận OTLP của Prometheus
                options.Endpoint = new Uri("http://localhost:9090/api/v1/otlp/v1/metrics");
                options.Protocol = OtlpExportProtocol.HttpProtobuf;

                // Chu kỳ đẩy dữ liệu (mỗi 5 giây một lần)
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000;
            })
            .Build();

        return meterProvider;
    }
}
```

---

### Chặng 2: Nguồn gốc từng Class & Hàm từ Tài liệu chính thức

Để không cảm thấy "ngợp", bảng sau đây chỉ rõ từng thành phần sinh ra từ đâu:

| Thành phần trong code              | Nguồn gốc trong Docs chính thức                                     | Ý nghĩa là gì? (Ví von dễ hiểu)                                                                                                                                                                 |
| :--------------------------------- | :------------------------------------------------------------------ | :---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `System.Diagnostics.Metrics.Meter` | Thư viện chuẩn có sẵn của .NET Runtime (Microsoft Learn).           | **"Chiếc hộp dụng cụ"**: Đại diện cho ứng dụng của bạn, bên trong chứa các dụng cụ đo đạc khác nhau.                                                                                            |
| `CreateObservableGauge(...)`       | Nằm trong class `Meter` của Microsoft.                              | **"Đồng hồ đo kim"**: Đo các chỉ số có thể trồi sụt theo thời gian (như CPU %, mức xăng xe máy, nhiệt độ phòng). Cứ mỗi chu kỳ đọc, nó sẽ gọi hàm `observeValue` để liếc nhìn giá trị hiện tại. |
| `Sdk.CreateMeterProviderBuilder()` | Nằm trong gói NuGet `OpenTelemetry`.                                | **"Nhà máy lắp ráp"**: Khởi tạo và thiết lập đường ống xử lý dữ liệu đo lường.                                                                                                                  |
| `.AddMeter("NetOps.Agent")`        | Nằm trong API của OpenTelemetry .NET.                               | **"Đầu vào (Input)"**: Báo cho OpenTelemetry biết cần lắng nghe những công cụ đo đạc nằm trong chiếc hộp mang tên `NetOps.Agent`.                                                               |
| `.AddOtlpExporter(...)`            | Nằm trong gói NuGet `OpenTelemetry.Exporter.OpenTelemetryProtocol`. | **"Đầu ra (Output / Xe chở hàng)"**: Bơm dữ liệu vừa đo được vào phong bì chuẩn OTLP và chuyển qua mạng đến trạm thu gom.                                                                       |
| `OtlpExportProtocol.HttpProtobuf`  | Chuẩn OpenTelemetry Protocol Specification.                         | Giao thức truyền dữ liệu: Đóng gói nhị phân (Protobuf) và gửi qua HTTP POST. Nhẹ và nhanh hơn gửi JSON thuần túy.                                                                               |
| `ExportIntervalMilliseconds`       | Nằm trong `PeriodicExportingMetricReaderOptions`.                   | Chu kỳ hẹn giờ: Cứ mỗi $X$ mili-giây thì "xe gom rác" OTLP lại đến lấy dữ liệu đo đi gửi một lần.                                                                                               |

---

### Chặng 3: Lắp thêm định danh máy chủ (Resource Attributes)

#### Vấn đề đặt ra:
Nếu có 10 máy trạm cùng gửi dữ liệu về Prometheus với tên metric là `system_cpu_utilization`, Prometheus sẽ gom chung lại và không biết chỉ số 15.5% này là của máy Phòng Lab 1 hay máy Server Web!

#### Cách giải quyết:
Trong OpenTelemetry có khái niệm **Resource** — đây là tấm "thẻ căn cước" dán lên mọi metric được gửi ra từ máy này.

Mở file `src/NetOps.Agent/MetricsExportService.cs`, chúng ta **cộng dồn** thêm phần `.ConfigureResource()` vào đoạn Builder:

```csharp
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources; // <-- Thêm namespace này

namespace NetOps.Agent;

public static class MetricsExportService
{
    private static readonly Meter AgentMeter = new("NetOps.Agent", "1.0.0");
    private static double _currentCpu = 15.5;

    public static MeterProvider InitMetrics(string agentId, string hostName)
    {
        AgentMeter.CreateObservableGauge(
            name: "system_cpu_utilization",
            observeValue: () => _currentCpu,
            unit: "%",
            description: "Ty le su dung CPU hien tai"
        );

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            // --- CỘNG DỒN THÊM ĐỊNH DANH MÁY CHỦ TẠI ĐÂY ---
            .ConfigureResource(resource => resource
                .AddService(serviceName: "NetOps.Agent")
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("host.id", agentId),
                    new KeyValuePair<string, object>("host.name", hostName)
                }))
            // ------------------------------------------------
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
}
```

> **Kết quả đạt được:**
> Khi Prometheus nhận dữ liệu, nó sẽ tự động sinh ra nhãn:
> `system_cpu_utilization{host_id="agent-01", host_name="DESKTOP-XYZ"}`

---

### Chặng 4: Lắp thêm đo đạc RAM và Dung lượng Ổ đĩa (Disk)

Bây giờ ta thay thế giá trị mẫu `_currentCpu = 15.5` bằng các hàm đọc chỉ số thực tế từ hệ điều hành:

1. **CPU:** Đo thời gian CPU sử dụng của tiến trình / máy.
2. **RAM:** Lấy từ `GC.GetGCMemoryInfo()` (Bộ nhớ đang dùng & Tổng bộ nhớ khả dụng).
3. **Ổ đĩa (Disk):** Dùng `DriveInfo.GetDrives()` để quét dung lượng trống ổ đĩa cài hệ điều hành.

Mở file `src/NetOps.Agent/MetricsExportService.cs` và hoàn thiện code như sau:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;

namespace NetOps.Agent;

public static class MetricsExportService
{
    private static readonly Meter AgentMeter = new("NetOps.Agent", "1.0.0");

    // Khai báo biến đo CPU
    private static DateTime _lastCpuCheck = DateTime.UtcNow;
    private static TimeSpan _lastTotalProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
    private static double _cachedCpuUsage = 0.0;

    public static MeterProvider InitMetrics(string agentId, string hostName)
    {
        // 1. Đo tỷ lệ CPU (%)
        AgentMeter.CreateObservableGauge(
            name: "system_cpu_utilization",
            observeValue: () => GetCpuUsagePercent(),
            unit: "%",
            description: "Ty le tieu thu CPU cua may"
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

        // 4. Khởi tạo Pipeline OTel
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

    // --- CÁC HÀM ĐO ĐẠC NGUYÊN BẢN TỪ .NET ---

    // Thuật toán đo CPU đơn giản: So sánh thời gian CPU chạy giữa 2 lần lấy mẫu
    private static double GetCpuUsagePercent()
    {
        var now = DateTime.UtcNow;
        var elapsedRealTime = (now - _lastCpuCheck).TotalMilliseconds;

        if (elapsedRealTime > 500) // Chỉ tính toán lại nếu cách lần trước hơn 0.5s
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
```

#### Gọi khởi động trong `src/NetOps.Agent/Program.cs`:
Mở file `Program.cs` của `NetOps.Agent`, bạn chỉ cần thêm 1 dòng gọi khởi chạy ở đầu:

```csharp
// Khởi tạo và giữ bộ phát OTel hoạt động suốt vòng đời Agent
using var metricsProvider = MetricsExportService.InitMetrics(info.AgentId, info.Hostname);
Console.WriteLine("Đã kích hoạt OpenTelemetry Metrics Exporter thành công!");
```

---

## BƯỚC 4: KIỂM TRA DỮ LIỆU TRỰC TIẾP TRÊN GIAO DIỆN PROMETHEUS (GUI)

Sau khi khởi chạy ứng dụng (qua Aspire hoặc chạy riêng lẻ), bạn có thể kiểm chứng ngay bằng mắt trên giao diện web của Prometheus:

1. Mở trình duyệt Web và truy cập: `http://localhost:9090`.
2. Giao diện **Prometheus Web UI** xuất hiện:
   * Nhấp chuột vào thanh tìm kiếm **`Expression`**.
   * Gõ tên chỉ số: `system_cpu_utilization_percent` hoặc `system_memory_used_bytes` hoặc `system_disk_free_bytes.
   * Bấm nút **`Execute`**.
3. Kết quả:
   * Trong tab **`Table`**, bạn sẽ thấy một dòng kết quả xuất hiện dạng:
     ```text
     system_cpu_utilization{host_id="agent-xxx", host_name="DESKTOP-ABC", job="NetOps.Agent"}  12.4
     ```
   * Chuyển sang tab **`Graph`**, bạn sẽ thấy đường biểu đồ trồi sụt theo từng khoảng thời gian 5 giây!

---

## BƯỚC 5: XÂY DỰNG API TRÊN SERVER ĐỂ TRUY VẤN CHỈ SỐ MỚI NHẤT

Bây giờ ta chuyển sang `NetOps.Server`. Nhiệm vụ của Server là làm chiếc "cầu nối":
* Khi ứng dụng quản trị Desktop hỏi: *"Cho tôi xin chỉ số CPU, RAM, Disk của máy `agent-01`!"*
* Server sẽ hỏi Prometheus bằng ngôn ngữ PromQL, sau đó trả về JSON cho Desktop.

---

### Chặng 1: Hiểu cách Prometheus trả lời qua câu lệnh PromQL

Prometheus cung cấp sẵn một HTTP API chuẩn:
```http
GET http://localhost:9090/api/v1/query?query=<câu_truy_vấn_promql>
```

Ví dụ khi bạn muốn lấy CPU mới nhất của máy `agent-01`, câu truy vấn PromQL là:
`system_cpu_utilization{host_id="agent-01"}`

Prometheus sẽ trả về JSON có cấu trúc như sau:
```json
{
  "status": "success",
  "data": {
    "resultType": "vector",
    "result": [
      {
        "metric": {
          "host_id": "agent-01",
          "host_name": "srv-lab-01"
        },
        "value": [ 1730900000.123, "15.5" ] // [Thời điểm Unix, Giá trị đo được]
      }
    ]
  }
}
```

---

### Chặng 2: Code tối giản Endpoint API trên Server

Mở file `src/NetOps.Server/Program.cs`, chúng ta đăng ký `HttpClient` và viết một API endpoint tối giản:

#### 1. Đăng ký HttpClient trong phần builder (gần đầu file `Program.cs`):
```csharp
builder.Services.AddHttpClient("Prometheus", client =>
{
    // Cổng Prometheus đã được cấu hình trong AppHost
    client.BaseAddress = new Uri("http://localhost:9090");
});
```

#### 2. Thêm endpoint API truy vấn nhanh (gần cuối file `Program.cs`):
```csharp
// Endpoint tra cứu nhanh chỉ số mới nhất của 1 máy
app.MapGet("/api/metrics/{agentId}/latest", async (string agentId, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("Prometheus");

    // Tạo câu truy vấn PromQL lọc theo agentId
    string query = $"system_cpu_utilization{{host_id=\"{agentId}\"}}";
    string url = $"/api/v1/query?query={Uri.EscapeDataString(query)}";

    // Gọi Prometheus
    var response = await client.GetAsync(url);
    if (!response.IsSuccessStatusCode)
    {
        return Results.Problem("Khong the ket noi toi Prometheus");
    }

    var content = await response.Content.ReadAsStringAsync();
    return Results.Content(content, "application/json");
})
.WithName("GetLatestMetrics");
```

---

### Chặng 3: Đóng gói dữ liệu DTO hoàn chỉnh trả lời cho Desktop

Để giao diện Desktop dễ dùng nhất, Server không nên trả về nguyên cả cục JSON rườm rà của Prometheus mà nên lọc thành một đối tượng DTO gọn nhẹ.

Tạo class DTO đại diện kết quả:
```csharp
public record HostMetricsSummaryDto(
    string AgentId,
    double CpuPercent,
    long MemoryUsedBytes,
    long DiskFreeBytes,
    DateTime Timestamp
);
```

Và hàm API hoàn chỉnh gom cả 3 chỉ số CPU, RAM, Disk:

```csharp
app.MapGet("/api/hosts/{agentId}/metrics/summary", async (string agentId, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("Prometheus");

    // Hàm phụ trợ gọi PromQL và lấy giá trị số kép (double)
    async Task<double> QueryPrometheusMetricAsync(string metricName)
    {
        try
        {
            string query = $"{metricName}{{host_id=\"{agentId}\"}}";
            var res = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonNode>($"/api/v1/query?query={Uri.EscapeDataString(query)}");
            var resultArr = res?["data"]?["result"]?.AsArray();
            if (resultArr != null && resultArr.Count > 0)
            {
                // value[1] là giá trị đo được dưới dạng chuỗi
                var valStr = resultArr[0]?["value"]?[1]?.ToString();
                if (double.TryParse(valStr, System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    return val;
                }
            }
        }
        catch { }
        return 0.0;
    }

    // Truy vấn song song cả 3 chỉ số từ Prometheus
    var cpuTask = QueryPrometheusMetricAsync("system_cpu_utilization");
    var memTask = QueryPrometheusMetricAsync("system_memory_used_bytes");
    var diskTask = QueryPrometheusMetricAsync("system_disk_free_bytes");

    await Task.WhenAll(cpuTask, memTask, diskTask);

    var summary = new HostMetricsSummaryDto(
        AgentId: agentId,
        CpuPercent: await cpuTask,
        MemoryUsedBytes: (long)(await memTask),
        DiskFreeBytes: (long)(await diskTask),
        Timestamp: DateTime.UtcNow
    );

    return Results.Ok(summary);
})
.WithName("GetHostMetricsSummary");
```

---

## TỔNG KẾT & CÁC BƯỚC TIẾP THEO

Qua tài liệu hướng dẫn trên, bạn đã nắm trọn vẹn quy trình 3 bên:
1. **Prometheus:** Được mở cờ `--web.enable-otlp-receiver` để biến thành trạm thu gom chuẩn OTLP.
2. **NetOps.Agent:** Dùng OpenTelemetry SDK đo CPU, RAM, Disk và định kỳ "bắn" về Prometheus kèm thẻ nhận diện `host.id`.
3. **NetOps.Server:** Cung cấp REST API sạch sẽ, sử dụng PromQL để lấy số liệu mới nhất trả về cho ứng dụng quản trị.

Khi bạn sẵn sàng triển khai thực tế vào code của dự án, hãy nhắn cho mình biết để cùng tiến hành từng bước một cách an toàn và chính xác nhất!

