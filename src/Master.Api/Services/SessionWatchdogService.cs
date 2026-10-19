using Master.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Master.Api.Services;

/// <summary>
/// Dịch vụ chạy nền (BackgroundService) giám sát sống còn và phát hiện các kết nối mạng ngầm (Half-Open Socket / Ghost Connection).
/// Định kỳ quét danh sách phiên làm việc trong IConnectionManager, tự động cưỡng chế đóng socket và gỡ bỏ các session
/// có thời gian không phản hồi (LastSeenAt) vượt quá ngưỡng thời gian chờ cấu hình (SessionTimeoutSeconds).
/// </summary>
public class SessionWatchdogService : BackgroundService
{
    private readonly IConnectionManager _connectionManager;
    private readonly IOptions<MasterServerOptions> _options;
    private readonly ILogger<SessionWatchdogService> _logger;

    /// <summary>
    /// Chu kỳ quét danh sách session của Watchdog (tính bằng giây).
    /// </summary>
    public int IntervalSeconds => Math.Max(1, _options.Value.WatchdogIntervalSeconds);

    /// <summary>
    /// Ngưỡng thời gian không có hoạt động tối đa trước khi coi session là Half-Open Socket (tính bằng giây).
    /// </summary>
    public int TimeoutSeconds => Math.Max(1, _options.Value.SessionTimeoutSeconds);

    /// <summary>
    /// Khởi tạo SessionWatchdogService với các phụ thuộc cần thiết.
    /// </summary>
    public SessionWatchdogService(
        IConnectionManager connectionManager,
        IOptions<MasterServerOptions>? options = null,
        ILogger<SessionWatchdogService>? logger = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _options = options ?? Microsoft.Extensions.Options.Options.Create(new MasterServerOptions());
        _logger = logger ?? NullLogger<SessionWatchdogService>.Instance;
    }

    /// <summary>
    /// Vòng lặp giám sát định kỳ sử dụng PeriodicTimer của .NET hiệu năng cao.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[SessionWatchdog] Started monitoring with interval {Interval}s and session timeout {Timeout}s",
            IntervalSeconds, TimeoutSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));

        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                int purged = PurgeTimedOutSessions();
                if (purged > 0)
                {
                    _logger.LogInformation(
                        "[SessionWatchdog] Purged {Count} half-open sessions. Remaining active sessions: {Remaining}",
                        purged, _connectionManager.ActiveCount);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dừng dịch vụ êm dịu khi host shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SessionWatchdog] Fatal unexpected error in watchdog loop");
        }
        finally
        {
            _logger.LogInformation("[SessionWatchdog] Stopped monitoring successfully");
        }
    }

    /// <summary>
    /// Quét và cưỡng chế giải phóng tất cả các session quá hạn thời gian chờ.
    /// Cho phép gọi trực tiếp từ Unit Test hoặc các trình điều phối bên ngoài một cách tất định.
    /// </summary>
    /// <param name="now">Mốc thời gian hiện tại tùy chọn (dùng cho unit test mô phỏng tương lai)</param>
    /// <param name="customTimeoutSeconds">Ngưỡng timeout tùy chọn ghi đè</param>
    /// <returns>Số lượng session bị loại bỏ</returns>
    public int PurgeTimedOutSessions(DateTimeOffset? now = null, int? customTimeoutSeconds = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        int timeoutSec = customTimeoutSeconds ?? TimeoutSeconds;
        int purgedCount = 0;

        var activeSessions = _connectionManager.GetAllSessions();
        foreach (var session in activeSessions)
        {
            try
            {
                var elapsed = currentTime - session.LastSeenAt;
                if (elapsed.TotalSeconds > timeoutSec)
                {
                    _logger.LogWarning(
                        "[SessionWatchdog] Session '{SessionId}' (AgentId: '{AgentId}', EndPoint: '{EndPoint}') timed out after {Elapsed:F1}s (threshold: {Threshold}s). Purging half-open socket.",
                        session.SessionId, session.AgentId ?? "Unknown", session.RemoteEndPoint, elapsed.TotalSeconds, timeoutSec);

                    if (_connectionManager.TryUnregister(session.SessionId, out _))
                    {
                        purgedCount++;
                        CloseSessionSocket(session);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SessionWatchdog] Error while processing session '{SessionId}' for timeout check", session.SessionId);
            }
        }

        return purgedCount;
    }

    /// <summary>
    /// Đóng an toàn kết nối socket của session đã bị timeout để giải phóng descriptor hệ điều hành.
    /// </summary>
    private void CloseSessionSocket(AgentSession session)
    {
        try
        {
            session.Client?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SessionWatchdog] Exception while closing TcpClient for session '{SessionId}'", session.SessionId);
        }

        try
        {
            session.SafeStream.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SessionWatchdog] Exception while disposing SafeStream for session '{SessionId}'", session.SessionId);
        }
    }
}
