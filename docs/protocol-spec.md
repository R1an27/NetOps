# Đặc Tả Giao Thức Message Framing (TCP Length-Prefixed Protocol)

Tài liệu này đặc tả cơ chế **Message Framing** nhị phân tầng thấp theo chuẩn **NetOps Protocol v0**, được thiết kế để bảo toàn ranh giới giữa các thông điệp khi truyền tải liên tục qua dòng byte TCP giữa Manager và Agent.

> [!NOTE]
> Tài liệu này tập trung vào tầng khung nhị phân (Binary Framing Layer). Để xem đầy đủ đặc tả tầng ứng dụng (Envelope, danh mục thông điệp, xác thực, nhịp tim và streaming lệnh), vui lòng tham chiếu [docs/specifications/protocol-v0-spec.md](specifications/protocol-v0-spec.md).

---

## 1. Bối Cảnh & Vấn Đề (Problem Statement)

Giao thức truyền vận TCP (Transmission Control Protocol) là giao thức hướng dòng byte liên tục (**Byte Stream**) không bảo toàn ranh giới bản tin:
1. **Dính gói (Packet Coalescing / Sticky Packets):** Khi phía gửi phát đi nhiều message liên tiếp hoặc mạng kích hoạt thuật toán Nagle, các message có thể bị gộp lại và bên nhận đọc được trong cùng một lệnh đọc `ReadAsync`.
2. **Phân mảnh gói (Packet Fragmentation):** Một message có kích thước lớn có thể bị chia thành nhiều phân đoạn TCP (TCP segments) dựa trên MTU/MSS của mạng, khiến bên nhận phải thực hiện nhiều lần `ReadAsync` mới thu thập đủ một message nguyên vẹn.

Để phân định ranh giới và độ dài chính xác của từng bản tin, NetOps Protocol v0 chuẩn hóa kỹ thuật **Length-Prefixed Framing**.

---

## 2. Cấu Trúc Khung Nhị Phân (Binary Frame Structure)

Mỗi Frame được truyền qua luồng TCP tuân thủ cấu trúc nhị phân chuẩn hóa như sau:

```
+--------------------------------+-----------------------------------+
|      Frame Length (4 Bytes)    |         Payload Data (N Bytes)    |
|   Big-Endian (32-bit signed)   |   (1 <= N <= 16 MB = 16,777,216)  |
+--------------------------------+-----------------------------------+
|<------- Header (4 bytes) ----->|<------- Frame Body (Length bytes)->|
|<---------------- Total Frame on Wire (4 + Length bytes) ----------->|
```

### Chi Tiết Các Trường:

| Trường | Kích Thước | Kiểu Dữ Liệu | Byte Order | Mô Tả |
| :--- | :---: | :---: | :---: | :--- |
| **Length** | 4 Bytes | `int32` (Signed) | **Big-Endian** | Độ dài chính xác tính bằng byte của phần `Payload Data`. |
| **Payload Data** | N Bytes | `byte[]` | N/A | Dữ liệu nội dung của bản tin (chuỗi JSON UTF-8 của đối tượng Envelope `NetOpsMessage`). |

*(Lưu ý: Mã định danh loại thông điệp `messageType` được đóng gói bên trong cấu trúc JSON Envelope `NetOpsMessage`, không nằm ở header nhị phân).*

---

## 3. Các Giới Hạn & Ràng Buộc Kỹ Thuật (Constraints)

* **Thứ tự byte (Endianness):** Bắt buộc sử dụng chuẩn **Big-Endian (Network Byte Order)** cho trường 4-byte `Length`. Điều này đảm bảo tính tương thích tuyệt đối giữa các nền tảng kiến trúc phần cứng (x86, x64, ARM, RISC-V) và đa ngôn ngữ lập trình (C#, Go, Python, Rust).
* **Độ dài tối thiểu (`MinPayloadSize`):** `1 byte`. Mọi frame có `Length <= 0` bị xem là vi phạm giao thức (`InvalidDataException`) và kết nối sẽ bị đóng ngay lập tức.
* **Độ dài tối đa (`MaxPayloadSize`):** `16,777,216 bytes` (16 MB = 16 * 1024 * 1024 bytes). Mọi frame có `Length > 16 MB` đều bị từ chối ngay lập tức để phòng chống tấn công tràn bộ nhớ (Denial-of-Service / Memory Exhaustion).

---

## 4. Thuật Toán Xử Lý Dòng & Triển Khai Mã Nguồn

### 4.1. Quy Trình Gửi (FrameWriter & FrameCodec)
1. **Kiểm tra độ dài:** `1 <= payloadLength <= 16 MB`.
2. **Đóng gói nguyên khối (Atomic Buffer):** Cấp phát mảng byte kích thước `4 + payloadLength`. Ghi 4 bytes độ dài Big-Endian và sao chép toàn bộ payload vào mảng.
3. **Truyền tải:** Ghi nguyên khối ra stream (`stream.WriteAsync`) và gọi `FlushAsync` để dữ liệu được chuyển giao tức thời xuống socket, giảm thiểu system calls và tránh phân mảnh gói tin ở tầng ứng dụng.
4. **Tuần tự hóa ghi (Write Serialization):** Sử dụng `SafeFrameStream` hoặc `SemaphoreSlim(1, 1)` để ngăn ngừa hiện tượng trộn lẫn byte khi có nhiều tác vụ cùng ghi đồng thời trên kết nối Full-Duplex.

### 4.2. Quy Trình Nhận (FrameReader & FrameCodec)
1. **Đọc Length Header:** Đọc chính xác 4 bytes đầu tiên từ stream vào bộ đệm tạm:
   * Nếu đối phương đóng kết nối sạch sẽ trước frame mới (`read == 0` khi `offset == 0`), trả về `null` (**Graceful EOF**).
   * Nếu stream bị ngắt khi mới đọc dở từ 1 đến 3 bytes, ném `EndOfStreamException`.
2. **Kiểm Tra Hợp Lệ:** Giải mã số nguyên 32-bit Big-Endian. Nếu `Length < 1` hoặc `Length > 16 MB`, ném `InvalidDataException` và hủy phiên kết nối.
3. **Đọc Payload Data:** Cấp phát mảng `new byte[Length]` và sử dụng vòng lặp đọc chính xác `Length` bytes từ stream trực tiếp vào mảng đích để tối ưu hiệu năng. Nếu stream bị ngắt giữa chừng, ném `EndOfStreamException`.
4. **Giải mã ứng dụng:** Chuyển mảng byte UTF-8 sang đối tượng `NetOpsMessage` (JSON Deserialization).

---

## 5. Xử Lý Ngoại Lệ & Sự Cố Mạng (Error Handling)

| Tình Huống | Hành Vi Xử Lý | Mục Đích |
| :--- | :--- | :--- |
| **Đối phương ngắt kết nối sạch (Graceful Close)** | Trả về `null` | Vòng lặp nhận dữ liệu thoát an toàn, dọn dẹp phiên kết nối mà không ghi log lỗi |
| **Stream bị ngắt giữa chừng khi đọc dở frame** | Ném `EndOfStreamException` | Phát hiện sự cố rớt mạng hoặc đứt kết nối đột ngột để kích hoạt kết nối lại |
| **Độ dài Length <= 0 hoặc > 16 MB** | Ném `InvalidDataException` | Ngăn chặn gói tin rác hoặc tấn công DoS tràn RAM, lập tức đóng socket |
| **Ghi đồng thời từ nhiều tác vụ** | Đồng bộ bằng `SemaphoreSlim(1, 1)` | Bảo đảm toàn vẹn frame stream, chống trộn lẫn byte |
| **Mất kết nối từ phía Agent** | Tự động thử lại với Exponential Backoff | Thử kết nối lại sau 3s ➔ 6s ➔ 12s ➔ tối đa 30s, chống bão kết nối |

---

## 6. Phân Lớp Mã Nguồn Trong Dự Án

* [FrameConstants.cs](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Framing/FrameConstants.cs): Định nghĩa các hằng số kích thước header (4 bytes), min payload (1 byte), max payload (16 MB) và struct `Frame`.
* [FrameWriter.cs](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Framing/FrameWriter.cs): Tầng ghi nhị phân `[4 Bytes Length][Payload Data]`.
* [FrameReader.cs](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Framing/FrameReader.cs): Tầng đọc nhị phân xử lý dính gói, phân mảnh và Graceful EOF.
* [FrameCodec.cs](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/FrameCodec.cs): Tầng trung gian mã hóa/giải mã đối tượng `NetOpsMessage` trên nền Framing layer.
* [SafeFrameStream.cs](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/SafeFrameStream.cs): Lớp bao gói an toàn đa luồng tích hợp `SemaphoreSlim(1, 1)` bảo vệ socket stream.
