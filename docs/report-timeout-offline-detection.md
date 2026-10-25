# BÁO CÁO KỸ THUẬT: TIMEOUT & OFFLINE DETECTION
**Giai đoạn 3: Connection Lifecycle (Vòng đời kết nối mạng)**  
**Dự án:** NetOps Manager (Môn NT106 - Lập trình mạng căn bản)  
**Người phụ trách theo Timeline:** Bùi Thanh Vin  
**Trạng thái kiểm thử:** 275/275 tests passed (100% PASS, 0 Warning, 0 Regression)

---

## 1. Bài toán mạng cần giải quyết (Tóm tắt cho cả nhóm)

Trong lập trình Socket TCP hướng kết nối:
* **Vấn đề "Kết nối chết ngầm" (Half-Open Socket):** Khi máy Agent bị rút dây mạng, mất nguồn, sập máy ảo đột ngột hoặc rớt sóng, hệ điều hành của Agent **không kịp** gửi cờ ngắt kết nối `TCP FIN` hay `TCP RST` về phía Master.
* **Hậu quả nếu không xử lý:**
  - Master Server nếu chỉ dùng `ReadAsync` sẽ bị treo chờ vô tận (do socket vẫn ngỡ là đang kết nối).
  - Tốn bộ nhớ và rò rỉ bộ mô tả tệp (File Descriptor Leak) của hệ điều hành.
  - Người quản trị nhìn trên Desktop Dashboard vẫn thấy máy ảo đó hiển thị "Online" dù thực tế máy đã mất mạng từ lâu.
* **Giải pháp đã triển khai:**
  - Master Server chạy một luồng ngầm **Watchdog Timer** giám sát thời gian phản hồi gần nhất (`LastSeenAt`) của từng Agent.
  - Ngưỡng chờ quy định: **15 giây** (tương đương 3 chu kỳ nhịp tim 5 giây).
  - Nếu sau 15 giây không có tín hiệu: Master **tự động đánh dấu Agent là `Offline`**, ghi nhận lý do `HeartbeatTimeout`, bắn sự kiện cảnh báo và **cưỡng chế đóng socket** để giải phóng tài nguyên.

---

## 2. Chi tiết các file đã thêm mới & chỉnh sửa

### A. Thư viện hợp đồng chung (`Shared.Contracts`)

1. **`src/Shared.Contracts/Models/AgentStatus.cs`** *(Tạo mới)*:
   - `AgentStatus` (Enum): Gồm 4 trạng thái vòng đời mạng:
     - `Online (0)`: Đang kết nối và gửi/nhận dữ liệu bình thường.
     - `Offline (1)`: Đã ngắt kết nối hoặc bị Watchdog kết luận mất tín hiệu.
     - `Connecting (2)`: Đang trong quá trình bắt tay TCP ban đầu.
     - `Reconnecting (3)`: Đang trong chu kỳ thử kết nối lại (Exponential Backoff).
   - `DisconnectReason` (Enum): Phân loại chính xác nguyên nhân ngắt kết nối:
     - `None`: Đang hoạt động bình thường.
     - `HeartbeatTimeout`: Mất nhịp tim quá 15s (Half-open socket).
     - `GracefulDisconnect`: Agent chủ động tắt ứng dụng êm dịu (gửi tín hiệu EOF).
     - `NetworkFault`: Đứt mạng đột ngột (Socket exception/RST).
     - `ServerShutdown`: Master tắt hoặc bảo trì.

2. **`src/Shared.Contracts/Models/AgentStatusRecord.cs`** *(Tạo mới)*:
   - Model lưu trữ thông tin Agent trên Master kể cả khi Agent đã Offline: `AgentId`, `CurrentSessionId`, `RemoteEndPoint`, `Status`, `ConnectedAt`, `LastSeenAt`, `DisconnectedAt`, `DisconnectReason`, và `IdleSeconds`.

---

### B. Máy chủ trung tâm (`Master.Api`)

1. **`src/Master.Api/Services/IConnectionManager.cs` & `ConnectionManager.cs`** *(Cập nhật)*:
   - Thêm bảng quản lý đa luồng `_agentRegistry`: Lưu vết danh sách tất cả các Agent đã từng kết nối để không bị "biến mất" khi socket bị đóng.
   - Bổ sung 2 sự kiện:
     - `AgentStatusChanged`: Phát ra khi Agent đổi trạng thái giữa `Online` và `Offline`.
     - `AgentOfflineDetected`: Phát ra chuyên biệt khi Agent chuyển sang `Offline` để kích hoạt báo động.
   - Thêm hàm `MarkAgentOnline(...)` và `MarkAgentOffline(...)`.
   - Hàm `TryUnregister` giờ đây hỗ trợ truyền rõ `DisconnectReason` thay vì chỉ xóa session thô thiển.

2. **`src/Master.Api/Services/SessionWatchdogService.cs`** *(Trọng tâm của Task)*:
   - Sử dụng `PeriodicTimer` hiệu năng cao của .NET để quét danh sách session định kỳ.
   - Trong hàm `PurgeTimedOutSessions`:
     - So sánh: `currentTime - session.LastSeenAt > 15 giây`.
     - Khi vượt ngưỡng: Tự động gọi `MarkAgentOffline(agentId, DisconnectReason.HeartbeatTimeout)`.
     - Bắn sự kiện `SessionTimedOut`.
     - Cưỡng chế gọi `session.Client.Close()` và `session.SafeStream.Dispose()` để thu hồi Socket Descriptor của hệ điều hành.

3. **`src/Master.Api/Services/TcpManagerServer.cs`** *(Cập nhật)*:
   - Khi luồng đọc TCP nhận được `message == null` (EOF): Xác nhận `DisconnectReason.GracefulDisconnect`.
   - Khi gặp ngoại lệ mạng bất ngờ: Xác nhận `DisconnectReason.NetworkFault`.

4. **`src/Master.Api/Services/MasterMessageControl.cs`** *(Cập nhật)*:
   - Tự động gọi `MarkAgentOnline` đồng bộ bất cứ khi nào nhận được tin nhắn hợp lệ từ Agent (`PING`, `HEARTBEAT`, hoặc `AUTH`).

5. **`src/Master.Api/Endpoints/MasterApiEndpoints.cs`** *(Cập nhật)*:
   - Cung cấp REST API cho Desktop Client:
     - `GET /api/agents`: Mặc định trả về các session online, hỗ trợ tham số lọc `?status=all` hoặc `?status=offline`.
     - `GET /api/agents/status/{agentId}`: Tra cứu trạng thái chi tiết của một Agent bất kỳ.

---

### C. Bộ kiểm thử tự động (`NetOps.Tests`)

1. **`tests/NetOps.Tests/SessionWatchdogServiceTests.cs`**:
   - Thêm **Region 4** với 6 bài kiểm thử toàn diện:
     - Kiểm thử quá 15s tự động đánh dấu Agent sang `Offline` với lý do `HeartbeatTimeout`.
     - Kiểm thử phát các sự kiện `AgentOfflineDetected`, `AgentStatusChanged`, `SessionTimedOut`.
     - Kiểm thử Agent hoạt động dưới 15s thì giữ nguyên trạng thái `Online`.
     - Kiểm thử Agent sau khi bị Timeout, nếu kết nối lại (Reconnect) sẽ tự động phục hồi về `Online`.
     - Kiểm thử xử lý đồng thời nhiều Agent cùng timeout một lúc an toàn không nghẽn luồng.
     - Kiểm thử phân loại đúng các lý do ngắt kết nối (`GracefulDisconnect`, `NetworkFault`).

2. **`tests/NetOps.Tests/MasterApiEndpointsTests.cs`**:
   - Thêm bài test xác thực API `GET /api/agents?status=offline` và `GET /api/agents/status/{agentId}` trả về đúng dữ liệu chuẩn JSON.

---

## 3. Lưu ý quan trọng cho 2 thành viên còn lại (Vương & Tuyển)

### Dành cho thành viên phụ trách Heartbeat & Automatic Reconnect (Tuyển):
* **Không cần lo lắng về sự phụ thuộc:** Cơ chế Watchdog hoạt động hoàn toàn độc lập với cấu trúc gói Heartbeat. Watchdog chỉ nhìn vào mốc thời gian nhận gói `LastSeenAt`.
* **Cơ chế Reconnect tự động:** Khi Agent của bạn kích hoạt Exponential Backoff và nối lại thành công tới Master, chỉ cần gửi bất kỳ gói tin nào (kể cả PING hoặc HEARTBEAT), Master đã được lập trình sẵn để tự động đưa Agent từ trạng thái `Offline` trở lại `Online` ngay lập tức mà bạn không cần cấu hình thêm gì ở phía Master.

### Dành cho thành viên phụ trách Desktop Client UI (Vương / Vin):
* **Hiển thị giao diện:**
  - Gọi endpoint `GET /api/agents?status=all` để lấy danh sách máy ảo kèm trạng thái và thời gian không hoạt động `idleSeconds`.
  - Nếu `Status == Online` $\rightarrow$ Vẽ icon/badge màu **Xanh lá**.
  - Nếu `Status == Offline` và `DisconnectReason == HeartbeatTimeout` $\rightarrow$ Vẽ badge màu **Đỏ** kèm tooltip *"Mất tín hiệu nhịp tim > 15s"*.
  - Nếu `Status == Offline` và `DisconnectReason == GracefulDisconnect` $\rightarrow$ Vẽ badge màu **Xám** kèm tooltip *"Agent đã chủ động đóng kết nối"*.

---

## 4. Hướng dẫn chạy kiểm thử để nghiệm thu

Để thẩm định toàn bộ mã nguồn vừa hoàn thành, chỉ cần chạy lệnh sau tại thư mục gốc của Solution:

```powershell
dotnet test NetOps.slnx
```
**Kết quả mong đợi:** Toàn bộ 275 bài kiểm thử chạy thành công (`Passed: 275, Failed: 0, Total: 275`).
