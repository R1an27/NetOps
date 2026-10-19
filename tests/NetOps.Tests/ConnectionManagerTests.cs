using System.Collections.Concurrent;
using System.Net.Sockets;
using Client.WinForms.Services;
using Master.Api.Services;
using NetOps.Tests.TestHelpers;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử chuyên sâu cho ConnectionManager và vòng đời AgentSession trên TcpManagerServer.
/// Xác thực:
/// 1. Unit tests: Đăng ký, gỡ bỏ, trùng lặp sessionId, lookup theo AgentId (case-insensitive), sự kiện vòng đời và Broadcast.
/// 2. Integration tests: Tự động đăng ký session khi client kết nối qua socket thật.
/// 3. Cập nhật AgentId và LastSeenAt khi nhận gói tin Heartbeat.
/// 4. Tự động gỡ bỏ session khi ngắt kết nối êm ái (Graceful EOF).
/// 5. Tự động gỡ bỏ session khi ngắt kết nối đột ngột (Socket abort / RST linger zero).
/// 6. Kiểm thử áp lực đa kết nối đồng thời: 15 clients kết nối và ngắt kết nối đồng thời.
/// </summary>
public class ConnectionManagerTests
{
    #region 1. Unit Tests cho ConnectionManager

    [Fact]
    public void ConnectionManager_TryRegisterAndUnregister_SucceedsAndFiresEvents()
    {
        var manager = new ConnectionManager();
        Assert.Equal(0, manager.ActiveCount);

        AgentSession? registeredEventSession = null;
        AgentSession? unregisteredEventSession = null;

        manager.SessionRegistered += s => registeredEventSession = s;
        manager.SessionUnregistered += s => unregisteredEventSession = s;

        using var ms = new MemoryStream();
        using var safeStream = new SafeFrameStream(ms);
        var session = new AgentSession("sess-01", safeStream, "127.0.0.1:5001", DateTimeOffset.UtcNow, agentId: "agent-alpha");

        // 1. Đăng ký session thành công
        bool registered = manager.TryRegister(session);
        Assert.True(registered);
        Assert.Equal(1, manager.ActiveCount);
        Assert.Same(session, registeredEventSession);
        Assert.Same(session, manager.GetSession("sess-01"));

        // 2. Không cho phép đăng ký trùng SessionId
        var duplicateSession = new AgentSession("sess-01", safeStream, "127.0.0.1:5002", DateTimeOffset.UtcNow);
        bool duplicateResult = manager.TryRegister(duplicateSession);
        Assert.False(duplicateResult);
        Assert.Equal(1, manager.ActiveCount);

        // 3. Gỡ bỏ session thành công
        bool unregistered = manager.TryUnregister("sess-01", out var removedSession);
        Assert.True(unregistered);
        Assert.Same(session, removedSession);
        Assert.Same(session, unregisteredEventSession);
        Assert.Equal(0, manager.ActiveCount);
        Assert.Null(manager.GetSession("sess-01"));

        // 4. Gỡ bỏ session không tồn tại trả về false
        bool unregisterNonExistent = manager.TryUnregister("sess-999", out var notFound);
        Assert.False(unregisterNonExistent);
        Assert.Null(notFound);
    }

    [Fact]
    public void ConnectionManager_LookupByAgentId_CaseInsensitiveAndNotFound()
    {
        var manager = new ConnectionManager();
        using var ms = new MemoryStream();
        using var safeStream = new SafeFrameStream(ms);

        var session = new AgentSession("sess-02", safeStream, "127.0.0.1:5002", DateTimeOffset.UtcNow, agentId: "Worker-Node-42");
        manager.TryRegister(session);

        // Tra cứu không phân biệt hoa thường
        Assert.Same(session, manager.GetSessionByAgentId("worker-node-42"));
        Assert.Same(session, manager.GetSessionByAgentId("WORKER-NODE-42"));
        Assert.Same(session, manager.GetSessionByAgentId("Worker-Node-42"));

        // Tra cứu không tồn tại hoặc chuỗi rỗng
        Assert.Null(manager.GetSessionByAgentId("worker-node-99"));
        Assert.Null(manager.GetSessionByAgentId(string.Empty));
    }

    [Fact]
    public void ConnectionManager_GetAllSessions_ReturnsIndependentSnapshot()
    {
        var manager = new ConnectionManager();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        using var s1 = new SafeFrameStream(ms1);
        using var s2 = new SafeFrameStream(ms2);

        var session1 = new AgentSession("s-01", s1, "127.0.0.1:5001", DateTimeOffset.UtcNow);
        var session2 = new AgentSession("s-02", s2, "127.0.0.1:5002", DateTimeOffset.UtcNow);

        manager.TryRegister(session1);
        manager.TryRegister(session2);

        var snapshot = manager.GetAllSessions();
        Assert.Equal(2, snapshot.Count);

        // Gỡ 1 session
        manager.TryUnregister("s-01", out _);
        Assert.Equal(1, manager.ActiveCount);

        // Snapshot trước đó không bị ảnh hưởng
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public async Task ConnectionManager_BroadcastAsync_SendsToAllActiveSessions()
    {
        var manager = new ConnectionManager();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        using var s1 = new SafeFrameStream(ms1);
        using var s2 = new SafeFrameStream(ms2);

        var session1 = new AgentSession("s-01", s1, "127.0.0.1:5001", DateTimeOffset.UtcNow);
        var session2 = new AgentSession("s-02", s2, "127.0.0.1:5002", DateTimeOffset.UtcNow);

        manager.TryRegister(session1);
        manager.TryRegister(session2);

        var pingMsg = NetOpsMessage.Create(MessageType.Ping, "BROADCAST_TEST");
        await manager.BroadcastAsync(pingMsg);

        // Cả 2 stream đều đã nhận được dữ liệu (length > 0)
        Assert.True(ms1.Length > 0);
        Assert.True(ms2.Length > 0);
    }

    #endregion

    #region 2. Integration Tests: TcpManagerServer Tự Động Quản Lý Vòng Đời

    [Fact]
    public async Task TcpManagerServer_ClientConnects_AutomaticallyRegistersSession()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var connectionManager = serverHelper.Server.ConnectionManager;
        Assert.Equal(0, connectionManager.ActiveCount);

        using (var client = new NetOpsClientService())
        {
            await client.ConnectAsync(serverHelper.Host, serverHelper.Port);
            Assert.True(client.IsConnected);

            // Chờ server đăng ký session
            await WaitForConditionAsync(() => connectionManager.ActiveCount == 1, TimeSpan.FromSeconds(3));

            var sessions = connectionManager.GetAllSessions();
            Assert.Single(sessions);

            var session = sessions.First();
            Assert.StartsWith("session-", session.SessionId);
            Assert.Contains("127.0.0.1", session.RemoteEndPoint);
            Assert.True(session.ConnectedAt <= DateTimeOffset.UtcNow);
            Assert.NotNull(session.SafeStream);

            // Gửi Ping để xác nhận kênh truyền hoạt động
            var (rtt, pong) = await client.PingAsync("ping-session", TimeSpan.FromSeconds(5));
            Assert.Equal(MessageType.Pong, pong.MessageType);
        }

        // Sau khi client dispose (ngắt kết nối), session tự động được gỡ bỏ
        await WaitForConditionAsync(() => connectionManager.ActiveCount == 0, TimeSpan.FromSeconds(3));
        Assert.Equal(0, connectionManager.ActiveCount);
    }

    [Fact]
    public async Task TcpManagerServer_Heartbeat_UpdatesAgentIdAndLastSeenAt()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var connectionManager = serverHelper.Server.ConnectionManager;

        using var client = new NetOpsClientService();
        await client.ConnectAsync(serverHelper.Host, serverHelper.Port);

        await WaitForConditionAsync(() => connectionManager.ActiveCount == 1, TimeSpan.FromSeconds(3));
        var session = connectionManager.GetAllSessions().First();

        var initialLastSeen = session.LastSeenAt;
        await Task.Delay(20); // Tạo khoảng cách thời gian nhỏ

        // Gửi Heartbeat mang AgentId định danh
        string testAgentId = "node-worker-99";
        var (ackPayload, response) = await client.SendHeartbeatAsync(testAgentId, TimeSpan.FromSeconds(5));
        Assert.NotNull(ackPayload);
        Assert.True(ackPayload.Acknowledged);

        // Kiểm chứng session được cập nhật AgentId và LastSeenAt
        Assert.Equal(testAgentId, session.AgentId);
        Assert.True(session.LastSeenAt >= initialLastSeen);

        // Có thể tra cứu session qua AgentId
        var sessionByAgent = connectionManager.GetSessionByAgentId(testAgentId);
        Assert.Same(session, sessionByAgent);

        // Ngắt kết nối
        await client.DisconnectAsync();
        await WaitForConditionAsync(() => connectionManager.ActiveCount == 0, TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task TcpManagerServer_GracefulDisconnect_UnregistersSessionAndFiresEvent()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var connectionManager = serverHelper.Server.ConnectionManager;

        var unregisteredTcs = new TaskCompletionSource<AgentSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        connectionManager.SessionUnregistered += s => unregisteredTcs.TrySetResult(s);

        using (var client = new NetOpsClientService())
        {
            await client.ConnectAsync(serverHelper.Host, serverHelper.Port);
            await WaitForConditionAsync(() => connectionManager.ActiveCount == 1, TimeSpan.FromSeconds(3));

            // Client chủ động ngắt kết nối (Graceful EOF)
            await client.DisconnectAsync();
        }

        // Chờ sự kiện SessionUnregistered kích hoạt
        var completedTask = await Task.WhenAny(unregisteredTcs.Task, Task.Delay(3000));
        Assert.Same(unregisteredTcs.Task, completedTask);

        var unregisteredSession = await unregisteredTcs.Task;
        Assert.NotNull(unregisteredSession);
        Assert.Equal(0, connectionManager.ActiveCount);
    }

    [Fact]
    public async Task TcpManagerServer_AbruptDisconnect_LingerZero_StillUnregistersSession()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var connectionManager = serverHelper.Server.ConnectionManager;

        // Kết nối qua raw TcpClient
        var rawClient = new TcpClient();
        await rawClient.ConnectAsync(serverHelper.Host, serverHelper.Port);

        // Chờ session được đăng ký
        await WaitForConditionAsync(() => connectionManager.ActiveCount == 1, TimeSpan.FromSeconds(3));
        Assert.Equal(1, connectionManager.ActiveCount);

        // Giả lập sự cố mạng đứt cáp / tiến trình crash bằng cách gửi cờ RST (LingerState = true, 0)
        rawClient.Client.LingerState = new LingerOption(true, 0);
        rawClient.Client.Close();
        rawClient.Dispose();

        // Khối finally trong HandleClientAsync phải luôn giải phóng session
        await WaitForConditionAsync(() => connectionManager.ActiveCount == 0, TimeSpan.FromSeconds(3));
        Assert.Equal(0, connectionManager.ActiveCount);
    }

    [Fact]
    public async Task TcpManagerServer_MultiClientConcurrency_AllSessionsRegisteredAndCleanedUp()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var connectionManager = serverHelper.Server.ConnectionManager;

        const int clientCount = 15;
        var clients = new ConcurrentBag<NetOpsClientService>();

        // 1. Kết nối 15 clients song song
        var connectTasks = Enumerable.Range(0, clientCount).Select(async i =>
        {
            var client = new NetOpsClientService();
            await client.ConnectAsync(serverHelper.Host, serverHelper.Port);
            clients.Add(client);

            // Mỗi client gửi 1 ping để hoàn tất bắt tay
            var (rtt, pong) = await client.PingAsync($"client-{i}", TimeSpan.FromSeconds(5));
            Assert.Equal(MessageType.Pong, pong.MessageType);
        });

        await Task.WhenAll(connectTasks);

        // Chờ toàn bộ 15 sessions được đăng ký
        await WaitForConditionAsync(() => connectionManager.ActiveCount == clientCount, TimeSpan.FromSeconds(5));
        Assert.Equal(clientCount, connectionManager.ActiveCount);

        var sessions = connectionManager.GetAllSessions();
        Assert.Equal(clientCount, sessions.Count);

        // Đảm bảo tất cả SessionId đều là duy nhất
        var uniqueSessionIds = sessions.Select(s => s.SessionId).Distinct().Count();
        Assert.Equal(clientCount, uniqueSessionIds);

        // 2. Ngắt kết nối toàn bộ 15 clients đồng thời
        var disconnectTasks = clients.Select(async client =>
        {
            await client.DisconnectAsync();
            client.Dispose();
        });

        await Task.WhenAll(disconnectTasks);

        // 3. Toàn bộ session phải được dọn dẹp sạch sẽ
        await WaitForConditionAsync(() => connectionManager.ActiveCount == 0, TimeSpan.FromSeconds(5));
        Assert.Equal(0, connectionManager.ActiveCount);
    }

    #endregion

    #region Helper Methods

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition() && !cts.IsCancellationRequested)
        {
            await Task.Delay(25, cts.Token).ConfigureAwait(false);
        }

        if (!condition())
        {
            throw aerialException ?? new TimeoutException($"Điều kiện kiểm tra không đạt sau {timeout.TotalMilliseconds}ms.");
        }
    }

    private static readonly Exception? aerialException = null;

    #endregion
}
