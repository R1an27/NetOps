using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NetOps.Server.Consumers;

namespace NetOps.Server.Extensions
{
    public static class MassTransitExtensions
    {
        /// <summary>
        /// Hàm mở rộng giúp đăng ký MassTransit & RabbitMQ gọn gàng hơn trong Program.cs
        /// </summary>
        public static IServiceCollection AddRabbitMqMassTransit(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddMassTransit(x =>
            { 
                // 1. Đăng ký Consumer
                x.AddConsumer<CommandConsumer>();

                // 2. Cấu hình RabbitMQ
                x.UsingRabbitMq((context, cfg) =>
                {
                    // Lấy connection string từ appsettings.json hoặc .NET Aspire
                    // Nếu bạn dùng Aspire, connection string thường được inject tự động qua tên tài nguyên
                    var rabbitConnectionString = configuration.GetConnectionString("rabbitmq") ?? "rabbitmq://localhost";
                    cfg.Host(rabbitConnectionString);

                    // 3. Tạo Queue tên là "agent-commands-queue"
                    cfg.ReceiveEndpoint("agent-commands-queue", e =>
                    {
                        // Gắn Consumer vào Queue này
                        e.ConfigureConsumer<CommandConsumer>(context);
                        
                        // Cấu hình Retry (Ví dụ: thử lại 3 lần, mỗi lần cách nhau 5 giây nếu lỗi mạng)
                        e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                    });
                });
            });

            return services;
        }
    }
}
