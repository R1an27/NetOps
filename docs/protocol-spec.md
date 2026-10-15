# Đặc Tả Giao Thức Message Framing (TCP Length-Prefix Protocol)

Tài liệu này đặc tả cơ chế **Message Framing** nhị phân tầng thấp, được thiết kế để phân biệt độ dài và bảo toàn ranh giới giữa các thông điệp khi truyền tải liên tục qua dòng byte TCP trong hệ thống **NetOps**.

---

## 1. Bối Cảnh & Vấn Đề (Problem Statement)

Giao thức truyền vận TCP (Transmission Control Protocol) là giao thức hướng dòng byte liên tục (**Byte Stream**). Điều này dẫn đến hai đặc tính tự nhiên của mạng:
1. **Dính gói (Packet Coalescing / Sticky Packets):** Khi phía gửi phát đi nhiều message liên tiếp hoặc nhanh hơn khả năng xử lý của mạng/bên nhận (Nagle's Algorithm), các message có thể bị gộp lại và bên nhận đọc được trong cùng một lệnh đọc `Read`.
2. **Phân mảnh gói (Packet Fragmentation):** Một message có kích thước lớn có thể bị chia thành nhiều phân đoạn TCP (TCP segments) dựa trên MTU/MSS của mạng, khiến bên nhận phải thực hiện nhiều lần `Read` mới thu thập đủ một message nguyên vẹn.

Để phân định ranh giới và độ dài của từng message độc lập, giải pháp chuẩn hóa là sử dụng kỹ thuật **Length-Prefix Framing**.

---

## 2. Cấu Trúc Khung Nhị Phân (Binary Frame Structure)

Mỗi Frame được truyền qua luồng TCP có cấu trúc nhị phân như sau:

```
+--------------------------------+---------------+-----------------------------------+
|      Frame Length (4 Bytes)    |  Type (1 Byte)|          Payload (N Bytes)        |
|     Big-Endian (Network Order) |               |           (0 <= N <= 1 MB - 1)    |
+--------------------------------+---------------+-----------------------------------+
|<-------------------------- Frame Body (Length bytes) ----------------------------->|
|<------------------------ Total Frame on Wire (4 + Length bytes) ------------------->|
```

### Chi Tiết Các Trường:

| Trường | Kích Thước | Kiểu Dữ Liệu | Mô Tả |
| :--- | :---: | :---: | :--- |
| **Length Prefix** | 4 Bytes | `uint32` (Big-Endian) | Kích thước của toàn bộ phần thân Frame (Frame Body), bao gồm: `1 Byte Type` + `N Bytes Payload`. |
| **Type** | 1 Byte | `byte` (`uint8`) | Mã định danh loại thông điệp nhị phân (phục vụ dispatching/routing ở tầng ứng dụng). |
| **Payload** | N Bytes | `byte[]` | Dữ liệu nội dung của bản tin (có thể là chuỗi JSON UTF-8, protobuf bytes, binary telemetry, v.v.). Cho phép payload rỗng (`N = 0`). |

---

## 3. Các Giới Hạn & Quy Ước Kỹ Thuật (Constraints)

* **Thứ tự byte (Endianness):** Bắt buộc sử dụng chuẩn **Big-Endian (Network Byte Order)** cho trường 4-byte `Length`. Điều này đảm bảo tính tương thích đa nền tảng và độc lập kiến trúc CPU (x86, x64, ARM, RISC-V).
* **Độ dài tối thiểu (`MinFrameSize`):** `1 byte` (Tương ứng với Frame chỉ chứa 1 byte `Type` và 0 byte `Payload`). Frame có `Length < 1` bị coi là vi phạm giao thức.
* **Độ dài tối đa (`MaxFrameSize`):** `1,048,576 bytes` (1 MB). Mọi Frame thông báo `Length > 1 MB` sẽ bị từ chối ngay lập tức để phòng tránh tấn công tràn bộ nhớ (Denial-of-Service / Memory Exhaustion).

---

## 4. Thuật Toán Xử Lý Dòng (Streaming Processing)

### 4.1. Quy Trình Gửi (FrameWriter)
1. Xác định độ dài: `length = 1 (Type) + Payload.Length`.
2. Kiểm tra giới hạn: `length <= MaxFrameSize` (1 MB).
3. Đóng gói vào một buffer liền mạch gồm 4 byte độ dài Big-Endian, tiếp nối là 1 byte Type và Payload.
4. Ghi nguyên khối ra Stream (`stream.WriteAsync`) và `FlushAsync` để đảm bảo gói tin được chuyển giao ngay xuống socket.

### 4.2. Quy Trình Nhận (FrameReader)
1. **Đọc Length Header:** Đọc chính xác 4 bytes từ luồng vào bộ đệm tạm.
   * Nếu đối phương đóng kết nối sạch trước khi gửi bất kỳ byte nào (`read == 0` khi `offset == 0`), trả về `null` (Graceful EOF).
   * Nếu đối phương ngắt kết nối khi mới chỉ nhận được từ 1 đến 3 bytes, ném `EndOfStreamException`.
2. **Kiểm Tra Hợp Lệ:** Đọc giá trị 32-bit Big-Endian. Nếu `Length < 1` hoặc `Length > 1 MB`, ném `InvalidDataException` và hủy phiên kết nối.
3. **Đọc Type:** Đọc tiếp 1 byte để xác định loại thông điệp. Nếu stream ngắt giữa chừng, ném `EndOfStreamException`.
4. **Đọc Payload:** Cấp phát mảng `new byte[Length - 1]`. Đọc vòng lặp cho đến khi nhận đủ chính xác `Length - 1` bytes. Đọc trực tiếp vào mảng đích để tối ưu hiệu năng và tránh sao chép bộ nhớ thừa.
5. Trả về đối tượng `Frame(Type, Payload)`.

---

## 5. Xử Lý Ngoại Lệ & Sự Cố Mạng (Error Handling)

| Tình Huống | Hành Vi Xử Lý | Mục Đích |
| :--- | :--- | :--- |
| **Đối phương đóng kết nối sạch (Graceful Close)** | `FrameReader.ReadAsync` trả về `null` | Cho phép vòng lặp xử lý kết nối thoát an toàn mà không sinh log lỗi |
| **Stream bị ngắt giữa chừng khi đang đọc Header/Payload** | Ném `EndOfStreamException` | Phát hiện lỗi mất mạng hoặc gói tin bị đứt đoạn bất thường |
| **Độ dài Length < 1 hoặc > 1 MB** | Ném `InvalidDataException` | Phòng chống client gửi frame rác hoặc tấn công DoS tràn RAM |
| **Stream null hoặc Frame null** | Ném `ArgumentNullException` | Kiểm tra tham số đầu vào an toàn, loại trừ lỗi NullReferenceException |
