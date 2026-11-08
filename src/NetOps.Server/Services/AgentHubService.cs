// Namespace chứa toàn bộ các khái niệm và giao diện nền tảng của công nghệ gRPC trong C#
using Grpc.Core;
using NetOps.Shared;

using Microsoft.AspNetCore.SignalR;
using NetOps.Server.Hubs;

namespace NetOps.Server.Services;

using Microsoft.Extensions.Caching.Memory;

// Kế thừa từ lớp AgentHub.AgentHubBase (được sinh ra do quá trình compile file netops.proto)
public class AgentHubService: AgentHub.AgentHubBase
{
    // Khai báo cuốn danh bạ
    private readonly AgentConnectionManager _connectionManager;

    // IHubContext<NetOpsHub> là kiểu do MS cugn cấp, cho phép 1 service khác
    // gửi thông báo đến Desktop đang kết nối với Hub.
    private readonly IHubContext<NetOpsHub> _hubContext;
    // Constructor: ASP.NET Core sẽ tự động truyền instance của AgentConnectionManager vào đây

    // Khai báo cả ở đây để AgentHubServices đọc được thông tin mà CommandGrpcService lưu
    private readonly IMemoryCache _commandRoutes;
    public AgentHubService(AgentConnectionManager connectionManager, IHubContext<NetOpsHub> hubContext, IMemoryCache commandRoutes)
    {
        _connectionManager = connectionManager;
        _hubContext = hubContext;
        _commandRoutes = commandRoutes;
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
                        currentAgentId = reg.AgentId;

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

                    //
                    case AgentMessage.PayloadOneofCase.CommandOutput:
                        // lấy kết quả Agent vừa gửi
                        var output = message.CommandOutput;
                        var cacheKey = $"command:{output.CommandId}";
                                            // Tìm connection ID kết nối từ Desktop tới Server đã lưu
                        if (!_commandRoutes.TryGetValue<string>(
                                cacheKey, out var desktopConnectionId)
                            || string.IsNullOrWhiteSpace(desktopConnectionId))
                        {
                            break;
                        }

                        try
                        {
                            await _hubContext.Clients
                                // chọn kết nối Desktop đó.
                                .Client(desktopConnectionId)
                                // gửi toàn bộ bản tin output tới handler Desktop đã đăng ký.
                                .SendAsync("CommandOutputReceived", output);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Không gửi được output tới Desktop: {ex.Message}");
                        }

                        if (output.IsCompleted)
                        {
                            // xóa thông tin định tuyến của lệnh vừa kết thúc.
                            _commandRoutes.Remove(cacheKey);
                        }

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