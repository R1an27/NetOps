using System.Buffers.Binary;
using System.Text.Json;
using ServerManager.Shared.Contracts.Models;
using Shared.Contracts.Framing;

namespace ServerManager.Shared.Contracts.Protocol;

/// <summary>
/// Hiện thực hóa mặc định của <see cref="IFrameCodec"/> cho NetOps Protocol v0.
/// Cung cấp cầu nối mã hóa/giải mã giữa đối tượng NetOpsMessage và Binary Frame.
/// </summary>
public class DefaultFrameCodec : IFrameCodec
{
    private readonly IFrameReader _frameReader;
    private readonly IFrameWriter _frameWriter;

    /// <summary>
    /// Thể hiện singleton mặc định dùng chung.
    /// </summary>
    public static DefaultFrameCodec Instance { get; } = new();

    /// <summary>
    /// Khởi tạo một đối tượng DefaultFrameCodec với reader và writer tùy chỉnh (hỗ trợ DI).
    /// </summary>
    /// <param name="frameReader">Bộ đọc frame (tùy chọn, mặc định là DefaultFrameReader.Instance)</param>
    /// <param name="frameWriter">Bộ ghi frame (tùy chọn, mặc định là DefaultFrameWriter.Instance)</param>
    public DefaultFrameCodec(IFrameReader? frameReader = null, IFrameWriter? frameWriter = null)
    {
        _frameReader = frameReader ?? DefaultFrameReader.Instance;
        _frameWriter = frameWriter ?? DefaultFrameWriter.Instance;
    }

    /// <inheritdoc/>
    public byte[] EncodeMessage(NetOpsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (payloadBytes.Length < FrameConstants.MinPayloadSize || payloadBytes.Length > FrameConstants.MaxPayloadSize)
        {
            throw new InvalidDataException(
                $"Kích thước payload ({payloadBytes.Length} bytes) vượt quá giới hạn cho phép ({FrameConstants.MinPayloadSize} - {FrameConstants.MaxPayloadSize} bytes).");
        }

        byte[] frame = new byte[FrameConstants.LengthPrefixSize + payloadBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, FrameConstants.LengthPrefixSize), payloadBytes.Length);
        payloadBytes.CopyTo(frame, FrameConstants.LengthPrefixSize);
        return frame;
    }

    /// <inheritdoc/>
    public NetOpsMessage DecodeMessage(ReadOnlySpan<byte> framePayload)
    {
        if (framePayload.IsEmpty)
        {
            throw new InvalidDataException("Payload frame rỗng, không thể giải mã thành NetOpsMessage.");
        }

        try
        {
            return JsonSerializer.Deserialize<NetOpsMessage>(framePayload)
                   ?? throw new InvalidDataException("Không thể giải mã dữ liệu JSON thành đối tượng NetOpsMessage.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Dữ liệu payload JSON không hợp lệ hoặc sai cấu trúc NetOpsMessage.", ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask WriteMessageAsync(Stream stream, NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await _frameWriter.WriteFrameAsync(stream, payloadBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<NetOpsMessage?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ReadOnlyMemory<byte>? memory = await _frameReader.ReadPayloadMemoryAsync(stream, cancellationToken).ConfigureAwait(false);
        if (memory == null)
        {
            return null;
        }

        return DecodeMessage(memory.Value.Span);
    }
}
