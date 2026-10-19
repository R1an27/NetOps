using Agent.Worker.Services;

namespace Agent.Worker;

// Kế thừa BackgroundService: biến Woker thành 1 dv chạy ngầm managed by .NET


public class Worker(
    // Ghi nhật ký tiến trình khởi động của Worker.
    ILogger<Worker> logger,
    // Bộ nạp cấu hình hệ thống (đọc appsetting.json, env var)
    IConfiguration configuration,
    // Client mạng đã viết trước đó, inject vào để thực hiện nv truyền thông
    TcpAgentClient tcpClient) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Các lệnh này truy xuất trực tiếp vào khối "AgentConfig" trong appsettings.json.

        // Toán tử ?? "127.0.0.1" đảm bảo nếu người dùng quên khai báo key 
        // trong file JSON hoặc gán giá trị null, ứng dụng vẫn có giá trị mặc định để chạy.
        string host = configuration.GetValue<string>("AgentConfig:ServerHost") ?? "127.0.0.1";
        // Với số (int): Tham số thứ hai của GetValue<int>(key, defaultValue) 
        // (ví dụ: 5050, 5) đóng vai trò giá trị mặc định nếu key bị thiếu hoặc parse lỗi.
        int port = configuration.GetValue<int>("AgentConfig:ServerPort", 5050);
        string agentId = configuration.GetValue<string>("AgentConfig:AgentId") ?? "agent-linux-01";
        int pingSeconds = configuration.GetValue<int>("AgentConfig:PingIntervalSeconds", 5);

        // ➞ Nhờ thiết kế phòng thủ này, ứng dụng sẽ không bao giờ bị crash 
        // (ném NullReferenceException) ngay khi vừa khởi động do lỗi cấu hình.

        // Dùng cú pháp template {AgentId}, {Host}, {Port} thay vì cộng chuỗi ($"").
        // Giúp các hệ thống thu thập log tập trung (như ElasticSearch, Seq, Grafana Loki)
        // có thể lập index và lọc riêng từng trường dữ liệu một cách tối ưu.
        logger.LogInformation("Agent.Worker starting with AgentId: {AgentId}, Target: {Host}:{Port}",
            agentId, host, port);

        // TimeSpan.FromSeconds(pingSeconds): Chuyển đổi số nguyên giây từ 
        // file JSON thành cấu trúc thời gian chuẩn TimeSpan cho hàm delay.
        // Chuyển giao quyền điều khiển: Worker gọi await tcpClient.RunAsync(...) 
        // và treo chờ tại đây. Toàn bộ chu trình kết nối, thử lại (retry loop), 
        // gửi Ping và nhận Pong sẽ được TcpAgentClient đảm nhiệm.
        // Gắn kết vòng đời qua stoppingToken: Khi ứng dụng Agent nhận tín hiệu 
        // dừng (bấm Ctrl + C hoặc tiến trình systemd/Docker gửi SIGTERM), 
        // stoppingToken sẽ tự động chuyển sang trạng thái đã kích hoạt (IsCancellationRequested = true), 
        // báo hiệu cho TcpAgentClient đóng socket và Graceful Shutdown.
        await tcpClient.RunAsync(host, port, agentId, TimeSpan.FromSeconds(pingSeconds), stoppingToken);
    }
}