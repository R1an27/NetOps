namespace Shared.Contracts.Framing;

/// <summary>
/// Các hằng số định nghĩa cấu trúc khung nhị phân (Binary Length-Prefix Framing).
/// Cấu trúc frame: [Length (4 Bytes, Big-Endian)] [Type (1 Byte)] [Payload (N Bytes)]
/// Trong đó: Length = 1 (Type) + Payload.Length
/// </summary>
public static class FrameConstants
{
    /// <summary>
    /// Kích thước trường tiền tố độ dài (4 bytes).
    /// </summary>
    public const int LengthPrefixSize = 4;

    /// <summary>
    /// Kích thước trường loại thông điệp (1 byte).
    /// </summary>
    public const int TypeSize = 1;

    /// <summary>
    /// Kích thước tối thiểu của một frame hợp lệ (1 byte Type + 0 byte Payload).
    /// </summary>
    public const int MinFrameSize = 1;

    /// <summary>
    /// Kích thước tối đa cho phép của một frame (1 MB = 1,048,576 bytes) để chống tràn bộ nhớ / DoS.
    /// </summary>
    public const int MaxFrameSize = 1024 * 1024; // 1 MB
}

/// <summary>
/// Đại diện cho một đơn vị dữ liệu (frame) sau khi đã phân tách ranh giới trên dòng TCP.
/// </summary>
public readonly record struct Frame
{
    public byte Type { get; }
    public byte[] Payload { get; }

    public Frame(byte type, byte[]? payload)
    {
        Type = type;
        Payload = payload ?? [];
    }
}
