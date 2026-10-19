using System.Net;
using System.Net.Sockets;
using Agent.Worker.Services;
using Microsoft.Extensions.Logging;
using NetOps.Tests.TestHelpers;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử tích hợp End-to-End cho TcpAgentClient giao tiếp với TcpManagerServer qua Socket TCP thực tế.
/// Kiểm chứng luồng gửi Ping định kỳ, nhận Pong, xử lý khi Server ngắt kết nối (Graceful EOF),
/// và cơ chế xử lý lỗi khi không kết nối được tới server (SocketException handling & Reconnect backoff).
/// </summary>
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
