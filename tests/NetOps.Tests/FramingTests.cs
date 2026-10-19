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
            await FrameWriter.WriteAsync(ms, new Frame([i, i, i]));
        }

        ms.Position = 0;
        for (byte i = 1; i <= 3; i++)
        {
            var f = await FrameReader.ReadAsync(ms);
            Assert.NotNull(f);
            Assert.Equal([i, i, i], f.Value.Payload);
            Assert.Equal(3, f.Value.Length);
        }

        Assert.Null(await FrameReader.ReadAsync(ms)); // Hết dữ liệu (Graceful EOF)
    }

    [Fact]
    public async Task Ghi_va_doc_frame_kich_thuoc_toi_thieu_1_byte()
    {
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, new Frame([0xFF]));

        ms.Position = 0;
        var f = await FrameReader.ReadAsync(ms);

        Assert.NotNull(f);
        Assert.Single(f.Value.Payload);
        Assert.Equal((byte)0xFF, f.Value.Payload[0]);
        Assert.Equal(1, f.Value.Length);
        Assert.Null(await FrameReader.ReadAsync(ms));
    }

    [Fact]
    public async Task Payload_rong_0_byte_khi_ghi_thi_nem_loi()
    {
        var ms = new MemoryStream();
        // Frame rỗng vi phạm MinPayloadSize = 1 byte
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameWriter.WriteAsync(ms, new Frame([])));
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameWriter.WriteAsync(ms, new Frame(null)));
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameWriter.WriteAsync(ms, Array.Empty<byte>()));
    }

    [Fact]
    public async Task Do_dai_bang_0_khi_doc_thi_nem_loi()
    {
        byte[] zeroLengthHeader = new byte[4] { 0, 0, 0, 0 };
        var ms = new MemoryStream(zeroLengthHeader);
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameReader.ReadAsync(ms));
    }

    [Fact]
    public async Task Do_dai_am_khi_doc_thi_nem_loi()
    {
        byte[] negativeLengthHeader = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(negativeLengthHeader, -100);

        var ms = new MemoryStream(negativeLengthHeader);
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameReader.ReadAsync(ms));
    }

    [Fact]
    public async Task Do_dai_vuot_gioi_han_16MB_khi_doc_thi_nem_loi()
    {
        // Ghi header với length > MaxPayloadSize (16 MB)
        byte[] invalidHeader = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(invalidHeader, FrameConstants.MaxPayloadSize + 1);

        var ms = new MemoryStream(invalidHeader);
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameReader.ReadAsync(ms));
    }

    [Fact]
    public async Task FrameWriter_Ghi_frame_vuot_qua_gioi_han_16MB_nem_loi()
    {
        var ms = new MemoryStream();
        var oversizedPayload = new byte[FrameConstants.MaxPayloadSize + 1];
        var frame = new Frame(oversizedPayload);

        await Assert.ThrowsAsync<InvalidDataException>(() => FrameWriter.WriteAsync(ms, frame));
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameWriter.WriteAsync(ms, oversizedPayload));
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
        await FrameWriter.WriteAsync(ms, new Frame(new byte[100]));

        // Cắt bớt dữ liệu chỉ còn 50 byte (4 byte header + 46 byte payload)
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
        await FrameWriter.WriteAsync(ms, new Frame([1, 2, 3, 4, 5]));
        await FrameWriter.WriteAsync(ms, new Frame([6, 7, 8, 9, 10, 11]));

        // Giả lập TCP Stream bị phân mảnh nặng (chỉ trả về tối đa 2 bytes mỗi lệnh Read)
        var chunkedStream = new ChunkedReadStream(ms.ToArray(), maxChunkSize: 2);

        var frame1 = await FrameReader.ReadAsync(chunkedStream);
        Assert.NotNull(frame1);
        Assert.Equal([1, 2, 3, 4, 5], frame1!.Value.Payload);

        var frame2 = await FrameReader.ReadAsync(chunkedStream);
        Assert.NotNull(frame2);
        Assert.Equal([6, 7, 8, 9, 10, 11], frame2!.Value.Payload);

        Assert.Null(await FrameReader.ReadAsync(chunkedStream));
    }

    [Fact]
    public async Task FrameReader_ReadPayloadAsync_hoat_dong_chinh_xac()
    {
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, [10, 20, 30, 40]);

        ms.Position = 0;
        byte[]? payload = await FrameReader.ReadPayloadAsync(ms);

        Assert.NotNull(payload);
        Assert.Equal([10, 20, 30, 40], payload);
    }

    [Fact]
    public async Task FrameWriter_WriteAsync_overload_voi_mang_byte_tien_loi()
    {
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, [100, 101, 102]);

        ms.Position = 0;
        var frame = await FrameReader.ReadAsync(ms);

        Assert.NotNull(frame);
        Assert.Equal([100, 101, 102], frame!.Value.Payload);
    }

    [Fact]
    public async Task FrameWriter_Ghi_voi_ReadOnlyMemory_hoat_dong_chinh_xac()
    {
        var ms = new MemoryStream();
        ReadOnlyMemory<byte> memory = new byte[] { 11, 22, 33, 44 };
        await FrameWriter.WriteAsync(ms, memory);

        ms.Position = 0;
        var frame = await FrameReader.ReadAsync(ms);

        Assert.NotNull(frame);
        Assert.Equal([11, 22, 33, 44], frame!.Value.Payload);
    }

    [Fact]
    public async Task FrameWriter_Ghi_ra_stream_dung_dinh_dang_BigEndian_4_bytes_header()
    {
        var ms = new MemoryStream();
        // Payload có độ dài 258 bytes = 0x00000102
        var payload = new byte[258];
        payload[0] = 0xAA;
        payload[257] = 0xBB;

        await FrameWriter.WriteAsync(ms, payload);

        byte[] rawBytes = ms.ToArray();
        Assert.Equal(4 + 258, rawBytes.Length);

        // Kiểm tra đúng 4 bytes Big-Endian
        Assert.Equal(0x00, rawBytes[0]);
        Assert.Equal(0x00, rawBytes[1]);
        Assert.Equal(0x01, rawBytes[2]);
        Assert.Equal(0x02, rawBytes[3]);

        // Kiểm tra payload
        Assert.Equal(0xAA, rawBytes[4]);
        Assert.Equal(0xBB, rawBytes[4 + 257]);
    }

    [Fact]
    public async Task DefaultFrameWriter_va_FrameWriter_sinh_ra_byte_stream_giong_nhau_hoan_toan()
    {
        var ms = new MemoryStream();
        var ms2 = new MemoryStream();
        byte[] testPayload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        await FrameWriter.WriteAsync(ms, testPayload);
        await DefaultFrameWriter.Instance.WriteFrameAsync(ms2, testPayload);

        Assert.Equal(ms.ToArray(), ms2.ToArray());
    }

    [Fact]
    public async Task FrameWriter_Ghi_tham_so_null_nem_ArgumentNullException()
    {
        var ms = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentNullException>(() => FrameWriter.WriteAsync(null!, new Frame([1])));
        await Assert.ThrowsAsync<ArgumentNullException>(() => FrameWriter.WriteAsync(null!, [1]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => FrameWriter.WriteAsync(ms, (byte[])null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => FrameWriter.WriteAsync(null!, new ReadOnlyMemory<byte>([1])));
    }

    [Fact]
    public async Task FrameReader_ReadPayloadMemoryAsync_hoat_dong_chinh_xac()
    {
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, [55, 66, 77, 88]);

        ms.Position = 0;
        var mem = await FrameReader.ReadPayloadMemoryAsync(ms);

        Assert.NotNull(mem);
        Assert.Equal([55, 66, 77, 88], mem.Value.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FrameReader_Stream_chi_co_1_den_3_bytes_header_roi_ngat_thi_nem_EndOfStreamException(int partialBytes)
    {
        byte[] truncatedHeader = new byte[partialBytes];
        var ms = new MemoryStream(truncatedHeader);

        var ex = await Assert.ThrowsAsync<EndOfStreamException>(() => FrameReader.ReadAsync(ms));
        Assert.Contains($"đã nhận {partialBytes}/4 bytes", ex.Message);
    }

    [Fact]
    public async Task FrameReader_Phan_manh_1_byte_moi_lan_doc_cho_nhieu_frame()
    {
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, [1, 2]);
        await FrameWriter.WriteAsync(ms, [3, 4, 5]);

        // Giả lập cực hạn: mỗi lần đọc chỉ nhả đúng 1 byte
        var chunkedStream = new ChunkedReadStream(ms.ToArray(), maxChunkSize: 1);

        var f1 = await FrameReader.ReadAsync(chunkedStream);
        Assert.NotNull(f1);
        Assert.Equal([1, 2], f1.Value.Payload);

        var f2 = await FrameReader.ReadAsync(chunkedStream);
        Assert.NotNull(f2);
        Assert.Equal([3, 4, 5], f2.Value.Payload);

        Assert.Null(await FrameReader.ReadAsync(chunkedStream));
    }

    [Fact]
    public async Task FrameReader_Doc_tham_so_null_nem_ArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => FrameReader.ReadAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => FrameReader.ReadPayloadAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => FrameReader.ReadPayloadMemoryAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => DefaultFrameReader.Instance.ReadFrameAsync(null!).AsTask());
    }

    [Fact]
    public async Task DefaultFrameReader_va_FrameReader_doc_ra_ket_qua_giong_nhau()
    {
        var ms1 = new MemoryStream();
        var ms2 = new MemoryStream();
        byte[] payload = [9, 8, 7, 6, 5];

        await FrameWriter.WriteAsync(ms1, payload);
        await FrameWriter.WriteAsync(ms2, payload);

        ms1.Position = 0;
        ms2.Position = 0;

        var f1 = await FrameReader.ReadAsync(ms1);
        var f2 = await DefaultFrameReader.Instance.ReadFrameAsync(ms2);

        Assert.NotNull(f1);
        Assert.NotNull(f2);
        Assert.Equal(f1.Value.Payload, f2.Value.Payload);
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
