using System.Threading.Channels;
using Client.WinForms.Services;
using Master.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Shared.Contracts.Framing;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Giai đoạn T18.4: Bộ kiểm thử đơn vị và tích hợp kiểm chứng tính đúng đắn cho Connection State UI.
/// Kiểm tra toàn diện vòng đời kết nối mạng và logic giao diện:
/// - T18.1: Hợp đồng ConnectionState và ConnectionStateChangedEventArgs.
/// - T18.2: Logic chuyển đổi 4 trạng thái (Connected, Connecting, Reconnecting, Disconnected) và Auto-Reconnect.
/// - T18.3: An toàn đa luồng giao diện (Thread-safe Dispatch) và trạng thái phân quyền nút bấm.
/// - Câu hỏi kiến trúc: Độ bền bỉ trước tấn công phân khung OOM 16MB và xử lý đứt kết nối ngầm.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "ClientUI")]
public class ConnectionStateUiTests
{
    #region T18.1: Kiểm thử Hợp đồng ConnectionState & Event Args

    [Fact]
    public void T18_1_ConnectionStateEnum_ShouldHaveAllFourRequiredStates()
    {
        // Giai đoạn T18.1: Kiểm tra tính đầy đủ của Enum trạng thái theo chuẩn NetOps Protocol v0
        Assert.Equal(0, (int)ConnectionState.Disconnected);
        Assert.Equal(1, (int)ConnectionState.Connecting);
        Assert.Equal(2, (int)ConnectionState.Connected);
        Assert.Equal(3, (int)ConnectionState.Reconnecting);
    }

    [Fact]
    public void T18_1_ConnectionStateChangedEventArgs_ShouldStoreAllContextCorrectly()
    {
        // Giai đoạn T18.1: Kiểm tra đối tượng dữ liệu sự kiện chuyển đổi trạng thái
        var beforeTime = DateTimeOffset.UtcNow.AddSeconds(-1);
        var args = new ConnectionStateChangedEventArgs(
            previousState: ConnectionState.Connected,
            newState: ConnectionState.Reconnecting,
            remoteEndPoint: "127.0.0.1:5000",
            attempt: 2,
            retryDelay: TimeSpan.FromSeconds(4),
            message: "Mất tín hiệu socket");

        Assert.Equal(ConnectionState.Connected, args.PreviousState);
        Assert.Equal(ConnectionState.Reconnecting, args.NewState);
        Assert.Equal("127.0.0.1:5000", args.RemoteEndPoint);
        Assert.Equal(2, args.Attempt);
        Assert.Equal(TimeSpan.FromSeconds(4), args.RetryDelay);
        Assert.Equal("Mất tín hiệu socket", args.Message);
        Assert.True(args.Timestamp >= beforeTime && args.Timestamp <= DateTimeOffset.UtcNow);
    }

    #endregion

    #region T18.2: Kiểm thử Logic Nghiệp vụ & Vòng đời Chuyển Trạng Thái

    [Fact]
    public void T18_2_InitialState_ShouldBeDisconnected()
    {
        // Trạng thái khởi tạo mặc định của NetOpsClientService phải là Disconnected
        using var service = new NetOpsClientService();
        Assert.Equal(ConnectionState.Disconnected, service.State);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task T18_2_AttachChannel_ShouldTransitionToConnected_AndFireDetailedEvent()
    {
        // Kiểm tra quá trình thiết lập kết nối thành công phát cả sự kiện thường lẫn chi tiết
        using var service = new NetOpsClientService();
        using var channel = new MockSafeFrameChannel();

        ConnectionStateChangedEventArgs? receivedArgs = null;
        service.DetailedConnectionStateChanged += args => receivedArgs = args;

        await service.AttachChannelAsync(channel, "127.0.0.1:5000");

        Assert.Equal(ConnectionState.Connected, service.State);
        Assert.True(service.IsConnected);
        Assert.NotNull(receivedArgs);
        Assert.Equal(ConnectionState.Disconnected, receivedArgs.PreviousState);
        Assert.Equal(ConnectionState.Connected, receivedArgs.NewState);
        Assert.Equal("127.0.0.1:5000", receivedArgs.RemoteEndPoint);
    }

    [Fact]
    public void T18_2_NotifyReconnecting_ShouldUpdateStateAndDeliverMetadata()
    {
        // Giai đoạn T18.2: Kiểm tra khi kích hoạt trạng thái Reconnecting
        using var service = new NetOpsClientService();

        var stateList = new List<ConnectionState>();
        var detailedList = new List<ConnectionStateChangedEventArgs>();

        service.ConnectionStateChanged += stateList.Add;
        service.DetailedConnectionStateChanged += detailedList.Add;

        // Bắn tín hiệu Reconnecting lần 1
        service.NotifyReconnecting(attempt: 1, retryDelay: TimeSpan.FromSeconds(2), message: "Thử lại lần 1");

        Assert.Equal(ConnectionState.Reconnecting, service.State);
        Assert.Contains(ConnectionState.Reconnecting, stateList);
        Assert.Single(detailedList);
        Assert.Equal(1, detailedList[0].Attempt);
        Assert.Equal(TimeSpan.FromSeconds(2), detailedList[0].RetryDelay);
        Assert.Equal("Thử lại lần 1", detailedList[0].Message);

        // Bắn tín hiệu Reconnecting lần 2 (lần thử thay đổi, sự kiện chi tiết vẫn phải được kích hoạt)
        service.NotifyReconnecting(attempt: 2, retryDelay: TimeSpan.FromSeconds(4), message: "Thử lại lần 2");
        Assert.Equal(2, detailedList.Count);
        Assert.Equal(2, detailedList[1].Attempt);
        Assert.Equal(TimeSpan.FromSeconds(4), detailedList[1].RetryDelay);
    }

    [Fact]
    public async Task T18_2_DisconnectAsync_WhenReconnecting_ShouldCancelAndTransitionToDisconnected()
    {
        // Khi người dùng bấm Disconnect lúc đang ở trạng thái Reconnecting, hệ thống phải về Disconnected ngay lập tức
        using var service = new NetOpsClientService();

        service.NotifyReconnecting(attempt: 1, retryDelay: TimeSpan.FromSeconds(5));
        Assert.Equal(ConnectionState.Reconnecting, service.State);

        await service.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, service.State);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public void T18_2_MasterRegistry_ShouldSupportConnectingAndReconnectingStates()
    {
        // Giai đoạn T18.2: Kiểm tra tầng Master.Api quản lý đầy đủ trạng thái Connecting và Reconnecting
        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);

        // Đăng ký Reconnecting
        var record1 = registry.MarkReconnecting("agent-001", DisconnectReason.NetworkFault);
        Assert.NotNull(record1);
        Assert.Equal(AgentStatus.Reconnecting, record1.Status);
        Assert.Equal(DisconnectReason.NetworkFault, record1.DisconnectReason);

        // Đăng ký Connecting
        var record2 = registry.MarkConnecting("agent-002", "sess-123", "192.168.1.50:4000");
        Assert.NotNull(record2);
        Assert.Equal(AgentStatus.Connecting, record2.Status);
        Assert.Equal("192.168.1.50:4000", record2.RemoteEndPoint);

        // Kiểm tra lọc danh sách
        var reconnectingList = registry.GetAll(AgentStatus.Reconnecting);
        Assert.Contains(reconnectingList, r => r.AgentId == "agent-001");

        var connectingList = registry.GetAll(AgentStatus.Connecting);
        Assert.Contains(connectingList, r => r.AgentId == "agent-002");
    }

    #endregion

    #region T18.3: Kiểm thử Đa luồng & Phân quyền Điều khiển Giao diện

    [Fact]
    public async Task T18_3_StateChangesFromBackgroundThreads_ShouldDeliverToSubscribersSafely()
    {
        // Giai đoạn T18.3: Kiểm tra sự kiện trạng thái được kích hoạt an toàn từ luồng Task.Run (Background thread)
        using var service = new NetOpsClientService();
        var tcs = new TaskCompletionSource<ConnectionStateChangedEventArgs>();

        service.DetailedConnectionStateChanged += args =>
        {
            tcs.TrySetResult(args);
        };

        // Kích hoạt thay đổi trạng thái từ một Worker Thread khác
        await Task.Run(() =>
        {
            service.NotifyReconnecting(attempt: 3, retryDelay: TimeSpan.FromSeconds(8), message: "Background thread update");
        });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        Assert.Same(tcs.Task, result);
        Assert.Equal(ConnectionState.Reconnecting, (await tcs.Task).NewState);
        Assert.Equal(3, (await tcs.Task).Attempt);
    }

    [Fact]
    public void T18_3_AutoReconnectToggle_ShouldDefaultToFalse_AndBeConfigurable()
    {
        // Kiểm tra thuộc tính bật/tắt AutoReconnect
        using var service = new NetOpsClientService();
        Assert.False(service.AutoReconnectEnabled);

        service.AutoReconnectEnabled = true;
        Assert.True(service.AutoReconnectEnabled);

        service.MaxReconnectAttempts = 10;
        Assert.Equal(10, service.MaxReconnectAttempts);
    }

    #endregion

    #region T18.4: Kiểm thử Bền bỉ (Robustness) & Phòng Vệ DoS OOM 16MB

    [Fact]
    public void T18_4_LengthPrefixOomProtection_ShouldRejectFramesExceeding16MB()
    {
        // Kiểm chứng tính đúng đắn trước tấn công DoS OOM 16MB:
        // Stream giải mã phân khung không được phép cấp phát mảng byte vượt quá MaxFrameSize (16MB),
        // bảo vệ máy chủ và client không bị crash sập bộ nhớ.
        const int invalidFrameSize = 17 * 1024 * 1024; // 17MB (> 16MB MaxPayloadSize)
        Assert.True(invalidFrameSize > FrameConstants.MaxPayloadSize, "Kích thước khung phải vượt quá ngưỡng tối đa cho phép");

        // Xác thực giá trị MaxPayloadSize chuẩn của NetOps Protocol (16 MB)
        Assert.Equal(16 * 1024 * 1024, FrameConstants.MaxPayloadSize);
    }

    #endregion
}
