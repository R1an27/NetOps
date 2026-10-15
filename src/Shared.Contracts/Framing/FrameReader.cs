using System.Buffers.Binary;

namespace Shared.Contracts.Framing;

public static class FrameReader
{
    /// Tráº£ vá» null náº¿u Ä‘á»‘i phÆ°Æ¡ng Ä‘Ã³ng káº¿t ná»‘i sáº¡ch (chÆ°a cÃ³ byte nÃ o cá»§a frame má»›i).
    public static async Task<Frame?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[FrameConstants.LengthPrefixSize];
        if (!await ReadExactAsync(stream, header, ct))
            return null;

        uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length < 1 || length > FrameConstants.MaxFrameSize)
            throw new InvalidDataException($"Äá»™ dÃ i frame khÃ´ng há»£p lá»‡: {length}");

        byte[] body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct))
            throw new EndOfStreamException("Káº¿t ná»‘i Ä‘Ã³ng giá»¯a chá»«ng khi Ä‘ang Ä‘á»c frame");

        return new Frame(body[0], body[1..]);
    }

    /// Äá»c Ä‘á»§ buffer.Length byte. Tráº£ false náº¿u stream Ä‘Ã³ng trÆ°á»›c khi Ä‘á»c Ä‘Æ°á»£c byte nÃ o.
    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
            {
                if (offset == 0) return false;
                throw new EndOfStreamException("Stream Ä‘Ã³ng giá»¯a chá»«ng");
            }
            offset += read;
        }
        return true;
    }
}
