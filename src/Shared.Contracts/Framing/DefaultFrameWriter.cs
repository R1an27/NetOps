using System.Buffers.Binary;

namespace Shared.Contracts.Framing;

/// <summary>
/// Hiện thực hóa mặc định của IFrameWriter theo chuẩn NetOps Protocol v0.
/// Hỗ trợ ghi nguyên khối atomic kèm header 4 byte Big-Endian.
/// </summary>
public class DefaultFrameWriter : IFrameWriter
{
    private readonly FramingOptions _options;

    /// <summary>
    /// Thể hiện singleton mặc định dùng chung.
    /// </summary>
    public static DefaultFrameWriter Instance { get; } = new();

    /// <summary>
    /// Khởi tạo một đối tượng DefaultFrameWriter với tùy chọn cấu hình framing.
    /// </summary>
    /// <param name="options">Tùy chọn cấu hình framing (tùy chọn)</param>
    public DefaultFrameWriter(FramingOptions? options = null)
    {
        _options = options ?? FramingOptions.Default;
    }

    /// <inheritdoc/>
    public async ValueTask WriteFrameAsync(Stream stream, Frame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] payload = frame.Payload ?? [];
        await WriteFrameAsync(stream, payload.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);

        await WriteFrameAsync(stream, payload.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        int length = payload.Length;
        if (length < _options.MinPayloadSize || length > _options.MaxPayloadSize)
        {
            throw new InvalidDataException(
                $"Độ dài payload ({length} bytes) không hợp lệ (cho phép từ {_options.MinPayloadSize} đến {_options.MaxPayloadSize} bytes).");
        }

        int totalSize = FrameConstants.LengthPrefixSize + length;
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, FrameConstants.LengthPrefixSize), length);
            payload.Span.CopyTo(buffer.AsSpan(FrameConstants.LengthPrefixSize));

            await stream.WriteAsync(buffer.AsMemory(0, totalSize), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
