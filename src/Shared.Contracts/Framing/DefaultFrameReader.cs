using System.Buffers;
using System.Buffers.Binary;

namespace Shared.Contracts.Framing;

/// <summary>
/// Hiện thực hóa mặc định của <see cref="IFrameReader"/> theo chuẩn NetOps Protocol v0.
/// Hỗ trợ đọc an toàn luồng byte TCP, xử lý dính gói, phân mảnh và Graceful EOF.
/// </summary>
public class DefaultFrameReader : IFrameReader
{
    private readonly FramingOptions _options;

    /// <summary>
    /// Thể hiện singleton mặc định dùng chung.
    /// </summary>
    public static DefaultFrameReader Instance { get; } = new();

    /// <summary>
    /// Khởi tạo một đối tượng DefaultFrameReader với tùy chọn cấu hình framing.
    /// </summary>
    /// <param name="options">Tùy chọn cấu hình framing (tùy chọn)</param>
    public DefaultFrameReader(FramingOptions? options = null)
    {
        _options = options ?? FramingOptions.Default;
    }

    /// <inheritdoc/>
    public async ValueTask<Frame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] headerBuffer = ArrayPool<byte>.Shared.Rent(FrameConstants.LengthPrefixSize);
        try
        {
            int headerBytesRead = await ReadExactAsync(
                stream,
                headerBuffer.AsMemory(0, FrameConstants.LengthPrefixSize),
                allowEofAtStart: true,
                cancellationToken).ConfigureAwait(false);

            if (headerBytesRead == 0)
            {
                return null;
            }

            int length = BinaryPrimitives.ReadInt32BigEndian(headerBuffer.AsSpan(0, FrameConstants.LengthPrefixSize));
            if (length < _options.MinPayloadSize || length > _options.MaxPayloadSize)
            {
                throw new InvalidDataException(
                    $"Độ dài frame không hợp lệ: {length} bytes (cho phép từ {_options.MinPayloadSize} đến {_options.MaxPayloadSize} bytes).");
            }

            byte[] payload = new byte[length];
            await ReadExactAsync(stream, payload.AsMemory(), allowEofAtStart: false, cancellationToken).ConfigureAwait(false);
            return new Frame(payload);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<byte[]?> ReadPayloadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        Frame? frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (frame == null)
        {
            return null;
        }

        return frame.Value.Payload;
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>?> ReadPayloadMemoryAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        Frame? frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (frame == null)
        {
            return null;
        }

        return frame.Value.Payload;
    }

    private static async Task<int> ReadExactAsync(Stream stream, Memory<byte> buffer, bool allowEofAtStart, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.Slice(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0 && allowEofAtStart)
                {
                    return 0;
                }

                throw new EndOfStreamException($"Kết nối đóng giữa chừng khi đang đọc dữ liệu (đã nhận {offset}/{buffer.Length} bytes).");
            }

            offset += read;
        }

        return offset;
    }
}
