using Agent.Worker;
using Agent.Worker.Services;

// Mô hình Generic Host tối giản: Được chuẩn hóa từ .NET 7/8 trở đi, 
// thay thế cho cách viết cũ rườm rà Host.CreateDefaultBuilder(args).ConfigureServices(...).
// Tối ưu hóa tài nguyên: Khác với WebApplication.CreateBuilder bên Master 
// (phải gánh thêm Web Server Kestrel và HTTP pipeline), Host.CreateApplicationBuilder 
// không chứa thành phần Web nào. Nó chỉ nạp những gì tối cần thiết cho một tiến trình chạy nền:
// - Nạp file cấu hình appsettings.json và biến môi trường.
// - Thiết lập hệ thống ghi log (ILogger).
// - Khởi tạo bộ chứa dịch vụ (IServiceCollection).
var builder = Host.CreateApplicationBuilder(args);
// Đăng ký TcpAgentClient với vòng đời Singleton 
// (duy nhất 1 instance trong suốt thời gian ứng dụng chạy).
// Khi Worker được khởi tạo, DI container sẽ tự động tạo một phiên bản 
// TcpAgentClient duy nhất này và tiêm (inject) vào constructor của Worker.
// Sử dụng Singleton ở đây rất hợp lý vì Agent chỉ cần duy nhất một client 
// quản lý kết nối socket tới Master, tránh việc khởi tạo nhiều client gây 
// lãng phí kết nối TCP và tài nguyên mạng.
builder.Services.AddSingleton<TcpAgentClient>();
// Báo cho runtime biết Worker là một Hosted Service 
// (chạy ngầm theo chuẩn IHostedService / BackgroundService).
// .NET sẽ tự động quản lý vòng đời của Worker:
// 1. Khi ứng dụng bật: Tự động gọi Worker.ExecuteAsync(...).
// 2. Khi ứng dụng tắt: Tự động kích hoạt stoppingToken để Worker và 
// TcpAgentClient dọn dẹp socket, Graceful Shutdown.
builder.Services.AddHostedService<Worker>();

// Đóng băng cấu hình dịch vụ, hoàn tất việc khởi tạo 
// DI Container và chuẩn bị sẵn sàng môi trường thực thi.
var host = builder.Build();
// Kích hoạt toàn bộ các HostedService đã đăng ký (chính thức chạy hàm Worker.ExecuteAsync).
// Block luồng chính để giữ tiến trình luôn sống. Ứng dụng sẽ duy trì 
// trạng thái này cho đến khi nhận tín hiệu dừng từ hệ điều hành 
// (như phím tắt Ctrl + C, lệnh kill / SIGTERM trên Linux, hoặc lệnh stop service từ Windows Services / systemd).
host.Run();