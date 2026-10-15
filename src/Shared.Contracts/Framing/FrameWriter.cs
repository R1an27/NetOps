using System.Buffers.Binary;

namespace Shared.Contracts.Framing;

public static class FrameWriter
{
    public static async Task WriteAsync(Stream stream, Frame frame, CancellationToken ct = default)
    {
        int length = 1 + frame.Payload.Length; // Type + Payload
        if (length > FrameConstants.MaxFrameSize)
            throw new InvalidDataException($"Frame quÃ¡ lá»›n: {length} byte");

        // Gá»™p thÃ nh 1 buffer rá»“i ghi 1 láº§n Ä‘á»ƒ cÃ¡c pháº§n khÃ´ng bá»‹ xen káº½
        byte[] buffer = new byte[FrameConstants.LengthPrefixSize + length];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)length);
        buffer[4] = frame.Type;
        frame.Payload.CopyTo(buffer, 5);

        await stream.WriteAsync(buffer, ct);
        await stream.FlushAsync(ct);
    }
}
