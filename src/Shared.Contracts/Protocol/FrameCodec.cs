using ServerManager.Shared.Contracts.Models;
using Shared.Contracts.Framing;

namespace ServerManager.Shared.Contracts.Protocol;

/// <summary>
/// Bộ mã hóa và giải mã khung dữ liệu Length-Prefixed Framing cho NetOps Protocol v0.
/// Đóng vai trò là cầu nối giữa tầng Framing nhị phân ([4 Bytes Length][Payload Data])
/// và tầng Envelope cấu trúc JSON (NetOpsMessage).
/// Lớp tĩnh này ủy nhiệm toàn bộ logic sang DefaultFrameCodec.Instance để đảm bảo tính đồng nhất 100%.
/// </summary>
public static class FrameCodec
{
    /// <summary>
    /// Kích thước trường tiền tố độ dài (4 bytes).
    /// </summary>
    public const int HeaderSize = FrameConstants.LengthPrefixSize;

    /// <summary>
    /// Giới hạn kích thước tối đa cho phép của payload (16 MB = 16,777,216 bytes).
    /// </summary>
    public const int MaxPayloadSize = FrameConstants.MaxPayloadSize;

    /// <summary>
    /// Đóng gói NetOpsMessage thành mảng byte hoàn chỉnh gồm:
    /// [4 Bytes Length Big-Endian] + [Payload JSON UTF-8]
    /// </summary>
    /// <param name="message">Thông điệp cần đóng gói</param>
    /// <returns>Mảng byte hoàn chỉnh của khung dữ liệu</returns>
    public static byte[] EncodeFrame(NetOpsMessage message)
    {
        return DefaultFrameCodec.Instance.EncodeMessage(message);
    }

    /// <summary>
    /// Giải mã một Span byte payload thành đối tượng NetOpsMessage.
    /// </summary>
    /// <param name="framePayload">Vùng nhớ byte payload của frame</param>
    /// <returns>Đối tượng NetOpsMessage đã giải mã</returns>
    public static NetOpsMessage DecodeMessage(ReadOnlySpan<byte> framePayload)
    {
        return DefaultFrameCodec.Instance.DecodeMessage(framePayload);
    }

    /// <summary>
    /// Ghi frame bất đồng bộ ra Stream thông qua tầng Framing
    /// </summary>
    /// <param name="stream">Luồng dữ liệu đích</param>
    /// <param name="message">Thông điệp cần ghi</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    public static async Task WriteFrameAsync(Stream stream, NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        await DefaultFrameCodec.Instance.WriteMessageAsync(stream, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ghi frame an toàn đa luồng ra Stream sử dụng SemaphoreSlim(1, 1).
    /// Tuân thủ quy tắc Write Serialization (Mục 5.1 NetOps Protocol v0) nhằm chống hiện tượng
    /// trộn lẫn byte khi có nhiều luồng cùng ghi vào kết nối Full-Duplex.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu đích</param>
    /// <param name="message">Thông điệp cần ghi</param>
    /// <param name="writeLock">Khóa SemaphoreSlim(1, 1)</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    public static async Task WriteFrameAsync(
        Stream stream,
        NetOpsMessage message,
        SemaphoreSlim writeLock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(writeLock);

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFrameAsync(stream, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Đọc frame bất đồng bộ từ Stream qua tầng Framing; xử lý an toàn dính gói và phân mảnh.
    /// Trả về null nếu Stream đóng an toàn (Graceful EOF).
    /// </summary>
    /// <param name="stream">Luồng dữ liệu nguồn</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    /// <returns>Đối tượng NetOpsMessage đã giải mã, hoặc null nếu ngắt kết nối sạch</returns>
    public static async Task<NetOpsMessage?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return await DefaultFrameCodec.Instance.ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}