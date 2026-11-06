using Grpc.Net.Client;
using NetOps.Agent;
using NetOps.Agent.Services;
using NetOps.Shared;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var executorLogger = loggerFactory.CreateLogger<CommandExecutor>();
var commandExecutor = new CommandExecutor(executorLogger);

var info = SystemInfoService.GetRegistrationInfo();
Console.WriteLine($"Đã đóng gói thành công máy: {info.Hostname} (ID: {info.AgentId})");

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

// Task 1: Gửi Heartbeat định kỳ mỗi 5 giây
var heartbeatTask = Task.Run(async () =>
{
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
        await stream.RequestStream.WriteAsync(new AgentMessage { Heartbeat = heartbeat });
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] -> Đã gửi Heartbeat tới Server.");
    }
});

// Task 2: Lắng nghe lệnh từ Server (CommandRequest) và thực thi
var receiveTask = Task.Run(async () =>
{
    while (await stream.ResponseStream.MoveNext(CancellationToken.None))
    {
        var serverMsg = stream.ResponseStream.Current;
        if (serverMsg.PayloadCase == ServerMessage.PayloadOneofCase.CommandRequest)
        {
            var cmdReq = serverMsg.CommandRequest;
            Console.WriteLine($"Nhận lệnh {cmdReq.CommandId}: {cmdReq.CommandText}");
            // Gọi hàm chạy lệnh (không await trực tiếp để không block vòng lặp nhận tin)
            _ = commandExecutor.ExecuteCommandAsync(cmdReq, stream.RequestStream, info.AgentId);
        }
    }
});

// Chờ cả 2 task
await Task.WhenAny(heartbeatTask, receiveTask);

Console.WriteLine("Nhấn phím bất kỳ để ngắt kết nối và thoát...");
Console.ReadKey();