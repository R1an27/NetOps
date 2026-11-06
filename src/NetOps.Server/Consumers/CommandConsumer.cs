using Grpc.Core;
using MassTransit;
using NetOps.Shared;
using NetOps.Shared.Messages;
using NetOps.Server.Services; // Thay bằng namespace chứa AgentConnectionManager của bạn
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace NetOps.Server.Consumers
{
    /// <summary>
    /// Consumer túc trực ở RabbitMQ, nhận lệnh và điều phối xuống Agent qua gRPC
    /// </summary>
    public class CommandConsumer : IConsumer<RunCommandMessage>
    {
        private readonly AgentConnectionManager _connectionManager;
        private readonly ILogger<CommandConsumer> _logger;

        public CommandConsumer(AgentConnectionManager connectionManager, ILogger<CommandConsumer> logger)
        {
            _connectionManager = connectionManager;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<RunCommandMessage> context)
        {
            var message = context.Message;
            _logger.LogInformation("Đã nhận lệnh {CommandId} từ Queue cho Agent {AgentId}", message.CommandId, message.AgentId);

            // 1. Tìm Agent trong cuốn danh bạ (Dictionary)
            // LƯU Ý: Bạn cần thêm hàm GetConnection(agentId) trong AgentConnectionManager 
            // để trả về IServerStreamWriter<ServerMessage> tương ứng với AgentId
            var agentStream = _connectionManager.GetConnection(message.AgentId);
            
            if (agentStream == null)
            {
                _logger.LogWarning("Agent {AgentId} hiện đang offline. Lệnh {CommandId} không thể gửi đi.", message.AgentId, message.CommandId);
                // Xử lý khi Agent offline (Lưu DB trạng thái Failed, hoặc throw lỗi để MassTransit retry)
                return; 
            }

            // 2. Gói lệnh vào Protobuf Message (ServerMessage)
            var grpcMessage = new ServerMessage
            {
                CommandRequest = new CommandRequest // Chắc chắn bạn đã định nghĩa message này trong netops.proto
                {
                    CommandId = message.CommandId,
                    CommandText = message.CommandText,
                    ShellType = message.ShellType,
                    TimeoutSeconds = message.TimeoutSeconds
                }
            };

            // 3. Đẩy lệnh xuống luồng gRPC của Agent
            try
            {
                await agentStream.WriteAsync(grpcMessage);
                _logger.LogInformation("Đã đẩy thành công lệnh {CommandId} xuống Agent {AgentId}", message.CommandId, message.AgentId);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "Lỗi gRPC khi đẩy lệnh {CommandId} xuống Agent {AgentId}", message.CommandId, message.AgentId);
                throw; // Throw để MassTransit tự động đẩy lại (retry) hoặc đưa vào Dead Letter Queue
            }
        }
    }
}
