using ServerManager.Shared.Contracts.Models;

namespace Agent.Worker.Services;

/// <summary>
/// Hợp đồng quản trị mạng phía Agent theo chuẩn NetOps Protocol v0.
/// Quản lý chu trình kết nối TCP tới Master, vòng lặp truyền thông PING/PONG,
/// gửi thông điệp chủ động có tuần tự hóa ghi (Write Serialization),
/// và tích hợp bộ phân loại thông điệp IAgentMessageControl.
/// </summary>
public interface IAgentNetworkClient
{
    /// <summary>
    /// Cho biết Agent có đang duy trì kết nối mạng ổn định tới Master hay không.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Địa chỉ endpoint của Master mà Agent đang kết nối tới (dạng Host:Port), hoặc null nếu chưa kết nối.
    /// </summary>
    string? ConnectedEndpoint { get; }

    /// <summary>
    /// Bộ điều phối và phân loại thông điệp chuẩn NetOps Protocol v0 gắn liền với client mạng này.
    /// </summary>
    IAgentMessageControl MessageControl { get; }

    /// <summary>
    /// Sự kiện kích hoạt khi Agent thiết lập kết nối TCP socket thành công tới Master.
    /// </summary>
    event Action? Connected;

    /// <summary>
    /// Sự kiện kích hoạt khi kết nối giữa Agent và Master bị đứt gãy hoặc đóng socket.
    /// </summary>
    event Action? Disconnected;

    /// <summary>
    /// Bắt đầu chu trình kết nối TCP, vòng lặp nhận thông điệp và gửi PING định kỳ tới Master.
    /// Tự động thực thi cơ chế Exponential Backoff khi gặp sự cố mạng cho tới khi cancellationToken được kích hoạt.
    /// </summary>
    /// <param name="host">Địa chỉ IP hoặc tên miền của Master Server</param>
    /// <param name="port">Cổng dịch vụ TCP của Master Server</param>
    /// <param name="agentId">Mã định danh duy nhất của Agent</param>
    /// <param name="pingInterval">Chu kỳ phát gói tin PING định kỳ</param>
    /// <param name="cancellationToken">Mã hủy thao tác để Graceful Shutdown</param>
    /// <returns>Tác vụ đại diện cho toàn bộ chu trình kết nối mạng của Agent</returns>
    Task RunAsync(string host, int port, string agentId, TimeSpan pingInterval, CancellationToken cancellationToken);

    /// <summary>
    /// Chủ động gửi một NetOpsMessage ra kênh truyền mạng an toàn tới Master.
    /// Tự động tuần tự hóa ghi (Write Serialization) qua khóa đồng bộ của SafeFrameStream
    /// để chống hiện tượng trộn lẫn byte khi gửi đồng thời với luồng PING định kỳ.
    /// </summary>
    /// <param name="message">Thông điệp cần gửi đi</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    /// <returns>Tác vụ Task đại diện cho quá trình gửi thông điệp</returns>
    Task SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default);
}
