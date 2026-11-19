// Dùng thư viện này thêm regex để kiểm tra hợp lệ ngay trước endpoint (chống PromQL Injection)
using System.Text.RegularExpressions;
// [5.2] Khai báo chính sách RunCommands
// Namespace này chứa các hàm của thư viện vừa cài ở [5.1], gồm AddKeycloakAuthorization() và RequireRealmRoles().
using Keycloak.AuthServices.Authorization;
using NetOps.Server.Extensions;
// Thêm vào để đăng kí SignalIR
using NetOps.Server.Hubs;
using NetOps.Server.Services;
using NetOps.Server.Services.Agentless;
// lấy HostMetricsSummaryDto từ dự án chung NetOps.Shared
using NetOps.Shared;

// Có sẵn khi tạp Project
// [1] Thiết lập các dịch vụ mặc định, bao gồm hệ thống logging
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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
// ➞ Nếu chạy bằng Docker Compose trên VM thì mỗi Service chạy trong 1 container riêng
// Chúng cô lập với nhau nên nếu vẫn dùng localhost thì nó chỉ có phạm vi trong riêng container netops-server
// ➞ Trong Dcoker Compose network thì các container gọi nhau bằng tên service (http://prometheus:9090)
var prometheusBaseUrl = AgentMetricsExporter.ResolvePrometheusBaseUrl(builder.Configuration);

builder.Services.AddHttpClient("Prometheus", client =>
{
    client.BaseAddress = new Uri(prometheusBaseUrl);
});

// Đẩy metrics Agent nhận từ SignalR sang Prometheus qua OTLP nội bộ
builder.Services.AddHostedService<AgentMetricsExporter>();

// Đăng ký SignalR trong Server
builder.Services.AddSignalR();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

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

app.MapHub<AgentHubService>("/hubs/agents");

// -- API của Prometheus --
// Bộ lọc kiểm tra định dạng agentId để chống tấn công PromQL Injection.
// Vì sao cần bước này?
// Bên dưới ta ghép trực tiếp agentId vào câu truy vấn Prometheus: system_cpu_utilization_percent{host_id="{agentId}"}
// Nếu kẻ xấu cố tình gửi agentId chứa ký tự đặc biệt như:  abc"} or up{a="
// thì câu truy vấn bị phá vỡ cấu trúc và có thể đọc trộm dữ liệu của máy khác hoặc làm quá tải Prometheus.
// Giải thích biểu thức Regex @"^[a-zA-Z0-9._-]{1,128}$":
//   ^ và $           : kiểm tra toàn bộ chuỗi từ ký tự đầu đến ký tự cuối (không cho lọt ký tự lạ ở giữa/cuối)
//   [a-zA-Z0-9._-]   : chỉ cho phép chữ cái, chữ số, dấu chấm, gạch dưới và gạch ngang (chặn sạch ", \, {, }, khoảng trắng...)
//   {1,128}          : độ dài bắt buộc từ 1 đến 128 ký tự (khớp giới hạn lúc Agent đăng ký)
//   RegexOptions.Compiled : biên dịch sẵn regex 1 lần khi Server khởi động để kiểm tra nhanh hơn ở mọi request sau đó.
var validAgentIdRegex = new Regex(@"^[a-zA-Z0-9._-]{1,128}$", RegexOptions.Compiled);

// Endpoint tra cứu nhanh chỉ số mới nhất của 1 máy
app.MapGet("/api/hosts/{agentId}/metrics/summary", async (
    string agentId,
    IHttpClientFactory clientFactory) =>
{
    // Từ chối ngay bằng mã 400 Bad Request nếu agentId rỗng hoặc chứa ký tự không hợp lệ trước khi ghép vào PromQL.
    if (string.IsNullOrWhiteSpace(agentId) || !validAgentIdRegex.IsMatch(agentId))
        return Results.BadRequest("AgentId không hợp lệ.");

    var client = clientFactory.CreateClient("Prometheus");

    async Task<double> QueryPrometheusMetricAsync(string metricName)
    {
        try
        {
            // -- Truy vấn trực tiếp nhãn host_id do AgentMetricsExporter đẩy lên --
            // Tạo câu truy vấn PromQL lọc đúng máy theo nhãn host_id
            string query = $"{metricName}{{host_id=\"{agentId}\"}}";
            // Gọi HTTP GET tới API truy vấn tức thời của Prometheus 
            var res = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonNode>(
                $"/api/v1/query?query={Uri.EscapeDataString(query)}");
            // Khui gói hàng ra và kiểm tra
            var resultArr = res?["data"]?["result"]?.AsArray();
            if (resultArr != null && resultArr.Count > 0)
            {
                var valStr = resultArr[0]?["value"]?[1]?.ToString();
                // Ép kiểu
                if (double.TryParse(valStr, System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    return val;
                }
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Loi khi truy van Prometheus summary cho metric {Metric}: {Message}", metricName, ex.Message);
        }
        return 0.0;
    }
    // Truy vấn đồng thời cả 3 cùng lúc
    var cpuTask = QueryPrometheusMetricAsync("system_cpu_utilization_percent");
    var memTask = QueryPrometheusMetricAsync("system_memory_used_bytes");
    var diskTask = QueryPrometheusMetricAsync("system_disk_free_bytes");

    await Task.WhenAll(cpuTask, memTask, diskTask);

    // Đóng gói kết quả vào DTO và gửi lên Desktop
    var summary = new HostMetricsSummaryDto(
        AgentId: agentId,
        CpuPercent: await cpuTask,
        MemoryUsedBytes: (long)(await memTask),
        DiskFreeBytes: (long)(await diskTask),
        Timestamp: DateTime.UtcNow
    );

    return Results.Ok(summary);
})
.WithName("GetHostMetricsSummary") // Đặt định danh cho Endpoint
.RequireAuthorization("ReadMetrics"); // Bắt buộc client gửi kèm JWT Token hợp lệ và có quyền ReadMetrics

// -- API HISTORY METRIC --
// Endpoint tra cứu chuỗi thời gian lịch sử (5m, 15m, 1h, 3h, 6h)
app.MapGet("/api/hosts/{agentId}/metrics/history", async (
    string agentId,
    string? range,
    IHttpClientFactory clientFactory) =>
{
    // Kiểm tra agentId giống endpoint summary: chặn ký tự lạ trước khi ghép vào câu truy vấn PromQL bên dưới.
    if (string.IsNullOrWhiteSpace(agentId) || !validAgentIdRegex.IsMatch(agentId))
        return Results.BadRequest("AgentId không hợp lệ.");

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
            // Truy vấn trực tiếp nhãn host_id do AgentMetricsExporter đẩy lên
            string query = $"{metricName}{{host_id=\"{agentId}\"}}";
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
                                var time = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;
                                resultList.Add(new MetricPointDto(time, val));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Loi khi truy van Range Query Prometheus cho metric {Metric}: {Message}", metricName, ex.Message);
        }
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
        StartTime: DateTimeOffset.FromUnixTimeSeconds(startUnix).UtcDateTime,
        EndTime: DateTimeOffset.FromUnixTimeSeconds(endUnix).UtcDateTime
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
