using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Agent.Worker.Services;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Kiểm thử cơ chế tự động kết nối lại của Agent:
/// - Thuật toán Exponential Backoff kèm Jitter (ReconnectBackoff) - test thuần, chạy tức thì.
/// - Chuyển trạng thái (AgentConnectionState / StateChanged) khi Master đóng kết nối hoặc khởi động lại.
/// </summary>
public class AgentReconnectTests
{
    // ======================================================================
    // 1. ReconnectBackoff (thuật toán thuần)
    // ======================================================================

    private static ReconnectBackoff NoJitter() => new(jitterRatio: 0);

    [Fact]
    public void Backoff_khong_jitter_tang_gap_doi_tu_2s_va_kep_o_30s()
    {
        var backoff = NoJitter();

        var seconds = Enumerable.Range(0, 7)
            .Select(_ => backoff.NextDelay().TotalSeconds)
            .ToArray();

        Assert.Equal(new double[] { 2, 4, 8, 16, 30, 30, 30 }, seconds);
    }

    [Fact]
    public void Reset_dua_do_tre_ve_muc_co_so_2s()
    {
        var backoff = NoJitter();
        backoff.NextDelay();
        backoff.NextDelay();
        backoff.NextDelay();
        Assert.Equal(3, backoff.Attempt);

        backoff.Reset();

        Assert.Equal(0, backoff.Attempt);
        Assert.Equal(TimeSpan.FromSeconds(2), backoff.NextDelay());
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.999)]
    public void Jitter_luon_nam_trong_khoang_50_den_100_phan_tram_muc_backoff(double random)
    {
        var backoff = new ReconnectBackoff(jitterRatio: 0.5, nextRandom: () => random);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            double nominal = Math.Min(2 * Math.Pow(2, attempt), 30);
            double actual = backoff.NextDelay().TotalSeconds;

            Assert.InRange(actual, nominal * 0.5 - 0.001, nominal + 0.001);
        }
    }

    [Fact]
    public void Full_jitter_cho_phep_do_tre_tu_0_den_muc_backoff()
    {
        var low = new ReconnectBackoff(jitterRatio: 1.0, nextRandom: () => 1.0);
        var high = new ReconnectBackoff(jitterRatio: 1.0, nextRandom: () => 0.0);

        Assert.Equal(0.0, low.NextDelay().TotalSeconds, 3);
        Assert.Equal(2.0, high.NextDelay().TotalSeconds, 3);
    }

    [Fact]
    public void Jitter_lam_cac_Agent_lech_nhau_de_tranh_Thundering_Herd()
    {
        // 200 Agent cùng mất kết nối một lúc (ví dụ Master khởi động lại) và cùng ở lần thử thứ 3.
        TimeSpan ThirdDelay(int seed, double jitterRatio)
        {
            var rng = new Random(seed);
            var backoff = new ReconnectBackoff(jitterRatio: jitterRatio, nextRandom: rng.NextDouble);
            backoff.NextDelay();
            backoff.NextDelay();
            return backoff.NextDelay();
        }

        var withJitter = Enumerable.Range(0, 200).Select(seed => ThirdDelay(seed, 0.5)).ToList();
        var withoutJitter = Enumerable.Range(0, 200).Select(seed => ThirdDelay(seed, 0.0)).ToList();

        // Không jitter: cả 200 Agent thử lại đúng cùng một thời điểm (đàn xô vào Master)
        Assert.Single(withoutJitter.Distinct());

        // Có jitter: thời điểm thử lại rải ra nhiều giá trị khác nhau
        Assert.True(withJitter.Distinct().Count() > 100,
            "Jitter phai lam cac Agent thu lai o nhieu thoi diem khac nhau.");
        Assert.True((withJitter.Max() - withJitter.Min()).TotalSeconds > 1.0,
            "Do lech giua Agent som nhat va muon nhat phai du lon de giam tai len Master.");
    }

    [Fact]
    public void Tham_so_khong_hop_le_thi_nem_loi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(baseDelay: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ReconnectBackoff(baseDelay: TimeSpan.FromSeconds(10), maxDelay: TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(jitterRatio: 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(jitterRatio: -0.1));
    }

    // ======================================================================
    // 2. Chuyển trạng thái của AgentNetworkClient (dùng Master giả qua TCP thật)
    // ======================================================================

    private static AgentNetworkClient CreateAgent() => new()
    {
        // Tắt cơ chế phát hiện mất PONG để test chỉ tập trung vào việc kết nối lại
        MissedPongLimit = 0,

        // Backoff ngắn (50ms đến 200ms, không jitter) để test chạy nhanh
        Backoff = new ReconnectBackoff(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), 0)
    };

    private static ConcurrentQueue<(AgentConnectionState From, AgentConnectionState To)> Record(AgentNetworkClient agent)
    {
        var transitions = new ConcurrentQueue<(AgentConnectionState From, AgentConnectionState To)>();
        agent.StateChanged += (prev, next) => transitions.Enqueue((prev, next));
        return transitions;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    private static async Task StopAsync(CancellationTokenSource cts, Task run)
    {
        cts.Cancel();
        await Task.WhenAny(run, Task.Delay(5000));
    }

    [Fact]
    public void Trang_thai_ban_dau_la_Disconnected()
    {
        Assert.Equal(AgentConnectionState.Disconnected, CreateAgent().CurrentState);
    }

    [Fact]
    public async Task Ket_noi_thanh_cong_thi_di_qua_Connecting_roi_Connected()
    {
        await using var master = new FakeMaster();
        var agent = CreateAgent();
        var transitions = Record(agent);
        using var cts = new CancellationTokenSource();

        var run = agent.RunAsync("127.0.0.1", master.Port, "agent-test", TimeSpan.FromSeconds(1), cts.Token);
        try
        {
            Assert.True(await WaitUntilAsync(() => agent.CurrentState == AgentConnectionState.Connected));

            var list = transitions.ToArray();
            Assert.Equal((AgentConnectionState.Disconnected, AgentConnectionState.Connecting), list[0]);
            Assert.Equal((AgentConnectionState.Connecting, AgentConnectionState.Connected), list[1]);
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task Master_dong_ket_noi_thi_Agent_chuyen_Reconnecting_roi_tu_noi_lai()
    {
        // Master giả chấp nhận kết nối rồi đóng ngay (mô phỏng Master đứt kết nối liên tục)
        await using var master = new FakeMaster(closeImmediately: true);
        var agent = CreateAgent();
        var transitions = Record(agent);
        using var cts = new CancellationTokenSource();

        var run = agent.RunAsync("127.0.0.1", master.Port, "agent-test", TimeSpan.FromSeconds(1), cts.Token);
        try
        {
            Assert.True(await WaitUntilAsync(() => master.AcceptedCount >= 2),
                "Agent phai tu ket noi lai it nhat mot lan sau khi Master dong ket noi.");

            var list = transitions.ToArray();
            Assert.Contains((AgentConnectionState.Connected, AgentConnectionState.Reconnecting), list);
            Assert.Contains((AgentConnectionState.Reconnecting, AgentConnectionState.Connecting), list);
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task Master_khoi_dong_lai_thi_Agent_tu_noi_lai_duoc()
    {
        var master1 = new FakeMaster();
        int port = master1.Port;
        FakeMaster? master2 = null;

        var agent = CreateAgent();
        var transitions = Record(agent);
        using var cts = new CancellationTokenSource();

        var run = agent.RunAsync("127.0.0.1", port, "agent-test", TimeSpan.FromSeconds(1), cts.Token);
        try
        {
            // Đảm bảo Agent đã báo Connected VÀ Master1 đã hoàn tất nhận socket vào danh sách kết nối (tránh race condition)
            Assert.True(await WaitUntilAsync(() =>
                agent.CurrentState == AgentConnectionState.Connected && master1.ClientCount >= 1));

            // Master "chết": đóng cổng lắng nghe và mọi kết nối đang mở
            await master1.DisposeAsync();
            Assert.True(await WaitUntilAsync(() =>
                transitions.Contains((AgentConnectionState.Connected, AgentConnectionState.Reconnecting))));

            // Master "sống lại" trên cùng cổng
            var second = master2 = new FakeMaster(port);
            Assert.True(await WaitUntilAsync(() =>
                agent.CurrentState == AgentConnectionState.Connected && second.AcceptedCount >= 1));
        }
        finally
        {
            await StopAsync(cts, run);
            await master1.DisposeAsync();
            if (master2 != null)
            {
                await master2.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Huy_RunAsync_thi_Agent_ve_Disconnected()
    {
        await using var master = new FakeMaster();
        var agent = CreateAgent();
        var transitions = Record(agent);
        using var cts = new CancellationTokenSource();

        var run = agent.RunAsync("127.0.0.1", master.Port, "agent-test", TimeSpan.FromSeconds(1), cts.Token);
        Assert.True(await WaitUntilAsync(() => agent.CurrentState == AgentConnectionState.Connected));

        cts.Cancel();
        await Task.WhenAny(run, Task.Delay(5000));

        Assert.True(run.IsCompleted, "RunAsync phai ket thuc sau khi huy.");
        Assert.Equal(AgentConnectionState.Disconnected, agent.CurrentState);
        Assert.Equal((AgentConnectionState.Connected, AgentConnectionState.Disconnected), transitions.Last());
    }

    // ======================================================================
    // Master giả: chấp nhận kết nối TCP, không trả lời gì (hoặc đóng ngay)
    // ======================================================================

    private sealed class FakeMaster : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<TcpClient> _clients = new();
        private readonly bool _closeImmediately;
        private readonly Task _loop;
        private int _accepted;
        private int _disposed;

        public int Port { get; }

        public int AcceptedCount => Volatile.Read(ref _accepted);

        public int ClientCount
        {
            get
            {
                lock (_clients)
                {
                    return _clients.Count;
                }
            }
        }

        public FakeMaster(int port = 0, bool closeImmediately = false)
        {
            _closeImmediately = closeImmediately;

            // Cổng vừa được giải phóng đôi khi chưa bind lại ngay được, nên thử lại vài lần
            TcpListener? listener = null;
            for (int attempt = 0; listener == null; attempt++)
            {
                var candidate = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    candidate.Start();
                    listener = candidate;
                }
                catch (SocketException) when (attempt < 40)
                {
                    candidate.Stop();
                    Thread.Sleep(50);
                }
            }

            _listener = listener!;
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);

                    if (_closeImmediately)
                    {
                        Interlocked.Increment(ref _accepted);
                        client.Dispose();
                    }
                    else
                    {
                        lock (_clients)
                        {
                            _clients.Add(client);
                        }
                        Interlocked.Increment(ref _accepted);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _cts.Cancel();
            _listener.Stop();
            await Task.WhenAny(_loop, Task.Delay(2000));

            lock (_clients)
            {
                foreach (var client in _clients)
                {
                    try
                    {
                        client.Client?.Shutdown(SocketShutdown.Both);
                    }
                    catch { }
                    client.Dispose();
                }

                _clients.Clear();
            }

            _cts.Dispose();
        }
    }
}
