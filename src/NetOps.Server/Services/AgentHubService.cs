// Namespace chứa toàn bộ các khái niệm và giao diện nền tảng của công nghệ gRPC trong C#
using Grpc.Core;
using NetOps.Shared;

namespace NetOps.Server.Services;

// Kế thừa từ lớp AgentHub.AgentHubBase (được sinh ra do quá trình compile file netops.proto)
public class AgentHubService: AgentHub.AgentHubBase
{
    // Khai báo cuốn danh bạ
    private readonly AgentConnectionManager _connectionManager;

    // Constructor: ASP.NET Core sẽ tự động truyền instance của AgentConnectionManager vào đây
    public AgentHubService(AgentConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    // Override lại ConnectStream (phương thức virtual trong AgentHubBase - phương thức này do t đặt tên trong netops.proto)
    public override async Task ConnectStream(
        // 3 args này trong thư viện Grpc.Core của MS và GG nha
        // Inbound giúp Server lắng nghe các tin nhắn AgentMessage do Agent gửi.
        IAsyncStreamReader<AgentMessage> requestStream,
        // Outbound giúp Server gửi ServerMessage xuống cho Agent
        IServerStreamWriter<ServerMessage> responseStream,
        // Chứa context của cuộc gọi mạng gồm: IP của máy đích, timeout time, token báo ngắt kn
        ServerCallContext context)
    {
        string? currentAgentId = null;
        try
        {
            // Thêm đọc từng tin nhắn mà Agent đẩy lên
            // requestStream.ReadAllAsync() là Extension Method trong Grpc.Core
            // Nó biến luồng dữ liệu gRPC thành 1 danh sách bất đồng bộ (IAsyncEnumerable)
            // cho phép dùng vòng lặp await foreach để cứ có message mới là tự chạy vào thân vòng lặp
            // context.CancellationToken giúp nhận biết ngay khi Agent đứt mạng
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                // Bóc phong bì kiểm tra xem là loại tin nhắn nào
                switch (message.PayloadCase)
                {
                    // Trường hợp 1: Agent gửi thông tin Đăng ký máy
                    case AgentMessage.PayloadOneofCase.RegisterRequest:
                        var reg = message.RegisterRequest;
                        Console.WriteLine($"Agent connect: ID={reg.AgentId}, Hostname={reg.Hostname}");

                        // Lưu AgentId và responseStream (Tin nhắn chào mừng mà server gửi) vào danh bạ
                        _connectionManager.Register(reg.AgentId, responseStream, reg);

                        // Server bắn tin nhắn phản hồi chào mừng ngược xuống cho Agent
                        await responseStream.WriteAsync(new ServerMessage
                        {
                            RegisterResponse = new RegisterAgentResponse
                            {
                                Success = true,
                                Message = "Server da nhan dang ky thanh cong!"
                            }
                        });
                        break;

                    // Trường hợp 2: Agent gửi nhịp tim Heartbeat định kỳ
                    case AgentMessage.PayloadOneofCase.Heartbeat:
                        var hb = message.Heartbeat;
                        // Cập nhật thời gian của hearbeat
                        _connectionManager.UpdateHeartbeat(hb.AgentId, hb);
                        Console.WriteLine($"Nhan Heartbeat tu {hb.AgentId}: CPU {hb.CpuUsagePercent}%");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Bắt sự kiện khi Agent ngắt kết nối hoặc hủy stream
        }
        finally
        {
            // Khi cuộc gọi kết thúc (dù bình thường hay do mất mạng),
            // khối finally LUÔN LUÔN chạy để xóa Agent khỏi danh bạ!
            if (!string.IsNullOrEmpty(currentAgentId))
            {
                _connectionManager.Unregister(currentAgentId);
            }
        }
        // 
    }
}