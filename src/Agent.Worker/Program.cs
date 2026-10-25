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

// Kích hoạt xác thực nghiêm ngặt toàn bộ Service Provider (ValidateScopes & ValidateOnBuild)
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true
}));
// Đăng ký bộ điều phối và phân loại thông điệp chuẩn NetOps Protocol v0 phía Agent (IAgentMessageControl)
// với vòng đời Singleton để quản lý tập trung việc định tuyến message và phân phối sự kiện.
builder.Services.AddSingleton<IAgentMessageControl, AgentMessageControl>();

// Đăng ký client mạng quản lý kết nối socket tới Master với vòng đời Singleton.
// Sử dụng triển khai TcpAgentClient làm instance cốt lõi và ánh xạ đồng thời sang cả
// AgentNetworkClient lẫn IAgentNetworkClient để đảm bảo:
// 1. Chỉ tồn tại DUY NHẤT một instance kết nối TCP trong suốt thời gian ứng dụng chạy.
// 2. Worker và các thành phần khác có thể phụ thuộc linh hoạt vào Interface IAgentNetworkClient.
// 3. Tương thích ngược 100% với bất kỳ thành phần cũ nào yêu cầu TcpAgentClient.
builder.Services.AddSingleton<TcpAgentClient>();
builder.Services.AddSingleton<AgentNetworkClient>(sp => sp.GetRequiredService<TcpAgentClient>());
builder.Services.AddSingleton<IAgentNetworkClient>(sp => sp.GetRequiredService<TcpAgentClient>());
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

// Xử lý cờ pre-flight validation (--validate-only hoặc --dry-run):
// Thẩm định nhanh đồ thị DI Container mà không kích hoạt kết nối mạng thật tới Master
var isValidateOnly = args.Contains("--validate-only", StringComparer.OrdinalIgnoreCase) ||
                     args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

if (isValidateOnly)
{
    Console.WriteLine("[DI Validation] Starting Pre-flight Dependency Injection Validation...");
    try
    {
        var messageControl = host.Services.GetRequiredService<IAgentMessageControl>();
        var networkClient = host.Services.GetRequiredService<IAgentNetworkClient>();
        var tcpClient = host.Services.GetRequiredService<TcpAgentClient>();
        var agentNetworkClient = host.Services.GetRequiredService<AgentNetworkClient>();
        var hostedServices = host.Services.GetServices<IHostedService>();

        var worker = hostedServices.OfType<Worker>().FirstOrDefault()
            ?? throw new InvalidOperationException("Worker hosted service was not found in service provider.");

        Console.WriteLine($"[DI Validation] Verified IAgentMessageControl: {messageControl.GetType().FullName}");
        Console.WriteLine($"[DI Validation] Verified IAgentNetworkClient: {networkClient.GetType().FullName}");
        Console.WriteLine($"[DI Validation] Verified TcpAgentClient: {tcpClient.GetType().FullName}");
        Console.WriteLine($"[DI Validation] Verified AgentNetworkClient: {agentNetworkClient.GetType().FullName}");
        Console.WriteLine($"[DI Validation] Verified Worker HostedService: {worker.GetType().FullName}");
        Console.WriteLine("[DI Validation] All services resolved successfully.");

        Environment.ExitCode = 0;
        return;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[DI Validation FAILED] {ex.Message}");
        Console.ResetColor();
        Environment.ExitCode = 1;
        return;
    }
}

// Kích hoạt toàn bộ các HostedService đã đăng ký (chính thức chạy hàm Worker.ExecuteAsync).
// Block luồng chính để giữ tiến trình luôn sống. Ứng dụng sẽ duy trì 
// trạng thái này cho đến khi nhận tín hiệu dừng từ hệ điều hành 
// (như phím tắt Ctrl + C, lệnh kill / SIGTERM trên Linux, hoặc lệnh stop service từ Windows Services / systemd).
host.Run();