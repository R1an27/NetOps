# NetOps Management Platform

Hệ thống quản trị và giám sát máy chủ phân tán hiệu năng cao trên nền tảng .NET 10, sử dụng giao thức truyền thông nhị phân tùy chỉnh **NetOps Protocol v0** (4-byte Big-Endian Length-Prefixed Framing).

---

## 🚀 Cấu Trúc Dự Án (Solution Architecture)

Solution [`NetOps.slnx`](file:///c:/Users/vuong/Documents/NetOps/NetOps.slnx) bao gồm 5 dự án thành phần:

| Dự án | Nền tảng | Vai trò & Trách nhiệm |
| :--- | :---: | :--- |
| **`src/Shared.Contracts`** | .NET 10.0 | Thư viện lõi chứa định nghĩa giao thức mạng, bộ tuần tự hóa khung truyền nhị phân [`ISafeFrameChannel`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/ISafeFrameChannel.cs), [`SafeFrameStream`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/SafeFrameStream.cs), cùng các Models và Payloads nghiệp vụ. |
| **`src/Master.Api`** | .NET 10.0 | Máy chủ trung tâm (Manager Server) lắng nghe kết nối TCP, tự động quản lý phiên làm việc của các Agent qua [`IConnectionManager`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/IConnectionManager.cs), phân luồng lệnh và điều phối giao tiếp. |
| **`src/Agent.Worker`** | .NET 10.0 | Background service triển khai trên các máy chủ đích (Linux/Windows), kết nối về Master để nhận lệnh shell và báo cáo trạng thái. |
| **`src/Client.WinForms`** | .NET 10.0 (WinForms) | Ứng dụng máy trạm đồ họa hiện đại dành cho quản trị viên, cung cấp Dashboard kết nối, kiểm tra đường truyền (Ping RTT, Heartbeat), thực thi lệnh từ xa (Remote Terminal Console) và bộ soi khung nhị phân thời gian thực (**Protocol Frame Inspector**). |
| **`tests/NetOps.Tests`** | .NET 10.0 (xUnit) | Bộ kiểm thử tự động toàn diện (195 test cases pass 100%) bao gồm Unit Tests, DTO Tests, TCP E2E Socket Tests, Connection Lifecycle Tests, High-Concurrency Stress Tests, Master Message Control Tests, Session Watchdog Tests và Chaos/Resilience Tests. |

---

## 🛠 Chuẩn Giao Thức Mạng NetOps Protocol v0

- **Framing Model:** Đóng gói tiền tố độ dài nhị phân (Length-Prefix):
  - **Header (4 bytes):** Độ dài của payload tính theo định dạng số nguyên 32-bit Big-Endian (`[0x00, 0x00, 0x01, 0x2A]`).
  - **Payload (N bytes):** Chuỗi mã hóa UTF-8 JSON biểu diễn khung thông điệp tiêu chuẩn `NetOpsMessage`.
- **An toàn luồng (Thread-safety):** Toàn bộ thao tác ghi gói tin được tuần tự hóa an toàn qua `SemaphoreSlim(1, 1)` trong `SafeFrameStream`, ngăn chặn hoàn toàn hiện tượng interleaving/trộn lẫn byte giữa nhiều luồng gửi đồng thời.
- **Request-Response Matching:** Tự động đối chiếu bất đồng bộ qua `RequestId` và `InReplyTo` sử dụng `TaskCompletionSource<NetOpsMessage>`.

### Danh mục 9 Data Transfer Objects (DTOs) Chuẩn Hóa

| # | Tên DTO Chuẩn | MessageType Tương Ứng | Vai Trò & Thuộc Tính Chính | Lớp Tương Thích Ngược |
| :-: | :--- | :--- | :--- | :--- |
| 1 | **`AuthRequestPayload`** | `AUTH` | Xác thực Agent: `agentId`, `token`, `osInfo`, `hostname` | `AuthPayload` |
| 2 | **`AuthResponsePayload`** | `AUTH_OK`, `AUTH_FAILED` | Kết quả xác thực: `success`, `sessionId`, `errorCode`, `message` | `AuthOkPayload`, `AuthFailedPayload` |
| 3 | **`HeartbeatPayload`** | `HEARTBEAT` | Nhịp tim sống còn định kỳ: `agentId`, `timestamp` | Giữ nguyên tên chuẩn |
| 4 | **`HeartbeatAckPayload`** | `HEARTBEAT_ACK` | Xác nhận nhịp tim: `timestamp`, `acknowledged` | Giữ nguyên tên chuẩn |
| 5 | **`MetricsPayload`** | `METRICS` | 9 chỉ số phần cứng: `cpuUsagePercent`, RAM, Disk, Uptime, OS | Giữ nguyên tên chuẩn |
| 6 | **`CommandRequestPayload`** | `COMMAND` | Điều khiển từ xa: `commandId`, `commandLine`, `timeoutSeconds`, `workingDir` | `CommandPayload` |
| 7 | **`CommandOutputPayload`** | `COMMAND_OUTPUT` | Stream đầu ra stdout/stderr: `commandId`, `output`, `isError` | Giữ nguyên tên chuẩn |
| 8 | **`CommandDonePayload`** | `COMMAND_DONE` | Hoàn thành lệnh: `commandId`, `exitCode`, `totalExecutionTimeMs`, `success` | Giữ nguyên tên chuẩn |
| 9 | **`ErrorMessagePayload`** | `ERROR` | Thông báo lỗi chuẩn: `errorCode`, `message`, `details` | `ErrorPayload` |

### Tiện Ích Chuyển Đổi DTO & Thông Điệp (`NetOpsMessage`)

Lớp `NetOpsMessage` cung cấp bộ API chuyển đổi strongly-typed tiện lợi, an toàn và trực quan:

```csharp
// 1. Khởi tạo thông điệp với Typed Payload (tự động tuần tự hóa sang JSON):
var heartbeatMsg = NetOpsMessage.Create(MessageType.Heartbeat, new HeartbeatPayload("agent-01"));
var cmdMsg = NetOpsMessage.Create(MessageType.Command, new CommandRequestPayload("cmd-01", "whoami"), requestId: "req-101");

// 2. Bóc tách DTO từ Payload (hỗ trợ case-insensitive và xử lý chuỗi an toàn):
var hb = heartbeatMsg.GetPayload<HeartbeatPayload>();

// 3. Bóc tách phòng vệ an toàn (trả về bool, không throw exception khi gói tin lỗi cú pháp):
if (msg.TryGetPayload<CommandRequestPayload>(out var command))
{
    Console.WriteLine($"Thực thi lệnh: {command!.CommandLine}");
}
```

### Quản Lý Phiên Kết Nối Tự Động (`ConnectionManager` & `AgentSession`)

Máy chủ trung tâm `Master.Api` tích hợp dịch vụ quản lý phiên làm việc [`IConnectionManager`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/IConnectionManager.cs) hoạt động theo cơ chế an toàn đa luồng:
- **Tự động đăng ký (Auto-Registration):** Ngay khi một Agent thiết lập kết nối TCP socket thành công tới `TcpManagerServer`, một đối tượng [`AgentSession`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/AgentSession.cs) mới được tạo với mã định danh duy nhất (`sessionId`), lưu giữ luồng an toàn [`SafeFrameStream`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/SafeFrameStream.cs), địa chỉ đầu cuối và thời điểm kết nối.
- **Theo dõi sống còn & Định danh:** Tự động cập nhật `session.LastSeenAt` và gán `session.AgentId` khi nhận các gói tin `HeartbeatPayload` hoặc `AuthRequestPayload`.
- **Giải phóng triệt để chống rò rỉ (Zero Memory Leak):** Khối `finally` của vòng lặp xử lý kết nối luôn đảm bảo gọi `TryUnregister`. Bất kể client ngắt kết nối bình thường (Graceful EOF), đứt cáp đột ngột (Socket RST), hay server tắt, session luôn được gỡ bỏ $O(1)$ và kích hoạt sự kiện `SessionUnregistered`.
- **Hỗ trợ Broadcast:** Cung cấp phương thức `BroadcastAsync` gửi thông điệp song song tới toàn bộ các Agent đang trực tuyến.

### Bộ Điều Phối & Phân Loại Thông Điệp Chuẩn (`MasterMessageControl`)

Để cô lập hoàn toàn tầng truyền dẫn TCP socket khỏi tầng nghiệp vụ phân loại thông điệp (tuân thủ nguyên lý Separation of Concerns), `Master.Api` cung cấp thành phần [`IMasterMessageControl`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/IMasterMessageControl.cs) và lớp hiện thực [`MasterMessageControl`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/MasterMessageControl.cs):

- **Hai Mô Hình Điều Phối Linh Hoạt:**
  - `InvokeAsync(session, message, cancellationToken)` / `Invoke(...)`: Tiếp nhận thông điệp, cập nhật `session.LastSeenAt`, thực thi phân loại và sinh đối tượng `NetOpsMessage?` phản hồi thích hợp (trả về `null` nếu thông điệp không yêu cầu gửi phản hồi ngược).
  - `BeginInvoke(session, message, cancellationToken)`: Xử lý dạng bất đồng bộ không chặn (non-blocking dispatch). Tự động phân loại và gửi thông điệp phản hồi (nếu có) thông qua kênh `session.SendMessageAsync`. Được bọc chống lỗi toàn diện, tự động bắt ngoại lệ mạng và ghi log mà không làm đứt gãy tiến trình máy chủ.
- **Quy Chuẩn Phân Loại & Xử Lý Thông Điệp Chuẩn NetOps Protocol v0:**
  1. **`PING` $\to$ `PONG`:** Tự động phản hồi `"PONG_ACK"`, kế thừa `InReplyTo = message.MessageId` và bảo toàn `RequestId`.
  2. **`HEARTBEAT` $\to$ `HEARTBEAT_ACK`:** Cập nhật `session.AgentId` và `session.LastSeenAt`, phản hồi `HeartbeatAckPayload` (`Acknowledged = true`).
  3. **`AUTH` $\to$ `AUTH_OK` / `AUTH_FAILED`:** Đọc `AuthRequestPayload`, kiểm tra hợp lệ, gán `session.AgentId`, bật cờ `session.IsAuthenticated = true` và phản hồi `AuthResponsePayload` chứa `session.SessionId`. Trả về `AUTH_FAILED` nếu payload bị hỏng.
  4. **`METRICS` $\to$ Thu Thập Chỉ Số:** Bóc tách `MetricsPayload` (CPU %, RAM MB, Disk MB, Uptime, OS), ghi log chi tiết và kích hoạt sự kiện `MetricsReceived`. Trả về `null` để không phát sinh gói tin thừa trên đường truyền.
  5. **`COMMAND` / `COMMAND_OUTPUT` / `COMMAND_DONE`:** Ghi nhận tiến trình thực thi lệnh shell từ xa, kích hoạt các sự kiện `CommandOutputReceived` và `CommandDoneReceived`.
  6. **`UNSUPPORTED`:** Với bất kỳ loại thông điệp nào chưa được hỗ trợ, tự động phản hồi `ErrorMessagePayload` với mã lỗi chuẩn `UNSUPPORTED_MESSAGE_TYPE`.
- **Tích Hợp Trong `TcpManagerServer` & DI:**
  - [`TcpManagerServer`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/TcpManagerServer.cs) ủy thác toàn bộ việc xử lý thông điệp cho `IMasterMessageControl` thay vì sử dụng switch-case cục bộ, tăng cường tính module hóa và dễ kiểm thử.
  - Được đăng ký Singleton trong container DI của ASP.NET Core ([`Program.cs`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Program.cs)).

### Quản Lý Sống Còn & Dọn Dẹp Half-Open Socket (`SessionWatchdogService`)

Để giải quyết triệt để vấn đề kết nối mạng ngầm (**Half-Open Socket / Ghost Connection**) do đứt cáp vật lý, mất nguồn đột ngột hoặc NAT timeout mà không gửi cờ `FIN`/`RST`, `Master.Api` tích hợp dịch vụ nền [`SessionWatchdogService`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/SessionWatchdogService.cs):
- **Cơ chế hoạt động:** Sử dụng `PeriodicTimer` định kỳ quét toàn bộ danh sách phiên (`IConnectionManager.GetAllSessions()`) theo chu kỳ cấu hình (`WatchdogIntervalSeconds`, mặc định 5s).
- **Ngưỡng Timeout:** Tính toán độ trễ im ắng $\Delta t = \text{UtcNow} - \text{session.LastSeenAt}$. Nếu $\Delta t > \text{SessionTimeoutSeconds}$ (mặc định 15s - tương đương $3 \times$ chu kỳ Heartbeat):
  - Tự động gỡ bỏ session khỏi [`ConnectionManager`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/ConnectionManager.cs).
  - Cưỡng chế đóng socket vật lý (`TcpClient.Close()`) và giải phóng luồng kênh truyền ([`SafeStream.Dispose()`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/ISafeFrameChannel.cs)) để thu hồi triệt để descriptor của hệ điều hành, chống rò rỉ bộ nhớ.
- **Khả năng chịu lỗi:** Bọc bắt lỗi riêng lẻ từng session, đảm bảo sự cố cục bộ trên một kết nối không làm gián đoạn chu kỳ giám sát chung.

### Hệ Sinh Thái Dịch Vụ DI & Minimal API Endpoints

Toàn bộ dịch vụ được module hóa qua Extension Method [`MasterServiceExtensions.AddNetOpsMasterServer`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Extensions/MasterServiceExtensions.cs) và cung cấp các REST API Minimal quản trị:
- **Cấu hình Options Pattern:** Lớp [`MasterServerOptions`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Options/MasterServerOptions.cs) hỗ trợ cấu hình linh hoạt qua `appsettings.json` (`Port`, `WatchdogIntervalSeconds`, `SessionTimeoutSeconds`).
- **Minimal API Endpoints (OpenAPI Ready):**
  - `GET /api/health`: Trạng thái sức khỏe máy chủ, số kết nối TCP đang hoạt động, cổng dịch vụ và cấu hình Watchdog.
  - `GET /api/agents`: Danh sách các Agent đang trực tuyến, kèm `SessionId`, `AgentId`, `RemoteEndPoint`, `ConnectedAt`, `LastSeenAt`, và thời gian không hoạt động `IdleSeconds`.
  - `GET /api/agents/{sessionId}`: Thông tin chi tiết của một phiên kết nối theo ID.

---

## 💻 Ứng Dụng Client.WinForms

Ứng dụng quản trị giao diện Windows Forms được thiết kế theo phong cách trực quan, hiện đại:
1. **Dashboard & Health:**
   - Quản lý kết nối TCP Host/Port với đèn báo trạng thái badge.
   - Gửi gói tin Ping đo độ trễ mạng thực tế Round-Trip Time (RTT ms).
   - Gửi nhịp tim Heartbeat thủ công hoặc hẹn giờ gửi tự động (Auto Heartbeat mỗi 5s).
   - Thống kê thời gian thực: số gói gửi (OUT), số gói nhận (IN), hoạt động gần nhất.
2. **Remote Commands & Terminal Console:**
   - Thực thi các câu lệnh shell từ xa với timeout có thể cấu hình.
   - Các nút lệnh mẫu nhanh: `whoami`, `hostname`, `ipconfig`, `dir /b`.
   - Khung Terminal màu đen Console hiển thị kết quả thời gian thực (`stdout` màu xám/trắng, `stderr` màu đỏ, kết quả `CommandDone` và exit code màu ngọc/cam), tự động cuộn dòng.
3. **Protocol Frame Inspector:**
   - Giám sát luồng khung dữ liệu nhị phân trên đường truyền theo thời gian thực (giới hạn 500 gói gần nhất).
   - Phân tích chi tiết trường **Wire Length Header 4-byte Big-Endian** dạng Hex (`[ 0x00 0x00 0x00 0x48 ] -> 72 bytes`).
   - Khung xem nội dung JSON `NetOpsMessage` định dạng thụt dòng (Indented) kèm nút sao chép vào Clipboard.

---

## ⚡ Hướng Dẫn Biên Dịch & Khởi Chạy

### 1. Yêu cầu hệ thống
- .NET 10.0 SDK trở lên.
- Hệ điều hành: Windows 10/11 (dành cho `Client.WinForms`), Linux/Windows (dành cho `Master.Api`, `Agent.Worker`, `Shared.Contracts`).

### 2. Biên dịch Solution
```bash
# Khôi phục và biên dịch toàn bộ solution
dotnet build NetOps.slnx
```

### 3. Chạy kiểm thử tự động (Automated Test Suite)

Hệ thống sở hữu bộ kiểm thử tự động đa tầng với **195 test cases** đạt tỷ lệ thành công 100% trên cả hai cấu hình `Debug` và `Release`:

| Nhóm Kiểm Thử | Mục Tiêu & Kịch Bản | Lệnh Lọc (`--filter`) |
| :--- | :--- | :--- |
| **Tất cả kiểm thử** | Chạy toàn bộ 195 test cases trong solution | `dotnet test NetOps.slnx` |
| **Session Watchdog & DI** | Quét và dọn dẹp Half-Open Socket định kỳ, giải phóng SafeStream, nạp Options Pattern và phân giải DI Container | `dotnet test --filter "FullyQualifiedName~SessionWatchdogServiceTests"` |
| **Master Message Control** | Phân loại thông điệp chuẩn (Ping, Heartbeat, Auth, Metrics, Command), cơ chế `BeginInvoke` không chặn, tích hợp socket TCP E2E | `dotnet test --filter "FullyQualifiedName~MasterMessageControlTests"` |
| **Connection & Session Lifecycle** | Tự động đăng ký khi kết nối, gỡ session khi ngắt (EOF / Abrupt RST), cập nhật AgentId, tải đồng thời 15 clients | `dotnet test --filter "FullyQualifiedName~ConnectionManagerTests"` |
| **TCP E2E & Agent Integration** | Giao tiếp Socket TCP thực tế qua localhost: bắt tay, Ping/Pong RTT, Heartbeat, Graceful EOF, backoff reconnect | `dotnet test --filter "FullyQualifiedName~TcpIntegrationTests\|FullyQualifiedName~AgentIntegrationTests"` |
| **NetOps Client Service** | Vòng đời dịch vụ máy trạm, Request-Response correlation, Ping/Pong, timeout, frame logging | `dotnet test --filter "FullyQualifiedName~NetOpsClientServiceTests"` |
| **Concurrency & Stress** | Tải nặng 20-30 luồng đồng thời ghi qua `SafeFrameStream`, chống byte interleaving, Correlation | `dotnet test --filter "FullyQualifiedName~ConcurrencyStressTests"` |
| **Multi-Client Tests** | 15-20 clients kết nối đồng thời tới `TcpManagerServer`, dọn dẹp theo đợt, cô lập đứt kết nối | `dotnet test --filter "FullyQualifiedName~MultiClientConcurrencyTests"` |
| **Fault & Chaos Tests** | Phòng thủ OOM (>16MB), header âm/0, half-frame truncation, malformed JSON, server abort | `dotnet test --filter "FullyQualifiedName~NetworkFaultResilienceTests"` |
| **Protocol DTO Roundtrip** | Xác nhận serialize/deserialize roundtrip độc lập cho từng loại payload trong 9 DTOs | `dotnet test --filter "FullyQualifiedName~ProtocolDtoTests"` |
| **DTOs & Serialization** | Kiểm tra toàn diện 9 DTOs chuẩn hóa, camelCase, bỏ qua null, factory methods | `dotnet test --filter "FullyQualifiedName~DtoPayloadTests"` |
| **Protocol & Framing** | Kiểm tra tương thích ngược, cấu trúc header 4-byte Big-Endian, boundary Min/Max | `dotnet test --filter "FullyQualifiedName~FramingTests\|FullyQualifiedName~ProtocolTests"` |

```bash
# Thực thi toàn bộ test suite ở cấu hình Release tối ưu hóa
dotnet test NetOps.slnx --configuration Release
```

### 4. Khởi chạy các thành phần
```bash
# Khởi động máy chủ Master
dotnet run --project src/Master.Api

# Khởi động tiến trình Agent Worker
dotnet run --project src/Agent.Worker

# Khởi động ứng dụng quản trị Client WinForms
dotnet run --project src/Client.WinForms
```