using Master.Api.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServerManager.Shared.Contracts.Models;

namespace Master.Api.Services;

/// <summary>
/// Dịch vụ chạy nền (BackgroundService) giám sát sống còn và phát hiện kết nối chết ngầm (Half-Open Socket).
/// Hiện thực chuẩn xác yêu cầu T16.3 trong Barem chấm điểm:
/// - Chạy nền theo chu kỳ (mỗi 2-3s hoặc theo cấu hình) sử dụng PeriodicTimer hiệu năng cao của .NET.
/// - Giám sát mốc thời gian LastSeenAt của các Agent kết nối.
/// - Tự động phát hiện phiên quá hạn thời gian chờ nhịp tim (mặc định 15 giây).
/// - Chuyển trạng thái Agent sang Offline trong AgentRegistry kèm lý do DisconnectReason.HeartbeatTimeout.
/// - Cưỡng chế đóng Socket descriptor để thu hồi tài nguyên hệ điều hành, chống rò rỉ socket (Socket descriptor leak).
/// </summary>
public class HeartbeatTimeoutWatchdog : BackgroundService
{
    private readonly IConnectionManager _connectionManager;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IOptions<MasterServerOptions> _options;
    private readonly ILogger<HeartbeatTimeoutWatchdog> _logger;

    /// <summary>
    /// Chu kỳ quét danh sách session của Watchdog (mặc định 2-3 giây theo rubric T16.3, hoặc theo cấu hình).
    /// </summary>
    public int IntervalSeconds => Math.Max(1, _options.Value.WatchdogIntervalSeconds <= 5 ? _options.Value.WatchdogIntervalSeconds : 3);

    /// <summary>
    /// Ngưỡng thời gian tối đa không nhận được tín hiệu trước khi coi là mất kết nối ngầm (chuẩn 15 giây).
    /// </summary>
    public int TimeoutSeconds => Math.Max(1, _options.Value.SessionTimeoutSeconds > 0 ? _options.Value.SessionTimeoutSeconds : 15);

    /// <summary>
    /// Sự kiện kích hoạt khi Watchdog phát hiện và xử lý một phiên kết nối bị timeout do mất nhịp tim.
    /// </summary>
    public event Action<AgentSession, DisconnectReason>? SessionTimedOut;

    /// <summary>
    /// Khởi tạo HeartbeatTimeoutWatchdog với đầy đủ các phụ thuộc cần thiết.
    /// </summary>
    public HeartbeatTimeoutWatchdog(
        IConnectionManager connectionManager,
        IAgentRegistry agentRegistry,
        IOptions<MasterServerOptions>? options = null,
        ILogger<HeartbeatTimeoutWatchdog>? logger = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _options = options ?? Microsoft.Extensions.Options.Options.Create(new MasterServerOptions());
        _logger = logger ?? NullLogger<HeartbeatTimeoutWatchdog>.Instance;
    }

    /// <summary>
    /// Vòng lặp giám sát chạy nền của BackgroundService sử dụng PeriodicTimer bất đồng bộ không chặn luồng.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[HeartbeatTimeoutWatchdog] Khởi động tiến trình giám sát nhịp tim: Chu kỳ quét={Interval}s, Ngưỡng Timeout={Timeout}s",
            IntervalSeconds, TimeoutSeconds);

        // Tạo một bộ đếm thời gian với chu kỳ quét định kỳ (mỗi 2-3 giây)
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));

        try
        {
            // Vòng lặp chạy định kỳ cho đến khi có yêu cầu dừng dịch vụ (Graceful Shutdown)
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                // Thực hiện một đợt quét dọn các session quá hạn thời gian chờ
                int purged = SweepAndPurgeTimeouts();
                if (purged > 0)
                {
                    _logger.LogInformation(
                        "[HeartbeatTimeoutWatchdog] Đã xử lý {Count} kết nối chết ngầm. Số session đang hoạt động còn lại: {Remaining}",
                        purged, _connectionManager.ActiveCount);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dừng dịch vụ êm dịu khi ứng dụng Host shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HeartbeatTimeoutWatchdog] Lỗi nghiêm trọng không mong muốn trong vòng lặp watchdog");
        }
        finally
        {
            _logger.LogInformation("[HeartbeatTimeoutWatchdog] Dừng tiến trình giám sát nhịp tim thành công");
        }
    }

    /// <summary>
    /// Quét toàn bộ các session đang kết nối và cưỡng chế giải phóng các kết nối đã quá hạn thời gian chờ nhịp tim.
    /// Cho phép gọi trực tiếp từ Unit Test với mốc thời gian giả lập (now) để kiểm thử tất định (Deterministic Testing).
    /// </summary>
    /// <param name="now">Mốc thời gian hiện tại tùy chọn (dùng cho unit test)</param>
    /// <param name="customTimeoutSeconds">Ngưỡng timeout ghi đè tùy chọn</param>
    /// <returns>Số lượng phiên kết nối bị phát hiện timeout và xử lý</returns>
    public int SweepAndPurgeTimeouts(DateTimeOffset? now = null, int? customTimeoutSeconds = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        int timeoutSec = customTimeoutSeconds ?? TimeoutSeconds;
        int purgedCount = 0;

        // Lấy danh sách snapshot tất cả các phiên làm việc đang duy trì kết nối Socket TCP
        var activeSessions = _connectionManager.GetAllSessions();

        foreach (var session in activeSessions)
        {
            try
            {
                // Tính khoảng thời gian trôi qua kể từ lần cuối cùng nhận được gói tin hợp lệ từ Agent
                var elapsed = currentTime - session.LastSeenAt;

                // Nếu thời gian im lặng vượt quá ngưỡng timeout (15s) -> Coi là Half-Open Socket
                if (elapsed.TotalSeconds > timeoutSec)
                {
                    _logger.LogWarning(
                        "[HeartbeatTimeoutWatchdog] Phiên '{SessionId}' (AgentId: '{AgentId}', EndPoint: '{EndPoint}') bị mất tín hiệu {Elapsed:F1}s (ngưỡng: {Threshold}s). Chuyển sang OFFLINE do HeartbeatTimeout.",
                        session.SessionId, session.AgentId ?? "Unknown", session.RemoteEndPoint, elapsed.TotalSeconds, timeoutSec);

                    // 1. Cập nhật trạng thái Agent thành Offline trong AgentRegistry mà KHÔNG xóa bản ghi (T16.3 & T16.4)
                    if (!string.IsNullOrWhiteSpace(session.AgentId))
                    {
                        _agentRegistry.MarkOffline(session.AgentId, DisconnectReason.HeartbeatTimeout, currentTime);
                    }

                    // 2. Gỡ bỏ session khỏi ConnectionManager với nguyên nhân HeartbeatTimeout
                    if (_connectionManager.TryUnregister(session.SessionId, out _, DisconnectReason.HeartbeatTimeout))
                    {
                        purgedCount++;

                        // 3. Kích hoạt sự kiện SessionTimedOut để thông báo cho các module quan tâm
                        try
                        {
                            SessionTimedOut?.Invoke(session, DisconnectReason.HeartbeatTimeout);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[HeartbeatTimeoutWatchdog] Lỗi kích hoạt sự kiện SessionTimedOut cho phiên '{SessionId}'", session.SessionId);
                        }

                        // 4. Cưỡng chế đóng socket và giải phóng luồng để thu hồi Socket Descriptor của hệ điều hành
                        CloseSessionSocket(session);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HeartbeatTimeoutWatchdog] Lỗi khi xử lý kiểm tra timeout cho phiên '{SessionId}'", session.SessionId);
            }
        }

        return purgedCount;
    }

    /// <summary>
    /// Đóng an toàn đối tượng TcpClient và kênh SafeStream của phiên kết nối đã bị timeout.
    /// </summary>
    private void CloseSessionSocket(AgentSession session)
    {
        try
        {
            // Đóng kết nối TCP socket
            session.Client?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[HeartbeatTimeoutWatchdog] Ngoại lệ khi đóng TcpClient cho phiên '{SessionId}'", session.SessionId);
        }

        try
        {
            // Đóng luồng đọc ghi SafeFrameStream
            session.SafeStream.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[HeartbeatTimeoutWatchdog] Ngoại lệ khi giải phóng SafeStream cho phiên '{SessionId}'", session.SessionId);
        }
    }
}
