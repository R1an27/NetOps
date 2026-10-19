namespace Shared.Contracts.Framing;

/// <summary>
/// Các hằng số định nghĩa cấu trúc khung nhị phân (Binary Length-Prefix Framing)
/// theo đặc tả NetOps Protocol v0 (docs/specifications/protocol-v0-spec.md).
/// Cấu trúc frame trên đường truyền: [Length (4 Bytes, Big-Endian)] [Payload Data (N Bytes)]
/// </summary>
public static class FrameConstants
{
    /// <summary>
    /// Kích thước trường tiền tố độ dài (4 bytes - 32-bit signed integer).
    /// </summary>
    public const int LengthPrefixSize = 4;

    /// <summary>
    /// Kích thước tối thiểu của phần Payload (1 byte).
    /// Frame có Length &lt; 1 byte bị xem là vi phạm giao thức.
    /// </summary>
    public const int MinPayloadSize = 1;

    /// <summary>
    /// Kích thước tối đa cho phép của phần Payload (16 MB = 16,777,216 bytes).
    /// Mọi frame có Length &gt; 16 MB đều bị từ chối để phòng chống tấn công tràn bộ nhớ (DoS).
    /// </summary>
    public const int MaxPayloadSize = 16 * 1024 * 1024; // 16 MB = 16,777,216 bytes
}

/// <summary>
/// Đại diện cho một đơn vị dữ liệu (frame) sau khi đã phân tách ranh giới trên dòng byte TCP.
/// Tuân thủ NetOps Protocol v0: Frame nhị phân thuần túy chứa mảng byte Payload (chuỗi JSON UTF-8 của Envelope).
/// </summary>
public readonly record struct Frame
{
    /// <summary>
    /// Nội dung dữ liệu nhị phân của frame (Payload Data).
    /// </summary>
    public byte[] Payload { get; }

    /// <summary>
    /// Độ dài tính bằng byte của Payload.
    /// </summary>
    public int Length => Payload.Length;

    /// <summary>
    /// Khởi tạo một đối tượng Frame mới với mảng byte dữ liệu được chỉ định.
    /// </summary>
    /// <param name="payload">Mảng byte dữ liệu payload của frame</param>
    public Frame(byte[]? payload)
    {
        Payload = payload ?? [];
    }
}
