using Grpc.Core;
using Grpc.Net.Client;
using NetOps.Agent;
using NetOps.Shared;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var info = SystemInfoService.GetRegistrationInfo();
Console.WriteLine($"Đã đóng gói thành công máy: {info.Hostname} (ID: {info.AgentId})");

while (true)
{
    try
    {
        using var channel = GrpcChannel.ForAddress("https://localhost:7027");
        var client = new AgentHub.AgentHubClient(channel);
        Console.WriteLine("Đã tạo gRPC Client thành công");

        // Mở luồng stream 2 chiều
        using var stream = client.ConnectStream();

        // Gửi gói tin đăng ký qua luồng
        await stream.RequestStream.WriteAsync(new AgentMessage { RegisterRequest = info });
        Console.WriteLine("Đã gửi gói tin đăng ký sang Server thành công!");

        // Chờ Server gửi câu chào mừng về
        if (await stream.ResponseStream.MoveNext(CancellationToken.None))
        {
            var reply = stream.ResponseStream.Current;
            Console.WriteLine($"Server phản hồi: {reply.RegisterResponse.Message}");
        }

        // Tạo timer đếm chu kỳ 5 giây
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (await timer.WaitForNextTickAsync())
        {
            var heartbeat = new HeartbeatMessage
            {
                AgentId = info.AgentId,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                CpuUsagePercent = 0.0, // Thu thập ở bước sau
                MemoryUsedBytes = 0
            };
            // Gửi nhịp tim sang Server qua luồng gRPC
            await stream.RequestStream.WriteAsync(new AgentMessage { Heartbeat = heartbeat });
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] -> Đã gửi Heartbeat tới Server.");
        }
    }
    // Thuộc namespace Grpc.Core, nó là 1 class
    // Đây là ngoại lệ mà thư viện gRPC ném ra khi đường truyền mạng bị đứt hoặc Server bị tắt
    catch(RpcException ex)
    {
        // Bắt lỗi mất kết nối gRPC (Server tắt hoặc rớt mạng)
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[!] Mất kết nối tới Server ({ex.StatusCode}). Thử kết nối lại sau 5 giây...");
        Console.ResetColor();
        // Tạm dừng 5s trước khi quay lại vòng lặp
        await Task.Delay(5000);
    }
    catch(Exception ex)
    {
        // Bắt các lỗi mạng khác (Socket, DNS, v.v...)
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[!] Sự cố mạng: {ex.Message}. Thử kết nối lại sau 5 giây...");
        Console.ResetColor();
        await Task.Delay(5000);
    }
}