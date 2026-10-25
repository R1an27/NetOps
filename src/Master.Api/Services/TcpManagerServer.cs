using System.Net;
using System.Net.Sockets;
using Master.Api.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;

namespace Master.Api.Services;

/// <summary>
/// Dịch vụ TCP Server chạy nền trong Master lắng nghe kết nối từ các Agent
/// </summary>

// Kế thừa từ BackgroundService trong .NET, cho phép server khởi động cùng 
// ứng dụng Web/Worker và dừng an toàn khi ứng dụng nhận tín hiệu tắt (stoppingToken).
public class TcpManagerServer : BackgroundService
{
    private readonly ILogger<TcpManagerServer> _logger;
    private readonly int _port;
    private readonly IConnectionManager _connectionManager;
    private readonly IMasterMessageControl _messageControl;
    private TcpListener? _listener;

    public int Port => _listener?.LocalEndpoint is IPEndPoint ep ? ep.Port : _port;
    public bool IsListening { get; private set; }

    /// <summary>
    /// Bộ quản lý danh sách phiên làm việc của các Agent đang kết nối.
    /// </summary>
    public IConnectionManager ConnectionManager => _connectionManager;

    /// <summary>
    /// Bộ điều phối và phân loại thông điệp chuẩn NetOps Protocol v0.
    /// </summary>
    public IMasterMessageControl MessageControl => _messageControl;

    public TcpManagerServer(
        ILogger<TcpManagerServer> logger,
        IOptions<MasterServerOptions> options,
        IConfiguration? configuration = null,
        IConnectionManager? connectionManager = null,
        IMasterMessageControl? messageControl = null)
    {
        _logger = logger;
        _port = options?.Value.Port ?? configuration?.GetValue<int>("MasterServer:Port", configuration?.GetValue<int>("TcpServer:Port", 5050) ?? 5050) ?? 5050;
        _connectionManager = connectionManager ?? new ConnectionManager();
        _messageControl = messageControl ?? new MasterMessageControl();
    }

    public TcpManagerServer(
        ILogger<TcpManagerServer> logger,
        IConfiguration configuration,
        IConnectionManager? connectionManager = null,
        IMasterMessageControl? messageControl = null)
        : this(logger, Microsoft.Extensions.Options.Options.Create(new MasterServerOptions()), configuration, connectionManager, messageControl)
    {
    }

    public TcpManagerServer(
        ILogger<TcpManagerServer> logger,
        int port,
        IConnectionManager? connectionManager = null,
        IMasterMessageControl? messageControl = null)
        : this(logger, Microsoft.Extensions.Options.Options.Create(new MasterServerOptions { Port = port }), null, connectionManager, messageControl)
    {
    }

    /// <summary>
    /// Vòng lặp lắng nghe kết nối TCP từ các Agent
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // IPAddress.Any: Lắng nghe trên tất cả các card mạng của máy chủ (IPv4).
            // Cổng mặc định lấy từ file appsettings.json (TcpServer:Port), nếu không có sẽ tự rơi về cổng 5050.
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            IsListening = true;
            _logger.LogInformation("TCP Manager Server started listening on port {Port}", _port);

            // Khi một Agent kết nối tới, AcceptTcpClientAsync nhả ra một đối tượng TcpClient.
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                // Khối catch bên trong vòng lặp đảm bảo nếu một kết nối bắt tay thất bại, 
                // server vẫn tiếp tục chạy để đón các client khác.
                try
                {
                    client = await _listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error accepting TCP client connection");
                    continue;
                }

                // Xử lý song song từng client trên Task riêng biệt (không chặn vòng lặp accept)
                // Sử dụng toán tử loại bỏ (discard _) để khởi chạy HandleClientAsync 
                // thành một background Task độc lập. Vòng lặp while lập tức 
                // quay lại chờ Agent tiếp theo kết nối mà không phải chờ Agent trước gửi xong dữ liệu.
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in TCP Manager Server listener");
        }
        finally
        {
            IsListening = false;
            _listener?.Stop();
            _logger.LogInformation("TCP Manager Server stopped listening on port {Port}", _port);
        }
    }

    /// <summary>
    /// Hàm này sẽ chạy khi có client kết nối tới
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        string clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        _logger.LogInformation("[Master] Client connected from {EndPoint}", clientEndpoint);

        string sessionId = $"session-{Guid.NewGuid():N}";
        AgentSession? session = null;
        DisconnectReason disconnectReason = DisconnectReason.GracefulDisconnect;

        // Khi client thoát vòng lặp (do ngắt kết nối, lỗi mạng hoặc server tắt)
        // khối using đảm bảo cổng Socket và luồng NetworkStream được đóng triệt để
        // giải phóng descriptor của OS và tránh leak memory.
        using (client)
        await using (var safeStream = new SafeFrameStream(client.GetStream()))
        {
            session = new AgentSession(sessionId, safeStream, clientEndpoint, DateTimeOffset.UtcNow, client);
            _connectionManager.TryRegister(session);

            try
            {
                /// Vòng lặp đọc & phản hồi tin nhắn
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Server gọi safeStream.ReadMessageAsync(cancellationToken): 
                    // Tận dụng cơ chế Length-Prefix đã thiết kế để đọc 
                    // nguyên vẹn từng NetOpsMessage, chống dính gói và phân mảnh.
                    NetOpsMessage? message = await safeStream.ReadMessageAsync(cancellationToken).ConfigureAwait(false);

                    // Xử lý Graceful EOF (message == null): 
                    // Nhận diện Agent chủ động ngắt kết nối bình thường và
                    // thoát vòng lặp êm thấu mà không quăng Exception.
                    if (message == null)
                    {
                        _logger.LogInformation("[Master] Client {EndPoint} disconnected gracefully (EOF)", clientEndpoint);
                        disconnectReason = DisconnectReason.GracefulDisconnect;
                        break;
                    }

                    /// Cập nhật thời gian cuối cùng thấy Agent
                    session.LastSeenAt = DateTimeOffset.UtcNow;

                    _logger.LogInformation("[Master] Received message '{Type}' (Id: {MessageId}) from {EndPoint}",
                        message.MessageType, message.MessageId, clientEndpoint);

                    // Phân loại và điều phối xử lý nghiệp vụ thông qua IMasterMessageControl
                    var response = await _messageControl.InvokeAsync(session, message, cancellationToken).ConfigureAwait(false);
                    /// Nếu có response từ controller thì gửi cho agent
                    if (response != null)
                    {
                        await safeStream.WriteMessageAsync(response, cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation("[Master] Sent response '{Type}' (InReplyTo: {InReplyTo}) to {EndPoint}",
                            response.MessageType, response.InReplyTo, clientEndpoint);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[Master] Session with {EndPoint} cancelled due to server shutdown", clientEndpoint);
                disconnectReason = DisconnectReason.ServerShutdown;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Master] Connection ended with {EndPoint}: {Message}", clientEndpoint, ex.Message);
                disconnectReason = DisconnectReason.NetworkFault;
            }
            finally
            {
                /// Khi agent ngắt kết nối thì xóa session
                if (session != null)
                {
                    _connectionManager.TryUnregister(session.SessionId, out _, disconnectReason);
                }
            }
        }

        _logger.LogInformation("[Master] Cleaned up session with {EndPoint}", clientEndpoint);
    }

    /// <summary>
    /// Dọn dẹp tài nguyên khi Master Server dừng
    /// </summary>
    public override void Dispose()
    {
        _listener?.Stop();
        base.Dispose();
    }
}

// Cơ chế tắt dịch vụ an toàn (Graceful Shutdown)
// Khi nhấn Ctrl + C hoặc ứng dụng chủ dừng: stoppingToken được kích hoạt (Cancel).
// Các tác vụ đang await (AcceptTcpClientAsync, ReadFrameAsync) sẽ ném ra OperationCanceledException.
// Khối finally đảm bảo:
// IsListening = false.
// _listener?.Stop(): Ngừng nhận kết nối mới.
// Toàn bộ socket của các client hiện tại sẽ lần lượt được giải phóng trong khối finally của HandleClientAsync.