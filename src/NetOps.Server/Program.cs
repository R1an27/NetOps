using NetOps.Server.Services;
using NetOps.Server.Extensions;
using NetOps.Server.Services.Agentless;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddGrpc();
// Đăng ký cuốn danh bạ hoạt động xuyên suốt vòng đời của Server
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

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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
.WithName("GetHostMetricsSummary");

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
