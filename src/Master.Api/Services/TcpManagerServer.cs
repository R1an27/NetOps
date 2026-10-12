using System.Net;
using System.Net.Sockets;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Protocol;

namespace Master.Api.Services;

/// <summary>
/// Dịch vụ TCP Server chạy nền trong Master lắng nghe kết nối từ các Agent
/// </summary>
public class TcpManagerServer : BackgroundService
{
    private readonly ILogger<TcpManagerServer> _logger;
    private readonly int _port;
    private TcpListener? _listener;

    public int Port => _port;
    public bool IsListening { get; private set; }

    public TcpManagerServer(ILogger<TcpManagerServer> logger, IConfiguration configuration)
    {
        _logger = logger;
        _port = configuration.GetValue<int>("TcpServer:Port", 5050);
    }

    public TcpManagerServer(ILogger<TcpManagerServer> logger, int port)
    {
        _logger = logger;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            IsListening = true;
            _logger.LogInformation("TCP Manager Server started listening on port {Port}", _port);

            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
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

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        string clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        _logger.LogInformation("[Master] Client connected from {EndPoint}", clientEndpoint);

        using (client)
        await using (NetworkStream stream = client.GetStream())
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    NetOpsMessage? message = await FrameCodec.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);

                    if (message == null)
                    {
                        _logger.LogInformation("[Master] Client {EndPoint} disconnected gracefully (EOF)", clientEndpoint);
                        break;
                    }

                    _logger.LogInformation("[Master] Received message '{Type}' (Id: {MessageId}) from {EndPoint}",
                        message.MessageType, message.MessageId, clientEndpoint);

                    switch (message.MessageType)
                    {
                        case MessageType.Ping:
                            // Tự động phản hồi Pong tương ứng
                            var pong = NetOpsMessage.CreatePong(message.MessageId, "PONG_ACK");
                            await FrameCodec.WriteFrameAsync(stream, pong, cancellationToken).ConfigureAwait(false);
                            _logger.LogInformation("[Master] Sent PONG (InReplyTo: {InReplyTo}) to {EndPoint}",
                                pong.InReplyTo, clientEndpoint);
                            break;

                        default:
                            _logger.LogWarning("[Master] Unhandled message type '{Type}' from {EndPoint}",
                                message.MessageType, clientEndpoint);
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[Master] Session with {EndPoint} cancelled due to server shutdown", clientEndpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Master] Connection ended with {EndPoint}: {Message}", clientEndpoint, ex.Message);
            }
        }

        _logger.LogInformation("[Master] Cleaned up session with {EndPoint}", clientEndpoint);
    }

    public override void Dispose()
    {
        _listener?.Stop();
        base.Dispose();
    }
}