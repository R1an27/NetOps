// [Introduction] Đây là thành phần để Server xử lý yêu cầu chạy lệnh mà Desktop gửi tới

// [7.1] Namespace này cung cấp ClaimTypes,
// trong đó có hằng số NameIdentifier dùng để nhận diện loại claim chứa định danh người dùng.
using System.Security.Claims;

// [5.3] Gắn chính sách vào SubmitCommand()
using Microsoft.AspNetCore.Authorization;

using Grpc.Core;
using MassTransit;
using NetOps.Shared;
using NetOps.Shared.Messages;
using Microsoft.Extensions.Caching.Memory;

namespace NetOps.Server.Services;

// Class CommandService.CommandServiceBase được sinh ra từ `service CommandService`
// trong [1:NetOps.Shared/Protos/netops.proto]
public class CommandGrpcService : CommandService.CommandServiceBase
{
    // Tra xem Server có đang kết nối tới Agent được yêu gửi lệnh tới không
    // ví dụ int SoLuong thì khai báo rằng SoLuong giữ một số nguyên
    // Còn _connections giữ tham chiếu tới một đối tượng AgentConnectionManager
    // Ở đây thêm readonly để biến chỉ gán được trong constructor, sau đó khôn được gán nữa
    // ➞ Um chỉ best practices thôi
    private readonly AgentConnectionManager _connections;

    // ISendEndpointProvider là kiểu interface của công cụ lấy ĐIỂM gửi message
    // Đối tượng được lấy từ chỗ đã đăng ký MassTransit trong Program.cs do th Trằn Tuyển viết
    // @TranTuyen
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly ILogger<CommandGrpcService> _logger;

    // Nơi lưu giữ THÔNG TIN ĐỊNH TUYẾN command output
    private readonly IMemoryCache _commandRoutes;

    public CommandGrpcService(
        AgentConnectionManager connections,
        ISendEndpointProvider sendEndpointProvider,
        ILogger<CommandGrpcService> logger,
        IMemoryCache commandRoutes)
    {
        _connections = connections;
        _sendEndpointProvider = sendEndpointProvider;
        _logger = logger;
        _commandRoutes = commandRoutes;
    }

    // Method này để Server xử lý lệnh gửi từ desktop
    // Nó override từ method cùng tên có trong class cha
    // async để cho phép dùng await trong method này
    // Task<SubmitCommandResponse> nghĩa là: method sẽ trả về
    // 1 object đại diện cho công việc mà method đang xử lý
    // Khi công việc THÀNH CÔNG sẽ trả về kết quả có kiểu SubmitCommandResponse
    // request là đối tượng request mà desktop gửi tới
    // context là thông tin về cuộc gọi gRPC hiện tại chứa thời hạn cuộc gọi và tín huỷ huỷ.

    // Dòng trong dấu ngoặc vuông là một attribute: thông tin khai báo được gắn vào phương thức.
    // Authorize là attribute có sẵn của Microsoft ASP.NET Core.
    // Trước khi cho phép chạy SubmitCommand(), hãy kiểm tra người gọi đáp ứng chính sách RunCommands.
    // https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz?view=aspnetcore-10.0#authorize-users-to-access-services-and-service-methods
    [Authorize(Policy = "RunCommands")]
    public override async Task<SubmitCommandResponse> SubmitCommand(
        SubmitCommandRequest request,
        ServerCallContext context)
    {
        // 1. Kiểm tra xem lệnh gửi từ Desktop tới Server.
        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "AgentId không được để trống."));
        }

        if (string.IsNullOrWhiteSpace(request.CommandText))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Nội dung lệnh không được để trống."));
        }

        // API quy định thời gian chạy từ 1 giây đến 1 giờ.
        if (request.TimeoutSeconds < 1 || request.TimeoutSeconds > 3600)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "TimeoutSeconds phải nằm trong khoảng 1–3600."));
        }

        // Kiểm tra có phải chủ kết nối là thành phần nhận output không
        // context.GetHttpContext()	Lấy thông tin HTTP của lời gọi gRPC hiện tại.
        // .User Lấy danh tính mà Server đã xác thực từ token.
        // .FindFirst(ClaimTypes.NameIdentifier) Tìm claim định danh người dùng.
        // ?.Value	Lấy giá trị của claim nếu tìm thấy.
        // Microsoft hướng dẫn truy cập người dùng trong gRPC qua context.GetHttpContext().User.
        // https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz?view=aspnetcore-10.0#authenticate-users-calling-a-grpc-service
        var userId = context.GetHttpContext().User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "Không tìm thấy định danh người gửi lệnh."));
        }

        // [7.2] Đọc chủ kết nối từ bộ nhớ
        // foundOwner: có tìm thấy mục cần tra hay không.
        // ownerId: ID người sở hữu kết nối được lấy ra.
        // <string>: giá trị chúng ta muốn đọc có kiểu chuỗi.
        var foundOwner = _commandRoutes.TryGetValue<string>(
            $"desktop:{request.DesktopConnectionId}",
            out var ownerId);

        if (!foundOwner || ownerId != userId)
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "Kết nối nhận output không hợp lệ hoặc không thuộc tài khoản của bạn."));
        }

        // 2. Kiểm tra kết nối của Agent.
        // found: cho biết có tìm thấy hay không.
        // connection: chứa đối tượng kết nối nếu tìm thấy.
        // out: gán kq vào biến connection
        // var: Để C# tự xác định kdl
        bool found = _connections.TryGetConnection(request.AgentId, out var connection);

        // [7.3] Quyết định cho phép hay từ chối
        // Nghĩa là từ chối khi:
        // -Không tìm thấy thông tin chủ kết nối.
        // - Hoặc tìm thấy nhưng chủ kết nối khác người gửi lệnh.
        if (!found || connection is null)
        {
            // Phát sinh lỗi, dừng phần xử lý hiện tại của phương thức
                  // Tạo exception gRPC chứa trạng thái lỗi đó đó <= // Tạo thông tinh trạng thái lỗi
            throw new RpcException(new Status(
                // Mã lỗi: điều kiện cần để thực hiện yêu cầu chưa được đáp ứng
                StatusCode.FailedPrecondition,
                "Không tìm thấy kết nối của Agent."));
        }

        // 3. Tạo mã nhận diện cho lần thực thi này.
        // Guid.NewGuid() tạo một GUID mới (kiểu Guid)
        // Chuyển GUID này thành dạng string với mã định dạng "N"
        // Mã định dạng "N": 32 ký tự hexadecimal, bỏ dấu gạch ngang trong Guid
        var commandId = Guid.NewGuid().ToString("N");

        // Điều chỉnh lại thông tin định tuyến
        _commandRoutes.Set($"command:{commandId}", request.DesktopConnectionId);

        // 4. Đóng gói dữ liệu gửi qua RabbitMQ.
        var message = new RunCommandMessage
        {
            AgentId = request.AgentId,
            CommandId = commandId,
            CommandText = request.CommandText,
            TimeoutSeconds = request.TimeoutSeconds
        };

        // 5. Gửi message vào queue.
        // Bọc bằng try để tránh phát sinh lỗi
        // giáo tiếp với RabbitMQ có thể lỗi, mất kn
        // Nếu lỗi thì đưa vào catch xử lý
        try
        {
            // Nhờ _sendEndpointProvider lấy cho tôi một điểm gửi tới queue agent-commands-queue, rồi giữ nó trong biến endpoint.

            // GetSendEndpoint(...): Lấy công cụ để gửi tới địa chỉ trong các đối số được đặt vào
            // await chờ lấy được kết quả rồi mới gán cho biến endpoint và chạy dòng tiếp theo.
            var endpoint = await _sendEndpointProvider.GetSendEndpoint(
                // Tạo đối tượng mô tả địa chỉ đích
                // queue: loại đích là queue
                // agent-commands-queue: tên queue
                // new Uri: tạo 1 object địa chỉ
                // @TranTuyen: Địa chỉ của RabbitMQ, tài khoản và các config
                // đã được thiết lập trong AddRabbitMqMassTransit(...)
                new Uri("queue:agent-commands-queue"));

            // ➞ _sendEndpointProvider: lấy điểm gửi theo địa chỉ truyền vào
            // ➞ endpoint: lúc này là đối tượng có chức năng gửi

            // endpoint.Send(...): dùng đối tượng đó để gửi message.
            await endpoint.Send(message, context.CancellationToken);
        }
        catch (OperationCanceledException)
            when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Lỗi gửi lệnh {CommandId} vào RabbitMQ.",
                commandId);

            throw new RpcException(new Status(
                StatusCode.Unavailable,
                "Không xác nhận được việc gửi lệnh vào hàng đợi."));
        }

        // 6. Trả mã lệnh cho Desktop.
        return new SubmitCommandResponse
        {
            CommandId = commandId
        };
    }
}