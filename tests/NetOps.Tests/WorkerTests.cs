using Agent.Worker;
using Agent.Worker.Services;
using Microsoft.Extensions.Configuration;
using NetOps.Tests.TestHelpers;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử toàn diện cho Worker, IAgentNetworkClient và AgentNetworkClient.
/// Kiểm chứng:
/// 1. Phòng vệ tham số constructor (ArgumentNullException).
/// 2. Khởi tạo tương thích ngược với TcpAgentClient.
/// 3. Nạp cấu hình phòng vệ với giá trị mặc định khi thiếu hoặc cấu hình sai (Defensive Defaults).
/// 4. Nạp cấu hình chính xác khi được chỉ định rõ ràng.
/// 5. Lắng nghe và điều phối các sự kiện kết nối Connected / Disconnected.
/// 6. Lắng nghe và ghi log sự kiện CommandReceived, HeartbeatAckReceived, PongReceived từ IAgentMessageControl.
/// 7. Chu trình dừng an toàn (Graceful Shutdown) và dọn dẹp sự kiện (Unsubscribe) trong finally block.
/// 8. Phương thức SendMessageAsync trên AgentNetworkClient khi ngắt kết nối và khi tham số null.
/// 9. Kiểm thử tích hợp End-to-End giữa Worker, TcpAgentClient và Server TCP thực tế.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Agent")]
public class WorkerTests
{
    #region 1. Constructor & Phòng Vệ Tham Số

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var config = new ConfigurationBuilder().Build();
        var client = new MockAgentNetworkClient();

        Assert.Throws<ArgumentNullException>(() => new Worker(null!, config, client));
    }

    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        var logger = new TestLogger<Worker>();
        var client = new MockAgentNetworkClient();

        Assert.Throws<ArgumentNullException>(() => new Worker(logger, null!, client));
    }

    [Fact]
    public void Constructor_NullNetworkClient_ThrowsArgumentNullException()
    {
        var logger = new TestLogger<Worker>();
        var config = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => new Worker(logger, config, (IAgentNetworkClient)null!));
    }

    [Fact]
    public void Constructor_WithTcpAgentClient_ResolvesPropertiesCorrectly()
    {
        var logger = new TestLogger<Worker>();
        var config = new ConfigurationBuilder().Build();
        var tcpClientLogger = new TestLogger<TcpAgentClient>();
        var tcpClient = new TcpAgentClient(tcpClientLogger);

        var worker = new Worker(logger, config, tcpClient);

        Assert.NotNull(worker.NetworkClient);
        Assert.NotNull(worker.MessageControl);
        Assert.Same(tcpClient, worker.NetworkClient);
    }

    #endregion

    #region 2. Cấu Hình Phòng Vệ (Defensive Defaults)

    [Fact]
    public async Task Worker_Configuration_UsesDefensiveDefaults_WhenConfigIsEmpty()
    {
        var logger = new TestLogger<Worker>();
        var config = new ConfigurationBuilder().Build(); // Cấu hình rỗng hoàn toàn
        var mockClient = new MockAgentNetworkClient();
        var worker = new Worker(logger, config, mockClient);

        await worker.StartAsync(CancellationToken.None);
        await mockClient.WaitForRunStartedAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal("127.0.0.1", mockClient.CapturedHost);
        Assert.Equal(5050, mockClient.CapturedPort);
        Assert.Equal("agent-linux-01", mockClient.CapturedAgentId);
        Assert.Equal(TimeSpan.FromSeconds(5), mockClient.CapturedPingInterval);

        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m => m.Contains("AgentId: agent-linux-01") && m.Contains("Target: 127.0.0.1:5050") && m.Contains("PingInterval: 5s"));
        }
    }

    [Fact]
    public async Task Worker_Configuration_UsesExplicitValues_WhenConfigIsProvided()
    {
        var logger = new TestLogger<Worker>();
        var configData = new Dictionary<string, string?>
        {
            ["AgentConfig:ServerHost"] = "192.168.1.100",
            ["AgentConfig:ServerPort"] = "9090",
            ["AgentConfig:AgentId"] = "custom-agent-node-01",
            ["AgentConfig:PingIntervalSeconds"] = "15"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var mockClient = new MockAgentNetworkClient();
        var worker = new Worker(logger, config, mockClient);

        await worker.StartAsync(CancellationToken.None);
        await mockClient.WaitForRunStartedAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal("192.168.1.100", mockClient.CapturedHost);
        Assert.Equal(9090, mockClient.CapturedPort);
        Assert.Equal("custom-agent-node-01", mockClient.CapturedAgentId);
        Assert.Equal(TimeSpan.FromSeconds(15), mockClient.CapturedPingInterval);

        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m => m.Contains("AgentId: custom-agent-node-01") && m.Contains("Target: 192.168.1.100:9090") && m.Contains("PingInterval: 15s"));
        }
    }

    [Fact]
    public async Task Worker_Configuration_FallsBackToDefaults_WhenValuesAreInvalidOrNegative()
    {
        var logger = new TestLogger<Worker>();
        var configData = new Dictionary<string, string?>
        {
            ["AgentConfig:ServerHost"] = "   ", // Toàn khoảng trắng
            ["AgentConfig:ServerPort"] = "-1",  // Cổng âm
            ["AgentConfig:AgentId"] = "",       // Rỗng
            ["AgentConfig:PingIntervalSeconds"] = "0" // Khoảng thời gian bằng 0
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var mockClient = new MockAgentNetworkClient();
        var worker = new Worker(logger, config, mockClient);

        await worker.StartAsync(CancellationToken.None);
        await mockClient.WaitForRunStartedAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal("127.0.0.1", mockClient.CapturedHost);
        Assert.Equal(5050, mockClient.CapturedPort);
        Assert.Equal("agent-linux-01", mockClient.CapturedAgentId);
        Assert.Equal(TimeSpan.FromSeconds(5), mockClient.CapturedPingInterval);
    }

    #endregion

    #region 3. Sự Kiện Vòng Đời Mạng (Connected / Disconnected)

    [Fact]
    public async Task Worker_Events_LogsConnectedAndDisconnected()
    {
        var logger = new TestLogger<Worker>();
        var config = new ConfigurationBuilder().Build();
        var mockClient = new MockAgentNetworkClient();
        var worker = new Worker(logger, config, mockClient);

        await worker.StartAsync(CancellationToken.None);
        await mockClient.WaitForRunStartedAsync(TimeSpan.FromSeconds(2));

        mockClient.TriggerConnected("127.0.0.1:5050");
        mockClient.TriggerDisconnected();

        await worker.StopAsync(CancellationToken.None);

        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m => m.Contains("Network client connected to Master at 127.0.0.1:5050"));
            Assert.Contains(logger.Messages, m => m.Contains("Network client disconnected from Master"));
        }
    }

    #endregion

    #region 4. Lắng Nghe & Điều Phối Sự Kiện Từ IAgentMessageControl

    [Fact]
    public async Task Worker_Events_HandlesCommandReceived_FromMessageControl()
    {
        var logger = new TestLogger<Worker>();
        var config = new ConfigurationBuilder().Build();
        var messageControl = new AgentMessageControl();
        var mockClient = new MockAgentNetworkClient(messageControl);
        var worker = new Worker(logger, config, mockClient, messageControl);

        await worker.StartAsync(CancellationToken.None);
        await mockClient.WaitForRunStartedAsync(TimeSpan.FromSeconds(2));

        // Bắn thông điệp COMMAND qua messageControl.InvokeAsync
        var cmdPayload = new CommandRequestPayload("cmd-uuid-42", "docker ps -a", timeoutSeconds: 20);
        var commandMsg = NetOpsMessage.CreateRequest(MessageType.Command, cmdPayload, requestId: "req-cmd-1");

        await messageControl.InvokeAsync(commandMsg);

        await worker.StopAsync(CancellationToken.None);

        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m =>
                m.Contains("COMMAND received from Master") &&
                m.Contains("cmd-uuid-42") &&
                m.Contains("docker ps -a") &&
                m.Contains("Timeout=20s") &&
                m.Contains("req-cmd-1"));
        }
    }

    [Fact]
    public async Task Worker_Events_HandlesHeartbeatAckReceived_FromMessageControl()
    {
        var logger = new TestLogger<Worker>();
        var config = new ConfigurationBuilder().Build();
        var messageControl = new AgentMessageControl();
        var mockClient = new MockAgentNetworkClient(messageControl);
        var worker = new Worker(logger, config, mockClient, messageControl);

        await worker.StartAsync(CancellationToken.None);
        await mockClient.WaitForRunStartedAsync(TimeSpan.FromSeconds(2));

        var ackPayload = new HeartbeatAckPayload(1710000000000, acknowledged: true);
        var ackMsg = new NetOpsMessage
        {
            MessageType = MessageType.HeartbeatAck,
            InReplyTo = "req-ping-01",
            RequestId = "req-ping-01"
        };
        ackMsg.SetPayload(ackPayload);

        await messageControl.InvokeAsync(ackMsg);

        await worker.StopAsync(CancellationToken.None);

        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m =>
                m.Contains("HEARTBEAT_ACK received") &&
                m.Contains("Timestamp=1710000000000") &&
                m.Contains("Acknowledged=True") &&
                m.Contains("req-ping-01"));
        }
    }

    [Fact]
    public async Task Worker_Events_HandlesPongReceived_FromMessageControl()
    {
        var logger = new TestLogger<Worker>();
        var config = new ConfigurationBuilder().Build();
        var messageControl = new AgentMessageControl();
        var mockClient = new MockAgentNetworkClient(messageControl);
        var worker = new Worker(logger, config, mockClient, messageControl);

        await worker.StartAsync(CancellationToken.None);
        await mockClient.WaitForRunStartedAsync(TimeSpan.FromSeconds(2));

        var pongMsg = new NetOpsMessage
        {
            MessageType = MessageType.Pong,
            InReplyTo = "req-ping-99",
            RequestId = "req-pong-99",
            Payload = "PONG_DATA"
        };
        await messageControl.InvokeAsync(pongMsg);

        await worker.StopAsync(CancellationToken.None);

        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m =>
                m.Contains("PONG received") &&
                m.Contains("req-ping-99") &&
                m.Contains("PONG_DATA"));
        }
    }

    #endregion

    #region 5. Graceful Shutdown & Dọn Dẹp Sự Kiện (Unsubscribe)

    [Fact]
    public async Task Worker_GracefulShutdown_UnsubscribesEvents_AndExitsCleanly()
    {
        var logger = new TestLogger<Worker>();
        var config = new ConfigurationBuilder().Build();
        var messageControl = new AgentMessageControl();
        var mockClient = new MockAgentNetworkClient(messageControl);
        var worker = new Worker(logger, config, mockClient, messageControl);

        await worker.StartAsync(CancellationToken.None);
        await mockClient.WaitForRunStartedAsync(TimeSpan.FromSeconds(2));

        // Dừng worker thông qua StopAsync
        await worker.StopAsync(CancellationToken.None);

        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m => m.Contains("Graceful shutdown completed"));
            Assert.Contains(logger.Messages, m => m.Contains("unsubscribed from all message events"));
        }

        int countBeforeNewEvent;
        lock (logger.Messages)
        {
            countBeforeNewEvent = logger.Messages.Count;
        }

        // Bắn thêm một COMMAND sau khi Worker đã dừng hoàn toàn -> không được có log mới
        var newCmd = NetOpsMessage.CreateRequest(MessageType.Command, new CommandRequestPayload("cmd-after-stop", "ls"));
        await messageControl.InvokeAsync(newCmd);

        lock (logger.Messages)
        {
            Assert.Equal(countBeforeNewEvent, logger.Messages.Count);
        }
    }

    #endregion

    #region 6. AgentNetworkClient SendMessageAsync Validation

    [Fact]
    public async Task AgentNetworkClient_SendMessageAsync_ThrowsInvalidOperationException_WhenDisconnected()
    {
        var client = new AgentNetworkClient();
        var message = NetOpsMessage.Create(MessageType.CommandOutput, "test output");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendMessageAsync(message));
        Assert.Contains("not currently connected", ex.Message);
    }

    [Fact]
    public async Task AgentNetworkClient_SendMessageAsync_ThrowsArgumentNullException_WhenMessageIsNull()
    {
        var client = new AgentNetworkClient();
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendMessageAsync(null!));
    }

    #endregion

    #region 7. Kiểm Thử End-to-End Tích Hợp (Worker + TcpAgentClient + Server Thật)

    [Fact]
    public async Task Worker_EndToEnd_CanRunAgainstRealServer_AndShutDownGracefully()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        var logger = new TestLogger<Worker>();
        var tcpClientLogger = new TestLogger<TcpAgentClient>();
        var messageControl = new AgentMessageControl();

        var tcpClient = new TcpAgentClient(tcpClientLogger, messageControl);

        var configData = new Dictionary<string, string?>
        {
            ["AgentConfig:ServerHost"] = server.Host,
            ["AgentConfig:ServerPort"] = server.Port.ToString(),
            ["AgentConfig:AgentId"] = "e2e-worker-node",
            ["AgentConfig:PingIntervalSeconds"] = "1"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var worker = new Worker(logger, config, tcpClient, messageControl);

        // Khởi động Worker chạy thực tế kết nối tới server
        await worker.StartAsync(CancellationToken.None);

        // Chờ 300ms để kết nối socket và ping đầu tiên được thiết lập
        await Task.Delay(300);

        // Dừng Worker một cách an toàn
        await worker.StopAsync(CancellationToken.None);

        // Xác minh kết quả ghi nhật ký
        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m => m.Contains("Agent.Worker starting with AgentId: e2e-worker-node"));
            Assert.Contains(logger.Messages, m => m.Contains("Network client connected to Master"));
            Assert.Contains(logger.Messages, m => m.Contains("Graceful shutdown completed"));
        }
    }

    #endregion

    #region Test Double Helper

    private sealed class MockAgentNetworkClient : IAgentNetworkClient
    {
        private readonly IAgentMessageControl _messageControl;
        private readonly TaskCompletionSource _runStartedTcs = new();

        public bool IsConnected { get; set; }
        public string? ConnectedEndpoint { get; set; }
        public IAgentMessageControl MessageControl => _messageControl;

        public event Action? Connected;
        public event Action? Disconnected;

        public string? CapturedHost { get; private set; }
        public int CapturedPort { get; private set; }
        public string? CapturedAgentId { get; private set; }
        public TimeSpan CapturedPingInterval { get; private set; }

        public MockAgentNetworkClient(IAgentMessageControl? messageControl = null)
        {
            _messageControl = messageControl ?? new AgentMessageControl();
        }

        public Task WaitForRunStartedAsync(TimeSpan timeout) => _runStartedTcs.Task.WaitAsync(timeout);

        public void TriggerConnected(string endpoint)
        {
            IsConnected = true;
            ConnectedEndpoint = endpoint;
            Connected?.Invoke();
        }

        public void TriggerDisconnected()
        {
            IsConnected = false;
            ConnectedEndpoint = null;
            Disconnected?.Invoke();
        }

        public async Task RunAsync(string host, int port, string agentId, TimeSpan pingInterval, CancellationToken cancellationToken)
        {
            CapturedHost = host;
            CapturedPort = port;
            CapturedAgentId = agentId;
            CapturedPingInterval = pingInterval;

            _runStartedTcs.TrySetResult();

            var tcs = new TaskCompletionSource();
            using (cancellationToken.Register(() => tcs.TrySetResult()))
            {
                await tcs.Task;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        public Task SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (!IsConnected)
            {
                throw new InvalidOperationException("AgentNetworkClient is not currently connected to Master.");
            }
            return Task.CompletedTask;
        }

        public bool IsHeartbeatSilenced { get; set; }

        public void SimulateAbruptDisconnect()
        {
            TriggerDisconnected();
        }

        public void SimulateHeartbeatSilence(bool silence)
        {
            IsHeartbeatSilenced = silence;
        }
    }

    #endregion
}
