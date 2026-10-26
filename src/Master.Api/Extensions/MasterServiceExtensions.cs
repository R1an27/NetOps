using Master.Api.Options;
using Master.Api.Services;

namespace Master.Api.Extensions;

/// <summary>
/// Các phương thức mở rộng (Extension Methods) cấu hình và đăng ký dịch vụ Master Server vào IServiceCollection.
/// Giúp chuẩn hóa, module hóa và làm sạch tệp Program.cs theo tiêu chuẩn Clean Architecture.
/// </summary>
public static class MasterServiceExtensions
{
    /// <summary>
    /// Đăng ký toàn bộ các dịch vụ thành phần của NetOps Master Server vào DI Container:
    /// - Cấu hình Options (MasterServerOptions)
    /// - IConnectionManager (Singleton)
    /// - IMasterMessageControl (Singleton)
    /// - TcpManagerServer (BackgroundService / HostedService)
    /// - SessionWatchdogService (BackgroundService / HostedService)
    /// </summary>
    /// <param name="services">Tập hợp dịch vụ IServiceCollection của ứng dụng</param>
    /// <param name="configuration">Đối tượng cấu hình IConfiguration</param>
    /// <returns>Đối tượng IServiceCollection phục vụ fluent chaining</returns>
    public static IServiceCollection AddNetOpsMasterServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Đảm bảo IConfiguration luôn có sẵn trong DI Container
        services.AddSingleton(configuration);

        // 1. Đăng ký và nạp Options Pattern từ section MasterServer hoặc fallback TcpServer
        services.Configure<MasterServerOptions>(options =>
        {
            var masterSection = configuration.GetSection(MasterServerOptions.SectionName);
            if (masterSection.Exists())
            {
                masterSection.Bind(options);
            }
            else
            {
                options.Port = configuration.GetValue<int>("TcpServer:Port", 5050);
            }
        });

        // 2. Đăng ký Agent Registry quản lý trạng thái sống còn của Agent tập trung đa luồng (T16.1)
        services.AddSingleton<IAgentRegistry, AgentRegistry>();

        // 3. Đăng ký bộ quản lý phiên kết nối AgentSession an toàn đa luồng (Singleton)
        services.AddSingleton<IConnectionManager, ConnectionManager>();

        // 4. Đăng ký bộ điều phối và phân loại thông điệp chuẩn NetOps Protocol v0 (Singleton, T16.2)
        services.AddSingleton<IMasterMessageControl, MasterMessageControl>();

        // 5. Đăng ký TCP Manager Server chạy nền tiếp nhận kết nối Socket từ các Agent
        services.AddHostedService<TcpManagerServer>();

        // 6. Đăng ký Heartbeat Timeout Watchdog giám sát sống còn và phát hiện timeout im lặng >15s định kỳ 2-3s (T16.3)
        services.AddHostedService<HeartbeatTimeoutWatchdog>();

        // 7. Đăng ký Session Watchdog Service chạy nền giám sát và dọn dẹp Half-Open Socket định kỳ (tương thích ngược)
        services.AddHostedService<SessionWatchdogService>();

        return services;
    }
}
