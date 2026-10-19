# Tài Liệu Đặc Tả Khảo Sát & Thiết Kế Giao Diện/Hợp Đồng Cho Message Framing

Tài liệu này đặc tả kiến trúc hợp đồng (Interface & Contract Design) cho tầng **Message Framing** nhị phân thuộc **NetOps Protocol v0**. Thiết kế nhằm nâng cấp hệ thống từ mô hình phụ thuộc tĩnh sang kiến trúc hướng giao diện (Interface-driven), hỗ trợ Dependency Injection (DI-friendly), dễ dàng viết Unit Test (Mockable), tối ưu hóa bộ nhớ (`ReadOnlyMemory<byte>`, `ValueTask`), và đảm bảo tương thích ngược 100%.

---

## 1. Bối Cảnh & Mục Tiêu Thiết Kế

### 1.1. Bối Cảnh Hệ Thống
Hệ thống NetOps sử dụng giao thức TCP hướng dòng byte (Byte Stream). Để giải quyết triệt để vấn đề dính gói (sticky packets) và phân mảnh (fragmentation), dự án đã hiện thực hóa kỹ thuật Length-Prefixed Framing chuẩn nhị phân:
```
+--------------------------------+-----------------------------------+
|      Frame Length (4 Bytes)    |         Payload Data (N Bytes)    |
|   Big-Endian (32-bit signed)   |   (1 <= N <= 16 MB = 16,777,216)  |
+--------------------------------+-----------------------------------+
```

### 1.2. Mục Tiêu Nâng Cấp Hợp Đồng Giao Diện
1. **Trừu tượng hóa & Dependency Injection:** Cung cấp các Interface chuẩn mực để các service ở tầng ứng dụng (`TcpManagerServer`, `TcpAgentClient`) phụ thuộc vào abstraction thay vì concrete/static classes.
2. **Khả năng kiểm thử (Mockability):** Cho phép lập trình viên viết Unit Test kiểm thử logic nghiệp vụ bằng cách Mock kênh truyền hoặc Codec mà không cần mở kết nối TCP socket thật.
3. **Hiệu năng & Tối ưu cấp phát bộ nhớ (Zero/Low Allocation):** Hỗ trợ `ReadOnlyMemory<byte>`, `ReadOnlySpan<byte>` và kiểu trả về `ValueTask` để loại bỏ chi phí heap allocation của `Task` và mảng byte khi đọc dữ liệu có sẵn trong bộ đệm.
4. **Bảo toàn tính tương thích ngược (The One-Version Rule):** Đảm bảo toàn bộ các phương thức static hiện có (`FrameReader.ReadAsync`, `FrameWriter.WriteAsync`, `FrameCodec.*`) tiếp tục hoạt động bình thường như các facade tiện ích.

---

## 2. Khảo Sát & Đánh Giá Hiện Trạng (As-Is vs To-Be)

| Tiêu Chí Đánh Giá | Hiện Trạng (As-Is) | Thiết Kế Mục Tiêu (To-Be) | Lợi Ích Mang Lại |
| :--- | :--- | :--- | :--- |
| **Tính đóng gói** | Static classes (`FrameReader`, `FrameWriter`, `FrameCodec`) | Interfaces (`IFrameReader`, `IFrameWriter`, `IFrameCodec`) | Tuân thủ SOLID (Interface Segregation & Dependency Inversion) |
| **Kiểm thử (Testing)** | Buộc phải dùng `Stream` thật / `MemoryStream` | Dễ dàng Mock qua NSubstitute/Moq hoặc Fake in-memory | Viết Unit Test nhanh, độc lập, không rò rỉ tài nguyên mạng |
| **Kiểu bất đồng bộ** | `Task` / `Task<Frame?>` | `ValueTask` / `ValueTask<Frame?>` | Tiết kiệm cấp phát đối tượng `Task` trên Heap khi dữ liệu sẵn sàng |
| **Quản lý bộ nhớ** | Luôn cấp phát mảng mới `byte[]` | Hỗ trợ `ReadOnlyMemory<byte>` & Buffer Pooling | Giảm áp lực lên Garbage Collector (GC LOH) |
| **Phiên truyền thông** | Khởi tạo cụ thể `SafeFrameStream` | Trừu tượng hóa qua `ISafeFrameChannel` | Linh hoạt thay thế transport (TCP, NamedPipe, WebSocket, Memory) |

---

## 3. Kiến Trúc Hợp Đồng Mục Tiêu (Contract Architecture)

```
                       [ Tầng Ứng Dụng / Application Services ]
                           TcpManagerServer / TcpAgentClient
                                          │
                                          ▼
                      ┌───────────────────────────────────────┐
                      │          ISafeFrameChannel            │
                      │  - SendMessageAsync(NetOpsMessage)    │
                      │  - ReceiveMessageAsync()              │
                      │  - SendRawFrameAsync(Memory<byte>)    │
                      └───────────────────┬───────────────────┘
                                          │
                                          ▼
                      ┌───────────────────────────────────────┐
                      │             IFrameCodec               │
                      │  - EncodeMessage(NetOpsMessage)       │
                      │  - DecodeMessage(ReadOnlySpan<byte>)  │
                      └───────────────────┬───────────────────┘
                                          │
                     ┌────────────────────┴────────────────────┐
                     ▼                                         ▼
        ┌─────────────────────────┐               ┌─────────────────────────┐
        │       IFrameReader      │               │       IFrameWriter      │
        │ - ReadFrameAsync()      │               │ - WriteFrameAsync()     │
        │ - ReadPayloadMemory()   │               │ - WriteRawMemory()      │
        └────────────┬────────────┘               └────────────┬────────────┘
                     │                                         │
                     └────────────────────┬────────────────────┘
                                          │
                                          ▼
                          [ Byte Stream: Network / Pipe / Memory ]
```

---

## 4. Đặc Tả Chi Tiết Các Interface Hợp Đồng

### 4.1. `IFrameReader` - Hợp Đồng Đọc Khung Nhị Phân
Chịu trách nhiệm đọc từ luồng byte và tái tạo nguyên vẹn ranh giới Frame nhị phân:
```csharp
namespace Shared.Contracts.Framing;

/// <summary>
/// Hợp đồng đọc khung dữ liệu TCP nhị phân theo chuẩn Length-Prefixed Framing.
/// Xử lý triệt để hiện tượng dính gói (sticky packets) và phân mảnh (fragmentation).
/// </summary>
public interface IFrameReader
{
    /// <summary>
    /// Đọc một frame hoàn chỉnh từ stream bất đồng bộ.
    /// Trả về null nếu stream đóng sạch trước khi nhận byte nào (Graceful EOF).
    /// </summary>
    ValueTask<Frame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc payload của frame dưới dạng vùng nhớ ReadOnlyMemory để tối ưu hóa bộ nhớ đệm.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>?> ReadPayloadMemoryAsync(Stream stream, CancellationToken cancellationToken = default);
}
```

### 4.2. `IFrameWriter` - Hợp Đồng Ghi Khung Nhị Phân
Chịu trách nhiệm đóng gói nguyên khối `[4 Bytes Length][Payload Data]` và ghi atomic ra stream:
```csharp
namespace Shared.Contracts.Framing;

/// <summary>
/// Hợp đồng ghi khung dữ liệu TCP nhị phân theo cấu trúc Length-Prefixed Framing.
/// </summary>
public interface IFrameWriter
{
    /// <summary>
    /// Ghi frame ra stream bất đồng bộ nguyên khối (atomic write) kèm tiền tố 4 byte Big-Endian.
    /// </summary>
    ValueTask WriteFrameAsync(Stream stream, Frame frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi trực tiếp một vùng nhớ payload nhị phân ra stream.
    /// </summary>
    ValueTask WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}
```

### 4.3. `IFrameCodec` - Hợp Đồng Cầu Nối Mã Hóa/Giải Mã Ứng Dụng
Đóng vai trò phiên dịch giữa tầng nhị phân và đối tượng `NetOpsMessage`:
```csharp
namespace ServerManager.Shared.Contracts.Protocol;

using ServerManager.Shared.Contracts.Models;
using Shared.Contracts.Framing;

/// <summary>
/// Hợp đồng mã hóa và giải mã đối tượng thông điệp NetOpsMessage qua tầng Framing.
/// </summary>
public interface IFrameCodec
{
    /// <summary>
    /// Mã hóa đối tượng NetOpsMessage thành mảng byte frame hoàn chỉnh [Header 4B][JSON UTF-8].
    /// </summary>
    byte[] EncodeMessage(NetOpsMessage message);

    /// <summary>
    /// Giải mã mảng byte payload JSON thành đối tượng NetOpsMessage.
    /// </summary>
    NetOpsMessage DecodeMessage(ReadOnlySpan<byte> framePayload);

    /// <summary>
    /// Ghi một NetOpsMessage ra stream bất đồng bộ qua tầng framing.
    /// </summary>
    ValueTask WriteMessageAsync(Stream stream, NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc một NetOpsMessage từ stream bất đồng bộ. Trả về null nếu ngắt kết nối an toàn (Graceful EOF).
    /// </summary>
    ValueTask<NetOpsMessage?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken = default);
}
```

### 4.4. `ISafeFrameChannel` - Hợp Đồng Kênh Truyền Hai Chiều An Toàn Luồng
Đại diện cho phiên kết nối hoàn chỉnh hỗ trợ truyền nhận song công (Full-Duplex):
```csharp
namespace ServerManager.Shared.Contracts.Protocol;

using ServerManager.Shared.Contracts.Models;

/// <summary>
/// Hợp đồng kênh truyền thông điệp hai chiều an toàn đa luồng trên nền TCP Full-Duplex.
/// Tự động đồng bộ tuần tự hóa ghi (Write Serialization) để ngăn ngừa trộn lẫn byte.
/// </summary>
public interface ISafeFrameChannel : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Trạng thái kết nối của kênh truyền.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Luồng dữ liệu cơ sở bên dưới kênh truyền (nếu có).
    /// </summary>
    Stream UnderlyingStream { get; }

    /// <summary>
    /// Gửi một NetOpsMessage ra kênh truyền an toàn đa luồng.
    /// </summary>
    ValueTask SendMessageAsync(NetOpsMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc một NetOpsMessage từ kênh truyền. Trả về null nếu đối phương đóng kết nối sạch sẽ (Graceful EOF).
    /// </summary>
    ValueTask<NetOpsMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi trực tiếp một khung dữ liệu nhị phân thô ra kênh truyền an toàn đa luồng.
    /// </summary>
    ValueTask SendRawFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}
```

### 4.5. `FramingOptions` - Cấu Hình Ràng Buộc Khung Dữ Liệu
Chứa các chính sách kiểm soát an toàn và tài nguyên:
```csharp
namespace Shared.Contracts.Framing;

/// <summary>
/// Tùy chọn cấu hình các tham số và giới hạn cho bộ xử lý khung dữ liệu TCP Framing.
/// </summary>
public sealed class FramingOptions
{
    /// <summary>
    /// Kích thước payload tối thiểu cho phép (mặc định 1 byte).
    /// </summary>
    public int MinPayloadSize { get; set; } = FrameConstants.MinPayloadSize;

    /// <summary>
    /// Kích thước payload tối đa cho phép để phòng chống tràn RAM/DoS (mặc định 16 MB).
    /// </summary>
    public int MaxPayloadSize { get; set; } = FrameConstants.MaxPayloadSize;

    /// <summary>
    /// Kích thước bộ đệm đọc ban đầu (tùy chọn tối ưu hóa).
    /// </summary>
    public int InitialBufferSize { get; set; } = 4096;

    /// <summary>
    /// Tùy chọn sử dụng bộ đệm tái sử dụng ArrayPool để hạn chế cấp phát bộ nhớ.
    /// </summary>
    public bool UseBufferPooling { get; set; } = false;
}
```

---

## 5. Quy Tắc Xử Lý Lỗi & Ranh Giới Hành Vi (Hyrum's Law)

Các hành vi quan sát được (observable behaviors) được cam kết trong hợp đồng:

| Tình Huống | Hành Vi Cam Kết Trong Hợp Đồng | Ngoại Lệ Phát Sinh |
| :--- | :--- | :--- |
| **Stream đóng sạch ở ranh giới frame** | Phương thức đọc trả về `null` | Không ném ngoại lệ |
| **Stream bị ngắt khi đang đọc dở 4B header** | Đứt kết nối bất thường giữa chừng | Ném `EndOfStreamException` |
| **Stream bị ngắt khi đang đọc dở payload** | Đứt kết nối bất thường giữa chừng | Ném `EndOfStreamException` |
| **Độ dài frame khai báo < 1 hoặc > 16 MB** | Vi phạm ràng buộc kích thước giao thức | Ném `InvalidDataException` |
| **Gọi thao tác khi kênh đã Dispose** | Kênh đã đóng tài nguyên | Ném `ObjectDisposedException` |
| **Tham số stream hoặc message bị null** | Tham số không hợp lệ | Ném `ArgumentNullException` |
| **CancellationToken bị kích hoạt** | Tác vụ bị hủy bởi caller | Ném `OperationCanceledException` |

---

## 6. Lộ Trình Triển Khai & Đảm Bảo Tương Thích Ngược

1. **Giai đoạn 1 (Phase 2):** Định nghĩa các file Interface mới trong `Shared.Contracts`:
   - `IFrameReader.cs`, `IFrameWriter.cs`, `FramingOptions.cs` trong thư mục `Framing/`.
   - `IFrameCodec.cs`, `ISafeFrameChannel.cs` trong thư mục `Protocol/`.
2. **Giai đoạn 2 (Phase 3):** Hiện thực hóa Default Implementations:
   - `DefaultFrameReader`: implement `IFrameReader`.
   - `DefaultFrameWriter`: implement `IFrameWriter`.
   - `DefaultFrameCodec`: implement `IFrameCodec`.
   - `SafeFrameStream`: implement `ISafeFrameChannel`.
   - Các class static `FrameReader`, `FrameWriter`, `FrameCodec` giữ nguyên chữ ký hàm, đóng vai trò Facade tiện ích gọi sang các default instances.
3. **Giai đoạn 3 (Testing):** Bổ sung bộ Unit Tests `FramingContractTests.cs` kiểm chứng:
   - Mock kênh `ISafeFrameChannel` thành công trong ngữ cảnh DI.
   - Các phương thức `ValueTask` và `ReadOnlyMemory<byte>` xử lý chính xác dữ liệu nhị phân.
   - Toàn bộ 52 test cases cũ giữ nguyên kết quả pass 100%.
