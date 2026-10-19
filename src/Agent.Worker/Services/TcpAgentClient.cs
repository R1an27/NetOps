using Microsoft.Extensions.Logging;

namespace Agent.Worker.Services;

/// <summary>
/// Client quản lý kết nối TCP từ Agent tới Master và thực hiện chu trình PING/PONG
/// Tuân thủ NetOps Protocol v0 (Length-Prefixed Framing, Write Serialization, Exponential Backoff).
/// Tích hợp IAgentMessageControl để phân luồng và điều phối thông điệp nghiệp vụ hai chiều.
/// Kế thừa AgentNetworkClient và hiện thực IAgentNetworkClient, bảo đảm tương thích ngược 100%.
/// </summary>
public class TcpAgentClient : AgentNetworkClient
{
    /// <summary>
    /// Khởi tạo TcpAgentClient với ILogger và IAgentMessageControl tùy chọn.
    /// </summary>
    /// <param name="logger">Bộ ghi nhật ký ILogger</param>
    /// <param name="messageControl">Bộ điều phối thông điệp (nếu null, khởi tạo instance AgentMessageControl mặc định)</param>
    public TcpAgentClient(ILogger<TcpAgentClient> logger, IAgentMessageControl? messageControl = null)
        : base(logger, messageControl)
    {
    }
}