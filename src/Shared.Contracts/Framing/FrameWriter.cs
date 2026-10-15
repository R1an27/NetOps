using System.Buffers.Binary;

namespace Shared.Contracts.Framing;

/// <summary>
/// Bộ ghi khung dữ liệu theo cấu trúc Length-Prefix Framing.
/// </summary>
public static class FrameWriter
{
    /// <summary>
    /// Ghi frame ra stream bất đồng bộ với định dạng:
    /// [Length (4 Bytes, Big-Endian)] [Type (1 Byte)] [Payload (N Bytes)]
    /// </summary>
    public static async Task WriteAsync(Stream stream, Frame frame, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] payload = frame.Payload ?? [];
        int length = FrameConstants.TypeSize + payload.Length;

        if (length > FrameConstants.MaxFrameSize)
        {
            throw new InvalidDataException($"Độ dài frame ({length} bytes) vượt quá giới hạn tối đa cho phép ({FrameConstants.MaxFrameSize} bytes).");
        }

        // Gộp thành 1 buffer để ghi 1 lần ra stream, tránh phân mảnh ở tầng ứng dụng
        byte[] buffer = new byte[FrameConstants.LengthPrefixSize + length];
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0, FrameConstants.LengthPrefixSize), (uint)length);
        buffer[FrameConstants.LengthPrefixSize] = frame.Type;

        if (payload.Length > 0)
        {
            payload.CopyTo(buffer.AsSpan(FrameConstants.LengthPrefixSize + FrameConstants.TypeSize));
        }

        await stream.WriteAsync(buffer, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
