namespace Agent.Worker.Services;

/// <summary>
/// Thuật toán Exponential Backoff kèm Jitter cho việc tự động kết nối lại.
/// - Mỗi lần thất bại liên tiếp, độ trễ gấp đôi: Base, 2*Base, 4*Base... nhưng không vượt MaxDelay.
/// - Jitter: độ trễ thật được trừ bớt một phần ngẫu nhiên (tối đa JitterRatio) để các Agent
///   cùng mất kết nối (ví dụ Master khởi động lại) không đồng loạt thử lại đúng một thời điểm
///   (Thundering Herd).
/// - Reset() đưa độ trễ về mức cơ sở khi kết nối thành công.
/// Lớp này không dùng Task.Delay nên test được ngay mà không cần chờ thời gian thật.
/// Không an toàn đa luồng: chỉ dùng trong một vòng lặp kết nối duy nhất.
/// </summary>
public sealed class ReconnectBackoff
{
    public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Mặc định 0.5: độ trễ thật nằm trong khoảng 50% đến 100% mức backoff (còn gọi là "equal jitter").
    /// Đặt 1.0 để dùng "full jitter" (0% đến 100%), đặt 0 để tắt jitter.
    /// </summary>
    public const double DefaultJitterRatio = 0.5;

    private readonly Func<double> _nextRandom;
    private int _attempt;

    /// <summary>Độ trễ cơ sở (lần thử lại đầu tiên).</summary>
    public TimeSpan BaseDelay { get; }

    /// <summary>Độ trễ tối đa (trần).</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>Tỉ lệ jitter, từ 0 đến 1.</summary>
    public double JitterRatio { get; }

    /// <summary>Số lần đã lấy độ trễ kể từ lần Reset gần nhất (0 nghĩa là đang ở mức cơ sở).</summary>
    public int Attempt => _attempt;

    /// <param name="baseDelay">Độ trễ cơ sở, mặc định 2 giây.</param>
    /// <param name="maxDelay">Độ trễ tối đa, mặc định 30 giây.</param>
    /// <param name="jitterRatio">Tỉ lệ jitter từ 0 đến 1, mặc định 0.5.</param>
    /// <param name="nextRandom">Hàm trả số ngẫu nhiên trong [0, 1). Mặc định Random.Shared; test truyền hàm giả để có kết quả cố định.</param>
    public ReconnectBackoff(
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        double jitterRatio = DefaultJitterRatio,
        Func<double>? nextRandom = null)
    {
        BaseDelay = baseDelay ?? DefaultBaseDelay;
        MaxDelay = maxDelay ?? DefaultMaxDelay;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(BaseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxDelay, BaseDelay);
        ArgumentOutOfRangeException.ThrowIfNegative(jitterRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(jitterRatio, 1.0);

        JitterRatio = jitterRatio;
        _nextRandom = nextRandom ?? (() => Random.Shared.NextDouble());
    }

    /// <summary>
    /// Trả về độ trễ cho lần thử lại kế tiếp rồi tăng bộ đếm lần thất bại.
    /// </summary>
    public TimeSpan NextDelay()
    {
        // Backoff mũ: Base * 2^attempt, kẹp ở MaxDelay
        double exponential = BaseDelay.TotalSeconds * Math.Pow(2, _attempt);
        double capped = Math.Min(exponential, MaxDelay.TotalSeconds);

        // Jitter: lấy ngẫu nhiên trong khoảng [capped * (1 - ratio), capped]
        double random = Math.Clamp(_nextRandom(), 0.0, 1.0);
        double seconds = capped * (1.0 - JitterRatio * random);

        if (_attempt < 62)
        {
            _attempt++;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Đưa độ trễ về mức cơ sở (gọi khi kết nối thành công).
    /// </summary>
    public void Reset() => _attempt = 0;
}
