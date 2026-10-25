using ServerManager.Shared.Contracts.Models;

namespace ServerManager.Shared.Contracts.Protocol;

/// <summary>
/// Hợp đồng kênh truyền thông điệp hai chiều an toàn đa luồng trên nền TCP Full-Duplex.
/// Tự động đồng bộ tuần tự hóa ghi (Write Serialization) qua SemaphoreSlim(1, 1) để ngăn ngừa trộn lẫn byte.
/// </summary>
public interface ISafeFrameChannel : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Cho biết trạng thái kênh truyền còn mở và sẵn sàng hay không.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Luồng dữ liệu mạng hoặc bộ nhớ cơ sở bên dưới kênh truyền (nếu có).
    /// </summary>
    Stream UnderlyingStream { get; }

    /// <summary>
    /// Gửi một NetOpsMessage ra kênh truyền an toàn đa luồng.
    /// Tự động tuần tự hóa ghi để bảo đảm tính toàn vẹn của frame.
    /// </summary>
    /// <param name="message">Thông điệp cần truyền đi</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    /// <returns>Một ValueTask đại diện cho tác vụ gửi bất đồng bộ</returns>
    ValueTask SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc một NetOpsMessage từ kênh truyền bất đồng bộ.
    /// Trả về null nếu đối phương đóng kết nối sạch sẽ (Graceful EOF).
    /// </summary>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    /// <returns>Đối tượng NetOpsMessage đọc được, hoặc null nếu kết nối đóng sạch</returns>
    ValueTask<NetOpsMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi trực tiếp một khung dữ liệu nhị phân thô ra kênh truyền an toàn đa luồng.
    /// </summary>
    /// <param name="payload">Vùng nhớ dữ liệu nhị phân cần truyền đi</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    /// <returns>Một ValueTask đại diện cho tác vụ gửi bất đồng bộ</returns>
    ValueTask SendRawFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}
