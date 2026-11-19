using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using NetOps.Server.Hubs;
using NetOps.Server.Services;
using NetOps.Shared;
using NetOps.Shared.Messages;

namespace NetOps.Server.Consumers;

/// <summary>
/// Consumer túc trực ở hàng đợi RabbitMQ ("agent-commands-queue"):
/// cứ có lệnh mới (RunCommandMessage) đẩy vào là lấy ra điều phối xuống máy trạm (Agent).
/// </summary>
// Cú pháp viết tham số ngay sau tên class là Primary Constructor (C# 12+):
// giúp nhận 5 service từ DI Container của ASP.NET Core mà không cần khai báo field + constructor dài dòng.
// Vai trò của từng service được truyền vào:
// - connections:   tra xem Agent có đang online không và lấy ConnectionId phiên SignalR của Agent đó.
// - agentHub:      kênh SignalR nói chuyện với Agent (dùng để đẩy lệnh "ReceiveCommand" xuống Agent).
// - desktopHub:    kênh SignalR nói chuyện với Desktop (dùng để báo lỗi ngược về Desktop nếu Agent đã offline).
// - commandRoutes: bộ nhớ tạm (RAM) lưu bảng ánh xạ "command:<CommandId> -> ConnectionId của Desktop".
// - logger:        ghi nhật ký vận hành của Server.
public class CommandConsumer(
    AgentConnectionManager connections,
    IHubContext<AgentHubService> agentHub,
    IHubContext<NetOpsHub> desktopHub,
    IMemoryCache commandRoutes,
    ILogger<CommandConsumer> logger) : IConsumer<RunCommandMessage>
{
    // MassTransit tự động gọi hàm Consume() mỗi khi lấy được 1 RunCommandMessage từ RabbitMQ.
    public async Task Consume(ConsumeContext<RunCommandMessage> context)
    {
        var msg = context.Message;
        logger.LogInformation("Đã nhận lệnh {CommandId} cho Agent {AgentId}", msg.CommandId, msg.AgentId);

        // Bước 1: Kiểm tra Agent mục tiêu hiện còn giữ kết nối SignalR tới Server hay không.
        // Tình huống thực tế hay gặp: lúc Desktop vừa bấm Send thì Agent còn online, nhưng trong tích tắc
        // lệnh đi qua RabbitMQ thì Agent bị rớt mạng hoặc tắt máy.
        if (!connections.TryGetConnection(msg.AgentId, out var conn) || conn is null)
        {
            logger.LogWarning("Agent {AgentId} đang offline, hủy lệnh {CommandId}.", msg.AgentId, msg.CommandId);

            // Báo lỗi ngay về màn hình Desktop và dọn địa chỉ tạm trong RAM (không im lặng bỏ qua để tránh Desktop treo chờ mãi).
            await NotifyOfflineAsync(msg, context.CancellationToken);
            return;
        }

        // Bước 2: Đóng gói dữ liệu lệnh thành CommandRequest để gửi xuống Agent qua SignalR.
        var request = new CommandRequest
        {
            CommandId = msg.CommandId,
            CommandText = msg.CommandText,
            TimeoutSeconds = msg.TimeoutSeconds
        };

        // Bước 3: Gửi sự kiện "ReceiveCommand" tới đúng phiên SignalR (ConnectionId) hiện hành của Agent.
        try
        {
            await agentHub.Clients.Client(conn.ConnectionId)
                .SendAsync("ReceiveCommand", request, context.CancellationToken);

            logger.LogInformation("Đã đẩy lệnh {CommandId} xuống Agent {AgentId} (phiên {ConnectionId})",
                msg.CommandId, msg.AgentId, conn.ConnectionId);
        }
        // "when (ex is not OperationCanceledException)": chỉ bắt lỗi thật (mạng/SignalR),
        // không ghi log lỗi nếu Server chỉ đang dừng ứng dụng nên hủy token.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Lỗi SignalR khi đẩy lệnh {CommandId} xuống Agent {AgentId}", msg.CommandId, msg.AgentId);
            // Ném lại lỗi để MassTransit biết chưa đẩy được và tự thử lại (retry 3 lần), nếu vẫn hỏng thì đẩy sang error queue.
            throw;
        }
    }

    // Xử lý khi Agent đã offline trước lúc nhận được lệnh:
    //   Tra RAM tìm máy Desktop đã gửi lệnh -> xóa ngay mục tạm khỏi RAM -> gửi gói báo lỗi kết thúc về Desktop.
    private async Task NotifyOfflineAsync(RunCommandMessage msg, CancellationToken ct)
    {
        // Key này phải khớp hoàn toàn với key lúc NetOpsHub.SubmitCommand lưu vào RAM.
        var cacheKey = $"command:{msg.CommandId}";
        if (!commandRoutes.TryGetValue<string>(cacheKey, out var desktopId) || string.IsNullOrWhiteSpace(desktopId))
            return;

        // Xóa ngay địa chỉ định tuyến khỏi RAM trước khi gửi thông báo để tránh rò rỉ bộ nhớ,
        // kể cả nếu bước gửi mạng bên dưới bị lỗi hoặc bị hủy.
        commandRoutes.Remove(cacheKey);

        try
        {
            // Gửi đúng sự kiện "CommandOutputReceived" mà Form1 trên Desktop đang lắng nghe.
            // Đặt IsCompleted = true và ExitCode = -1 để giao diện Desktop lập tức chuyển badge sang lỗi (đỏ)
            // và kết thúc trạng thái "Waiting for output".
            await desktopHub.Clients.Client(desktopId).SendAsync("CommandOutputReceived", new CommandOutput
            {
                AgentId = msg.AgentId,
                CommandId = msg.CommandId,
                OutputLine = $"[ERROR] Agent {msg.AgentId} đang offline, không thể thực thi lệnh.",
                IsError = true,
                IsCompleted = true,
                ExitCode = -1
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nếu Desktop cũng đã đóng cửa sổ/mất mạng thì chỉ ghi cảnh báo, không ném lỗi làm RabbitMQ retry vô ích.
            logger.LogWarning(ex, "Không gửi được thông báo offline cho lệnh {CommandId}.", msg.CommandId);
        }
    }
}
