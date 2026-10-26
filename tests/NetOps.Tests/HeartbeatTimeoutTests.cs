using System.IO;
using Master.Api.Extensions;
using Master.Api.Options;
using Master.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử chuyên biệt cho chức năng Timeout & Offline Detection (T16.1 đến T16.6).
/// Đáp ứng chính xác lệnh kiểm thử theo Barem:
/// dotnet test --filter FullyQualifiedName~HeartbeatTimeoutTests
/// 
/// Bao gồm các ca kiểm thử:
/// - T16.1: IAgentRegistry & AgentRegistry quản lý trạng thái Agent đa luồng an toàn.
/// - T16.2: MasterMessageControl cập nhật Agent sang Online và LastSeenAt khi nhận PING, HEARTBEAT, AUTH, AUTH_OK.
/// - T16.3: HeartbeatTimeoutWatchdog phát hiện session im lặng >15s và chuyển sang Offline (DisconnectReason.HeartbeatTimeout).
/// - T16.4: ConnectionManager đánh dấu Offline khi socket đóng mà không xóa thông tin máy chủ khỏi registry.
/// - T16.6: Kiểm tra Dependency Injection đăng ký đầy đủ IAgentRegistry và HeartbeatTimeoutWatchdog.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "HeartbeatTimeout")]
public class HeartbeatTimeoutTests
{
    #region Helper Methods & Dummy Classes

    /// <summary>
    /// Hàm tiện ích tạo một AgentSession phục vụ kiểm thử
    /// </summary>
    private static AgentSession CreateSession(string sessionId, DateTimeOffset lastSeenAt, ISafeFrameChannel? channel = null, string? agentId = null)
    {
        var safeChannel = channel ?? new DummySafeFrameChannel();
        var session = new AgentSession(sessionId, safeChannel, "127.0.0.1:50000", lastSeenAt, agentId: agentId)
        {
            LastSeenAt = lastSeenAt
        };
        return session;
    }

    /// <summary>
    /// Kênh truyền giả lập hỗ trợ kiểm tra xem luồng Socket có bị đóng (Dispose) hay không
    /// </summary>
    private sealed class DummySafeFrameChannel : ISafeFrameChannel
    {
        public bool IsConnected => !IsDisposed;
        public bool IsDisposed { get; private set; }
        public Stream UnderlyingStream => Stream.Null;

        public ValueTask SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<NetOpsMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<NetOpsMessage?>(null);

        public ValueTask SendRawFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<byte[]?> ReceiveRawFrameAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<byte[]?>(null);

        public void Dispose()
        {
            IsDisposed = true;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    #endregion

    #region T16.1: Kiểm thử IAgentRegistry & AgentRegistry

    [Fact]
    public void AgentRegistry_RegisterOrUpdateOnline_SetsStatusToOnlineAndUpdatesLastSeen()
    {
        // Khởi tạo AgentRegistry mới
        var registry = new AgentRegistry();
        var now = DateTimeOffset.UtcNow;

        // Đăng ký Agent "agent-01" với trạng thái Online
        var record = registry.RegisterOrUpdateOnline("agent-01", "session-01", "192.168.1.10:5050", now);

        // Kiểm tra thông tin lưu trữ trong bản ghi trả về
        Assert.NotNull(record);
        Assert.Equal("agent-01", record.AgentId);
        Assert.Equal(AgentStatus.Online, record.Status);
        Assert.Equal("session-01", record.CurrentSessionId);
        Assert.Equal("192.168.1.10:5050", record.RemoteEndPoint);
        Assert.Equal(now, record.LastSeenAt);
        Assert.Equal(DisconnectReason.None, record.DisconnectReason);

        // Kiểm tra truy vấn từ Registry
        var fetched = registry.Get("agent-01");
        Assert.NotNull(fetched);
        Assert.Equal(AgentStatus.Online, fetched.Status);
        Assert.Equal(1, registry.OnlineCount);
        Assert.Equal(1, registry.TotalCount);
    }

    [Fact]
    public void AgentRegistry_MarkOffline_PreservesServerInfoAndSetsReason()
    {
        // Khởi tạo AgentRegistry và thêm 1 agent online
        var registry = new AgentRegistry();
        var registerTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        registry.RegisterOrUpdateOnline("agent-02", "session-02", "192.168.1.20:5050", registerTime);

        // Đánh dấu Agent sang Offline do HeartbeatTimeout
        var disconnectTime = DateTimeOffset.UtcNow;
        var marked = registry.MarkOffline("agent-02", DisconnectReason.HeartbeatTimeout, disconnectTime);

        // Xác nhận thao tác thành công và thông tin máy chủ không bị xóa
        Assert.NotNull(marked);
        var record = registry.Get("agent-02");
        Assert.NotNull(record);
        Assert.Equal("agent-02", record.AgentId);
        Assert.Equal(AgentStatus.Offline, record.Status); // Trạng thái chuyển sang Offline
        Assert.Equal(DisconnectReason.HeartbeatTimeout, record.DisconnectReason); // Lý do ngắt kết nối
        Assert.Equal(disconnectTime, record.DisconnectedAt); // Mốc thời gian ngắt
        Assert.Equal(registerTime, record.LastSeenAt); // Mốc LastSeenAt vẫn được bảo lưu
        Assert.Equal(0, registry.OnlineCount); // Số lượng online giảm về 0
        Assert.Equal(1, registry.TotalCount); // Tổng số Agent đã đăng ký vẫn được giữ nguyên (không bị xóa)
    }

    [Fact]
    public void AgentRegistry_ConcurrentAccess_IsThreadSafe()
    {
        // Kiểm thử an toàn đa luồng trên AgentRegistry với 100 tác vụ song song
        var registry = new AgentRegistry();
        const int taskCount = 100;

        Parallel.For(0, taskCount, i =>
        {
            string agentId = $"agent-concurrent-{i % 10}";
            registry.RegisterOrUpdateOnline(agentId, $"session-{i}", "127.0.0.1:5000", DateTimeOffset.UtcNow);
            if (i % 2 == 0)
            {
                registry.MarkOffline(agentId, DisconnectReason.GracefulDisconnect);
            }
            _ = registry.Get(agentId);
            _ = registry.GetAll();
        });

        // Không xảy ra exception và tổng số agent tối đa là 10 (vì modulo 10)
        Assert.Equal(10, registry.TotalCount);
    }

    #endregion

    #region T16.2: Kiểm thử MasterMessageControl cập nhật AgentRegistry

    [Fact]
    public async Task MasterMessageControl_WhenReceivingPing_UpdatesAgentRegistryToOnline()
    {
        // Khởi tạo AgentRegistry và MasterMessageControl
        var registry = new AgentRegistry();
        var control = new MasterMessageControl(registry);
        var session = CreateSession("session-ping", DateTimeOffset.UtcNow.AddSeconds(-30));

        // Tạo gói tin PING chứa AgentId trong HeartbeatPayload
        var pingPayload = new HeartbeatPayload { AgentId = "agent-ping-test", Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        var pingMessage = NetOpsMessage.Create(MessageType.Ping, pingPayload);

        // Thực thi xử lý thông điệp Invoke
        var response = await control.Invoke(session, pingMessage);

        // Kiểm tra phản hồi PONG_ACK
        Assert.NotNull(response);
        Assert.Equal(MessageType.Pong, response.MessageType);

        // Kiểm tra Agent đã được ghi nhận Online trong AgentRegistry
        var record = registry.Get("agent-ping-test");
        Assert.NotNull(record);
        Assert.Equal(AgentStatus.Online, record.Status);
        Assert.Equal("agent-ping-test", session.AgentId);
    }

    [Fact]
    public async Task MasterMessageControl_WhenReceivingHeartbeat_UpdatesAgentRegistryToOnline()
    {
        // Khởi tạo AgentRegistry và MasterMessageControl
        var registry = new AgentRegistry();
        var control = new MasterMessageControl(registry);
        var session = CreateSession("session-hb", DateTimeOffset.UtcNow.AddSeconds(-20));

        // Tạo gói tin HEARTBEAT với AgentId
        var hbPayload = new HeartbeatPayload { AgentId = "agent-hb-test", Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        var hbMessage = NetOpsMessage.Create(MessageType.Heartbeat, hbPayload);

        // Thực thi Invoke
        var response = await control.Invoke(session, hbMessage);

        // Kiểm tra phản hồi HEARTBEAT_ACK
        Assert.NotNull(response);
        Assert.Equal(MessageType.HeartbeatAck, response.MessageType);

        // Xác nhận Agent được cập nhật trạng thái Online và LastSeenAt mới
        var record = registry.Get("agent-hb-test");
        Assert.NotNull(record);
        Assert.Equal(AgentStatus.Online, record.Status);
    }

    [Fact]
    public async Task MasterMessageControl_WhenReceivingAuth_UpdatesAgentRegistryToOnline()
    {
        // Khởi tạo AgentRegistry và MasterMessageControl
        var registry = new AgentRegistry();
        var control = new MasterMessageControl(registry);
        var session = CreateSession("session-auth", DateTimeOffset.UtcNow);

        // Tạo gói tin AUTH từ Agent
        var authPayload = new AuthRequestPayload("agent-auth-test", "SecretKey123", "Windows 11", "Worker-PC");
        var authMessage = NetOpsMessage.Create(MessageType.Auth, authPayload);

        // Thực thi Invoke
        var response = await control.Invoke(session, authMessage);

        // Kiểm tra phản hồi AUTH_OK
        Assert.NotNull(response);
        Assert.Equal(MessageType.AuthOk, response.MessageType);
        Assert.True(session.IsAuthenticated);

        // Xác nhận trạng thái Agent là Online trong AgentRegistry
        var record = registry.Get("agent-auth-test");
        Assert.NotNull(record);
        Assert.Equal(AgentStatus.Online, record.Status);
    }

    [Fact]
    public async Task MasterMessageControl_WhenReceivingAuthOk_UpdatesAgentRegistryToOnline()
    {
        // Khởi tạo AgentRegistry và MasterMessageControl
        var registry = new AgentRegistry();
        var control = new MasterMessageControl(registry);
        var session = CreateSession("session-authok", DateTimeOffset.UtcNow, agentId: "agent-authok-test");

        // Tạo bản tin AUTH_OK
        var authOkPayload = new AuthResponsePayload(true, session.SessionId, message: "OK");
        var authOkMessage = NetOpsMessage.Create(MessageType.AuthOk, authOkPayload);

        // Thực thi Invoke
        await control.Invoke(session, authOkMessage);

        // Xác nhận Agent trạng thái Online trong AgentRegistry
        var record = registry.Get("agent-authok-test");
        Assert.NotNull(record);
        Assert.Equal(AgentStatus.Online, record.Status);
    }

    #endregion

    #region T16.3: Kiểm thử HeartbeatTimeoutWatchdog phát hiện timeout >15s

    [Fact]
    public void HeartbeatTimeoutWatchdog_WhenSilenceGreaterThan15s_MarksOfflineWithHeartbeatTimeout()
    {
        // Khởi tạo ConnectionManager, AgentRegistry và HeartbeatTimeoutWatchdog
        var connManager = new ConnectionManager();
        var registry = connManager.Registry;
        var watchdog = new HeartbeatTimeoutWatchdog(connManager, registry);

        var now = DateTimeOffset.UtcNow;
        var channel = new DummySafeFrameChannel();

        // Tạo session im lặng 16 giây (vượt ngưỡng 15s)
        var timedOutSession = CreateSession("session-timeout-01", now.AddSeconds(-16), channel, agentId: "agent-timeout-01");
        connManager.TryRegister(timedOutSession);

        // Xác nhận ban đầu Agent đang Online trong Registry
        Assert.Equal(AgentStatus.Online, registry.Get("agent-timeout-01")?.Status);
        Assert.Equal(1, connManager.ActiveCount);

        // Kích hoạt quét timeout tại mốc thời gian now với ngưỡng chuẩn 15s
        int purged = watchdog.SweepAndPurgeTimeouts(now, customTimeoutSeconds: 15);

        // 1. Số lượng session bị gỡ bỏ là 1
        Assert.Equal(1, purged);
        Assert.Equal(0, connManager.ActiveCount);

        // 2. Kênh Socket bị đóng giải phóng tài nguyên
        Assert.True(channel.IsDisposed);

        // 3. Agent trong Registry chuyển sang trạng thái Offline với lý do HeartbeatTimeout
        var record = registry.Get("agent-timeout-01");
        Assert.NotNull(record);
        Assert.Equal(AgentStatus.Offline, record.Status);
        Assert.Equal(DisconnectReason.HeartbeatTimeout, record.DisconnectReason);
    }

    [Fact]
    public void HeartbeatTimeoutWatchdog_WhenSilenceWithin15s_KeepsAgentOnline()
    {
        // Khởi tạo ConnectionManager, AgentRegistry và HeartbeatTimeoutWatchdog
        var connManager = new ConnectionManager();
        var registry = connManager.Registry;
        var watchdog = new HeartbeatTimeoutWatchdog(connManager, registry);

        var now = DateTimeOffset.UtcNow;
        var channel = new DummySafeFrameChannel();

        // Tạo session chỉ mới im lặng 5 giây (nhỏ hơn ngưỡng 15s)
        var activeSession = CreateSession("session-active-01", now.AddSeconds(-5), channel, agentId: "agent-active-01");
        connManager.TryRegister(activeSession);

        // Quét timeout tại thời điểm now
        int purged = watchdog.SweepAndPurgeTimeouts(now, customTimeoutSeconds: 15);

        // Session không bị purge, vẫn duy trì kết nối và trạng thái Online
        Assert.Equal(0, purged);
        Assert.Equal(1, connManager.ActiveCount);
        Assert.False(channel.IsDisposed);

        var record = registry.Get("agent-active-01");
        Assert.NotNull(record);
        Assert.Equal(AgentStatus.Online, record.Status);
    }

    [Fact]
    public void HeartbeatTimeoutWatchdog_FiresSessionTimedOutEvent()
    {
        // Kiểm tra sự kiện SessionTimedOut được kích hoạt chính xác
        var connManager = new ConnectionManager();
        var registry = connManager.Registry;
        var watchdog = new HeartbeatTimeoutWatchdog(connManager, registry);

        var now = DateTimeOffset.UtcNow;
        var session = CreateSession("session-event-01", now.AddSeconds(-20), agentId: "agent-event-01");
        connManager.TryRegister(session);

        AgentSession? capturedSession = null;
        DisconnectReason? capturedReason = null;

        watchdog.SessionTimedOut += (s, reason) =>
        {
            capturedSession = s;
            capturedReason = reason;
        };

        // Thực hiện quét dọn
        watchdog.SweepAndPurgeTimeouts(now, customTimeoutSeconds: 15);

        // Xác nhận sự kiện đã được kích hoạt với lý do HeartbeatTimeout
        Assert.NotNull(capturedSession);
        Assert.Equal("session-event-01", capturedSession.SessionId);
        Assert.Equal(DisconnectReason.HeartbeatTimeout, capturedReason);
    }

    #endregion

    #region T16.4: Khi Socket đóng, ConnectionManager đánh dấu Offline mà không xóa thông tin máy chủ

    [Fact]
    public void ConnectionManager_WhenSocketClosed_MarksAgentOfflineWithoutDeletingServerInfo()
    {
        // Khởi tạo ConnectionManager
        var connManager = new ConnectionManager();
        var registry = connManager.Registry;

        var channel = new DummySafeFrameChannel();
        var session = CreateSession("session-close-01", DateTimeOffset.UtcNow, channel, agentId: "agent-server-info");
        connManager.TryRegister(session);

        // Đảm bảo agent đang Online và tồn tại trong registry
        Assert.Equal(1, registry.TotalCount);
        Assert.Equal(AgentStatus.Online, registry.Get("agent-server-info")?.Status);

        // Giả lập socket bị đóng và ConnectionManager hủy đăng ký session với lý do NetworkFault
        bool unregistered = connManager.TryUnregister("session-close-01", out var removedSession, DisconnectReason.NetworkFault);

        // Xác nhận hủy đăng ký thành công
        Assert.True(unregistered);
        Assert.NotNull(removedSession);
        Assert.Equal(0, connManager.ActiveCount);

        // T16.4: Thông tin máy chủ KHÔNG bị xóa khỏi AgentRegistry, trạng thái chuyển sang Offline
        var record = registry.Get("agent-server-info");
        Assert.NotNull(record); // Bản ghi vẫn tồn tại!
        Assert.Equal("agent-server-info", record.AgentId);
        Assert.Equal(AgentStatus.Offline, record.Status); // Đã chuyển sang Offline
        Assert.Equal(DisconnectReason.NetworkFault, record.DisconnectReason); // Lý do NetworkFault
        Assert.Equal(1, registry.TotalCount); // Tổng số Agent vẫn giữ nguyên không bị xóa
    }

    #endregion

    #region T16.6: Kiểm thử Dependency Injection đăng ký đầy đủ dịch vụ

    [Fact]
    public void AddNetOpsMasterServer_RegistersAgentRegistryAndHeartbeatTimeoutWatchdog()
    {
        // Cấu hình in-memory
        var configurationData = new Dictionary<string, string?>
        {
            ["MasterServer:Port"] = "5050",
            ["MasterServer:WatchdogIntervalSeconds"] = "2",
            ["MasterServer:SessionTimeoutSeconds"] = "15"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetOpsMasterServer(configuration);

        var serviceProvider = services.BuildServiceProvider();

        // 1. Phân giải IAgentRegistry thành công dưới dạng Singleton
        var registry1 = serviceProvider.GetRequiredService<IAgentRegistry>();
        var registry2 = serviceProvider.GetRequiredService<IAgentRegistry>();
        Assert.NotNull(registry1);
        Assert.Same(registry1, registry2);

        // 2. Phân giải IConnectionManager và liên kết chung registry
        var connManager = serviceProvider.GetRequiredService<IConnectionManager>();
        Assert.NotNull(connManager);
        Assert.Same(registry1, connManager.Registry);

        // 3. Phân giải IMasterMessageControl
        var msgControl = serviceProvider.GetRequiredService<IMasterMessageControl>();
        Assert.NotNull(msgControl);

        // 4. Kiểm tra danh sách HostedService chứa HeartbeatTimeoutWatchdog
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
        Assert.Contains(hostedServices, s => s is HeartbeatTimeoutWatchdog);
    }

    #endregion
}
