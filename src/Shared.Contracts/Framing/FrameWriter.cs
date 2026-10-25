namespace Shared.Contracts.Framing;

/// <summary>
/// Bộ ghi khung dữ liệu theo cấu trúc Length-Prefixed Framing chuẩn NetOps Protocol v0.
/// Cấu trúc trên đường truyền: [Length (4 Bytes, Big-Endian)] [Payload Data (N Bytes)]
/// </summary>
public static class FrameWriter
{
    /// <summary>
    /// Ghi frame ra stream bất đồng bộ với định dạng chuẩn NetOps Protocol v0:
    /// [Length (4 Bytes, Big-Endian)] [Payload Data (N Bytes)]
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ</param>
    /// <param name="frame">Khung dữ liệu cần truyền tải</param>
    /// <param name="ct">Mã thông báo hủy tác vụ</param>
    public static async Task WriteAsync(Stream stream, Frame frame, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] payload = frame.Payload ?? [];
        await DefaultFrameWriter.Instance.WriteFrameAsync(stream, payload.AsMemory(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Tiện ích ghi trực tiếp một mảng byte payload ra stream dưới dạng frame nhị phân.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ</param>
    /// <param name="payload">Mảng byte dữ liệu cần truyền tải</param>
    /// <param name="ct">Mã thông báo hủy tác vụ</param>
    public static Task WriteAsync(Stream stream, byte[] payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);

        return DefaultFrameWriter.Instance.WriteFrameAsync(stream, payload.AsMemory(), ct).AsTask();
    }

    /// <summary>
    /// Ghi trực tiếp một vùng nhớ ReadOnlyMemory payload nhị phân ra stream dưới dạng frame nhị phân chuẩn.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng hoặc bộ nhớ</param>
    /// <param name="payload">Vùng nhớ dữ liệu cần truyền tải</param>
    /// <param name="ct">Mã thông báo hủy tác vụ</param>
    public static Task WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return DefaultFrameWriter.Instance.WriteFrameAsync(stream, payload, ct).AsTask();
    }
}
