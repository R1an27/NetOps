using System.Buffers.Binary;
using System.Text;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Shared.Contracts.Framing;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử xác thực các hợp đồng giao diện (Interface & Contract Suite) của Message Framing.
/// Kiểm tra IFrameReader, IFrameWriter, IFrameCodec, ISafeFrameChannel, và FramingOptions.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Protocol")]
public class FramingContractTests
{
    [Fact]
    public async Task IFrameWriter_va_IFrameReader_truyen_nhan_qua_interface_chinh_xac()
    {
        IFrameWriter writer = DefaultFrameWriter.Instance;
        IFrameReader reader = DefaultFrameReader.Instance;

        using var ms = new MemoryStream();
        byte[] originalPayload = Encoding.UTF8.GetBytes("Test contract message framing payload");

        // 1. Ghi qua IFrameWriter với ReadOnlyMemory<byte>
        await writer.WriteFrameAsync(ms, originalPayload.AsMemory());

        // 2. Đọc qua IFrameReader
        ms.Position = 0;
        Frame? receivedFrame = await reader.ReadFrameAsync(ms);

        Assert.NotNull(receivedFrame);
        Assert.Equal(originalPayload.Length, receivedFrame.Value.Length);
        Assert.Equal(originalPayload, receivedFrame.Value.Payload);

        // 3. Đọc tiếp sau khi cạn stream phải là Graceful EOF (null)
        var eofFrame = await reader.ReadFrameAsync(ms);
        Assert.Null(eofFrame);
    }

    [Fact]
    public async Task IFrameCodec_ma_hoa_va_giai_ma_qua_interface_chuan_xac()
    {
        IFrameCodec codec = DefaultFrameCodec.Instance;

        var originalMsg = NetOpsMessage.CreateRequest(
            MessageType.Heartbeat,
            new HeartbeatPayload("agent-contract-01", 1728500000000),
            "req-codec-contract-1"
        );

        // 1. Mã hóa thông điệp thành mảng byte
        byte[] frameBytes = codec.EncodeMessage(originalMsg);
        Assert.True(frameBytes.Length > FrameConstants.LengthPrefixSize);

        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(frameBytes.AsSpan(0, 4));
        Assert.Equal(frameBytes.Length - 4, payloadLength);

        // 2. Giải mã thông điệp từ Span
        var decodedMsg = codec.DecodeMessage(frameBytes.AsSpan(4));
        Assert.NotNull(decodedMsg);
        Assert.Equal(originalMsg.MessageId, decodedMsg.MessageId);
        Assert.Equal(MessageType.Heartbeat, decodedMsg.MessageType);
        Assert.Equal("req-codec-contract-1", decodedMsg.RequestId);

        // 3. Ghi và đọc bất đồng bộ qua Stream với IFrameCodec
        using var ms = new MemoryStream();
        await codec.WriteMessageAsync(ms, originalMsg);

        ms.Position = 0;
        var readMsg = await codec.ReadMessageAsync(ms);
        Assert.NotNull(readMsg);
        Assert.Equal(originalMsg.MessageId, readMsg.MessageId);
    }

    [Fact]
    public async Task ISafeFrameChannel_hoat_dong_tron_tru_qua_SafeFrameStream()
    {
        using var ms = new MemoryStream();
        ISafeFrameChannel channel = new SafeFrameStream(ms);

        Assert.True(channel.IsConnected);
        Assert.Same(ms, channel.UnderlyingStream);

        var msg = NetOpsMessage.CreateRequest(MessageType.Ping, "channel-test", "req-ch-01");

        // Gửi qua ISafeFrameChannel
        await channel.SendMessageAsync(msg);

        // Đọc lại qua ISafeFrameChannel
        ms.Position = 0;
        var received = await channel.ReceiveMessageAsync();

        Assert.NotNull(received);
        Assert.Equal(MessageType.Ping, received.MessageType);
        Assert.Equal("channel-test", received.Payload);

        // Gửi mảng nhị phân thô
        ms.Position = 0;
        ms.SetLength(0);
        byte[] rawBytes = Encoding.UTF8.GetBytes("raw-binary-frame-data");
        await channel.SendRawFrameAsync(rawBytes.AsMemory());

        ms.Position = 0;
        var frame = await DefaultFrameReader.Instance.ReadFrameAsync(ms);
        Assert.NotNull(frame);
        Assert.Equal(rawBytes, frame.Value.Payload);
    }

    [Fact]
    public async Task Kha_nang_Mock_Fake_ISafeFrameChannel_cho_Unit_Tests_khong_can_socket()
    {
        // Giả lập một Fake Channel hoàn toàn trong bộ nhớ để test logic mà không mở Socket
        var fakeChannel = new InMemoryFakeChannel();

        var testMessage = NetOpsMessage.CreateRequest(
            MessageType.Command,
            new CommandPayload("cmd-mock-01", "echo Hello NetOps", 10),
            "req-mock-01"
        );

        // Giả lập Client gửi thông điệp vào kênh
        await fakeChannel.SendMessageAsync(testMessage);

        // Giả lập Server đọc thông điệp từ kênh
        var received = await fakeChannel.ReceiveMessageAsync();
        Assert.NotNull(received);
        Assert.Equal("cmd-mock-01", received.GetPayload<CommandPayload>()!.CommandId);
        Assert.Equal("echo Hello NetOps", received.GetPayload<CommandPayload>()!.CommandLine);
    }

    [Fact]
    public async Task FramingOptions_ap_dung_chinh_xac_cac_rang_buoc_kich_thuoc_payload()
    {
        // Thiết lập cấu hình tùy chỉnh: giới hạn tối đa chỉ 50 bytes
        var customOptions = new FramingOptions
        {
            MinPayloadSize = 5,
            MaxPayloadSize = 50
        };

        var customWriter = new DefaultFrameWriter(customOptions);
        var customReader = new DefaultFrameReader(customOptions);

        using var ms = new MemoryStream();

        // 1. Payload nhỏ hơn MinPayloadSize (ví dụ 3 bytes < 5 bytes) -> ném InvalidDataException
        byte[] tooShort = [0x01, 0x02, 0x03];
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await customWriter.WriteFrameAsync(ms, tooShort);
        });

        // 2. Payload lớn hơn MaxPayloadSize (ví dụ 60 bytes > 50 bytes) -> ném InvalidDataException
        byte[] tooLong = new byte[60];
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await customWriter.WriteFrameAsync(ms, tooLong);
        });

        // 3. Payload hợp lệ (ví dụ 20 bytes) -> truyền nhận thành công
        byte[] valid = new byte[20];
        await customWriter.WriteFrameAsync(ms, valid);

        ms.Position = 0;
        var readFrame = await customReader.ReadFrameAsync(ms);
        Assert.NotNull(readFrame);
        Assert.Equal(20, readFrame.Value.Length);
    }

    [Fact]
    public async Task SafeFrameStream_ho_tro_DI_tuy_chinh_IFrameWriter_va_IFrameCodec()
    {
        var customOptions = new FramingOptions { MinPayloadSize = 10, MaxPayloadSize = 100 };
        var customWriter = new DefaultFrameWriter(customOptions);
        var customReader = new DefaultFrameReader(customOptions);
        var customCodec = new DefaultFrameCodec(customReader, customWriter);

        using var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms, customWriter, customCodec);

        // Payload < 10 bytes -> ném InvalidDataException do customWriter áp dụng MinPayloadSize = 10
        byte[] shortBytes = [1, 2, 3];
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await safeStream.SendRawFrameAsync(shortBytes.AsMemory());
        });

        // Payload hợp lệ (10 bytes) -> thành công
        byte[] validBytes = new byte[10];
        await safeStream.SendRawFrameAsync(validBytes.AsMemory());
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task SafeFrameStream_nem_ObjectDisposedException_khi_da_bi_dispose()
    {
        using var ms = new MemoryStream();
        var safeStream = new SafeFrameStream(ms);
        safeStream.Dispose();

        Assert.False(safeStream.IsConnected);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => safeStream.SendMessageAsync(NetOpsMessage.CreateRequest(MessageType.Ping, "test")).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => safeStream.SendRawFrameAsync(new byte[1]).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => safeStream.WriteMessageAsync(NetOpsMessage.CreateRequest(MessageType.Ping, "test")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => safeStream.WriteRawFrameAsync([1]));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => safeStream.ReadMessageAsync());
    }

    [Fact]
    public async Task SafeFrameStream_SendRawFrameAsync_ghi_dong_thoi_nhieu_luong_an_toan_tuyet_doi()
    {
        using var ms = new MemoryStream();
        await using var safeStream = new SafeFrameStream(ms);

        const int taskCount = 30;
        const int payloadSize = 64;

        var tasks = Enumerable.Range(0, taskCount).Select(async i =>
        {
            byte[] payload = new byte[payloadSize];
            Array.Fill(payload, (byte)i);
            await safeStream.SendRawFrameAsync(payload.AsMemory());
        });

        await Task.WhenAll(tasks);

        // Đọc lại tuần tự toàn bộ 30 frames từ stream và xác nhận không có frame nào bị đè hoặc lẫn lộn byte
        ms.Position = 0;
        int framesRead = 0;
        while (true)
        {
            var frame = await DefaultFrameReader.Instance.ReadFrameAsync(ms);
            if (frame == null) break;

            Assert.Equal(payloadSize, frame.Value.Length);
            byte expectedVal = frame.Value.Payload[0];
            for (int k = 1; k < payloadSize; k++)
            {
                Assert.Equal(expectedVal, frame.Value.Payload[k]);
            }
            framesRead++;
        }

        Assert.Equal(taskCount, framesRead);
    }

    #region Helper Mock/Fake Implementation for Unit Test

    /// <summary>
    /// Lớp giả lập ISafeFrameChannel phục vụ Unit Test không cần TCP Socket
    /// </summary>
    private sealed class InMemoryFakeChannel : ISafeFrameChannel
    {
        private readonly Queue<NetOpsMessage> _messages = new();
        private bool _disposed;

        public bool IsConnected => !_disposed;
        public Stream UnderlyingStream => Stream.Null;

        public ValueTask SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _messages.Enqueue(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask<NetOpsMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_messages.TryDequeue(out var msg))
            {
                return ValueTask.FromResult<NetOpsMessage?>(msg);
            }

            return ValueTask.FromResult<NetOpsMessage?>(null);
        }

        public ValueTask SendRawFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            _disposed = true;
            _messages.Clear();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
