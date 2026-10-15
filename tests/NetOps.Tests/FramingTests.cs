using System.Buffers.Binary;
using Shared.Contracts.Framing;
using Xunit;

namespace NetOps.Tests;

public class FramingTests
{
    [Fact]
    public async Task Ghi_3_frame_lien_tiep_thi_doc_lai_dung_3_frame()
    {
        var ms = new MemoryStream();
        for (byte i = 1; i <= 3; i++)
        {
            await FrameWriter.WriteAsync(ms, new Frame(i, [i, i, i]));
        }

        ms.Position = 0;
        for (byte i = 1; i <= 3; i++)
        {
            var f = await FrameReader.ReadAsync(ms);
            Assert.NotNull(f);
            Assert.Equal(i, f!.Value.Type);
            Assert.Equal([i, i, i], f.Value.Payload);
        }

        Assert.Null(await FrameReader.ReadAsync(ms)); // Hết dữ liệu (Graceful EOF)
    }

    [Fact]
    public async Task Ghi_va_doc_frame_khong_co_payload()
    {
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, new Frame(99, []));

        ms.Position = 0;
        var f = await FrameReader.ReadAsync(ms);

        Assert.NotNull(f);
        Assert.Equal(99, f!.Value.Type);
        Assert.Empty(f.Value.Payload);
        Assert.Null(await FrameReader.ReadAsync(ms));
    }

    [Fact]
    public async Task Do_dai_vuot_gioi_han_thi_nem_loi()
    {
        // Ghi header với length > MaxFrameSize (1 MB)
        byte[] invalidHeader = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(invalidHeader, (uint)(FrameConstants.MaxFrameSize + 1));

        var ms = new MemoryStream(invalidHeader);
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameReader.ReadAsync(ms));
    }

    [Fact]
    public async Task Do_dai_bang_0_thi_nem_loi()
    {
        byte[] zeroLengthHeader = new byte[4] { 0, 0, 0, 0 };
        var ms = new MemoryStream(zeroLengthHeader);
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameReader.ReadAsync(ms));
    }

    [Fact]
    public async Task Stream_bi_cat_khi_dang_doc_header_do_dai_thi_nem_loi()
    {
        // Stream chỉ gửi 2 byte của header 4 byte rồi ngắt kết nối
        var ms = new MemoryStream([0x00, 0x00]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => FrameReader.ReadAsync(ms));
    }

    [Fact]
    public async Task Stream_bi_cat_khi_dang_doc_payload_thi_nem_loi()
    {
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, new Frame(1, new byte[100]));

        // Cắt bớt dữ liệu payload chỉ còn 50 byte
        var cut = new MemoryStream(ms.ToArray()[..50]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => FrameReader.ReadAsync(cut));
    }

    [Fact]
    public async Task Stream_dong_sach_thi_tra_ve_null()
    {
        var ms = new MemoryStream(); // Rỗng hoàn toàn
        var result = await FrameReader.ReadAsync(ms);
        Assert.Null(result);
    }

    [Fact]
    public async Task Doc_frame_khi_stream_bi_phan_manh_tung_byte()
    {
        // Chuẩn bị dữ liệu 2 frame hoàn chỉnh
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, new Frame(10, [1, 2, 3, 4, 5]));
        await FrameWriter.WriteAsync(ms, new Frame(20, [6, 7, 8, 9, 10, 11]));

        // Giả lập TCP Stream bị phân mảnh nặng (chỉ trả về tối đa 2 bytes mỗi lệnh Read)
        var chunkedStream = new ChunkedReadStream(ms.ToArray(), maxChunkSize: 2);

        var frame1 = await FrameReader.ReadAsync(chunkedStream);
        Assert.NotNull(frame1);
        Assert.Equal(10, frame1!.Value.Type);
        Assert.Equal([1, 2, 3, 4, 5], frame1.Value.Payload);

        var frame2 = await FrameReader.ReadAsync(chunkedStream);
        Assert.NotNull(frame2);
        Assert.Equal(20, frame2!.Value.Type);
        Assert.Equal([6, 7, 8, 9, 10, 11], frame2.Value.Payload);

        Assert.Null(await FrameReader.ReadAsync(chunkedStream));
    }

    [Fact]
    public async Task FrameWriter_Ghi_frame_voi_payload_null_van_an_toan()
    {
        var ms = new MemoryStream();
        // default(Frame) có payload là null
        var frame = new Frame(5, null);
        await FrameWriter.WriteAsync(ms, frame);

        ms.Position = 0;
        var readFrame = await FrameReader.ReadAsync(ms);
        Assert.NotNull(readFrame);
        Assert.Equal(5, readFrame!.Value.Type);
        Assert.Empty(readFrame.Value.Payload);
    }

    [Fact]
    public async Task FrameWriter_Ghi_frame_vuot_qua_gioi_han_nem_loi()
    {
        var ms = new MemoryStream();
        var oversizedPayload = new byte[FrameConstants.MaxFrameSize];
        var frame = new Frame(1, oversizedPayload); // 1 byte Type + 1 MB Payload = 1 MB + 1 byte -> Quá giới hạn

        await Assert.ThrowsAsync<InvalidDataException>(() => FrameWriter.WriteAsync(ms, frame));
    }
}

/// <summary>
/// Stream kiểm thử chuyên dụng để giả lập đặc tính phân mảnh mạng TCP.
/// Giới hạn số byte tối đa trả về trong mỗi lần ReadAsync.
/// </summary>
internal sealed class ChunkedReadStream(byte[] data, int maxChunkSize) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => data.Length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= data.Length) return 0;
        int bytesToRead = Math.Min(count, Math.Min(maxChunkSize, data.Length - _position));
        Array.Copy(data, _position, buffer, offset, bytesToRead);
        _position += bytesToRead;
        return bytesToRead;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= data.Length) return ValueTask.FromResult(0);
        int bytesToRead = Math.Min(buffer.Length, Math.Min(maxChunkSize, data.Length - _position));
        data.AsMemory(_position, bytesToRead).CopyTo(buffer);
        _position += bytesToRead;
        return ValueTask.FromResult(bytesToRead);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
