namespace Shared.Contracts.Framing;

/// <summary>
/// Hợp đồng ghi khung dữ liệu TCP nhị phân theo cấu trúc Length-Prefixed Framing NetOps Protocol v0.
/// [Length (4 Bytes, Big-Endian)] [Payload Data (N Bytes)]
/// </summary>
public interface IFrameWriter
{
    /// <summary>
    /// Ghi frame ra stream bất đồng bộ nguyên khối (atomic write) kèm tiền tố 4 byte Big-Endian.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ đích</param>
    /// <param name="frame">Khung dữ liệu cần truyền tải</param>
    /// <param name="cancellationToken">Mã thông báo hủy tác vụ bất đồng bộ</param>
    /// <returns>Một ValueTask đại diện cho tác vụ ghi bất đồng bộ</returns>
    /// <exception cref="ArgumentNullException">Ném ra khi tham số stream là null</exception>
    /// <exception cref="InvalidDataException">Ném ra khi kích thước payload vi phạm giới hạn giao thức</exception>
    ValueTask WriteFrameAsync(Stream stream, Frame frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi trực tiếp một mảng byte payload ra stream dưới dạng frame nhị phân.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ đích</param>
    /// <param name="payload">Mảng byte dữ liệu cần truyền tải</param>
    /// <param name="cancellationToken">Mã thông báo hủy tác vụ bất đồng bộ</param>
    /// <returns>Một ValueTask đại diện cho tác vụ ghi bất đồng bộ</returns>
    ValueTask WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi trực tiếp một vùng nhớ ReadOnlyMemory payload nhị phân ra stream.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ đích</param>
    /// <param name="payload">Vùng nhớ dữ liệu cần truyền tải</param>
    /// <param name="cancellationToken">Mã thông báo hủy tác vụ bất đồng bộ</param>
    /// <returns>Một ValueTask đại diện cho tác vụ ghi bất đồng bộ</returns>
    ValueTask WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}
