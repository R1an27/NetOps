using Master.Api.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetOps.Tests.TestHelpers;

/// <summary>
/// Helper quản lý vòng đời của một TcpManagerServer thực tế chạy trên localhost với cổng ngẫu nhiên.
/// Đảm bảo môi trường kiểm thử tích hợp TCP độc lập, chống xung đột cổng giữa các bài test.
/// </summary>
public sealed class TcpTestServerHelper : IAsyncDisposable, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _executingTask;
    private bool _isDisposed;

    public TcpManagerServer Server { get; }
    public string Host => "127.0.0.1";
    public int Port => Server.Port;

    private TcpTestServerHelper(TcpManagerServer server)
    {
        Server = server;
    }

    /// <summary>
    /// Khởi động một instance TcpManagerServer trên cổng ngẫu nhiên (Port = 0) và chờ cho tới khi server sẵn sàng lắng nghe.
    /// </summary>
    public static async Task<TcpTestServerHelper> StartNewAsync(ILogger<TcpManagerServer>? logger = null, CancellationToken cancellationToken = default)
    {
        var serverLogger = logger ?? NullLogger<TcpManagerServer>.Instance;
        var server = new TcpManagerServer(serverLogger, port: 0);

        var helper = new TcpTestServerHelper(server);
        await helper.StartInternalAsync(cancellationToken).ConfigureAwait(false);
        return helper;
    }

    private async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        _executingTask = Server.StartAsync(_cts.Token);

        // Chờ server bắt đầu lắng nghe với timeout 5 giây
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        while (!Server.IsListening && !_cts.IsCancellationRequested)
        {
            await Task.Delay(10, timeoutCts.Token).ConfigureAwait(false);
        }

        if (!Server.IsListening)
        {
            throw new InvalidOperationException("TcpManagerServer không thể khởi động lắng nghe trong thời gian quy định.");
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _cts.Cancel();
            try
            {
                Server.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // Bỏ qua lỗi dừng server
            }
            Server.Dispose();
            _cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _cts.Cancel();
            try
            {
                await Server.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Bỏ qua lỗi dừng server
            }
            Server.Dispose();
            _cts.Dispose();
        }
    }
}
