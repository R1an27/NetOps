using Master.Api.Extensions;
using Master.Api.Options;
using Master.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử chuyên sâu cho SessionWatchdogService và hệ sinh thái Dependency Injection trong Master.Api.
/// Kiểm chứng:
/// 1. Đăng ký và phân giải đầy đủ dịch vụ (DI Container Resolution) qua extension method AddNetOpsMasterServer.
/// 2. Khả năng fallback cấu hình cổng TCP linh hoạt từ TcpServer sang MasterServer.
/// 3. Cơ chế quét tự động và dọn dẹp các Half-Open Socket quá hạn thời gian chờ (LastSeenAt).
/// 4. Giữ nguyên vẹn các kết nối Agent đang hoạt động tích cực.
/// 5. Giải phóng triệt để luồng kênh truyền (SafeStream.Dispose()) khi purge session.
/// 6. Miễn nhiễm lỗi khi đóng kết nối socket bất thường không làm gián đoạn chu trình quét.
/// 7. Vòng lặp giám sát chạy nền với PeriodicTimer và dừng êm dịu khi nhận tín hiệu hủy.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Master")]
public class SessionWatchdogServiceTests
{
    private static AgentSession CreateSession(string sessionId, DateTimeOffset lastSeenAt, ISafeFrameChannel? channel = null, string? agentId = null)
    {
        var safeChannel = channel ?? new DummySafeFrameChannel();
        var session = new AgentSession(sessionId, safeChannel, "127.0.0.1:45000", lastSeenAt, agentId: agentId)
        {
            LastSeenAt = lastSeenAt
        };
        return session;
    }

    #region 1. Kiểm Thử Đăng Ký Dependency Injection & Options Pattern

    [Fact]
    public void AddNetOpsMasterServer_RegistersAllExpectedServicesAndOptions()
    {
        var configurationData = new Dictionary<string, string?>
        {
            ["MasterServer:Port"] = "6060",
            ["MasterServer:WatchdogIntervalSeconds"] = "3",
            ["MasterServer:SessionTimeoutSeconds"] = "9"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetOpsMasterServer(configuration);

        var serviceProvider = services.BuildServiceProvider();

        // 1. Kiểm tra Options
        var options = serviceProvider.GetRequiredService<IOptions<MasterServerOptions>>().Value;
        Assert.NotNull(options);
        Assert.Equal(6060, options.Port);
        Assert.Equal(3, options.WatchdogIntervalSeconds);
        Assert.Equal(9, options.SessionTimeoutSeconds);

        // 2. Kiểm tra Singleton IConnectionManager
        var connManager1 = serviceProvider.GetRequiredService<IConnectionManager>();
        var connManager2 = serviceProvider.GetRequiredService<IConnectionManager>();
        Assert.NotNull(connManager1);
        Assert.Same(connManager1, connManager2);

        // 3. Kiểm tra Singleton IMasterMessageControl
        var msgControl1 = serviceProvider.GetRequiredService<IMasterMessageControl>();
        var msgControl2 = serviceProvider.GetRequiredService<IMasterMessageControl>();
        Assert.NotNull(msgControl1);
        Assert.Same(msgControl1, msgControl2);

        // 4. Kiểm tra các HostedService đã được đăng ký
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
        Assert.Contains(hostedServices, s => s is TcpManagerServer);
        Assert.Contains(hostedServices, s => s is SessionWatchdogService);
    }

    [Fact]
    public void MasterServerOptions_FallbackToTcpServerPort_WhenMasterServerSectionMissing()
    {
        var configurationData = new Dictionary<string, string?>
        {
            ["TcpServer:Port"] = "7070"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetOpsMasterServer(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<MasterServerOptions>>().Value;

        Assert.Equal(7070, options.Port);
        Assert.Equal(5, options.WatchdogIntervalSeconds); // Mặc định
        Assert.Equal(15, options.SessionTimeoutSeconds); // Mặc định
    }

    #endregion

    #region 2. Kiểm Thử Nghiệp Vụ Purge Half-Open Sockets Của Watchdog

    [Fact]
    public void PurgeTimedOutSessions_RemovesOnlyInactiveSessions_AndKeepsActiveSessions()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        // Session 1: Mới hoạt động cách đây 5 giây (còn sống)
        var activeSession = CreateSession("sess-active", now.AddSeconds(-5));
        // Session 2: Quá hạn 20 giây (chết ngầm)
        var deadSession1 = CreateSession("sess-dead-1", now.AddSeconds(-20));
        // Session 3: Quá hạn 60 giây (chết ngầm)
        var deadSession2 = CreateSession("sess-dead-2", now.AddSeconds(-60));

        manager.TryRegister(activeSession);
        manager.TryRegister(deadSession1);
        manager.TryRegister(deadSession2);
        Assert.Equal(3, manager.ActiveCount);

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            SessionTimeoutSeconds = 15
        });

        var watchdog = new SessionWatchdogService(manager, options);

        // Kích hoạt quét tại thời điểm now
        int purgedCount = watchdog.PurgeTimedOutSessions(now);

        Assert.Equal(2, purgedCount);
        Assert.Equal(1, manager.ActiveCount);
        Assert.Same(activeSession, manager.GetSession("sess-active"));
        Assert.Null(manager.GetSession("sess-dead-1"));
        Assert.Null(manager.GetSession("sess-dead-2"));
    }

    [Fact]
    public void PurgeTimedOutSessions_DisposesSafeStream_OfPurgedSessions()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        var channel = new DummySafeFrameChannel();
        var deadSession = CreateSession("sess-to-dispose", now.AddSeconds(-30), channel);
        manager.TryRegister(deadSession);

        Assert.False(channel.IsDisposed);

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            SessionTimeoutSeconds = 10
        });

        var watchdog = new SessionWatchdogService(manager, options);
        int purged = watchdog.PurgeTimedOutSessions(now);

        Assert.Equal(1, purged);
        Assert.True(channel.IsDisposed);
        Assert.Equal(0, manager.ActiveCount);
    }

    [Fact]
    public void PurgeTimedOutSessions_WhenNoSessionsTimedOut_DoesNothing()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        manager.TryRegister(CreateSession("sess-1", now.AddSeconds(-2)));
        manager.TryRegister(CreateSession("sess-2", now.AddSeconds(-4)));
        Assert.Equal(2, manager.ActiveCount);

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            SessionTimeoutSeconds = 15
        });

        var watchdog = new SessionWatchdogService(manager, options);
        int purged = watchdog.PurgeTimedOutSessions(now);

        Assert.Equal(0, purged);
        Assert.Equal(2, manager.ActiveCount);
    }

    [Fact]
    public void PurgeTimedOutSessions_WhenExceptionOccursDuringSessionClosing_ContinuesSafely()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        var faultyChannel = new FaultyDisposeSafeFrameChannel();
        var normalChannel = new DummySafeFrameChannel();

        var faultySession = CreateSession("sess-faulty", now.AddSeconds(-30), faultyChannel);
        var normalSession = CreateSession("sess-normal", now.AddSeconds(-30), normalChannel);

        manager.TryRegister(faultySession);
        manager.TryRegister(normalSession);

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            SessionTimeoutSeconds = 15
        });

        var watchdog = new SessionWatchdogService(manager, options);

        // Không được quăng unhandled exception, tiếp tục purge session thứ 2
        var exception = Record.Exception(() =>
        {
            int purged = watchdog.PurgeTimedOutSessions(now);
            Assert.Equal(2, purged);
        });

        Assert.Null(exception);
        Assert.Equal(0, manager.ActiveCount);
        Assert.True(normalChannel.IsDisposed);
    }

    #endregion

    #region 3. Kiểm Thử Tiến Trình Chạy Nền BackgroundService

    [Fact]
    public async Task WatchdogService_ExecuteAsync_RunsPeriodicSweep_AndCancelsGracefully()
    {
        var manager = new ConnectionManager();
        var deadSession = CreateSession("sess-background", DateTimeOffset.UtcNow.AddSeconds(-20));
        manager.TryRegister(deadSession);

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            WatchdogIntervalSeconds = 1, // Quét mỗi 1 giây
            SessionTimeoutSeconds = 5    // Timeout sau 5 giây
        });

        var watchdog = new SessionWatchdogService(manager, options);

        using var cts = new CancellationTokenSource();
        // Khởi chạy tiến trình nền
        await watchdog.StartAsync(cts.Token);

        // Chờ 1.5 giây để PeriodicTimer thực hiện ít nhất một chu kỳ quét
        await Task.Delay(1500);

        // Session đã được tự động dọn dẹp ngầm
        Assert.Equal(0, manager.ActiveCount);
        Assert.Null(manager.GetSession("sess-background"));

        // Dừng dịch vụ êm dịu
        await watchdog.StopAsync(CancellationToken.None);
    }

    #endregion

    #region 4. Kiểm Thử Cơ Chế Timeout & Tự Động Đánh Dấu Agent Offline (Giai đoạn 3)

    [Fact]
    public void Watchdog_WhenSessionTimesOut_AutomaticallyMarksAgentOfflineWithHeartbeatTimeoutReason()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        // Giả lập Agent kết nối ban đầu
        var session = CreateSession("sess-agent-01", now.AddSeconds(-20), agentId: "agent-linux-node");
        manager.TryRegister(session);

        // Ban đầu Agent phải ở trạng thái Online
        var initialStatus = manager.GetAgentStatus("agent-linux-node");
        Assert.NotNull(initialStatus);
        Assert.Equal(AgentStatus.Online, initialStatus.Status);
        Assert.Equal(DisconnectReason.None, initialStatus.DisconnectReason);
        Assert.Null(initialStatus.DisconnectedAt);
        Assert.Equal("sess-agent-01", initialStatus.CurrentSessionId);

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            SessionTimeoutSeconds = 15 // Ngưỡng timeout 15s chuẩn Giai đoạn 3
        });

        var watchdog = new SessionWatchdogService(manager, options);

        // Kích hoạt quét dọn dẹp tại thời điểm now (đã quá hạn 20s > 15s)
        int purged = watchdog.PurgeTimedOutSessions(now);

        Assert.Equal(1, purged);
        Assert.Equal(0, manager.ActiveCount);

        // Sau khi Watchdog purge, Agent phải tự động chuyển sang trạng thái Offline
        var offlineStatus = manager.GetAgentStatus("agent-linux-node");
        Assert.NotNull(offlineStatus);
        Assert.Equal(AgentStatus.Offline, offlineStatus.Status);
        Assert.Equal(DisconnectReason.HeartbeatTimeout, offlineStatus.DisconnectReason);
        Assert.NotNull(offlineStatus.DisconnectedAt);
        Assert.Null(offlineStatus.CurrentSessionId);
    }

    [Fact]
    public void Watchdog_WhenSessionTimesOut_FiresAgentOfflineDetectedAndStatusChangedEvents()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        var session = CreateSession("sess-event-01", now.AddSeconds(-25), agentId: "agent-event-node");
        manager.TryRegister(session);

        AgentStatusRecord? offlineDetectedRecord = null;
        AgentStatusRecord? statusChangedRecord = null;
        AgentSession? timedOutSession = null;
        DisconnectReason? watchdogReportedReason = null;

        manager.AgentOfflineDetected += record => offlineDetectedRecord = record;
        manager.AgentStatusChanged += record => statusChangedRecord = record;

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            SessionTimeoutSeconds = 15
        });

        var watchdog = new SessionWatchdogService(manager, options);
        watchdog.SessionTimedOut += (sess, reason) =>
        {
            timedOutSession = sess;
            watchdogReportedReason = reason;
        };

        // Kích hoạt quét timeout
        watchdog.PurgeTimedOutSessions(now);

        // Kiểm chứng các sự kiện được kích hoạt đầy đủ và chuẩn xác
        Assert.NotNull(offlineDetectedRecord);
        Assert.Equal("agent-event-node", offlineDetectedRecord.AgentId);
        Assert.Equal(AgentStatus.Offline, offlineDetectedRecord.Status);
        Assert.Equal(DisconnectReason.HeartbeatTimeout, offlineDetectedRecord.DisconnectReason);

        Assert.NotNull(statusChangedRecord);
        Assert.Equal("agent-event-node", statusChangedRecord.AgentId);
        Assert.Equal(AgentStatus.Offline, statusChangedRecord.Status);

        Assert.NotNull(timedOutSession);
        Assert.Equal("sess-event-01", timedOutSession.SessionId);
        Assert.Equal(DisconnectReason.HeartbeatTimeout, watchdogReportedReason);
    }

    [Fact]
    public void Watchdog_WhenAgentActiveWithin15Seconds_RemainsOnlineAndDoesNotTimeout()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        // Session mới hoạt động cách đây 10 giây (chưa chạm ngưỡng 15 giây)
        var session = CreateSession("sess-healthy", now.AddSeconds(-10), agentId: "agent-healthy");
        manager.TryRegister(session);

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            SessionTimeoutSeconds = 15
        });

        var watchdog = new SessionWatchdogService(manager, options);
        int purged = watchdog.PurgeTimedOutSessions(now);

        Assert.Equal(0, purged);
        Assert.Equal(1, manager.ActiveCount);

        var status = manager.GetAgentStatus("agent-healthy");
        Assert.NotNull(status);
        Assert.Equal(AgentStatus.Online, status.Status);
        Assert.Equal(DisconnectReason.None, status.DisconnectReason);
    }

    [Fact]
    public void Agent_WhenReconnectingAfterTimeout_TransitionsBackFromOfflineToOnline()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        // 1. Agent kết nối lần đầu
        var sessionOld = CreateSession("sess-old", now.AddSeconds(-30), agentId: "agent-reconnect");
        manager.TryRegister(sessionOld);

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            SessionTimeoutSeconds = 15
        });

        var watchdog = new SessionWatchdogService(manager, options);
        watchdog.PurgeTimedOutSessions(now);

        // Xác nhận Agent đã bị đánh dấu Offline
        var offlineRecord = manager.GetAgentStatus("agent-reconnect");
        Assert.NotNull(offlineRecord);
        Assert.Equal(AgentStatus.Offline, offlineRecord.Status);
        Assert.Equal(DisconnectReason.HeartbeatTimeout, offlineRecord.DisconnectReason);

        // 2. Agent phục hồi mạng và kết nối lại (Exponential Backoff reconnect)
        var sessionNew = CreateSession("sess-new", now, agentId: "agent-reconnect");
        manager.TryRegister(sessionNew);

        // Trạng thái Agent phải tự động khôi phục sang Online
        var reconnectedRecord = manager.GetAgentStatus("agent-reconnect");
        Assert.NotNull(reconnectedRecord);
        Assert.Equal(AgentStatus.Online, reconnectedRecord.Status);
        Assert.Equal(DisconnectReason.None, reconnectedRecord.DisconnectReason);
        Assert.Null(reconnectedRecord.DisconnectedAt);
        Assert.Equal("sess-new", reconnectedRecord.CurrentSessionId);
    }

    [Fact]
    public void Watchdog_ConcurrentMultipleAgents_PurgesOnlyTimedOutAndPreservesActiveStatuses()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        // Tạo 3 agent hoạt động tốt (<15s) và 3 agent chết ngầm (>15s)
        for (int i = 1; i <= 3; i++)
        {
            var activeSession = CreateSession($"sess-live-{i}", now.AddSeconds(-5), agentId: $"agent-live-{i}");
            manager.TryRegister(activeSession);

            var deadSession = CreateSession($"sess-dead-{i}", now.AddSeconds(-25), agentId: $"agent-dead-{i}");
            manager.TryRegister(deadSession);
        }

        Assert.Equal(6, manager.ActiveCount);

        var options = Microsoft.Extensions.Options.Options.Create(new MasterServerOptions
        {
            SessionTimeoutSeconds = 15
        });

        var watchdog = new SessionWatchdogService(manager, options);
        int purged = watchdog.PurgeTimedOutSessions(now);

        Assert.Equal(3, purged);
        Assert.Equal(3, manager.ActiveCount);

        // Kiểm tra danh sách trạng thái tổng hợp
        var onlineAgents = manager.GetAllAgentStatuses(AgentStatus.Online);
        var offlineAgents = manager.GetAllAgentStatuses(AgentStatus.Offline);

        Assert.Equal(3, onlineAgents.Count);
        Assert.Equal(3, offlineAgents.Count);

        foreach (var dead in offlineAgents)
        {
            Assert.Equal(DisconnectReason.HeartbeatTimeout, dead.DisconnectReason);
            Assert.Null(dead.CurrentSessionId);
        }
    }

    [Fact]
    public void ConnectionManager_UnregisterWithDifferentReasons_RecordsReasonAccurately()
    {
        var manager = new ConnectionManager();
        var now = DateTimeOffset.UtcNow;

        // Session 1: Ngắt kết nối êm dịu (Graceful EOF)
        var session1 = CreateSession("sess-graceful", now, agentId: "agent-graceful");
        manager.TryRegister(session1);
        manager.TryUnregister("sess-graceful", out _, DisconnectReason.GracefulDisconnect);

        var status1 = manager.GetAgentStatus("agent-graceful");
        Assert.NotNull(status1);
        Assert.Equal(AgentStatus.Offline, status1.Status);
        Assert.Equal(DisconnectReason.GracefulDisconnect, status1.DisconnectReason);

        // Session 2: Lỗi mạng bất thường (NetworkFault / Socket RST)
        var session2 = CreateSession("sess-fault", now, agentId: "agent-fault");
        manager.TryRegister(session2);
        manager.TryUnregister("sess-fault", out _, DisconnectReason.NetworkFault);

        var status2 = manager.GetAgentStatus("agent-fault");
        Assert.NotNull(status2);
        Assert.Equal(AgentStatus.Offline, status2.Status);
        Assert.Equal(DisconnectReason.NetworkFault, status2.DisconnectReason);
    }

    #endregion

    #region Test Double Helpers

    private sealed class DummySafeFrameChannel : ISafeFrameChannel
    {
        public bool IsDisposed { get; private set; }
        public bool IsConnected => !IsDisposed;
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

    private sealed class FaultyDisposeSafeFrameChannel : ISafeFrameChannel
    {
        public bool IsConnected => true;
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
            throw new InvalidOperationException("Simulated socket teardown fault during dispose");
        }

        public ValueTask DisposeAsync()
        {
            throw new InvalidOperationException("Simulated socket teardown fault during dispose");
        }
    }

    #endregion
}
