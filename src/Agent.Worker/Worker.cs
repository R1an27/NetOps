using Agent.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;

namespace Agent.Worker;

/// <summary>
/// Dịch vụ nền Hosted Service quản trị chu trình sống của Agent Worker.
/// Tự động nạp cấu hình hệ thống với cơ chế phòng vệ (defensive defaults),
/// kết nối tới Master thông qua IAgentNetworkClient, và điều phối các sự kiện
/// thông điệp nghiệp vụ nhận được từ IAgentMessageControl.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IAgentNetworkClient _networkClient;
    private readonly IAgentMessageControl _messageControl;

    /// <summary>
    /// Client mạng mà Worker đang sử dụng để kết nối tới Master.
    /// </summary>
    public IAgentNetworkClient NetworkClient => _networkClient;

    /// <summary>
    /// Bộ điều phối thông điệp mà Worker đang sử dụng.
    /// </summary>
    public IAgentMessageControl MessageControl => _messageControl;

    /// <summary>
    /// Khởi tạo Worker với đầy đủ các dịch vụ phụ thuộc được tiêm qua DI Container.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        IAgentNetworkClient networkClient,
        IAgentMessageControl? messageControl = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _networkClient = networkClient ?? throw new ArgumentNullException(nameof(networkClient));
        _messageControl = messageControl ?? networkClient.MessageControl;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nạp cấu hình phòng vệ: nếu thiếu hoặc rỗng sẽ fallback về các giá trị chuẩn mặc định
        string host = _configuration.GetValue<string>("AgentConfig:ServerHost") ?? "127.0.0.1";
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "127.0.0.1";
        }

        int port = _configuration.GetValue<int>("AgentConfig:ServerPort", 5050);
        if (port <= 0)
        {
            port = 5050;
        }

        string agentId = _configuration.GetValue<string>("AgentConfig:AgentId") ?? "agent-linux-01";
        if (string.IsNullOrWhiteSpace(agentId))
        {
            agentId = "agent-linux-01";
        }

        int pingSeconds = _configuration.GetValue<int>("AgentConfig:PingIntervalSeconds", 5);
        if (pingSeconds <= 0)
        {
            pingSeconds = 5;
        }

        _logger.LogInformation(
            "Agent.Worker starting with AgentId: {AgentId}, Target: {Host}:{Port}, PingInterval: {PingInterval}s",
            agentId, host, port, pingSeconds);

        // Đăng ký các sự kiện vòng đời và điều phối thông điệp
        _networkClient.Connected += OnConnected;
        _networkClient.Disconnected += OnDisconnected;
        _messageControl.CommandReceived += OnCommandReceived;
        _messageControl.HeartbeatAckReceived += OnHeartbeatAckReceived;
        _messageControl.PongReceived += OnPongReceived;

        try
        {
            // Bắt đầu chu trình chạy của AgentNetworkClient (kết nối, nhận tin, gửi PING định kỳ)
            await _networkClient.RunAsync(host, port, agentId, TimeSpan.FromSeconds(pingSeconds), stoppingToken).ConfigureAwait(false);
            if (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Agent.Worker received shutdown signal via stoppingToken. Graceful shutdown completed.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Agent.Worker received shutdown signal via stoppingToken. Graceful shutdown completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent.Worker encountered an unexpected error during execution.");
            throw;
        }
        finally
        {
            // Dọn dẹp an toàn các event listener để tránh rò rỉ bộ nhớ (Event Memory Leak)
            _networkClient.Connected -= OnConnected;
            _networkClient.Disconnected -= OnDisconnected;
            _messageControl.CommandReceived -= OnCommandReceived;
            _messageControl.HeartbeatAckReceived -= OnHeartbeatAckReceived;
            _messageControl.PongReceived -= OnPongReceived;

            _logger.LogInformation("Agent.Worker has stopped and unsubscribed from all message events.");
        }
    }

    private void OnConnected()
    {
        _logger.LogInformation("[Worker] Network client connected to Master at {Endpoint}.", _networkClient.ConnectedEndpoint ?? "Unknown");
    }

    private void OnDisconnected()
    {
        _logger.LogWarning("[Worker] Network client disconnected from Master.");
    }

    private void OnCommandReceived(NetOpsMessage message, CommandRequestPayload? payload)
    {
        _logger.LogInformation(
            "[Worker] COMMAND received from Master: CommandId={CommandId}, CommandLine='{CommandLine}', Timeout={Timeout}s (RequestId: {RequestId})",
            payload?.CommandId ?? "N/A",
            payload?.CommandLine ?? string.Empty,
            payload?.TimeoutSeconds ?? 0,
            message.RequestId);
    }

    private void OnHeartbeatAckReceived(NetOpsMessage message, HeartbeatAckPayload? payload)
    {
        _logger.LogInformation(
            "[Worker] HEARTBEAT_ACK received: Timestamp={Timestamp}, Acknowledged={Acknowledged} (InReplyTo: {InReplyTo})",
            payload?.Timestamp ?? 0,
            payload?.Acknowledged ?? false,
            message.InReplyTo);
    }

    private void OnPongReceived(NetOpsMessage message, string? payload)
    {
        _logger.LogDebug(
            "[Worker] PONG received: InReplyTo={InReplyTo}, RequestId={RequestId}, Payload={Payload}",
            message.InReplyTo,
            message.RequestId,
            payload);
    }
}