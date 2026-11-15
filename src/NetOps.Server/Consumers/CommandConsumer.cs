using MassTransit;
using Microsoft.AspNetCore.SignalR;
using NetOps.Server.Services;
using NetOps.Shared;
using NetOps.Shared.Messages;

namespace NetOps.Server.Consumers
{
    /// <summary>
    /// Consumer túc trực ở RabbitMQ, nhận lệnh và điều phối xuống Agent qua SignalR Hub.
    /// </summary>
    public class CommandConsumer : IConsumer<RunCommandMessage>
    {
        private readonly AgentConnectionManager _connectionManager;
        private readonly IHubContext<AgentHubService> _agentHubContext;
        private readonly ILogger<CommandConsumer> _logger;

        public CommandConsumer(
            AgentConnectionManager connectionManager,
            IHubContext<AgentHubService> agentHubContext,
            ILogger<CommandConsumer> logger)
        {
            _connectionManager = connectionManager;
            _agentHubContext = agentHubContext;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<RunCommandMessage> context)
        {
            var message = context.Message;
            _logger.LogInformation("Đã nhận lệnh {CommandId} từ Queue cho Agent {AgentId}", message.CommandId, message.AgentId);

            // 1. Tìm phiên SignalR đang hoạt động của Agent
            if (!_connectionManager.TryGetConnection(message.AgentId, out var connection) || connection is null)
            {
                _logger.LogWarning("Agent {AgentId} hiện đang offline. Lệnh {CommandId} không thể gửi đi.", message.AgentId, message.CommandId);
                return;
            }

            // 2. Đóng gói lệnh thành CommandRequest gửi qua SignalR
            var commandRequest = new CommandRequest
            {
                CommandId = message.CommandId,
                CommandText = message.CommandText,
                TimeoutSeconds = message.TimeoutSeconds
            };

            // 3. Đẩy lệnh xuống đúng kết nối SignalR hiện hành của Agent
            try
            {
                await _agentHubContext.Clients
                    .Client(connection.ConnectionId)
                    .SendAsync("ReceiveCommand", commandRequest, context.CancellationToken);
                _logger.LogInformation("Đã đẩy thành công lệnh {CommandId} xuống Agent {AgentId} (phien {ConnectionId})",
                    message.CommandId, message.AgentId, connection.ConnectionId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Lỗi SignalR khi đẩy lệnh {CommandId} xuống Agent {AgentId}", message.CommandId, message.AgentId);
                throw; // Throw để MassTransit tự động đẩy lại (retry) hoặc đưa vào Dead Letter Queue
            }
        }
    }
}
