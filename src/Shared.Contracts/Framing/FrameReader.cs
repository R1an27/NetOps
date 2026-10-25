namespace Shared.Contracts.Framing;

/// <summary>
/// Bộ đọc khung dữ liệu TCP theo cơ chế Length-Prefixed Framing chuẩn NetOps Protocol v0.
/// Cấu trúc: [Length (4 Bytes, Big-Endian)] [Payload Data (N Bytes)]
/// Xử lý triệt để hiện tượng dính gói (sticky packets) và phân mảnh (fragmentation) của TCP stream.
/// </summary>
public static class FrameReader
{
    /// <summary>
    /// Đọc một frame hoàn chỉnh từ stream theo thuật toán Frame Decoding Algorithm (Mục 2.2 spec v0).
    /// Trả về null nếu kết nối đóng sạch (Graceful Close - chưa nhận byte nào của frame mới).
    /// Ném InvalidDataException nếu chiều dài frame vi phạm quy tắc (Length &lt; 1 hoặc Length &gt; 16 MB).
    /// Ném EndOfStreamException nếu luồng bị ngắt kết nối giữa chừng khi đang đọc dở frame.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu TCP hoặc MemoryStream cần đọc</param>
    /// <param name="ct">CancellationToken thông báo hủy tác vụ</param>
    /// <returns>Đối tượng Frame chứa mảng byte dữ liệu, hoặc null nếu stream đã đóng an toàn</returns>
    public static async Task<Frame?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return await DefaultFrameReader.Instance.ReadFrameAsync(stream, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Tiện ích đọc trực tiếp mảng byte Payload từ stream.
    /// Trả về null nếu kết nối đóng sạch trước frame mới.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu TCP hoặc MemoryStream cần đọc</param>
    /// <param name="ct">CancellationToken thông báo hủy tác vụ</param>
    /// <returns>Mảng byte payload, hoặc null nếu stream đã đóng an toàn</returns>
    public static async Task<byte[]?> ReadPayloadAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return await DefaultFrameReader.Instance.ReadPayloadAsync(stream, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Tiện ích đọc trực tiếp vùng nhớ ReadOnlyMemory Payload từ stream.
    /// Trả về null nếu kết nối đóng sạch trước frame mới.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu TCP hoặc MemoryStream cần đọc</param>
    /// <param name="ct">CancellationToken thông báo hủy tác vụ</param>
    /// <returns>Vùng nhớ ReadOnlyMemory byte payload, hoặc null nếu stream đã đóng an toàn</returns>
    public static async Task<ReadOnlyMemory<byte>?> ReadPayloadMemoryAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return await DefaultFrameReader.Instance.ReadPayloadMemoryAsync(stream, ct).ConfigureAwait(false);
    }
}
