namespace Shared.Contracts.Framing;

/// <summary>
/// Hợp đồng đọc khung dữ liệu TCP nhị phân theo chuẩn Length-Prefixed Framing NetOps Protocol v0.
/// Xử lý triệt để hiện tượng dính gói (sticky packets) và phân mảnh (fragmentation) của luồng TCP.
/// </summary>
public interface IFrameReader
{
    /// <summary>
    /// Đọc một frame hoàn chỉnh từ stream bất đồng bộ.
    /// Trả về null nếu đối phương đóng kết nối sạch trước khi nhận bất kỳ byte nào (Graceful EOF).
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ cần đọc</param>
    /// <param name="cancellationToken">Mã thông báo hủy tác vụ bất đồng bộ</param>
    /// <returns>Khung dữ liệu Frame đọc được, hoặc null nếu kết nối đóng sạch</returns>
    /// <exception cref="ArgumentNullException">Ném ra khi tham số stream là null</exception>
    /// <exception cref="InvalidDataException">Ném ra khi chiều dài frame vi phạm quy tắc (Length &lt; 1 hoặc Length &gt; 16 MB)</exception>
    /// <exception cref="EndOfStreamException">Ném ra khi stream bị ngắt kết nối giữa chừng khi đang đọc dở frame</exception>
    ValueTask<Frame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tiện ích đọc trực tiếp dữ liệu Payload của frame dưới dạng mảng byte bất đồng bộ.
    /// Trả về null nếu stream đóng sạch trước frame mới (Graceful EOF).
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ cần đọc</param>
    /// <param name="cancellationToken">Mã thông báo hủy tác vụ bất đồng bộ</param>
    /// <returns>Mảng byte payload đọc được, hoặc null nếu kết nối đóng sạch</returns>
    ValueTask<byte[]?> ReadPayloadAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc payload của frame dưới dạng vùng nhớ ReadOnlyMemory để tối ưu hóa bộ nhớ đệm.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ cần đọc</param>
    /// <param name="cancellationToken">Mã thông báo hủy tác vụ bất đồng bộ</param>
    /// <returns>Vùng nhớ ReadOnlyMemory chứa payload, hoặc null nếu kết nối đóng sạch</returns>
    ValueTask<ReadOnlyMemory<byte>?> ReadPayloadMemoryAsync(Stream stream, CancellationToken cancellationToken = default);
}
