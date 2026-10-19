using System.Net;
using System.Net.Sockets;
using Agent.Worker.Services;
using Master.Api.Services;
using Microsoft.Extensions.Logging;
using NetOps.Tests.TestHelpers;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử tích hợp End-to-End cho TcpAgentClient giao tiếp với TcpManagerServer qua Socket TCP thực tế.
/// Kiểm chứng luồng gửi Ping định kỳ, nhận Pong, xử lý khi Server ngắt kết nối (Graceful EOF),
/// cơ chế xử lý lỗi khi không kết nối được tới server (SocketException handling & Reconnect backoff),
/// và tích hợp định tuyến thông điệp hai chiều qua IAgentMessageControl (Master gửi PING -> Agent phản hồi PONG, Master gửi COMMAND).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Agent")]
public class AgentIntegrationTests
{
    [Fact]
    public async Task Agent_CanConnectToMaster_AndExchangePingPong()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();
        var logger = new TestLogger<TcpAgentClient>();
        var agent = new TcpAgentClient(logger);

        using var cts = new CancellationTokenSource();

        // Chạy agent với chu kỳ Ping 50ms
        var agentTask = agent.RunAsync(server.Host, server.Port, "agent-integration-01", TimeSpan.FromMilliseconds(50), cts.Token);

        // Chờ khoảng 300ms để agent gửi ít nhất 2-3 Pings và nhận Pongs
        await Task.Delay(300);

        // Hủy token để dừng agent sạch sẽ
        cts.Cancel();
        await agentTask;

        // Xác minh nhật ký hoạt động của Agent
        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m => m.Contains("Successfully connected to Master"));
            Assert.Contains(logger.Messages, m => m.Contains("Sent PING"));
            Assert.Contains(logger.Messages, m => m.Contains("Received PONG"));
        }
    }

    [Fact]
    public async Task Master_CanSendPingToAgent_AndAgentAutomaticallyRepliesPong_OverRealTcpSocket()
    {
        var serverLogger = new TestLogger<TcpManagerServer>();
        await using var server = await TcpTestServerHelper.StartNewAsync(serverLogger);

        var agentLogger = new TestLogger<TcpAgentClient>();
        var controlLogger = new TestLogger<AgentMessageControl>();
        var control = new AgentMessageControl(controlLogger);
        var agent = new TcpAgentClient(agentLogger, control);

        Assert.Same(control, agent.MessageControl);

        using var cts = new CancellationTokenSource();
        var agentTask = agent.RunAsync(server.Host, server.Port, "agent-ping-pong-e2e", TimeSpan.FromMilliseconds(50), cts.Token);

        // Chờ Agent kết nối thành công và Master ghi nhận phiên làm việc
        AgentSession? session = null;
        for (int i = 0; i < 50; i++)
        {
            session = server.Server.ConnectionManager.GetAllSessions().FirstOrDefault();
            if (session != null) break;
            await Task.Delay(20);
        }
        Assert.NotNull(session);

        // Master chủ động gửi một gói tin PING tới Agent
        var pingFromMaster = NetOpsMessage.Create(MessageType.Ping, "PROBE_FROM_MASTER", requestId: "req-master-probe-42");
        await session.SendMessageAsync(pingFromMaster);

        // Chờ Agent xử lý và gửi phản hồi PONG qua socket TCP
        await Task.Delay(200);

        cts.Cancel();
        await agentTask;

        // Xác minh nhật ký phía Agent: AgentMessageControl tự động tạo và gửi PONG
        lock (controlLogger.Messages)
        {
            Assert.Contains(controlLogger.Messages, m => m.Contains("Processed PING") && m.Contains("generating PONG response"));
            Assert.Contains(controlLogger.Messages, m => m.Contains("Sent response 'PONG'"));
        }

        // Xác minh nhật ký phía Master: Master Server đã nhận được gói tin PONG từ Agent
        lock (serverLogger.Messages)
        {
            Assert.Contains(serverLogger.Messages, m => m.Contains("Received message 'PONG'"));
        }
    }

    [Fact]
    public async Task Master_CanSendCommandToAgent_AndTriggerCommandReceivedEvent_OverRealTcpSocket()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        var agentLogger = new TestLogger<TcpAgentClient>();
        var control = new AgentMessageControl();
        var agent = new TcpAgentClient(agentLogger, control);

        var cmdReceivedTcs = new TaskCompletionSource<CommandRequestPayload>();
        control.CommandReceived += (_, payload) => cmdReceivedTcs.TrySetResult(payload);

        using var cts = new CancellationTokenSource();
        var agentTask = agent.RunAsync(server.Host, server.Port, "agent-cmd-e2e", TimeSpan.FromMilliseconds(50), cts.Token);

        AgentSession? session = null;
        for (int i = 0; i < 50; i++)
        {
            session = server.Server.ConnectionManager.GetAllSessions().FirstOrDefault();
            if (session != null) break;
            await Task.Delay(20);
        }
        Assert.NotNull(session);

        // Master gửi lệnh COMMAND thực thi từ xa tới Agent
        var cmdPayload = new CommandRequestPayload("cmd-uuid-777", "docker ps -a", timeoutSeconds: 45, workingDir: "/opt/app");
        var cmdMsg = NetOpsMessage.Create(MessageType.Command, cmdPayload, requestId: "req-cmd-flow-01");
        await session.SendMessageAsync(cmdMsg);

        // Chờ sự kiện CommandReceived kích hoạt trên Agent
        var received = await cmdReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        cts.Cancel();
        await agentTask;

        Assert.NotNull(received);
        Assert.Equal("cmd-uuid-777", received.CommandId);
        Assert.Equal("docker ps -a", received.CommandLine);
        Assert.Equal(45, received.TimeoutSeconds);
        Assert.Equal("/opt/app", received.WorkingDir);
    }

    [Fact]
    public async Task Agent_WhenMasterShutsDown_HandlesGracefulEofWithoutCrashing()
    {
        var server = await TcpTestServerHelper.StartNewAsync();
        var logger = new TestLogger<TcpAgentClient>();
        var agent = new TcpAgentClient(logger);

        using var cts = new CancellationTokenSource();

        var agentTask = agent.RunAsync(server.Host, server.Port, "agent-eof-test", TimeSpan.FromMilliseconds(50), cts.Token);

        // Chờ agent kết nối và gửi ít nhất 1 Ping
        await Task.Delay(150);

        // Chủ động dừng Server để gửi tín hiệu EOF tới Agent
        await server.DisposeAsync();

        // Chờ Agent phát hiện EOF và chuyển sang chế độ chờ kết nối lại
        await Task.Delay(200);

        // Dừng Agent
        cts.Cancel();
        await agentTask;

        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m => m.Contains("Successfully connected to Master"));
            Assert.Contains(logger.Messages, m => m.Contains("Master closed the connection (EOF)") || m.Contains("Reconnecting in"));
        }
    }

    [Fact]
    public async Task Agent_WhenServerUnavailable_CatchesSocketExceptionAndEntersBackoff()
    {
        // Tìm một cổng không có server lắng nghe
        int unusedPort = GetUnusedPort();
        var logger = new TestLogger<TcpAgentClient>();
        var agent = new TcpAgentClient(logger);

        using var cts = new CancellationTokenSource();

        var agentTask = agent.RunAsync("127.0.0.1", unusedPort, "agent-offline-test", TimeSpan.FromMilliseconds(100), cts.Token);

        // Chờ agent cố gắng kết nối và bắt lỗi SocketException
        await Task.Delay(250);

        cts.Cancel();
        await agentTask;

        lock (logger.Messages)
        {
            Assert.Contains(logger.Messages, m => m.Contains("Socket error connecting to Master") || m.Contains("Attempting to connect"));
        }
    }

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// Test logger lưu lại toàn bộ các log messages để phục vụ kiểm tra Assertion trong Unit/Integration Test.
/// </summary>
public sealed class TestLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception);
        lock (Messages)
        {
            Messages.Add(message);
        }
    }
}
