namespace Client.WinForms.Services;

/// <summary>
/// Trạng thái kết nối TCP của Client tới Master.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// Chưa kết nối hoặc đã ngắt kết nối.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Đang trong quá trình thiết lập kết nối TCP socket.
    /// </summary>
    Connecting,

    /// <summary>
    /// Đã kết nối thành công và kênh truyền SafeFrameStream sẵn sàng.
    /// </summary>
    Connected,

    /// <summary>
    /// Đang thử kết nối lại sau sự cố mất mạng.
    /// </summary>
    Reconnecting
}
