using System.Buffers.Binary;

namespace Shared.Contracts.Framing;

/// <summary>
/// Bộ đọc khung dữ liệu TCP theo cơ chế Length-Prefix Framing.
/// Hỗ trợ xử lý hiện tượng dính gói (sticky packets) và phân mảnh (fragmentation) của TCP.
/// </summary>
public static class FrameReader
{
    /// <summary>
    /// Đọc một frame hoàn chỉnh từ stream.
    /// Trả về null nếu kết nối đóng sạch (Graceful Close - chưa nhận byte nào của frame mới).
    /// </summary>
    public static async Task<Frame?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // 1. Đọc trường độ dài (4 bytes Big-Endian)
        byte[] lengthHeader = new byte[FrameConstants.LengthPrefixSize];
        int headerBytesRead = await ReadExactAsync(stream, lengthHeader, allowEofAtStart: true, ct).ConfigureAwait(false);
        if (headerBytesRead == 0)
        {
            return null; // Graceful EOF: kết nối đóng sạch trước khi có byte nào của frame mới
        }

        uint length = BinaryPrimitives.ReadUInt32BigEndian(lengthHeader);
        if (length < FrameConstants.MinFrameSize || length > FrameConstants.MaxFrameSize)
        {
            throw new InvalidDataException($"Độ dài frame không hợp lệ: {length} bytes (cho phép từ {FrameConstants.MinFrameSize} đến {FrameConstants.MaxFrameSize} bytes).");
        }

        // 2. Đọc byte Type (1 byte)
        byte[] typeBuffer = new byte[FrameConstants.TypeSize];
        await ReadExactAsync(stream, typeBuffer, allowEofAtStart: false, ct).ConfigureAwait(false);
        byte type = typeBuffer[0];

        // 3. Đọc Payload trực tiếp vào mảng đích (không qua mảng trung gian, loại bỏ hoàn toàn slice copy)
        int payloadLength = (int)length - FrameConstants.TypeSize;
        byte[] payload = payloadLength == 0 ? [] : new byte[payloadLength];

        if (payloadLength > 0)
        {
            await ReadExactAsync(stream, payload, allowEofAtStart: false, ct).ConfigureAwait(false);
        }

        return new Frame(type, payload);
    }

    /// <summary>
    /// Đọc chính xác buffer.Length bytes từ stream.
    /// Nếu allowEofAtStart = true và stream đóng trước khi đọc bất kỳ byte nào, trả về 0.
    /// Nếu stream đóng giữa chừng khi chưa đọc đủ số byte yêu cầu, ném EndOfStreamException.
    /// </summary>
    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, bool allowEofAtStart, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
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
