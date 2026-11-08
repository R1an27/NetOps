// [5.2] Khai báo chính sách RunCommands
// Namespace này chứa các hàm của thư viện vừa cài ở [5.1], gồm AddKeycloakAuthorization() và RequireRealmRoles().
using Keycloak.AuthServices.Authorization;
using NetOps.Server.Extensions;
// Thêm vào để đăng kí SignalIR
using NetOps.Server.Hubs;
using NetOps.Server.Services;
using NetOps.Server.Services.Agentless;
using System.Data;

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

// Endpoint tra cứu nhanh chỉ số mới nhất của 1 máy
app.MapGet("/api/hosts/{agentId}/metrics/summary", async (string agentId, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("Prometheus");

    // Hàm phụ trợ gọi PromQL và lấy giá trị số kép (double)
    async Task<double> QueryPrometheusMetricAsync(string metricName)
    {
        try
        {
            string query = $"{metricName}{{host_id=\"{agentId}\"}}";
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
