// [5.2] Khai báo chính sách RunCommands
// Namespace này chứa các hàm của thư viện vừa cài ở [5.1], gồm AddKeycloakAuthorization() và RequireRealmRoles().
using Keycloak.AuthServices.Authorization;
using NetOps.Server.Extensions;
// Thêm vào để đăng kí SignalIR
using NetOps.Server.Hubs;
using NetOps.Server.Services;
using NetOps.Server.Services.Agentless;
using System.Data;
// lấy HostMetricsSummaryDto từ dự án chung NetOps.Shared
using NetOps.Shared;

// Có sẵn khi tạp Project
// [1] Thiết lập các dịch vụ mặc định, bao gồm hệ thống logging
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddGrpc();

// [3.3] Đăng ký bộ xác thực trong
// Đăng ký và cấu hình bộ xác thực token.
// builder.Configuration cung cấp cấu hình ứng dụng, bao gồm mục Keycloak vừa thêm ở [3.2: appsettings.Development.json]
// Phương thức này thuộc thư viện Keycloak.AuthServices.Authentication.
// Web project của bạn đã bật implicit usings nên có thể sử dụng trực tiếp.
// https://nikiforovall.blog/keycloak-authorization-services-dotnet/migration.html#namespace-changes-for-better-discoverability
builder.Services.AddKeycloakWebApiAuthentication(builder.Configuration);

// Đăng ký các dịch vụ dùng để kiểm tra yêu cầu truy cập, chẳng hạn yêu cầu người gọi phải được xác thực.
// Đây là API của Microsoft ASP.NET Core.
// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/security?view=aspnetcore-10.0
// Được thêm một số cái [5]
// Đăng ký các thành phần của thư viện dùng để kiểm tra quyền Keycloak.
// https://nikiforovall.blog/keycloak-authorization-services-dotnet/configuration/configuration-authorization.html#require-realm-roles
builder.Services.AddKeycloakAuthorization();

builder.Services.AddAuthorization(options =>
{
    // Tạo một chính sách mang tên RunCommands, với các điều kiện được khai báo bên trong.
    // options là đối tượng cấu hình AuthorizationOptions của Microsoft.
    //
    options.AddPolicy("RunCommands", policy =>
    {
        // policy là đối tượng AuthorizationPolicyBuilder của Microsoft, dùng để xây các điều kiện.
        // Khai báo điều kiện: người gọi phải được xác thực và có ít nhất một trong hai realm như dưới.
        // hàm này chấp nhận một trong các role được truyền vào.
        // https://github.com/NikiforovAll/keycloak-authorization-services-dotnet/blob/main/src/Keycloak.AuthServices.Authorization/PoliciesBuilderExtensions.cs
        policy.RequireRealmRoles(
            "NetOpsAdmin",
            "NetOpsOperator");
    });
    // [8.1] Thêm chính sách ReadMetrics
    // Tạo chính sách tên ReadMetrics: người gọi phải được xác thực và có ít nhất một trong ba role NetOps.
    options.AddPolicy("ReadMetrics", policy =>
    {
        policy.RequireRealmRoles(
            "NetOpsAdmin",
            "NetOpsOperator",
            "NetOpsViewer");
    });
});

// Đăng ký bộ nhớ dùng chung để các service trên server có thể nhận qua constructor
builder.Services.AddMemoryCache();

// Tạo và quản lý đối tượng AgentConnectionManager
// ASP.NET core sẽ lo việc lấy đối tượng có tên là logger
// rồi truyền vào constructor của class AgentConnectionManager
builder.Services.AddSingleton<AgentConnectionManager>();

// Kích hoạt RabbitMQ & MassTransit
builder.Services.AddRabbitMqMassTransit(builder.Configuration);

// Đăng ký module Agentless (SSH)
builder.Services.AddSingleton<SshCommandService>();

// Đăng ký HttpClient kết nối tới Prometheus Time-Series Database

builder.Services.AddHttpClient("Prometheus", client =>
{
    // Cổng Prometheus đã được cấu hình trong AppHost
    client.BaseAddress = new Uri("http://localhost:9090");
});

// Đăng ký SignalR trong Server
builder.Services.AddSignalR();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// [3.4] Đưa việc kiểm tra vào luồng xử lý yêu cầu
//Yêu cầu đi vào Server
//          ↓
//UseAuthentication()
//    Kiểm tra token và xác định người gọi.
//          ↓
//UseAuthorization()
//    Kiểm tra người gọi có đáp ứng
//    yêu cầu truy cập của endpoint hay không.
//          ↓
//Endpoint được thực thi nếu được phép.

// Ví dụ: một yêu cầu không gửi token sẽ chưa có danh tính được xác thực.
// Nếu endpoint yêu cầu đăng nhập, bước authorization sẽ từ chối yêu cầu đó.
// UseAuthentication() cần nằm trước UseAuthorization() để danh tính được xác định trước khi kiểm tra quyền.
// Cả hai đều thuộc Microsoft ASP.NET Core.
// https://learn.microsoft.com/en-us/aspnet/core/security/authentication/?view=aspnetcore-10.0
app.UseAuthentication();
app.UseAuthorization();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapGrpcService<AgentHubService>();
// CommandGrpcService là class xử lý
// MapGrpcService đưa class đó vào hệ thống tiếp nhận lời gọi của Server.
app.MapGrpcService<CommandGrpcService>()
    // [4.1] Yêu cầu đăng nhập đối với API gửi lệnh
    // Người gọi các phương thức của CommandGrpcService phải được xác thực.
    .RequireAuthorization();

// -- API của Prometheus --
// Endpoint tra cứu nhanh chỉ số mới nhất của 1 máy
app.MapGet("/api/hosts/{agentId}/metrics/summary", async (string agentId, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("Prometheus");

    // Hàm phụ trợ gọi PromQL và lấy giá trị số kép (double)
    async Task<double> QueryPrometheusMetricAsync(string metricName)
    {
        try
        {
            // Trong OpenTelemetry OTLP đẩy vào Prometheus:
            // - Thông tin host_id nằm trong metric target_info, kết hợp qua phép join trên (instance, job)
            // - Fallback truy vấn trực tiếp nếu metric mang sẵn nhãn host_id
            string query = $"{metricName} * on(instance, job) group_left(host_id) target_info{{host_id=\"{agentId}\"}} or {metricName}{{host_id=\"{agentId}\"}}";
            // Gửi câu hỏi sang Prometheus qua mạng
            var res = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonNode>($"/api/v1/query?query={Uri.EscapeDataString(query)}");
            // Trích xuất giá trị số từ kết quả JSON trả về
            var resultArr = res?["data"]?["result"]?.AsArray();
            // Kiểm tra xem có máy nào khớp với ID đó không. Nếu máy đó chưa từng gửi dữ liệu (mảng rỗng), thì bỏ qua
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
    var cpuTask = QueryPrometheusMetricAsync("system_cpu_utilization_percent");
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
.WithName("GetHostMetricsSummary")
// [8.2] Gắn chính sách vào API metrics
// Trước khi xử lý yêu cầu tới API này, hãy kiểm tra chính sách ReadMetrics.
.RequireAuthorization("ReadMetrics");

// -- API HISTORY METRIC --
// Endpoint tra cứu chuỗi thời gian lịch sử (5m, 15m, 1h, 3h, 6h)
app.MapGet("/api/hosts/{agentId}/metrics/history", async (
    string agentId,
    string? range,
    IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("Prometheus");

    // 1. Bảng quy đổi dải thời gian sang số giây và bước nhảy (step)
    string selectedRange = (range ?? "15m").ToLowerInvariant();
    int rangeSeconds = selectedRange switch
    {
        "5m" => 5 * 60,
        "15m" => 15 * 60,
        "1h" => 60 * 60,
        "3h" => 3 * 60 * 60,
        "6h" => 6 * 60 * 60,
        _ => 15 * 60
    };

    // Đảm bảo số điểm đo luôn khoảng 30 đến 60 điểm
    int step = rangeSeconds / 45;
    if (step < 5) step = 5;

    long endUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    long startUnix = endUnix - rangeSeconds;

    // 2. Hàm phụ trợ truy vấn Range Query từ Prometheus
    async Task<List<MetricPointDto>> QueryRangePrometheusAsync(string metricName)
    {
        var resultList = new List<MetricPointDto>();
        try
        {
            // Cú pháp join nhãn host_id tương thích OpenTelemetry
            // ** Bản chất target_info lưu host_id nên khi truy vấn theo host_id thì
            // ** bề ngoài ta chỉ biết target_info(struct) còn cái host_id thật sự
            // ** đang NẰM BÊN TRONG target_info --> Phải left_join mới lấy ra được
            string query = $"{metricName} * on(instance, job) group_left(host_id) target_info{{host_id=\"{agentId}\"}} or {metricName}{{host_id=\"{agentId}\"}}";
            // Merge query với dải thời gian quy định ở trên thành URL
            string url = $"/api/v1/query_range?query={Uri.EscapeDataString(query)}&start={startUnix}&end={endUnix}&step={step}";

            // Ghé thăm DB thông qua URL và bưng thùng hàng Json về
            var res = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonNode>(url);
            // Dữ liệu thô của cả thùng hàng
            var resultArr = res?["data"]?["result"]?.AsArray();

            // Kiểm tra có máy nào khớp ID không?
            if (resultArr != null && resultArr.Count > 0)
            {
                // Bóc từng items trong thùng hàng
                var valuesArr = resultArr[0]?["values"]?.AsArray();
                if (valuesArr != null)
                {
                    foreach (var pair in valuesArr)
                    {
                        // Xử lý dữ liệu thô để người dùng có thể đọc
                        if (pair is System.Text.Json.Nodes.JsonArray p && p.Count >= 2) // ktra chuẩn cấu trúc mới làm
                        {
                            long sec = p[0]!.GetValue<long>();
                            string? valStr = p[1]?.ToString();
                            if (double.TryParse(valStr, System.Globalization.CultureInfo.InvariantCulture, out double val))
                            {
                                var time = DateTimeOffset.FromUnixTimeSeconds(sec).LocalDateTime;
                                resultList.Add(new MetricPointDto(time, val));
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return resultList;
    }

    // 3. Thực thi song song 3 truy vấn
    var cpuTask = QueryRangePrometheusAsync("system_cpu_utilization_percent");
    var memTask = QueryRangePrometheusAsync("system_memory_used_bytes");
    var diskTask = QueryRangePrometheusAsync("system_disk_free_bytes");

    await Task.WhenAll(cpuTask, memTask, diskTask);

    var cpuHistory = await cpuTask;
    var memHistory = await memTask;
    var diskHistory = await diskTask;

    // 4. Tính toán các chỉ số KPI tóm tắt trong dải
    // p => p.Value: chỉ lấy Value ra tính toán, không đụng vào TimeStamp
    double maxCpu = cpuHistory.Count > 0 ? cpuHistory.Max(p => p.Value) : 0.0;
    double avgCpu = cpuHistory.Count > 0 ? cpuHistory.Average(p => p.Value) : 0.0;
    long peakMem = memHistory.Count > 0 ? (long)memHistory.Max(p => p.Value) : 0L;

    var responseDto = new HostMetricsHistoryDto(
        AgentId: agentId,
        Range: selectedRange,
        CpuHistory: cpuHistory,
        MemoryHistory: memHistory,
        DiskHistory: diskHistory,
        MaxCpuPercent: maxCpu,
        AvgCpuPercent: avgCpu,
        PeakMemoryUsedBytes: peakMem,
        StartTime: DateTimeOffset.FromUnixTimeSeconds(startUnix).LocalDateTime,
        EndTime: DateTimeOffset.FromUnixTimeSeconds(endUnix).LocalDateTime
    );

    return Results.Ok(responseDto);
})
.WithName("GetHostMetricsHistory")
.RequireAuthorization("ReadMetrics");

// Mở địa chỉ để client kết nối tới Hub (SignalIR)
app.MapHub<NetOpsHub>("/hubs/netops")
    // [3.5] Yêu cầu người kết nối Hub phải đăng nhập
    // gắn chính sách truy cập mặc định vào endpoint đó.
    // Với cấu hình hiện tại, người gọi phải được xác thực.
    // Phương thức này thuộc Microsoft ASP.NET Core.
    // https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.authorizationendpointconventionbuilderextensions.requireauthorization?view=aspnetcore-10.0
    .RequireAuthorization();


app.Run();

// CÁC DTO ĐÓNG GÓI DỮ LIỆU CỦA SERVER
public record HostMetricsSummaryDto(
    string AgentId,
    double CpuPercent,
    long MemoryUsedBytes,
    long DiskFreeBytes,
    DateTime Timestamp
);
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
