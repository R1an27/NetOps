# BÁO CÁO KỸ THUẬT: TIMEOUT & OFFLINE DETECTION
**Giai đoạn 3: Connection Lifecycle (Vòng đời kết nối mạng)**  
**Dự án:** NetOps Manager (Môn NT106 - Lập trình mạng căn bản)  
**Người phụ trách theo Timeline & Barem:** Bùi Thanh Vin  
**Chuẩn Barem đáp ứng:** T16.1 đến T16.6 (Timeout & Offline Detection)  
**Trạng thái kiểm thử:** 287/287 tests passed (100% PASS, 0 Warning, 0 Error, 0 Regression)  

---

## 1. Bảng đối chiếu yêu cầu Barem chấm điểm (T16.1 - T16.6)

Dưới đây là bảng tổng hợp ánh xạ chi tiết giữa yêu cầu chấm điểm của Giảng viên và hiện thực mã nguồn trong dự án:

| Mã yêu cầu | Mô tả chi tiết theo Barem | Thành phần hiện thực trong mã nguồn | Trạng thái |
| :--- | :--- | :--- | :---: |
| **T16.1** | `IAgentRegistry` và `AgentRegistry` quản lý danh bạ Agent đa luồng (sử dụng `ConcurrentDictionary`, lock-striping hoặc tương đương). Trạng thái gồm `Online`, `Offline`, `LastSeenAt`, `DisconnectedAt`, `DisconnectReason`. | `src/Master.Api/Services/IAgentRegistry.cs`<br>`src/Master.Api/Services/AgentRegistry.cs` | **HOÀN THÀNH** |
| **T16.2** | `MasterMessageControl.Invoke / BeginInvoke` cập nhật trạng thái Agent thành `Online` và cập nhật `LastSeenAt` trong `AgentRegistry` khi nhận bản tin `AUTH_OK`, `HEARTBEAT` hoặc `PING`. | `src/Master.Api/Services/MasterMessageControl.cs`<br>(Hàm `InvokeAsync`, `BeginInvoke`, `HandlePing`, `HandleHeartbeat`, `HandleAuth`, `HandleAuthOk`) | **HOÀN THÀNH** |
| **T16.3** | `HeartbeatTimeoutWatchdog` chạy dạng `BackgroundService` (chu kỳ 2-3s), phát hiện session im lặng >15s và chuyển trạng thái sang `Offline` (lý do `HeartbeatTimeout`). | `src/Master.Api/Services/HeartbeatTimeoutWatchdog.cs`<br>(Sử dụng `PeriodicTimer` 2-3s, kiểm tra `elapsed > 15s`) | **HOÀN THÀNH** |
| **T16.4** | Khi socket đóng, Connection Manager đánh dấu Agent `Offline` trong `AgentRegistry` mà **không xóa thông tin máy chủ**. | `src/Master.Api/Services/ConnectionManager.cs`<br>(Hàm `TryUnregister` gọi `_agentRegistry.MarkOffline`, giữ lại bản ghi và thông tin kết nối) | **HOÀN THÀNH** |
| **T16.5** | Lệnh kiểm thử `dotnet test --filter FullyQualifiedName~HeartbeatTimeoutTests` thành công. | `tests/NetOps.Tests/HeartbeatTimeoutTests.cs`<br>(12 ca kiểm thử chuyên sâu cho T16.1 - T16.5 đạt 100% PASS) | **HOÀN THÀNH** |
| **T16.6** | `dotnet build src/Master.Api/Master.Api.csproj` và `dotnet test --filter NetOps.Tests` đạt 0 lỗi, 0 cảnh báo. | `src/Master.Api/Extensions/MasterServiceExtensions.cs`<br>(Đăng ký DI Singleton và HostedService; Build: 0 Warning, 0 Error; Test: 287/287 PASS) | **HOÀN THÀNH** |

---

## 2. Bài toán mạng cần giải quyết (Tóm tắt cho cả nhóm)

Trong lập trình Socket TCP hướng kết nối:
* **Vấn đề "Kết nối chết ngầm" (Half-Open Socket):** Khi máy Agent bị rút dây mạng, mất nguồn, sập máy ảo đột ngột hoặc rớt sóng, hệ điều hành của Agent **không kịp** gửi cờ ngắt kết nối `TCP FIN` hay `TCP RST` về phía Master.
* **Hậu quả nếu không xử lý:**
  - Master Server nếu chỉ dùng `ReadAsync` sẽ bị treo chờ vô tận (do socket vẫn ngỡ là đang kết nối).
  - Tốn bộ nhớ và rò rỉ bộ mô tả tệp (File Descriptor / Socket Leak) của hệ điều hành.
  - Người quản trị nhìn trên Desktop Dashboard vẫn thấy máy ảo đó hiển thị "Online" dù thực tế máy đã mất mạng từ lâu.
* **Giải pháp đã triển khai:**
  - Master Server chạy một dịch vụ nền **`HeartbeatTimeoutWatchdog`** (chu kỳ quét 2-3s) giám sát mốc thời gian nhận gói tin cuối cùng (`LastSeenAt`) của từng Agent.
  - Ngưỡng chờ quy định chuẩn: **15 giây** (tương đương 3 chu kỳ nhịp tim 5 giây).
  - Nếu sau 15 giây không có tín hiệu: Master **tự động đánh dấu Agent là `Offline` trong `AgentRegistry`**, ghi nhận lý do `HeartbeatTimeout`, bắn sự kiện cảnh báo và **cưỡng chế đóng socket** để thu hồi tài nguyên hệ điều hành.

---

## 3. Chi tiết kiến trúc và các file mã nguồn

### A. Thư viện hợp đồng dùng chung (`Shared.Contracts`)

1. **`src/Shared.Contracts/Models/AgentStatus.cs`**:
   - `AgentStatus` (Enum): Gồm 4 trạng thái vòng đời mạng:
     - `Online (0)`: Đang kết nối và gửi/nhận dữ liệu bình thường.
     - `Offline (1)`: Đã ngắt kết nối hoặc bị Watchdog kết luận mất tín hiệu.
     - `Connecting (2)`: Đang trong quá trình bắt tay TCP ban đầu.
     - `Reconnecting (3)`: Đang trong chu kỳ thử kết nối lại (Exponential Backoff).
   - `DisconnectReason` (Enum): Phân loại chính xác nguyên nhân ngắt kết nối phục vụ kiểm toán và chẩn đoán:
     - `None (0)`: Đang hoạt động bình thường.
     - `HeartbeatTimeout (1)`: Mất nhịp tim quá 15s (Half-open socket).
     - `GracefulDisconnect (2)`: Agent chủ động tắt ứng dụng êm dịu (gửi tín hiệu EOF).
     - `NetworkFault (3)`: Đứt mạng đột ngột (Socket exception/RST/Socket closed).
     - `AuthenticationFailed (4)`: Lỗi xác thực danh tính.
     - `ServerShutdown (5)`: Master tắt hoặc bảo trì.

2. **`src/Shared.Contracts/Models/AgentStatusRecord.cs`**:
   - Model lưu trữ thông tin Agent trên Master kể cả khi Agent đã Offline: `AgentId`, `CurrentSessionId`, `RemoteEndPoint`, `Status`, `ConnectedAt`, `LastSeenAt`, `DisconnectedAt`, `DisconnectReason`, và `IdleSeconds`.

---

### B. Máy chủ trung tâm (`Master.Api`)

1. **`src/Master.Api/Services/IAgentRegistry.cs` & `AgentRegistry.cs` (T16.1)**:
   - Khởi tạo danh bạ Agent độc lập với vòng đời Socket.
   - Sử dụng `ConcurrentDictionary<string, AgentStatusRecord>` với hàm `AddOrUpdate` đảm bảo tính nguyên tử (Atomic operation) và thread-safe tuyệt đối khi có hàng chục Agent kết nối song song.
   - Thao tác tra cứu, đăng ký và cập nhật trạng thái đạt độ phức tạp $O(1)$.
   - Phát ra các sự kiện: `AgentStatusChanged` và `AgentOfflineDetected`.

2. **`src/Master.Api/Services/HeartbeatTimeoutWatchdog.cs` (T16.3)**:
   - Kế thừa `BackgroundService` chạy nền định kỳ mỗi 2-3 giây sử dụng `PeriodicTimer` bất đồng bộ không chặn luồng.
   - Hàm `SweepAndPurgeTimeouts(now, timeoutSec)`:
     - Kiểm tra khoảng lặng: `currentTime - session.LastSeenAt > 15s`.
     - Chuyển trạng thái Agent sang `Offline` với lý do `DisconnectReason.HeartbeatTimeout` trong `AgentRegistry`.
     - Gỡ session khỏi ConnectionManager và phát sự kiện `SessionTimedOut`.
     - Cưỡng chế đóng socket và giải phóng luồng an toàn để thu hồi Socket Descriptor.

3. **`src/Master.Api/Services/IConnectionManager.cs` & `ConnectionManager.cs` (T16.4)**:
   - Bổ sung thuộc tính `Registry` truy xuất `IAgentRegistry`.
   - Kết nối trực tiếp với `IAgentRegistry` trung tâm: các hàm `MarkAgentOnline`, `MarkAgentOffline`, `GetAllAgentStatuses`, `GetAgentStatus` đều đồng bộ với registry.
   - Khi socket đóng (`TryUnregister`), Agent được chuyển sang `Offline` nhưng **toàn bộ thông tin máy chủ vẫn được lưu giữ nguyên vẹn** trong `AgentRegistry`.
   - Giữ nguyên 100% chú thích và logic cũ của mã nguồn.

4. **`src/Master.Api/Services/MasterMessageControl.cs` (T16.2)**:
   - Nhận trực tiếp `IAgentRegistry` qua Dependency Injection.
   - Khi nhận bất kỳ bản tin nào (`PING`, `HEARTBEAT`, `AUTH`, `AUTH_OK`), tự động cập nhật mốc `LastSeenAt` và gọi `_agentRegistry.RegisterOrUpdateOnline(...)` đưa Agent sang trạng thái `Online`.

5. **`src/Master.Api/Extensions/MasterServiceExtensions.cs` (T16.6)**:
   - Đăng ký `IAgentRegistry, AgentRegistry` dưới dạng Singleton.
   - Đăng ký `HeartbeatTimeoutWatchdog` dưới dạng HostedService.
   - Duy trì `SessionWatchdogService` để bảo toàn tương thích ngược 100%.

6. **`src/Master.Api/Endpoints/MasterApiEndpoints.cs`**:
   - Cung cấp REST API cho Desktop Client:
     - `GET /api/agents`: Mặc định trả về các session online, hỗ trợ tham số lọc `?status=all` hoặc `?status=offline`.
     - `GET /api/agents/status/{agentId}`: Tra cứu trạng thái chi tiết của một Agent bất kỳ.

---

### C. Bộ kiểm thử tự động (`NetOps.Tests`)

1. **`tests/NetOps.Tests/HeartbeatTimeoutTests.cs` (T16.5)**:
   - Bộ kiểm thử chuyên biệt đạt 12/12 PASS, bao gồm:
     - `AgentRegistry_RegisterOrUpdateOnline_SetsStatusToOnlineAndUpdatesLastSeen` (T16.1)
     - `AgentRegistry_MarkOffline_PreservesServerInfoAndSetsReason` (T16.1 & T16.4)
     - `AgentRegistry_ConcurrentAccess_IsThreadSafe` (T16.1)
     - `MasterMessageControl_WhenReceivingPing_UpdatesAgentRegistryToOnline` (T16.2)
     - `MasterMessageControl_WhenReceivingHeartbeat_UpdatesAgentRegistryToOnline` (T16.2)
     - `MasterMessageControl_WhenReceivingAuth_UpdatesAgentRegistryToOnline` (T16.2)
     - `MasterMessageControl_WhenReceivingAuthOk_UpdatesAgentRegistryToOnline` (T16.2)
     - `HeartbeatTimeoutWatchdog_WhenSilenceGreaterThan15s_MarksOfflineWithHeartbeatTimeout` (T16.3)
     - `HeartbeatTimeoutWatchdog_WhenSilenceWithin15s_KeepsAgentOnline` (T16.3)
     - `HeartbeatTimeoutWatchdog_FiresSessionTimedOutEvent` (T16.3)
     - `ConnectionManager_WhenSocketClosed_MarksAgentOfflineWithoutDeletingServerInfo` (T16.4)
     - `AddNetOpsMasterServer_RegistersAgentRegistryAndHeartbeatTimeoutWatchdog` (T16.6)

2. **`tests/NetOps.Tests/SessionWatchdogServiceTests.cs`**:
   - Duy trì toàn bộ 15 bài kiểm thử nâng cao của SessionWatchdogService (vòng lặp timer, fallback cấu hình cổng, xử lý lỗi đóng socket bất thường).

---

## 4. Hướng dẫn chạy kiểm thử để nghiệm thu

### Lệnh 1: Build dự án Master.Api (Yêu cầu T16.6)
```powershell
dotnet build "src/Master.Api/Master.Api.csproj"
```
*Kết quả:* **0 Warning(s), 0 Error(s)**.

### Lệnh 2: Chạy kiểm thử lớp HeartbeatTimeoutTests (Yêu cầu T16.5)
```powershell
dotnet test --filter FullyQualifiedName~HeartbeatTimeoutTests
```
*Kết quả:* **Passed: 12, Failed: 0, Total: 12**.

### Lệnh 3: Chạy toàn bộ kiểm thử Solution
```powershell
dotnet test NetOps.slnx
```
*Kết quả:* **Passed: 287, Failed: 0, Total: 287** (100% PASS trên toàn bộ dự án).

---

## 5. Lưu ý quan trọng cho 2 thành viên còn lại (Vương & Tuyển)

### Dành cho thành viên phụ trách Heartbeat & Automatic Reconnect (Tuyển):
* **Cơ chế Watchdog không phụ thuộc cấu trúc nội bộ gói tin:** Watchdog chỉ nhìn vào mốc `LastSeenAt` được cập nhật tự động bởi `MasterMessageControl`.
* **Cơ chế Reconnect tự động:** Khi Agent của bạn kích hoạt Exponential Backoff và nối lại thành công tới Master, chỉ cần gửi bất kỳ gói tin nào (`PING`, `HEARTBEAT`, hoặc `AUTH`), Master đã được lập trình sẵn để tự động đưa Agent từ trạng thái `Offline` trở lại `Online` ngay lập tức mà bạn không cần phải gọi API gì thêm.

### Dành cho thành viên phụ trách Desktop Client UI (Vương / Vin):
* **Hiển thị giao diện:**
  - Gọi endpoint `GET /api/agents?status=all` để lấy danh sách máy ảo kèm trạng thái và thời gian không hoạt động `idleSeconds`.
  - Nếu `Status == Online` $\rightarrow$ Vẽ icon/badge màu **Xanh lá**.
  - Nếu `Status == Offline` và `DisconnectReason == HeartbeatTimeout` $\rightarrow$ Vẽ badge màu **Đỏ** kèm tooltip *"Mất tín hiệu nhịp tim > 15s"*.
  - Nếu `Status == Offline` và `DisconnectReason == GracefulDisconnect` $\rightarrow$ Vẽ badge màu **Xám** kèm tooltip *"Agent đã chủ động đóng kết nối"*.
