using ServerManager.Shared.Contracts.Models;

namespace ServerManager.Shared.Contracts.Protocol;

/// <summary>
/// Hợp đồng mã hóa và giải mã đối tượng thông điệp NetOpsMessage qua tầng Framing.
/// Đóng vai trò là cầu nối giữa tầng Framing nhị phân ([4 Bytes Length][Payload Data])
/// và tầng Envelope cấu trúc JSON (NetOpsMessage).
/// </summary>
public interface IFrameCodec
{
    /// <summary>
    /// Mã hóa đối tượng NetOpsMessage thành mảng byte frame hoàn chỉnh [Header 4B Big-Endian][JSON UTF-8].
    /// </summary>
    /// <param name="message">Thông điệp cần mã hóa</param>
    /// <returns>Mảng byte frame nhị phân hoàn chỉnh</returns>
    byte[] EncodeMessage(NetOpsMessage message);

    /// <summary>
    /// Giải mã mảng byte payload JSON thành đối tượng NetOpsMessage.
    /// </summary>
    /// <param name="framePayload">Vùng nhớ ReadOnlySpan chứa chuỗi JSON UTF-8 của envelope</param>
    /// <returns>Đối tượng NetOpsMessage giải mã được</returns>
    NetOpsMessage DecodeMessage(ReadOnlySpan<byte> framePayload);

    /// <summary>
    /// Ghi một NetOpsMessage ra stream bất đồng bộ qua tầng framing.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ đích</param>
    /// <param name="message">Thông điệp cần truyền tải</param>
    /// <param name="cancellationToken">Mã thông báo hủy tác vụ bất đồng bộ</param>
    /// <returns>Một ValueTask đại diện cho tác vụ ghi bất đồng bộ</returns>
    ValueTask WriteMessageAsync(Stream stream, NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc một NetOpsMessage từ stream bất đồng bộ qua tầng framing.
    /// Trả về null nếu đối phương đóng kết nối sạch sẽ (Graceful EOF).
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ cần đọc</param>
    /// <param name="cancellationToken">Mã thông báo hủy tác vụ bất đồng bộ</param>
    /// <returns>Đối tượng NetOpsMessage đọc được, hoặc null nếu kết nối đóng sạch</returns>
    ValueTask<NetOpsMessage?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken = default);
}
