using System.Net;
using System.Net.Sockets;
using Agent.Worker.Services;
using Xunit;

namespace NetOps.Tests;

public class PongTimeoutTests
{
    /// <summary>
    /// Master giả chấp nhận kết nối TCP nhưng không bao giờ trả PONG (mô phỏng Master chết ngầm / half-open).
    /// Agent phải tự đóng kết nối sau MissedPongLimit chu kỳ PING rồi kết nối lại theo backoff.
    /// </summary>
    [Fact]
    public async Task Master_im_lang_khong_tra_PONG_thi_Agent_tu_dong_noi_lai()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var stop = new CancellationTokenSource();
        var secondConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new List<TcpClient>();
        int accepted = 0;

        var acceptLoop = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync(stop.Token);
                    lock (held) held.Add(client);
                    if (Interlocked.Increment(ref accepted) >= 2) secondConnection.TrySetResult();
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        });

        // PING mỗi 500ms, quá 2 chu kỳ (khoảng 1s) không có PONG thì Agent phải tự ngắt.
        // Sau đó chờ backoff 3s rồi nối lại, nên kết nối thứ hai đến sau khoảng 5s.
        var agent = new AgentNetworkClient { MissedPongLimit = 2 };
        var run = agent.RunAsync("127.0.0.1", port, "agent-test", TimeSpan.FromMilliseconds(500), stop.Token);

        var finished = await Task.WhenAny(secondConnection.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        // Dọn dẹp
        stop.Cancel();
        listener.Stop();
        await Task.WhenAny(Task.WhenAll(run, acceptLoop), Task.Delay(TimeSpan.FromSeconds(5)));
        lock (held)
        {
            foreach (var c in held) c.Dispose();
        }

        Assert.True(finished == secondConnection.Task,
            "Agent khong tu dong ket noi lai sau khi Master ngung tra PONG.");
    }
}
