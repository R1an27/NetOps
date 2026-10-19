namespace Shared.Contracts.Framing;

/// <summary>
/// Tùy chọn cấu hình các tham số và giới hạn cho bộ xử lý khung dữ liệu TCP Framing.
/// Hỗ trợ cấu hình linh hoạt qua Dependency Injection hoặc truyền trực tiếp vào FrameReader/Writer.
/// </summary>
public sealed class FramingOptions
{
    /// <summary>
    /// Kích thước payload tối thiểu cho phép (mặc định 1 byte theo FrameConstants.MinPayloadSize).
    /// Frame có kích thước nhỏ hơn giá trị này sẽ bị coi là vi phạm giao thức.
    /// </summary>
    public int MinPayloadSize { get; set; } = FrameConstants.MinPayloadSize;

    /// <summary>
    /// Kích thước payload tối đa cho phép để phòng chống tấn công tràn bộ nhớ DoS (mặc định 16 MB theo FrameConstants.MaxPayloadSize).
    /// Mọi frame có kích thước lớn hơn giá trị này sẽ bị từ chối ngay lập tức.
    /// </summary>
    public int MaxPayloadSize { get; set; } = FrameConstants.MaxPayloadSize;

    /// <summary>
    /// Kích thước bộ đệm đọc ban đầu (tính bằng bytes), mặc định là 4096 bytes (4 KB).
    /// </summary>
    public int InitialBufferSize { get; set; } = 4096;

    /// <summary>
    /// Cho biết có sử dụng bộ đệm tái sử dụng ArrayPool&lt;byte&gt; để hạn chế cấp phát bộ nhớ rác (GC pressure) hay không.
    /// Mặc định là false để ưu tiên tính an toàn vòng đời bộ nhớ, có thể kích hoạt trong các kịch bản hiệu năng cao.
    /// </summary>
    public bool UseBufferPooling { get; set; }

    /// <summary>
    /// Phiên bản tùy chọn cấu hình mặc định dùng chung.
    /// </summary>
    public static FramingOptions Default { get; } = new();
}
