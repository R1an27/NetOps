using Master.Api.Extensions;
using Master.Api.Options;
using Master.Api.Services;
using Microsoft.Extensions.Options;

// Khởi tạo đối tượng builder theo mô hình Minimal API của ASP.NET Core.
// Nạp cấu hình từ appsettings.json, biến môi trường, thiết lập hệ thống ghi log và Kestrel Web Server.
var builder = WebApplication.CreateBuilder(args);

// Đăng ký dịch vụ phân tích mã nguồn để tự động sinh tài liệu chuẩn OpenAPI (Swagger) cho các API HTTP.
builder.Services.AddOpenApi();

// Đăng ký toàn bộ hệ sinh thái dịch vụ Master Server (ConnectionManager, MessageControl, TcpServer, SessionWatchdog)
// thông qua Extension Method đã được module hóa sạch đẹp theo tiêu chuẩn Clean Architecture.
builder.Services.AddNetOpsMasterServer(builder.Configuration);

// Đóng băng danh sách dịch vụ và hoàn thiện container Dependency Injection.
var app = builder.Build();

// Kiểm tra môi trường: Chỉ kích hoạt đường dẫn xem schema OpenAPI khi đang chạy ở môi trường Development.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// ==========================================
// Minimal API Endpoints Quản Trị & Giám Sát
// ==========================================

// 1. Endpoint kiểm tra sức khỏe và thông số vận hành của Master Server
app.MapGet("/api/health", (
    IConnectionManager connectionManager,
    IOptions<MasterServerOptions> options) =>
{
    return Results.Ok(new
    {
        Status = "Healthy",
        Timestamp = DateTimeOffset.UtcNow,
        TcpPort = options.Value.Port,
        ActiveConnections = connectionManager.ActiveCount,
        Watchdog = new
        {
            IntervalSeconds = options.Value.WatchdogIntervalSeconds,
            TimeoutSeconds = options.Value.SessionTimeoutSeconds
        }
    });
})
.WithName("GetServerHealth")
.WithSummary("Kiểm tra trạng thái sức khỏe và số lượng kết nối đang hoạt động của Master Server");

// 2. Endpoint lấy danh sách toàn bộ các Agent đang kết nối trực tuyến
app.MapGet("/api/agents", (IConnectionManager connectionManager) =>
{
    var agents = connectionManager.GetAllSessions().Select(s => new
    {
        s.SessionId,
        s.AgentId,
        s.RemoteEndPoint,
        s.ConnectedAt,
        s.LastSeenAt,
        s.IsAuthenticated,
        IdleSeconds = Math.Round((DateTimeOffset.UtcNow - s.LastSeenAt).TotalSeconds, 1)
    });

    return Results.Ok(agents);
})
.WithName("GetConnectedAgents")
.WithSummary("Lấy danh sách các Agent đang kết nối trực tuyến tới Master Server");

// 3. Endpoint lấy chi tiết một phiên kết nối theo SessionId
app.MapGet("/api/agents/{sessionId}", (string sessionId, IConnectionManager connectionManager) =>
{
    var session = connectionManager.GetSession(sessionId);
    if (session == null)
    {
        return Results.NotFound(new { Message = $"Session '{sessionId}' not found" });
    }

    return Results.Ok(new
    {
        session.SessionId,
        session.AgentId,
        session.RemoteEndPoint,
        session.ConnectedAt,
        session.LastSeenAt,
        session.IsAuthenticated,
        IdleSeconds = Math.Round((DateTimeOffset.UtcNow - session.LastSeenAt).TotalSeconds, 1)
    });
})
.WithName("GetAgentSessionById")
.WithSummary("Lấy thông tin chi tiết của một phiên kết nối theo SessionId");

app.Run();