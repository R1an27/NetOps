using System.Buffers.Binary;
using System.Text.Json;
using ServerManager.Shared.Contracts.Models;

namespace ServerManager.Shared.Contracts.Protocol;

/// <summary>
/// Bộ mã hóa và giải mã khung dữ liệu Length-Prefix Framing cho dòng dữ liệu TCP
/// </summary>
public static class FrameCodec
{
	public const int HeaderSize = 4;
	public const int MaxPayloadSize = 16 * 1024 * 1024; // Giới hạn 16 MB phòng ngừa tấn công tràn bộ nhớ

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	/// <summary>
	/// Đóng gói NetOpsMessage thành mảng byte gồm [4 Bytes Header Big-Endian] + [Payload JSON UTF-8]
	/// </summary>
	public static byte[] EncodeFrame(NetOpsMessage message)
	{
		byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
		if (payloadBytes.Length > MaxPayloadSize)
		{
			throw new InvalidOperationException($"Payload size ({payloadBytes.Length} bytes) exceeds limit ({MaxPayloadSize} bytes).");
		}

		byte[] frame = new byte[HeaderSize + payloadBytes.Length];
		// Ghi độ dài theo thứ tự Big-Endian (Network Byte Order)
		BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, HeaderSize), payloadBytes.Length);
		Buffer.BlockCopy(payloadBytes, 0, frame, HeaderSize, payloadBytes.Length);
		return frame;
	}

	/// <summary>
	/// Giải mã mảng byte thành đối tượng NetOpsMessage
	/// </summary>
	public static NetOpsMessage DecodeFrame(ReadOnlySpan<byte> frameSpan)
	{
		if (frameSpan.Length < HeaderSize)
		{
			throw new InvalidDataException($"Frame size ({frameSpan.Length}) is smaller than header size ({HeaderSize}).");
		}

		int payloadLength = BinaryPrimitives.ReadInt32BigEndian(frameSpan[..HeaderSize]);
		if (payloadLength <= 0 || payloadLength > MaxPayloadSize)
		{
			throw new InvalidDataException($"Invalid payload length: {payloadLength} bytes.");
		}

		if (frameSpan.Length - HeaderSize < payloadLength)
		{
			throw new InvalidDataException($"Frame data incomplete. Expected {payloadLength} bytes.");
		}

		ReadOnlySpan<byte> payloadSpan = frameSpan.Slice(HeaderSize, payloadLength);
		return JsonSerializer.Deserialize<NetOpsMessage>(payloadSpan, JsonOptions)
			   ?? throw new InvalidDataException("Failed to deserialize message payload.");
	}

	/// <summary>
	/// Ghi frame bất đồng bộ ra NetworkStream
	/// </summary>
	public static async Task WriteFrameAsync(Stream stream, NetOpsMessage message, CancellationToken cancellationToken = default)
	{
		byte[] frameBytes = EncodeFrame(message);
		await stream.WriteAsync(frameBytes, cancellationToken).ConfigureAwait(false);
		await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Đọc frame bất đồng bộ từ NetworkStream; xử lý an toàn dính gói và phân mảnh
	/// Trả về null nếu Stream đóng an toàn (Graceful EOF)
	/// </summary>
	public static async Task<NetOpsMessage?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
	{
		byte[] headerBuffer = new byte[HeaderSize];
		int headerBytesRead = 0;

		// Vòng lặp đọc đủ 4 bytes Header
		while (headerBytesRead < HeaderSize)
		{
			int read = await stream.ReadAsync(
				headerBuffer.AsMemory(headerBytesRead, HeaderSize - headerBytesRead),
				cancellationToken).ConfigureAwait(false);

			if (read == 0)
			{
				if (headerBytesRead == 0) return null; // Đối tác đóng kết nối bình thường
				throw new EndOfStreamException("Connection terminated prematurely while reading frame header.");
			}

			headerBytesRead += read;
		}

		int payloadLength = BinaryPrimitives.ReadInt32BigEndian(headerBuffer);
		if (payloadLength <= 0 || payloadLength > MaxPayloadSize)
		{
			throw new InvalidDataException($"Invalid frame payload size received: {payloadLength} bytes.");
		}

		// Vòng lặp đọc đủ payloadLength bytes
		byte[] payloadBuffer = new byte[payloadLength];
		int payloadBytesRead = 0;

		while (payloadBytesRead < payloadLength)
		{
			int read = await stream.ReadAsync(
				payloadBuffer.AsMemory(payloadBytesRead, payloadLength - payloadBytesRead),
				cancellationToken).ConfigureAwait(false);

			if (read == 0)
			{
				throw new EndOfStreamException($"Connection terminated prematurely while reading payload.");
			}

			payloadBytesRead += read;
		}

		return JsonSerializer.Deserialize<NetOpsMessage>(payloadBuffer, JsonOptions)
			   ?? throw new InvalidDataException("Failed to deserialize payload into NetOpsMessage.");
	}
}