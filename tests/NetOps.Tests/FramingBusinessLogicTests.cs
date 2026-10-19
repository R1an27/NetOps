using System.Buffers.Binary;
using System.Text;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Protocol;
using Shared.Contracts.Framing;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử chuyên sâu cho Logic Nghiệp Vụ Giao Thức Length-Prefix Framing 4-byte Big-Endian.
/// Bao gồm: Endianness Validation, Phân mảnh cực hạn (Irregular Fragmentation),
/// Dính gói hàng loạt (Sticky Packets), Chịu tải đồng thời (Concurrent Stress), và Mô phỏng tấn công biên.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Protocol")]
public class FramingBusinessLogicTests
{
    #region 1. Endianness Verification (Network Byte Order)

    [Theory]
    [InlineData(1, new byte[] { 0x00, 0x00, 0x00, 0x01 })]
    [InlineData(258, new byte[] { 0x00, 0x00, 0x01, 0x02 })]
    [InlineData(65536, new byte[] { 0x00, 0x01, 0x00, 0x00 })]
    [InlineData(16777216, new byte[] { 0x01, 0x00, 0x00, 0x00 })]
    public async Task Wire_Format_Header_luon_ghi_dung_chuan_BigEndian(int length, byte[] expectedHeaderBytes)
    {
        var ms = new MemoryStream();
        byte[] payload = new byte[length];
        payload[0] = 0xAA;
        payload[^1] = 0x55;

        await DefaultFrameWriter.Instance.WriteFrameAsync(ms, payload);

        byte[] rawBytes = ms.ToArray();
        Assert.Equal(4 + length, rawBytes.Length);

        // Kiểm tra đúng từng byte trong 4 byte header
        Assert.Equal(expectedHeaderBytes[0], rawBytes[0]);
        Assert.Equal(expectedHeaderBytes[1], rawBytes[1]);
        Assert.Equal(expectedHeaderBytes[2], rawBytes[2]);
        Assert.Equal(expectedHeaderBytes[3], rawBytes[3]);

        // Đọc lại bằng BinaryPrimitives để xác minh Network Byte Order
        int readLength = BinaryPrimitives.ReadInt32BigEndian(rawBytes.AsSpan(0, 4));
        Assert.Equal(length, readLength);
    }

    [Fact]
    public async Task LittleEndian_Header_se_bi_tu_choi_hoac_nhan_sai_neu_khong_tuan_thu_BigEndian()
    {
        // Giả sử có bên đối tác gửi nhầm số 258 theo Little-Endian: [0x02, 0x01, 0x00, 0x00]
        byte[] littleEndianHeader = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(littleEndianHeader, 258);

        // Khi đọc theo chuẩn Big-Endian của protocol: 0x02010000 = 33,619,968 bytes (> 16MB)
        // Hệ thống phải phát hiện vi phạm và ném InvalidDataException ngay lập tức
        var ms = new MemoryStream(littleEndianHeader);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => DefaultFrameReader.Instance.ReadFrameAsync(ms).AsTask());
        Assert.Contains("không hợp lệ", ex.Message);
    }

    #endregion

    #region 2. Extreme TCP Fragmentation & Packet Splitting

    [Fact]
    public async Task Phan_manh_bien_thien_ngau_nhien_1_den_3_byte_van_doc_tron_ven_moi_frame()
    {
        var ms = new MemoryStream();
        var expectedFrames = new List<byte[]>();

        // Chuẩn bị 10 frames với kích thước khác nhau từ nhỏ đến vừa
        int[] sizes = [1, 2, 3, 4, 15, 32, 64, 127, 256, 512];
        for (int i = 0; i < sizes.Length; i++)
        {
            byte[] frameData = new byte[sizes[i]];
            Array.Fill(frameData, (byte)(i + 1));
            expectedFrames.Add(frameData);
            await DefaultFrameWriter.Instance.WriteFrameAsync(ms, frameData);
        }

        // Tạo IrregularReadStream biến thiên: lần đọc thứ k chỉ trả về (k % 3) + 1 byte
        var irregularStream = new IrregularChunkReadStream(ms.ToArray());

        for (int i = 0; i < expectedFrames.Count; i++)
        {
            var frame = await DefaultFrameReader.Instance.ReadFrameAsync(irregularStream);
            Assert.NotNull(frame);
            Assert.Equal(expectedFrames[i].Length, frame.Value.Length);
            Assert.Equal(expectedFrames[i], frame.Value.Payload);
        }

        // Cuối cùng phải là Graceful EOF (null)
        Assert.Null(await DefaultFrameReader.Instance.ReadFrameAsync(irregularStream));
    }

    #endregion

    #region 3. Sticky Packets (Dính Gói Liên Tiếp Hàng Loạt)

    [Fact]
    public async Task Dinh_goi_50_frames_lien_tiep_khong_bi_lech_frame_boundary()
    {
        var ms = new MemoryStream();
        const int totalFrames = 50;

        for (int i = 1; i <= totalFrames; i++)
        {
            byte[] payload = Encoding.UTF8.GetBytes($"sticky-packet-payload-sequence-{i:D3}");
            await DefaultFrameWriter.Instance.WriteFrameAsync(ms, payload);
        }

        // Đọc lại toàn bộ trên cùng một stream liên tục
        ms.Position = 0;
        for (int i = 1; i <= totalFrames; i++)
        {
            var frame = await DefaultFrameReader.Instance.ReadFrameAsync(ms);
            Assert.NotNull(frame);
            string text = Encoding.UTF8.GetString(frame.Value.Payload);
            Assert.Equal($"sticky-packet-payload-sequence-{i:D3}", text);
        }

        // Kiểm tra sau frame cuối cùng là kết thúc luồng sạch
        Assert.Null(await DefaultFrameReader.Instance.ReadFrameAsync(ms));
    }

    #endregion

    #region 4. Concurrent Write Stress & Serialization

    [Fact]
    public async Task Ap_luc_ghi_dong_thoi_60_tasks_qua_SafeFrameStream_khong_bi_tron_byte()
    {
        using var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);

        const int taskCount = 60;
        var tasks = Enumerable.Range(0, taskCount).Select(async i =>
        {
            var msg = NetOpsMessage.CreateRequest(
                MessageType.Command,
                $"concurrent-stress-data-{i:D4}",
                $"req-stress-{i:D4}"
            );
            await safeStream.SendMessageAsync(msg);
        });

        await Task.WhenAll(tasks);

        // Đọc lại tuần tự toàn bộ 60 frames từ stream
        ms.Position = 0;
        var receivedRequestIds = new HashSet<string>();

        for (int i = 0; i < taskCount; i++)
        {
            var msg = await safeStream.ReceiveMessageAsync();
            Assert.NotNull(msg);
            Assert.Equal(MessageType.Command, msg.MessageType);
            Assert.NotNull(msg.RequestId);
            Assert.True(receivedRequestIds.Add(msg.RequestId));
        }

        Assert.Equal(taskCount, receivedRequestIds.Count);
        Assert.Null(await safeStream.ReceiveMessageAsync());
    }

    #endregion

    #region 5. Boundary & Attack Simulation

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    [InlineData(FrameConstants.MaxPayloadSize + 1)]
    [InlineData(FrameConstants.MaxPayloadSize + 5000)]
    [InlineData(int.MaxValue)]
    public async Task Cac_gia_tri_do_dai_header_vi_pham_deu_nem_InvalidDataException(int invalidLength)
    {
        byte[] headerBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(headerBytes, invalidLength);

        var ms = new MemoryStream(headerBytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => DefaultFrameReader.Instance.ReadFrameAsync(ms).AsTask());
    }

    [Fact]
    public async Task Khai_bao_payload_1000_byte_nhung_ngat_ket_noi_khi_moi_gui_999_byte_nem_EndOfStreamException()
    {
        var ms = new MemoryStream();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, 1000);
        await ms.WriteAsync(header);

        byte[] partialPayload = new byte[999]; // Thiếu 1 byte
        await ms.WriteAsync(partialPayload);

        ms.Position = 0;
        var ex = await Assert.ThrowsAsync<EndOfStreamException>(() => DefaultFrameReader.Instance.ReadFrameAsync(ms).AsTask());
        Assert.Contains("999/1000 bytes", ex.Message);
    }

    [Fact]
    public async Task Khai_bao_payload_qua_IFrameWriter_vuot_nguong_16MB_nem_InvalidDataException()
    {
        var ms = new MemoryStream();
        var oversizedPayload = new byte[FrameConstants.MaxPayloadSize + 1];

        await Assert.ThrowsAsync<InvalidDataException>(() => DefaultFrameWriter.Instance.WriteFrameAsync(ms, oversizedPayload).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => DefaultFrameWriter.Instance.WriteFrameAsync(ms, oversizedPayload.AsMemory()).AsTask());
    }

    #endregion
}

/// <summary>
/// Stream kiểm thử chuyên dụng mô phỏng biến thiên số byte trả về trong mỗi lần Read:
/// Lần 1: 1 byte, Lần 2: 2 bytes, Lần 3: 3 bytes, Lần 4: 1 byte,...
/// </summary>
internal sealed class IrregularChunkReadStream(byte[] data) : Stream
{
    private int _position;
    private int _readCount;

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

        int chunkSize = (_readCount % 3) + 1; // 1, 2, 3 xoay vòng
        _readCount++;

        int bytesToRead = Math.Min(count, Math.Min(chunkSize, data.Length - _position));
        Array.Copy(data, _position, buffer, offset, bytesToRead);
        _position += bytesToRead;
        return bytesToRead;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= data.Length) return ValueTask.FromResult(0);

        int chunkSize = (_readCount % 3) + 1; // 1, 2, 3 xoay vòng
        _readCount++;

        int bytesToRead = Math.Min(buffer.Length, Math.Min(chunkSize, data.Length - _position));
        data.AsMemory(_position, bytesToRead).CopyTo(buffer);
        _position += bytesToRead;
        return ValueTask.FromResult(bytesToRead);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
