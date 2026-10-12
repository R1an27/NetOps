# NetOps Protocol v0

## 1. Giới Thiệu & Mục Tiêu

Tài liệu này định nghĩa chi tiết **NetOps Protocol v0** - giao thức truyền thông tầng ứng dụng hoạt động trên nền kết nối **TCP Socket** toàn phần (Full-Duplex TCP Connection) giữa **Manager** (Máy chủ điều phối trung tâm) và **Agent** (Tiến trình nền chạy trên các máy chủ mục tiêu).

### Mục Tiêu Thiết Kế
1. **Độc lập nền tảng:** Định dạng gói tin dựa trên JSON mã hóa UTF-8, dễ dàng hiện thực trên C#, Python, Go, Rust hoặc C/C++.
2. **Khắc phục triệt để vấn đề TCP Stream:** Sử dụng kỹ thuật **Length-Prefixed Framing** để bảo đảm ranh giới thông điệp khi gặp hiện tượng dính gói (packet coalescing) và phân mảnh (fragmentation).
3. **Truyền thông bất đồng bộ 2 chiều (Full-Duplex):** Cả Manager và Agent đều có thể chủ động khởi tạo yêu cầu hoặc truyền tải dữ liệu theo thời gian thực trên cùng một kết nối TCP duy nhất.
4. **Đối chiếu tác vụ chính xác (Request-Response Correlation):** Hỗ trợ cặp khóa `messageId` và `requestId` / `inReplyTo` phục vụ mô hình giao tiếp bất đồng bộ không phong tỏa (Non-blocking RPC & Streaming).

---

## 2. Cấu Trúc Khung Nhị Phân (Binary Framing Layer)

Giao thức TCP là dòng byte liên tục (Byte Stream) không bảo toàn ranh giới bản tin. NetOps Protocol v0 chuẩn hóa cấu trúc đóng gói khung nhị phân như sau:

### 2.1. Chi Tiết Header
* **Kích thước:** Cố định 4 bytes (32-bit signed integer).
* **Byte Order (Endianness):** **Big-Endian** (Network Byte Order chuẩn).
* **Giá trị (`Length`):** Độ dài chính xác tính bằng byte của phần `Payload Data`.
* **Giới hạn kích thước:** 
  * Chiều dài tối thiểu: `1 byte`.
  * Chiều dài tối đa cho phép: `16,777,216 bytes` (16 MB).
  * Mọi frame có `Length <= 0` hoặc `Length > 16 MB` đều bị xem là vi phạm giao thức (`InvalidDataException`) và kết nối sẽ bị đóng ngay lập tức để phòng chống tấn công tràn bộ nhớ (Memory Exhaustion / DoS).

### 2.2. Thuật Toán Giải Mã (Frame Decoding Algorithm)
1. Đọc đúng 4 bytes đầu tiên từ stream vào bộ đệm `headerBuffer`.
2. Đọc giá trị số nguyên 32-bit Big-Endian để xác định `payloadLength`.
3. Kiểm tra tính hợp lệ của `payloadLength` (`1 <= payloadLength <= 16MB`).
4. Cấp phát bộ đệm `payloadBuffer` kích thước đúng `payloadLength` và tiếp tục đọc từ stream cho đến khi nhận đủ `payloadLength` bytes.
5. Giải mã chuỗi UTF-8 trong `payloadBuffer` thành đối tượng thông điệp (JSON Deserialization).

---

## 3. Cấu Trúc Khung Thông Điệp Envelope (`NetOpsMessage`)

Mọi thông điệp truyền qua NetOps Protocol v0 đều tuân thủ cấu trúc phong bì (Envelope) tiêu chuẩn:

```json
{
  "messageId": "9b1deb4d3b7d4bad9bdd2b0d7b3dcb6d",
  "requestId": "req-1728500000-001",
  "inReplyTo": "9b1deb4d3b7d4bad9bdd2b0d7b3dcb6d",
  "messageType": "PING",
  "timestamp": 1728500000000,
  "payload": "{\"agentId\":\"agent-linux-01\"}"
}
```

### Các Trường Dữ Liệu
| Tên trường | Kiểu | Bắt buộc | Mô tả |
| :--- | :--- | :---: | :--- |
| `messageId` | `string` | **Có** | Định danh duy nhất toàn cục (UUID/GUID) của chính frame tin này. |
| `requestId` | `string` | Không | Định danh của luồng tác vụ logic (Request correlation ID). Dùng để ghép đôi yêu cầu và phản hồi khi có nhiều tác vụ chạy song song. |
| `inReplyTo` | `string` | Không | Định danh của thông điệp mà bản tin này đang phản hồi (trỏ tới `requestId` hoặc `messageId` của phía đối diện). |
| `messageType`| `string` | **Có** | Mã định danh loại thông điệp (viết hoa dạng CONSTANT). Xem mục 4. |
| `timestamp`  | `long`   | **Có** | Dấu thời gian Unix theo đơn vị mili-giây (UTC Epoch Milliseconds). |
| `payload`    | `string` | Không | Chuỗi JSON chứa dữ liệu chi tiết của từng loại thông điệp (hoặc null nếu thông điệp không cần nội dung). |

---

## 4. Danh Mục Thông Điệp & Chi Tiết Payloads (Protocol v0)

### 4.1. Thông Điệp Kiểm Tra Sống Còn (Liveness & Handshake)
* **`PING`:** Được gửi từ Agent hoặc Manager để kiểm tra thông tuyến và độ trễ.
  * Phản hồi tương ứng: **`PONG`** (chứa `inReplyTo = ping.messageId`).
* **`HEARTBEAT`:** Agent gửi định kỳ (mặc định mỗi 5 - 10 giây) để báo cho Manager biết Agent vẫn đang hoạt động bình thường.
  * `payload`: `{"agentId": "string", "timestamp": 1728500000000}`
* **`HEARTBEAT_ACK`:** Manager phản hồi xác nhận đã nhận được Heartbeat.
  * `payload`: `{"timestamp": 1728500000000, "acknowledged": true}`

### 4.2. Thông Điệp Xác Thực (Authentication Baseline)
* **`AUTH`:** Được gửi từ Agent ngay sau khi kết nối TCP được thiết lập.
  * `payload`: `{"agentId": "string", "token": "string", "osInfo": "string", "hostname": "string"}`
* **`AUTH_OK`:** Manager chấp nhận phiên kết nối của Agent.
  * `payload`: `{"sessionId": "string", "message": "Authenticated successfully"}`
* **`AUTH_FAILED`:** Manager từ chối Agent (sai token, server cấm, v.v.).
  * `payload`: `{"errorCode": "AUTH_INVALID_TOKEN", "message": "Invalid agent secret key"}`

### 4.3. Thông Điệp Giám Sát Phần Cứng (Metrics)
* **`METRICS`:** Agent thu thập và gửi số liệu định kỳ về Manager.
  * `payload`:
    ```json
    {
      "agentId": "agent-linux-01",
      "cpuUsagePercent": 14.8,
      "memoryTotalMb": 16384,
      "memoryUsedMb": 4096,
      "diskTotalMb": 256000,
      "diskUsedMb": 51200,
      "uptimeSeconds": 86400,
      "hostname": "ubuntu-vps-01",
      "osVersion": "Linux 6.8.0-generic"
    }
    ```

### 4.4. Thông Điệp Thực Thi Câu Lệnh Từ Xa (Remote Command Execution)
* **`COMMAND`:** Manager gửi yêu cầu thực thi lệnh bash/shell xuống Agent.
  * `payload`: `{"commandId": "cmd-001", "commandLine": "uname -a", "timeoutSeconds": 30, "workingDir": "/home"}`
* **`COMMAND_OUTPUT`:** Agent truyền (stream) từng dòng kết quả stdout/stderr về Manager trong quá trình lệnh đang chạy.
  * `payload`: `{"commandId": "cmd-001", "output": "Linux ubuntu 6.8.0\n", "isError": false}`
* **`COMMAND_DONE`:** Agent thông báo đã hoàn tất thực thi lệnh.
  * `payload`: `{"commandId": "cmd-001", "exitCode": 0, "totalExecutionTimeMs": 125}`

### 4.5. Thông Điệp Báo Lỗi (Error Handling)
* **`ERROR`:** Gửi đi khi một bên nhận được thông điệp sai định dạng, không hỗ trợ `messageType`, hoặc gặp lỗi xử lý nghiêm trọng.
  * `payload`: `{"errorCode": "INVALID_MESSAGE_TYPE", "message": "MessageType 'UNKNOWN' is not supported", "details": "..."}`

---

## 5. Quy Tắc Ứng Xử Mạng & Xử Lý Sự Cố (Network Guidelines)

1. **Tuần tự hóa ghi Socket (Write Serialization):** Vì TCP Socket không hỗ trợ nhiều thread ghi đồng thời vào cùng một Stream mà không bị trộn lẫn byte, mỗi kết nối **phải** được đồng bộ hóa ghi bằng `SemaphoreSlim(1, 1)` ở cả hai đầu Manager và Agent.
2. **Graceful Disconnect (EOF Detection):** Khi đầu đối diện đóng kết nối có chủ đích, hàm `ReadAsync` sẽ trả về `0`. Bên nhận phải kết thúc vòng lặp đọc, dọn dẹp bộ nhớ và giải phóng Socket mà không ném ra exception không mong muốn.
3. **Timeout & Heartbeat:** Manager duy trì bộ đếm thời gian `LastSeenAt` cho từng Agent. Nếu quá `3 * HeartbeatInterval` (ví dụ 30 giây) không nhận được bất kỳ message hoặc heartbeat nào, Manager sẽ chủ động đóng session và đánh dấu Agent là **Offline**.
4. **Reconnection Strategy:** Agent bị mất kết nối sẽ tự động thử kết nối lại với độ trễ tăng dần (Exponential Backoff: 3s -> 6s -> 12s -> tối đa 30s) nhằm tránh gây bão kết nối (Connection storm).