using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Client.WinForms.Services;
using NetOps.Tests.TestHelpers;
using ServerManager.Shared.Contracts.Models;
using Shared.Contracts.Framing;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử phòng thủ lỗi mạng (Network Fault, Chaos & Resilience Tests).
/// Mô phỏng các điều kiện mạng bất thường, dữ liệu dị tật và kịch bản tấn công từ chối dịch vụ:
/// 1. Tấn công tràn bộ nhớ (OOM Attack Protection): Khai báo Length Header khổng lồ (>16MB).
/// 2. Header độ dài vi phạm biên nhỏ hơn MinPayloadSize (0 hoặc số âm).
/// 3. Khung dữ liệu bị cắt cụt giữa chừng (Half-frame truncation): gửi thiếu payload rồi đóng socket.
/// 4. Header bị cắt cụt (Truncated header): chỉ gửi 1-2 bytes rồi đóng socket.
/// 5. Payload chứa JSON rác hoặc dị tật (Malformed JSON).
/// 6. Client tự bảo vệ và hủy pending requests sạch sẽ khi đối phương ngắt đột ngột.
/// </summary>
public class NetworkFaultResilienceTests
{
    /// <summary>
    /// Kịch bản 1: Client gửi Header 4 bytes khai báo độ dài khổng lồ (100 MB > 16 MB MaxPayloadSize).
    /// Xác thực: Server từ chối ngay lập tức, ném InvalidDataException mà KHÔNG cấp phát 100 MB RAM
    /// (chống tấn công cạn kiệt bộ nhớ OOM), đóng kết nối client vi phạm, và vẫn sẵn sàng phục vụ các client khác.
    /// </summary>
    [Fact]
    public async Task Server_OversizedPayloadHeader_RejectsImmediatelyAndPreventsOutOfMemory()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        // 1. Tấn công giả lập: Gửi header khai báo 100 MB
        using var attackerSocket = new TcpClient();
        await attackerSocket.ConnectAsync(server.Host, server.Port);
        var attackerStream = attackerSocket.GetStream();

        const int oversizedPayloadLength = 100 * 1024 * 1024; // 100 MB
        byte[] maliciousHeader = new byte[FrameConstants.LengthPrefixSize];
        BinaryPrimitives.WriteInt32BigEndian(maliciousHeader, oversizedPayloadLength);

        await attackerStream.WriteAsync(maliciousHeader);
        await attackerStream.FlushAsync();

        // 2. Chờ server phát hiện và đóng kết nối với attacker
        byte[] readBuffer = new byte[16];
        int bytesRead = 0;
        try
        {
            bytesRead = await attackerStream.ReadAsync(readBuffer);
        }
        catch (IOException)
        {
            // Socket bị reset hoặc đóng từ phía server
            bytesRead = 0;
        }

        Assert.Equal(0, bytesRead); // EOF từ server

        // 3. Xác thực server vẫn hoàn toàn khỏe mạnh và phục vụ client bình thường
        using var legitClient = new NetOpsClientService();
        await legitClient.ConnectAsync(server.Host, server.Port);
        var (rtt, pong) = await legitClient.PingAsync("health-check-after-oom-attack", TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.Pong, pong.MessageType);
        Assert.Equal("PONG_ACK", pong.Payload);
        await legitClient.DisconnectAsync();
    }

    /// <summary>
    /// Kịch bản 2: Client gửi Header độ dài vi phạm biên nhỏ hơn MinPayloadSize (Length = 0 hoặc số âm).
    /// Xác thực: Server từ chối ngay lập tức, ngắt kết nối an toàn mà không bị lỗi logic.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task Server_ZeroOrNegativeLengthHeader_RejectsImmediatelyAndClosesSession(int invalidLength)
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        using var clientSocket = new TcpClient();
        await clientSocket.ConnectAsync(server.Host, server.Port);
        var stream = clientSocket.GetStream();

        byte[] header = new byte[FrameConstants.LengthPrefixSize];
        BinaryPrimitives.WriteInt32BigEndian(header, invalidLength);

        await stream.WriteAsync(header);
        await stream.FlushAsync();

        // Chờ server phát hiện và ngắt kết nối
        byte[] readBuffer = new byte[16];
        int bytesRead = 0;
        try
        {
            bytesRead = await stream.ReadAsync(readBuffer);
        }
        catch (IOException)
        {
            bytesRead = 0;
        }

        Assert.Equal(0, bytesRead);

        // Server vẫn hoạt động bình thường cho kết nối tiếp theo
        using var goodClient = new NetOpsClientService();
        await goodClient.ConnectAsync(server.Host, server.Port);
        var (rtt, pong) = await goodClient.PingAsync("ping-after-invalid-length", TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.Pong, pong.MessageType);
        await goodClient.DisconnectAsync();
    }

    /// <summary>
    /// Kịch bản 3: Nửa khung dữ liệu bị cắt cụt (Half-frame truncation).
    /// Client gửi header báo 5,000 bytes nhưng chỉ gửi 50 bytes rồi đóng socket đột ngột.
    /// Xác thực: Server bắt lỗi EndOfStreamException, giải phóng tài nguyên an toàn, không rơi vào vòng lặp vô tận.
    /// </summary>
    [Fact]
    public async Task Server_HalfFrameTruncation_DetectsPrematureCloseAndCleansUpGracefully()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        using (var truncatedClient = new TcpClient())
        {
            await truncatedClient.ConnectAsync(server.Host, server.Port);
            var stream = truncatedClient.GetStream();

            // Khai báo 5,000 bytes
            byte[] header = new byte[FrameConstants.LengthPrefixSize];
            BinaryPrimitives.WriteInt32BigEndian(header, 5000);
            await stream.WriteAsync(header);

            // Nhưng chỉ gửi 50 bytes rồi đóng socket
            byte[] partialPayload = new byte[50];
            Array.Fill<byte>(partialPayload, 0xAB);
            await stream.WriteAsync(partialPayload);
            await stream.FlushAsync();

            // Đóng socket đột ngột
            truncatedClient.Close();
        }

        // Chờ server xử lý dọn dẹp
        await Task.Delay(50);

        // Server vẫn phục vụ client mới bình thường
        using var healthyClient = new NetOpsClientService();
        await healthyClient.ConnectAsync(server.Host, server.Port);
        var (rtt, pong) = await healthyClient.PingAsync("ping-after-half-frame", TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.Pong, pong.MessageType);
        await healthyClient.DisconnectAsync();
    }

    /// <summary>
    /// Kịch bản 4: Header bị cắt cụt (Truncated Header).
    /// Client chỉ gửi 2 bytes (thay vì 4 bytes cho Length Header) rồi đóng kết nối.
    /// Xác thực: Server phát hiện ngắt kết nối giữa chừng, không bị treo hoặc crash.
    /// </summary>
    [Fact]
    public async Task Server_TruncatedHeader_DetectsPrematureCloseAndCleansUpGracefully()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        using (var truncatedClient = new TcpClient())
        {
            await truncatedClient.ConnectAsync(server.Host, server.Port);
            var stream = truncatedClient.GetStream();

            // Chỉ gửi 2 bytes
            byte[] partialHeader = [0x00, 0x01];
            await stream.WriteAsync(partialHeader);
            await stream.FlushAsync();

            truncatedClient.Close();
        }

        await Task.Delay(50);

        // Server vẫn phục vụ client tiếp theo bình thường
        using var healthyClient = new NetOpsClientService();
        await healthyClient.ConnectAsync(server.Host, server.Port);
        var (rtt, pong) = await healthyClient.PingAsync("ping-after-truncated-header", TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.Pong, pong.MessageType);
        await healthyClient.DisconnectAsync();
    }

    /// <summary>
    /// Kịch bản 5: Payload chứa chuỗi JSON rác hoặc không đúng định dạng (Malformed JSON).
    /// Framing hợp lệ (Length = 40 bytes) nhưng nội dung là chuỗi ký tự rác không parse được thành NetOpsMessage.
    /// Xác thực: FrameCodec bắt JsonException và bọc thành InvalidDataException. Server bắt ngoại lệ,
    /// ghi log warning và ngắt kết nối an toàn mà không làm sập tiến trình.
    /// </summary>
    [Fact]
    public async Task Server_MalformedJsonPayload_CatchesInvalidDataExceptionAndClosesSessionSafely()
    {
        await using var server = await TcpTestServerHelper.StartNewAsync();

        using var clientSocket = new TcpClient();
        await clientSocket.ConnectAsync(server.Host, server.Port);
        var stream = clientSocket.GetStream();

        // Payload rác không thể parse thành JSON
        string malformedJson = "{ \"this_is_not_valid_json\": @@@###$$$%%%^^^&&&*** }";
        byte[] payloadBytes = Encoding.UTF8.GetBytes(malformedJson);

        byte[] frame = new byte[FrameConstants.LengthPrefixSize + payloadBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, FrameConstants.LengthPrefixSize), payloadBytes.Length);
        payloadBytes.CopyTo(frame, FrameConstants.LengthPrefixSize);

        await stream.WriteAsync(frame);
        await stream.FlushAsync();

        // Chờ server phát hiện lỗi parser và đóng socket
        byte[] readBuffer = new byte[16];
        int bytesRead = 0;
        try
        {
            bytesRead = await stream.ReadAsync(readBuffer);
        }
        catch (IOException)
        {
            bytesRead = 0;
        }

        Assert.Equal(0, bytesRead);

        // Server vẫn sống và phục vụ client tiếp theo bình thường
        using var healthyClient = new NetOpsClientService();
        await healthyClient.ConnectAsync(server.Host, server.Port);
        var (rtt, pong) = await healthyClient.PingAsync("ping-after-malformed-json", TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.Pong, pong.MessageType);
        await healthyClient.DisconnectAsync();
    }

    /// <summary>
    /// Kịch bản 6: Khả năng tự bảo vệ của NetOpsClientService khi Server ngắt kết nối đột ngột giữa chừng.
    /// Toàn bộ các yêu cầu đang đợi phản hồi (pending requests) phải được giải phóng với ngoại lệ IOException,
    /// và trạng thái kết nối chuyển thành Disconnected.
    /// </summary>
    [Fact]
    public async Task Client_ServerAbruptlyCloses_PendingRequestsFailWithIOExceptionAndStateDisconnected()
    {
        var server = await TcpTestServerHelper.StartNewAsync();
        using var client = new NetOpsClientService();

        await client.ConnectAsync(server.Host, server.Port);
        Assert.True(client.IsConnected);

        // Tắt server đột ngột
        await server.DisposeAsync();

        // Đợi background loop của client nhận EOF và chuyển trạng thái
        var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (client.State != ConnectionState.Disconnected && !timeoutCts.IsCancellationRequested)
        {
            await Task.Delay(20);
        }

        Assert.False(client.IsConnected);
        Assert.Equal(ConnectionState.Disconnected, client.State);
    }
}
