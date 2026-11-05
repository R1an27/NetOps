using Grpc.Net.Client;
using NetOps.Agent;
using NetOps.Shared;

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

Console.WriteLine("Nhấn phím bất kỳ để ngắt kết nối và thoát...");
Console.ReadKey();