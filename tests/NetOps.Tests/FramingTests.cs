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
            await FrameWriter.WriteAsync(ms, new Frame(i, new byte[] { i, i, i }));

        ms.Position = 0;
        for (byte i = 1; i <= 3; i++)
        {
            var f = await FrameReader.ReadAsync(ms);
            Assert.NotNull(f);
            Assert.Equal(i, f!.Value.Type);
            Assert.Equal(new byte[] { i, i, i }, f.Value.Payload);
        }
        Assert.Null(await FrameReader.ReadAsync(ms)); // hết dữ liệu
    }

    [Fact]
    public async Task Do_dai_vuot_gioi_han_thi_nem_loi()
    {
        var ms = new MemoryStream(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameReader.ReadAsync(ms));
    }

    [Fact]
    public async Task Stream_bi_cat_giua_chung_thi_nem_loi()
    {
        var ms = new MemoryStream();
        await FrameWriter.WriteAsync(ms, new Frame(1, new byte[100]));
        var cut = new MemoryStream(ms.ToArray()[..50]); // cắt còn 50 byte
        await Assert.ThrowsAsync<EndOfStreamException>(() => FrameReader.ReadAsync(cut));
    }
}
