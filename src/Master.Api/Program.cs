using Master.Api.Endpoints;
using Master.Api.Extensions;

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
app.MapMasterApiEndpoints();

app.Run();

/// <summary>
/// Cho phép các dự án test tham chiếu đến entry point Program phục vụ Integration Testing.
/// </summary>
public partial class Program { }