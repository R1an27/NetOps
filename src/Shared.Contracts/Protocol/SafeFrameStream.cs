using ServerManager.Shared.Contracts.Models;
using Shared.Contracts.Framing;

namespace ServerManager.Shared.Contracts.Protocol;

/// <summary>
/// Bao bọc NetworkStream với cơ chế khóa ghi an toàn SemaphoreSlim(1, 1).
/// Hiện thực hóa hợp đồng ISafeFrameChannel và quy tắc ứng xử mạng (Mục 5.1 NetOps Protocol v0: Write Serialization).
/// Đảm bảo không xảy ra hiện tượng trộn lẫn byte (packet interleaving) khi nhiều tác vụ
/// chạy ngầm (ví dụ: Heartbeat, Ping, Response, Command output streaming) cùng ghi dữ liệu
/// đồng thời vào một kết nối TCP Full-Duplex duy nhất.
/// </summary>
public sealed class SafeFrameStream : ISafeFrameChannel
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IFrameWriter _frameWriter;
    private readonly IFrameCodec _frameCodec;
    private bool _disposed;

    /// <summary>
    /// Cho biết trạng thái kênh truyền còn mở và sẵn sàng hoạt động hay không.
    /// </summary>
    public bool IsConnected => !_disposed && (_stream.CanRead || _stream.CanWrite);

    /// <summary>
    /// Luồng dữ liệu mạng hoặc bộ nhớ nền bên dưới.
    /// </summary>
    public Stream UnderlyingStream => _stream;

    /// <summary>
    /// Khởi tạo một đối tượng SafeFrameStream mới bọc quanh một luồng Stream cơ sở.
    /// </summary>
    /// <param name="stream">Luồng dữ liệu mạng NetworkStream hoặc MemoryStream</param>
    /// <param name="frameWriter">Bộ ghi khung dữ liệu tùy chọn (mặc định là DefaultFrameWriter.Instance)</param>
    /// <param name="frameCodec">Bộ mã hóa khung dữ liệu tùy chọn (mặc định là DefaultFrameCodec.Instance)</param>
    public SafeFrameStream(Stream stream, IFrameWriter? frameWriter = null, IFrameCodec? frameCodec = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _frameWriter = frameWriter ?? DefaultFrameWriter.Instance;
        _frameCodec = frameCodec ?? DefaultFrameCodec.Instance;
    }

    /// <summary>
    /// Gửi một NetOpsMessage ra kênh truyền an toàn đa luồng theo chuẩn ISafeFrameChannel.
    /// </summary>
    /// <param name="message">Thông điệp cần truyền tải</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    /// <returns>Một ValueTask đại diện cho tác vụ gửi bất đồng bộ</returns>
    public async ValueTask SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Đọc một NetOpsMessage từ kênh truyền bất đồng bộ theo chuẩn ISafeFrameChannel.
    /// Trả về null nếu đối phương đóng kết nối sạch sẽ (Graceful EOF).
    /// </summary>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    /// <returns>Đối tượng NetOpsMessage đọc được, hoặc null nếu kết nối đóng sạch</returns>
    public async ValueTask<NetOpsMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default)
    {
        return await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gửi trực tiếp một khung dữ liệu nhị phân thô ra kênh truyền an toàn đa luồng theo chuẩn ISafeFrameChannel.
    /// Tối ưu không sao chép bộ nhớ trung gian bằng cách sử dụng trực tiếp ReadOnlyMemory.
    /// </summary>
    /// <param name="payload">Vùng nhớ dữ liệu nhị phân cần truyền đi</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    /// <returns>Một ValueTask đại diện cho tác vụ gửi bất đồng bộ</returns>
    public async ValueTask SendRawFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _frameWriter.WriteFrameAsync(_stream, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Ghi một NetOpsMessage ra stream một cách an toàn đa luồng.
    /// Tự động tuần tự hóa ghi thông qua SemaphoreSlim(1, 1).
    /// </summary>
    /// <param name="message">Thông điệp NetOpsMessage cần truyền đi</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    public async Task WriteMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _frameCodec.WriteMessageAsync(_stream, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Đọc một NetOpsMessage từ stream.
    /// Trả về null nếu kết nối đóng an toàn (Graceful EOF).
    /// </summary>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    /// <returns>Đối tượng NetOpsMessage đọc được, hoặc null nếu kết nối ngắt an toàn</returns>
    public async Task<NetOpsMessage?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _frameCodec.ReadMessageAsync(_stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ghi một mảng byte nhị phân ra stream một cách an toàn đa luồng.
    /// </summary>
    /// <param name="payload">Mảng byte dữ liệu frame</param>
    /// <param name="cancellationToken">Mã hủy tác vụ bất đồng bộ</param>
    public async Task WriteRawFrameAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(payload);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _frameWriter.WriteFrameAsync(_stream, payload.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Giải phóng các tài nguyên đồng bộ bao gồm khóa SemaphoreSlim và luồng Stream cơ sở.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _writeLock.Dispose();
            _stream.Dispose();
        }
    }

    /// <summary>
    /// Giải phóng các tài nguyên bất đồng bộ bao gồm khóa SemaphoreSlim và luồng Stream cơ sở.
    /// </summary>
    /// <returns>Một ValueTask đại diện cho tác vụ dọn dẹp bất đồng bộ</returns>
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _writeLock.Dispose();
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
