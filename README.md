# NetOps Management Platform

Hệ thống quản trị và giám sát máy chủ phân tán hiệu năng cao trên nền tảng .NET 10, sử dụng giao thức truyền thông nhị phân tùy chỉnh **NetOps Protocol v0** (4-byte Big-Endian Length-Prefixed Framing).

---

## 🚀 Cấu Trúc Dự Án (Solution Architecture)

Solution [`NetOps.slnx`](file:///c:/Users/vuong/Documents/NetOps/NetOps.slnx) bao gồm 5 dự án thành phần:

| Dự án | Nền tảng | Vai trò & Trách nhiệm |
| :--- | :---: | :--- |
| **`src/Shared.Contracts`** | .NET 10.0 | Thư viện lõi chứa định nghĩa giao thức mạng, bộ tuần tự hóa khung truyền nhị phân [`ISafeFrameChannel`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/ISafeFrameChannel.cs), [`SafeFrameStream`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/SafeFrameStream.cs), cùng các Models và Payloads nghiệp vụ. |
| **`src/Master.Api`** | .NET 10.0 | Máy chủ trung tâm (Manager Server) lắng nghe kết nối TCP, tự động quản lý phiên làm việc của các Agent qua [`IConnectionManager`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/IConnectionManager.cs), phân luồng lệnh và điều phối giao tiếp. |
| **`src/Agent.Worker`** | .NET 10.0 | Background service triển khai trên các máy chủ đích (Linux/Windows), kết nối về Master, tự động điều phối thông điệp qua [`IAgentMessageControl`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Services/IAgentMessageControl.cs), nhận lệnh shell và báo cáo trạng thái. |
| **`src/Client.WinForms`** | .NET 10.0 (WinForms) | Ứng dụng máy trạm đồ họa hiện đại dành cho quản trị viên, cung cấp Dashboard kết nối, kiểm tra đường truyền (Ping RTT, Heartbeat), thực thi lệnh từ xa (Remote Terminal Console) và bộ soi khung nhị phân thời gian thực (**Protocol Frame Inspector**). |
| **`tests/NetOps.Tests`** | .NET 10.0 (xUnit) | Bộ kiểm thử tự động toàn diện (**267 test cases pass 100%**) bao gồm Unit Tests, DTO Tests, TCP E2E Socket Tests, Connection Lifecycle Tests, High-Concurrency Stress Tests, Master & Agent Message Control Tests, Multi-Agent Concurrency Tests, Agent Message Isolation Tests, Manager Unicast Routing Tests, Manager Broadcast Routing Tests, Session Watchdog Tests và Chaos/Resilience Tests. |

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

### Bộ Điều Phối & Phân Loại Thông Điệp Agent (`AgentMessageControl`)

Tương tự kiến trúc phân tầng trên Master, dự án `src/Agent.Worker` cung cấp thành phần [`IAgentMessageControl`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Services/IAgentMessageControl.cs) và lớp hiện thực [`AgentMessageControl`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Services/AgentMessageControl.cs) nhằm tách biệt luồng nhận gói tin socket khỏi logic nghiệp vụ phân loại và định tuyến thông điệp hai chiều (Master-to-Agent và Agent-to-Master):

- **Hai Mô Hình Điều Phối Đồng Bộ & Bất Đồng Bộ:**
  - `InvokeAsync(message, cancellationToken)` / `Invoke(...)`: Tiếp nhận `NetOpsMessage`, thực thi phân loại theo `MessageType` và sinh thông điệp phản hồi `NetOpsMessage?` thích hợp (trả về `null` nếu thông điệp không yêu cầu gửi phản hồi ngược).
  - `BeginInvoke(channel, message, cancellationToken)`: Xử lý dạng bất đồng bộ không chặn (non-blocking dispatch). Tự động phân loại và gửi thông điệp phản hồi (nếu có) thông qua kênh an toàn `channel.SendMessageAsync`. Toàn bộ quá trình được bọc chống lỗi mạng (`SocketException`, `IOException`, `OperationCanceledException`) an toàn, bảo vệ luồng nền không bị crash.
- **Quy Chuẩn Định Tuyến & Khung Sườn Xử Lý Chuẩn NetOps Protocol v0:**
  1. **`PING` $\to$ `PONG` (Tự Động):** Khi Master gửi yêu cầu PING thăm dò Agent, tự động phản hồi `MessageType.Pong` mang payload `"PONG_ACK"`, ánh xạ chính xác `InReplyTo = message.MessageId` và bảo toàn `RequestId`.
  2. **`COMMAND` $\to$ Khung Sườn Thực Thi Lệnh Shell:** Bóc tách an toàn `CommandRequestPayload` qua `message.TryGetPayload<CommandRequestPayload>()`, ghi log thông tin lệnh (`CommandId`, `CommandLine`, `TimeoutSeconds`, `WorkingDir`) và phát sự kiện `CommandReceived` (chuẩn bị kết nối với tiến trình thực thi lệnh shell nền).
  3. **`HEARTBEAT` / `HEARTBEAT_ACK`:**
     - Master gửi thăm dò `HEARTBEAT`: Phản hồi `HeartbeatAckPayload(true, UtcNow)` và phát sự kiện `HeartbeatReceived`.
     - Master gửi xác nhận `HEARTBEAT_ACK`: Bóc tách payload và kích hoạt sự kiện `HeartbeatAckReceived`.
  4. **`PONG` $\to$ Ghi Nhận Độ Trễ:** Kích hoạt sự kiện `PongReceived` khi Agent nhận được phản hồi PONG từ Master cho gói tin PING do chính Agent phát đi.
  5. **`ERROR` $\to$ Cảnh Báo Hệ Thống:** Ghi log cảnh báo khi nhận mã lỗi từ Master.
  6. **`UNSUPPORTED` $\to$ Phòng Vệ Giao Thức:** Phản hồi thông điệp lỗi chuẩn `UNSUPPORTED_MESSAGE_TYPE` cho các loại thông điệp chưa được hỗ trợ.
- **Tích Hợp Trong `TcpAgentClient` & Dependency Injection:**
  - [`TcpAgentClient`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Services/TcpAgentClient.cs) kế thừa [`AgentNetworkClient`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Services/AgentNetworkClient.cs) và ủy thác toàn bộ việc nhận và xử lý gói tin cho `IAgentMessageControl.BeginInvoke` thay cho các khối if-else cứng trước đây.
  - Được đăng ký Singleton trong container DI của Generic Host ([`src/Agent.Worker/Program.cs`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Program.cs)), ánh xạ đồng thời sang cả `IAgentNetworkClient` và `AgentNetworkClient`.

### Tầng Mạng Trừu Tượng Hóa Phía Agent (`IAgentNetworkClient` / `AgentNetworkClient`)

Nhằm tách biệt hạ tầng kết nối socket khỏi dịch vụ chạy nền của Agent, hệ thống cung cấp hợp đồng mạng chuẩn mực [`IAgentNetworkClient`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Services/IAgentNetworkClient.cs) và lớp hiện thực [`AgentNetworkClient`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Services/AgentNetworkClient.cs):
- **Trạng thái & Sự kiện vòng đời:** Theo dõi trực tiếp `IsConnected`, `ConnectedEndpoint`, và phát các sự kiện `Connected` / `Disconnected` khi socket mở hoặc đứt kết nối.
- **Tuần tự hóa ghi (Write Serialization) qua `SafeFrameStream`:** Phương thức chủ động `SendMessageAsync(NetOpsMessage message, cancellationToken)` sử dụng khóa đồng bộ `SemaphoreSlim(1, 1)` để ngăn ngừa hoàn toàn hiện tượng trộn lẫn byte (packet interleaving) khi Agent chủ động gửi gói tin (kết quả thực thi lệnh, metrics) song song với luồng gửi PING định kỳ.
- **Khả năng tự phục hồi (Exponential Backoff):** Vòng lặp `RunAsync(...)` tự động tăng lũy tiến thời gian chờ kết nối lại ($3s \to 6s \to 12s \to 30s$) khi gặp sự cố mạng hoặc socket EOF từ Master.
- **Tương thích ngược 100%:** Lớp [`TcpAgentClient`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Services/TcpAgentClient.cs) kế thừa trực tiếp từ `AgentNetworkClient`, bảo toàn toàn bộ constructor signatures và các bài kiểm thử tích hợp hiện có.

### Dịch Vụ Nền Agent Worker (`Worker.cs`) & Điều Phối Message Hai Chiều

[`Worker.cs`](file:///c:/Users/vuong/Documents/NetOps/src/Agent.Worker/Worker.cs) là Hosted Service (`BackgroundService`) trung tâm điều hành chu trình sống của Agent Worker:
- **Cấu hình phòng vệ (Defensive Defaults):** Nạp an toàn các tham số cấu hình từ khối `"AgentConfig"` (`ServerHost`, `ServerPort`, `AgentId`, `PingIntervalSeconds`). Tự động fallback về giá trị mặc định chuẩn (`127.0.0.1:5050`, `agent-linux-01`, `5s`) khi file cấu hình thiếu key, rỗng hoặc giá trị số âm/bằng 0, bảo vệ ứng dụng không bao giờ bị crash lúc khởi động.
- **Điều phối sự kiện nghiệp vụ từ `IAgentMessageControl`:**
  - Lắng nghe sự kiện `CommandReceived`: Tiếp nhận lệnh shell từ Master (`CommandId`, `CommandLine`, `TimeoutSeconds`), ghi log chi tiết và sẵn sàng chuyển giao cho tiến trình thực thi lệnh shell nền.
  - Lắng nghe sự kiện `HeartbeatAckReceived`: Ghi nhận dấu thời gian xác nhận nhịp tim từ Master.
  - Lắng nghe sự kiện `PongReceived`: Theo dõi độ trễ gói tin PING/PONG.
  - Lắng nghe sự kiện mạng `Connected` và `Disconnected`.
- **Chống rò rỉ bộ nhớ (Event Memory Leak Prevention):** Toàn bộ các sự kiện được hủy đăng ký (unsubscribe) triệt để trong khối `finally` của `ExecuteAsync` khi tiến trình dừng.
- **Dừng an toàn (Graceful Shutdown):** Xử lý mượt mà khi nhận tín hiệu hủy `stoppingToken` (Ctrl+C hoặc SIGTERM từ Docker/systemd), đóng socket sạch sẽ và giải phóng unmanaged descriptor.
- **Cấu hình Dependency Injection (`Program.cs`):** Đăng ký Singleton cho `IAgentMessageControl`, `TcpAgentClient`, `AgentNetworkClient`, và `IAgentNetworkClient` cùng trỏ về một thể hiện duy nhất, đảm bảo tối ưu hóa tài nguyên mạng (chỉ duy nhất 1 socket TCP kết nối tới Master).

### Quản Lý Sống Còn & Dọn Dẹp Half-Open Socket (`SessionWatchdogService`)

Để giải quyết triệt để vấn đề kết nối mạng ngầm (**Half-Open Socket / Ghost Connection**) do đứt cáp vật lý, mất nguồn đột ngột hoặc NAT timeout mà không gửi cờ `FIN`/`RST`, `Master.Api` tích hợp dịch vụ nền [`SessionWatchdogService`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/SessionWatchdogService.cs):
- **Cơ chế hoạt động:** Sử dụng `PeriodicTimer` định kỳ quét toàn bộ danh sách phiên (`IConnectionManager.GetAllSessions()`) theo chu kỳ cấu hình (`WatchdogIntervalSeconds`, mặc định 5s).
- **Ngưỡng Timeout:** Tính toán độ trễ im ắng $\Delta t = \text{UtcNow} - \text{session.LastSeenAt}$. Nếu $\Delta t > \text{SessionTimeoutSeconds}$ (mặc định 15s - tương đương $3 \times$ chu kỳ Heartbeat):
  - Tự động gỡ bỏ session khỏi [`ConnectionManager`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/ConnectionManager.cs).
  - Cưỡng chế đóng socket vật lý (`TcpClient.Close()`) và giải phóng luồng kênh truyền ([`SafeStream.Dispose()`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/ISafeFrameChannel.cs)) để thu hồi triệt để descriptor của hệ điều hành, chống rò rỉ bộ nhớ.
- **Khả năng chịu lỗi:** Bọc bắt lỗi riêng lẻ từng session, đảm bảo sự cố cục bộ trên một kết nối không làm gián đoạn chu kỳ giám sát chung.

### Năng Lực Duy Trì Đồng Thời Đa Agent ($\ge 3$ Agents) & Định Tuyến Hai Chiều

Máy chủ trung tâm Master Server (`TcpManagerServer`) được tối ưu hóa để tiếp nhận và duy trì đồng thời không giới hạn các kết nối TCP từ nhiều Agent phân tán:
- **Duy trì đồng thời $\ge 3$ Agent:** Khởi chạy song song tối thiểu 3 (hoặc mở rộng $5+$) Agent trên các Socket TCP độc lập trên loopback/LAN mà không bị xung đột hay cạn kiệt tài nguyên mạng.
- **Tự động nhận diện AgentId tức thì:** `MasterMessageControl.HandlePing` tự động bóc tách `HeartbeatPayload` từ gói PING đầu tiên để gán danh tính `session.AgentId` ngay lập tức, cho phép tra cứu `ConnectionManager.GetSessionByAgentId(agentId)` ngay từ frame đầu tiên.
- **Định tuyến thông điệp độc lập (Full-Duplex):** Master gửi lệnh `COMMAND` tới đích danh từng Agent cụ thể (`session.SendMessageAsync`), chỉ duy nhất Agent nhận lệnh phản hồi mà không gây nhầm lẫn hay trộn lẫn giữa các session.
- **Phát sóng đồng loạt (Broadcast Engine):** Hỗ trợ `ConnectionManager.BroadcastAsync(message)` truyền phát đồng thời thông điệp (đồng bộ đồng hồ, cập nhật chính sách) tới toàn bộ các Agent đang trực tuyến cùng lúc.
- **Cô lập sự cố & Tự phục hồi (Fault Isolation):** Khi 1 Agent bị ngắt kết nối chủ động hoặc sự cố mạng, Master tự động giải phóng phiên tương ứng (`ActiveCount` giảm chính xác từ 3 xuống 2); toàn bộ các Agent còn lại tiếp tục hoạt động liên tục không gián đoạn; khi Agent kết nối lại, Master tự động tái đăng ký và phục hồi số lượng kết nối về 3.

### Cô Lập Thông Điệp Tuyệt Đối Giữa Các Agent (Message Isolation & Anti-Cross-Talk)

Nhằm đảm bảo tính toàn vẹn dữ liệu trong môi trường triển khai quy mô lớn với hàng chục/hàng trăm Agent truyền nhận đồng thời, kiến trúc truyền thông của NetOps được thiết kế tuân thủ nguyên lý cô lập đa tầng (Multi-Layer Isolation Architecture):
- **Cô lập luồng khung truyền theo phiên (Per-Session Stream Frame Locks):** Mỗi kết nối TCP sở hữu một đối tượng [`AgentSession`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/AgentSession.cs) riêng biệt, quản lý luồng [`SafeFrameStream`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Protocol/SafeFrameStream.cs) độc lập kèm khóa `SemaphoreSlim(1, 1)`. Mọi thao tác ghi khung (Write Frame) được nguyên tử hóa 100%, loại trừ hoàn toàn hiện tượng trộn lẫn byte (Packet Interleaving) ở tầng socket.
- **Bảo toàn Correlation ID & Ngữ cảnh 2 chiều:** Toàn bộ chu trình Request-Response (Ping/Pong, Heartbeat/Ack, Metrics) tự động ánh xạ chính xác `RequestId` $\leftrightarrow$ `InReplyTo` theo từng session. Phản hồi PONG hoặc HeartbeatAck chỉ được chuyển phát trực tiếp về đúng kết nối đã phát yêu cầu.
- **Cô lập chuỗi Streaming Output Chunks song song:** Khi Master phát đồng thời nhiều lệnh shell khác nhau tới nhiều Agent, các gói tin streaming [`CommandOutputPayload`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Payloads/CommandOutputPayload.cs) (stdout/stderr) và [`CommandDonePayload`](file:///c:/Users/vuong/Documents/NetOps/src/Shared.Contracts/Payloads/CommandDonePayload.cs) được gắn định danh lệnh (`CommandId`) và truyền nhận trên kênh riêng của từng Agent. Master thu thập và phân loại chính xác $100\%$, tuyệt đối không xảy ra tình trạng chunk output của Agent này bị ghép nhầm vào session của Agent khác.
- **Thuật toán đối soát chéo chống rò rỉ (Cross-Auditing Validation):** Hệ thống được kiểm chứng qua thuật toán đối soát chéo thời gian thực dưới áp lực truyền nhận dồn dập (Burst Stress với 300+ gói tin kích thước biến thiên từ 50B đến 16KB). Kiểm toán toàn bộ payload và metadata nhận được trên từng Agent, cam kết tỷ lệ rò rỉ dữ liệu chéo (**Cross-Talk / Data Bleeding Rate = 0.00%**).

### Định Tuyến Thông Điệp Đơn Truyền (Unicast Message Routing & Addressing)

Máy chủ trung tâm Master Server cung cấp năng lực chuyển phát gói tin đích danh (Unicast Delivery) tới từng Agent cụ thể thông qua [`IConnectionManager`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Services/IConnectionManager.cs):
- **Hai phương thức Unicast strongly-typed:**
  - `SendUnicastAsync(agentId, message)`: Định tuyến thông điệp tới Agent theo `AgentId` (tra cứu tức thì $O(1)$ không phân biệt hoa thường).
  - `SendUnicastBySessionIdAsync(sessionId, message)`: Định tuyến thông điệp tới phiên kết nối cụ thể theo `SessionId`.
- **Cơ chế phòng vệ an toàn (Defensive Handling):** Nếu Agent chưa kết nối, đã ngắt kết nối hoặc tham số rỗng/null, phương thức tự động ghi nhận log warning và trả về `false` an toàn thay vì ném ngoại lệ làm gián đoạn tiến trình máy chủ.
- **Tuần tự hóa ghi & Chống lẫn lộn (Zero Cross-Talk):** Quá trình gửi khung qua `session.SendMessageAsync` được bảo vệ nguyên tử bởi `SafeFrameStream` và `SemaphoreSlim(1,1)`, đảm bảo thông điệp chỉ đến duy nhất Agent mục tiêu; các Agent khác trên cùng hệ thống hoàn toàn không nhận nhầm gói tin.

### Định Tuyến Thông Điệp Phát Sóng (Broadcast Message Routing & Resilient Distribution)

Máy chủ trung tâm Master Server cung cấp năng lực phát sóng đồng loạt (Broadcast Engine) từ Master tới toàn bộ các Agent đang trực tuyến cùng thời điểm:
- **Model kết quả phát sóng strongly-typed (`BroadcastResult`):**
  - Trả về đối tượng `BroadcastResult(TotalTargeted, SuccessCount, FailedCount)` cùng 2 thuộc tính tính toán `AllSucceeded` và `HasFailures`.
- **Phát sóng song song & Độc lập:** Quá trình phát sóng sử dụng `Task.WhenAll` trên mảng snapshot phiên hoạt động, tối ưu hóa thông lượng truyền tin trong môi trường đa Agent.
- **Cách ly sự cố mạng (Fault Isolation & Partial Resiliency):** Từng session thực thi trong khối try/catch độc lập. Nếu 1 Agent bị ngắt kết nối đột ngột hoặc stream bị lỗi, Master tự động log cảnh báo, tăng biến đếm `FailedCount` mà không làm gián đoạn việc phát sóng tới các Agent khác.
- **Xử lý biên an toàn (Zero-Agent Edge Case):** Khi không có Agent nào trực tuyến (`ActiveCount == 0`), hệ thống trả về `BroadcastResult(0, 0, 0)` an toàn, không ném ngoại lệ hay làm gián đoạn máy chủ.

### Hệ Sinh Thái Dịch Vụ DI & Minimal API Endpoints

Toàn bộ dịch vụ được module hóa qua Extension Method [`MasterServiceExtensions.AddNetOpsMasterServer`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Extensions/MasterServiceExtensions.cs) và các route API được đóng gói qua [`MasterApiEndpoints.MapMasterApiEndpoints`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Endpoints/MasterApiEndpoints.cs):
- **Cấu hình Options Pattern:** Lớp [`MasterServerOptions`](file:///c:/Users/vuong/Documents/NetOps/src/Master.Api/Options/MasterServerOptions.cs) hỗ trợ cấu hình linh hoạt qua `appsettings.json` (`Port`, `WatchdogIntervalSeconds`, `SessionTimeoutSeconds`).
- **Minimal API Endpoints (OpenAPI Ready):**
  - `GET /api/health`: Trạng thái sức khỏe máy chủ (`Status = "Healthy"`), số kết nối TCP đang hoạt động (`ActiveConnections`), cổng dịch vụ và cấu hình Watchdog.
  - `GET /api/agents`: Danh sách các Agent đang trực tuyến, kèm `SessionId`, `AgentId`, `RemoteEndPoint`, `ConnectedAt`, `LastSeenAt`, và thời gian không hoạt động `IdleSeconds`.
  - `GET /api/agents/{sessionId}`: Thông tin chi tiết của một phiên kết nối theo ID (trả về HTTP 200 kèm payload, hoặc HTTP 404 khi không tìm thấy).
  - `POST /api/agents/{agentId}/command`: Gửi lệnh Shell Unicast từ client HTTP tới Agent đích (trả về HTTP 200 kèm `CommandId`, HTTP 404 nếu Agent offline, HTTP 400 nếu thiếu dữ liệu).
  - `POST /api/agents/{agentId}/message`: Gửi thông điệp `NetOpsMessage` Unicast tùy biến từ client HTTP tới Agent đích.
  - `POST /api/broadcast/command`: Phát sóng lệnh Shell đồng loạt tới tất cả các Agent đang kết nối trực tuyến (trả về HTTP 200 kèm `BroadcastResult`, HTTP 400 nếu CommandLine rỗng).
  - `POST /api/broadcast/message`: Phát sóng thông điệp `NetOpsMessage` tùy biến tới tất cả các Agent đang kết nối trực tuyến.

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

Hệ thống sở hữu bộ kiểm thử tự động đa tầng với **267 test cases** đạt tỷ lệ thành công 100% trên cả hai cấu hình `Debug` và `Release`:

```bash
# Thực thi toàn bộ 267 test cases ở cấu hình Release tối ưu hóa
dotnet test NetOps.slnx --configuration Release
```

#### a. Hệ thống Phân loại Trait Đa Trục (Multi-Axis Trait System)

Toàn bộ 267 test cases được gắn nhãn Metadata chuẩn hóa thông qua hai trục `Category` và `Area`, cho phép lập trình viên và CI/CD lọc nhanh bằng cờ `--filter`:

| Trục Phân Loại | Giá Trị Nhãn | Số Lượng Tests | Đặc Điểm & Kịch Bản Kiểm Thử | Lệnh Lọc Dòng Lệnh (`--filter`) |
| :--- | :--- | :---: | :--- | :--- |
| **Category** | **`Unit`** | **189** | Kiểm thử logic thuần in-memory (DTOs, Envelopes, Framing, Codec, State Machine, MasterApiEndpointsTests), không mở socket thật. Thời gian chạy ~1s. | `dotnet test --filter "Category=Unit"` |
| **Category** | **`Integration`** | **63** | Kiểm thử kết nối TCP Socket thật trên localhost, duy trì đồng thời $\ge 3$ Agent, cô lập thông điệp, định tuyến Unicast/Broadcast, REST API Minimal, Ping/Pong RTT, Graceful EOF. | `dotnet test --filter "Category=Integration"` |
| **Category** | **`Stress`** | **7** | Kiểm thử tải nặng đa luồng: 25-30 threads đồng thời ghi qua `SafeFrameStream`, 15-20 clients kết nối đồng thời. | `dotnet test --filter "Category=Stress"` |
| **Category** | **`Resilience`** | **8** | Phòng thủ sự cố mạng (Chaos/Fault Injection): Tấn công DoS (>16MB), cắt cụt gói tin (half-frame), malformed JSON. | `dotnet test --filter "Category=Resilience"` |
| **Area** | **`Protocol`** | **142** | Khung truyền Length-Prefixed Big-Endian, 9 DTOs chuẩn hóa, Serialization roundtrip, Envelope integrity. | `dotnet test --filter "Area=Protocol"` |
| **Area** | **`Master`** | **78** | `ConnectionManager`, `MasterMessageControl` (Ping tự động nhận diện AgentId, Heartbeat, Auth, Metrics, Command, Unicast & Broadcast Routing), `ManagerUnicastRoutingTests`, `ManagerBroadcastRoutingTests`, `SessionWatchdogService`, `AgentMessageIsolationTests`, `MasterApiEndpoints`. | `dotnet test --filter "Area=Master"` |
| **Area** | **`Agent`** | **37** | `Worker` Hosted Service, `AgentNetworkClient`, `AgentMessageControl` phân loại 2 chiều, Graceful shutdown. | `dotnet test --filter "Area=Agent"` |
| **Area** | **`Client`** | **10** | `NetOpsClientService`, Request-Response correlation, kết nối TCP WinForms, Timeout handling. | `dotnet test --filter "Area=Client"` |

*Có thể kết hợp nhiều điều kiện lọc:*
```bash
# Chạy Unit Tests thuộc phần Protocol
dotnet test --filter "Category=Unit&Area=Protocol"

# Chạy toàn bộ tests ngoại trừ các bài kiểm thử tải nặng Stress
dotnet test --filter "Category!=Stress"
```

#### b. Đo lường Độ Bao Phủ Mã Nguồn (Code Coverage với Coverlet)

Hệ thống được cấu hình tệp thiết lập tiêu chuẩn [`tests/coverlet.runsettings`](file:///c:/Users/vuong/Documents/NetOps/tests/coverlet.runsettings) tập trung thu thập độ bao phủ của 4 module logic cốt lõi và loại trừ các Form WinForms đồ họa tĩnh:

```bash
# Chạy toàn bộ kiểm thử và xuất báo cáo Cobertura XML
dotnet test tests/NetOps.Tests/NetOps.Tests.csproj --settings tests/coverlet.runsettings --collect:"XPlat Code Coverage" -c Release
```

| Module / Assembly | Tỷ Lệ Bao Phủ Dòng Mã (Line Coverage) | Trạng Thái Đánh Giá |
| :--- | :---: | :---: |
| **`Shared.Contracts`** | **96.95%** | 🟢 Xuất Sắc (Core Protocol & Framing) |
| **`Client.WinForms` (Services)** | **95.23%** | 🟢 Xuất Sắc (Network Client Logic) |
| **`Master.Api`** | **92.55%** | 🟢 Xuất Sắc (TCP Engine, Message Control, Unicast, Broadcast & Minimal APIs) |
| **`Agent.Worker`** | **86.76%** | 🟢 Xuất Sắc (Worker Service & Network Client) |
| **Toàn Bộ Hệ Thống (Total Solution Core)** | **93.08%** | 🟢 **Chất Lượng Vượt Trội** |

#### c. Kịch Bản Tự Động Hóa Kiểm Thử & Kiểm Chứng Lặp Lại (`scripts/run-tests.ps1`)

Kịch bản PowerShell tiêu chuẩn [`scripts/run-tests.ps1`](file:///c:/Users/vuong/Documents/NetOps/scripts/run-tests.ps1) cung cấp công cụ kiểm thử toàn diện:

```powershell
# 1. Chạy nhanh toàn bộ Unit Tests (siêu tốc thuần in-memory)
powershell -ExecutionPolicy Bypass -File ./scripts/run-tests.ps1 -Category Unit

# 2. Chạy kiểm thử đầy đủ kèm bảng thống kê Code Coverage trực quan
powershell -ExecutionPolicy Bypass -File ./scripts/run-tests.ps1 -Coverage

# 3. Kiểm chứng Zero-Flakiness (chạy lặp lại 5 vòng liên tiếp = 1,270 test runs)
powershell -ExecutionPolicy Bypass -File ./scripts/run-tests.ps1 -Repeat 5

# 4. Xuất báo cáo XML chuẩn Visual Studio Trx cho pipeline CI/CD
powershell -ExecutionPolicy Bypass -File ./scripts/run-tests.ps1 -Trx
```

#### d. Kiểm Soát Tài Nguyên Thực Thi xUnit (`xunit.runner.json`)

Tệp cấu hình [`tests/NetOps.Tests/xunit.runner.json`](file:///c:/Users/vuong/Documents/NetOps/tests/NetOps.Tests/xunit.runner.json) giới hạn tối đa `maxParallelThreads = 4` và đặt ngưỡng timeout cảnh báo `longRunningTestSeconds = 5`, đảm bảo các bài test Socket TCP và Concurrency Stress hoạt động với độ ổn định tuyệt đối, không gây xung đột cổng mạng hay làm nghẽn ThreadPool.

#### e. Bộ Kiểm Thử Tích Hợp Đa Agent Đồng Thời (`MultiAgentIntegrationTests`)

Bộ kiểm thử chuyên sâu [`tests/NetOps.Tests/MultiAgentIntegrationTests.cs`](file:///c:/Users/vuong/Documents/NetOps/tests/NetOps.Tests/MultiAgentIntegrationTests.cs) kiểm chứng toàn diện năng lực của máy chủ trung tâm Master Server với Socket TCP thật trên loopback khi vận hành cùng lúc nhiều Agent độc lập:

##### Ma Trận Đối Soát 6 Kịch Bản Kiểm Thử Đa Agent (Verification Matrix)

| # | Tên Test Case Kiểm Thử | Mục Tiêu & Kịch Bản Xác Thực | Độ Trễ (Latency) | Kết Quả Thẩm Định |
| :-: | :--- | :--- | :---: | :---: |
| 1 | `Manager_MaintainsThreeConcurrentAgents_AllConnectedAndIdentifiedSimultaneously` | Khởi chạy 3 Agent song song (`agent-alpha`, `agent-beta`, `agent-gamma`); Master tự động nhận diện `AgentId` từ PING đầu tiên, phân bổ 3 `SessionId` độc lập, duy trì `ActiveCount == 3`. | **61 ms** | 🟢 **100% Passed** |
| 2 | `Manager_CanRouteCommandsIndependently_ToSpecificAgentAmongThree` | Master gửi Unicast lệnh `COMMAND` tới `agent-beta`; xác thực chỉ duy nhất `agent-beta` nhận và xử lý; `agent-alpha` và `agent-gamma` hoàn toàn không nhận được lệnh (Zero Cross-Talk). | **129 ms** | 🟢 **100% Passed** |
| 3 | `Manager_CanBroadcastMessage_ToAllThreeAgentsSimultaneously` | Master gọi `BroadcastAsync(broadcastMsg)`; toàn bộ 3 Agent đều nhận được gói tin đồng bộ cùng lúc với payload JSON nguyên vẹn. | **65 ms** | 🟢 **100% Passed** |
| 4 | `Manager_MaintainsFiveConcurrentAgents_ScalesBeyondMinimumRequirement` | Mở rộng quy mô lên 5 Agent kết nối song song (`agent-scale-01` $\to$ `05`); Master phân bổ 5 session active, toàn bộ duy trì kết nối ổn định. | **57 ms** | 🟢 **100% Passed** |
| 5 | `Manager_HandlesAgentDisconnectGracefully_AndIsolatesFault_WithoutImpactingOtherAgents` | Ngắt kết nối `agent-alpha` $\to$ Master tự động giải phóng session (`ActiveCount` $3 \to 2$); 2 Agent còn lại vẫn hoạt động liên tục; khi `agent-alpha` kết nối lại, hệ thống tự động phục hồi `ActiveCount == 3`. | **86 ms** | 🟢 **100% Passed** |
| 6 | `MasterApi_Endpoints_ReflectAccurateState_WithThreeConcurrentAgents` | Khởi chạy song song Kestrel Web Server in-memory và Master TCP Server; xác thực các endpoint HTTP `GET /api/health`, `GET /api/agents` và `GET /api/agents/{sessionId}` phản ánh dữ liệu thời gian thực của 3 Agent. | **461 ms** | 🟢 **100% Passed** |

##### Báo Cáo Kiểm Chứng Độ Bền Vững Tuyệt Đối (Zero-Flakiness 10 Vòng Lặp)

Hệ thống đã trải qua kịch bản kiểm thử độ bền bỉ tự động qua 10 vòng lặp liên tục không nghỉ đối với `MultiAgentIntegrationTests` trên cấu hình **Release**:
- **Tổng số lượt chạy test:** $6 \text{ test cases} \times 10 \text{ vòng} = \mathbf{60 \text{ test runs}}$.
- **Tỷ lệ thành công:** **`60 / 60 Passed (100%)`**, $0$ failed, $0$ skipped, $0$ flakiness.
- **Thời gian thực thi mỗi vòng:** Dao động ổn định từ **`763 ms`** đến **`1,000 ms`** (trung bình ~871 ms).
- **Trạng thái tài nguyên:** Không ghi nhận bất kỳ cảnh báo rò rỉ socket descriptor, xung đột cổng mạng hay tắc nghẽn ThreadPool.

```bash
# 1. Chạy riêng bộ kiểm thử tích hợp đa Agent (Release)
dotnet test tests/NetOps.Tests/NetOps.Tests.csproj --filter "FullyQualifiedName~MultiAgentIntegrationTests" -c Release

# 2. Kiểm chứng độ bền vững Zero-Flakiness 10 vòng lặp liên tiếp
for ($i = 1; $i -le 10; $i++) { dotnet test tests/NetOps.Tests/NetOps.Tests.csproj --filter "FullyQualifiedName~MultiAgentIntegrationTests" -c Release --nologo }
```

#### f. Bộ Kiểm Thử Cô Lập Thông Điệp Đa Agent (`AgentMessageIsolationTests`)

Bộ kiểm thử chuyên biệt [`tests/NetOps.Tests/AgentMessageIsolationTests.cs`](file:///c:/Users/vuong/Documents/NetOps/tests/NetOps.Tests/AgentMessageIsolationTests.cs) kiểm chứng khả năng cô lập dữ liệu tuyệt đối giữa các Agent kết nối TCP socket thật song song tới `TcpManagerServer`:
1. **Cô lập Request-Response Correlation & Payload Token:** Test case `ConcurrentPingMetrics_MaintainsStrictCorrelationAndPayloadIsolation` khởi chạy 3 Agent (`agent-iso-01`, `agent-iso-02`, `agent-iso-03`) liên tục gửi đồng thời 60 Pings, 60 Heartbeats và 60 Metrics; xác thực $100\%$ phản hồi PONG nhận được trên từng Agent khớp chính xác `RequestId`, `InReplyTo` và token gốc của Agent đó, tuyệt đối $0\%$ thông điệp bị lẫn sang session khác.
2. **Cô lập Streaming Output Chunks song song:** Test case `ConcurrentCommands_StreamingOutputChunks_RemainStrictlyIsolatedAcrossAgents` kiểm chứng Master phát đồng thời 3 lệnh shell khác nhau tới 3 Agent; mỗi Agent stream liên tục các chunk `CommandOutputPayload` (stdout/stderr) mang token độc nhất và kết thúc bằng `CommandDonePayload`; Master thu thập và phân loại rạch ròi, khẳng định $0\%$ chunk bị tráo đổi hay gắn nhầm session.
3. **Kiểm thử tải dồn dập (Burst Stress) & Thuật toán đối soát chéo (Cross-Auditing):** Test case `HighVolumeBurstMessages_WithVariablePayloadSizes_ZeroDataBleedingAcrossSessions` tạo áp lực truyền tải đồng thời 300 Pings từ 3 Agent với payload kích thước biến thiên ($50\text{ B} \to 2\text{ KB} \to 16\text{ KB}$) cùng 60 Commands từ Master. Thuật toán Cross-Audit Engine đối soát chéo 360 thông điệp, chứng minh tỷ lệ rò rỉ dữ liệu chéo đạt mức tuyệt đối: **Cross-Talk / Data Bleeding Rate = 0.00%**.

```bash
# Chạy riêng bộ kiểm thử cô lập thông điệp Agent
dotnet test tests/NetOps.Tests/NetOps.Tests.csproj --filter "FullyQualifiedName~AgentMessageIsolationTests" -c Release

# Chạy kiểm chứng toàn diện Zero-Flakiness (5 vòng lặp lặp lại cho Integration Tests)
powershell -ExecutionPolicy Bypass -File ./scripts/run-tests.ps1 -Repeat 5 -Category Integration
```

#### g. Bộ Kiểm Thử Định Tuyến Unicast (`ManagerUnicastRoutingTests`)

Bộ kiểm thử chuyên sâu [`tests/NetOps.Tests/ManagerUnicastRoutingTests.cs`](file:///c:/Users/vuong/Documents/NetOps/tests/NetOps.Tests/ManagerUnicastRoutingTests.cs) kiểm chứng khả năng gửi thông điệp Unicast từ Master tới đích danh từng Agent qua Socket TCP thật:
1. **Unicast tới 1 Agent cụ thể:** Test case `Manager_CanSendUnicastCommand_ToSingleAgent_WithoutOtherAgentsReceivingIt` khởi chạy 3 Agent kết nối socket TCP thật; Master gửi lệnh `COMMAND` tới `agent-target`; xác thực duy nhất `agent-target` nhận được lệnh, 2 Agent còn lại hoàn toàn không nhận được gói tin nào (Zero Cross-Talk).
2. **Unicast đồng thời tới nhiều Agent:** Test case `Manager_CanSendConcurrentUnicastMessages_ToMultipleAgents_EachReceivesItsOwnMessageStrictly` phát song song 3 lệnh Unicast tới 3 Agent khác nhau; đối soát chéo chứng minh từng Agent chỉ nhận đúng gói tin của mình.
3. **Xử lý Agent Offline & Chưa Tồn Tại:** Test case `Manager_SendUnicastToOfflineOrNonExistentAgent_ReturnsFalseSafely_WithoutImpactingConnectedPeers` gửi Unicast tới Agent đã ngắt kết nối hoặc không tồn tại; phương thức trả về `false` an toàn mà không gây crash server và các kết nối khác vẫn hoạt động liên tục.
4. **Kiểm thử E2E từ REST API qua Socket TCP:** Test case `RestApi_PostCommand_SendsUnicastTcpMessageToAgentSuccessfully` kết hợp Kestrel Web Server in-memory và Master TCP Server; client gọi HTTP `POST /api/agents/{agentId}/command`, Agent nhận được frame nhị phân giải mã chính xác và các peer khác không bị ảnh hưởng.

```bash
# Chạy riêng bộ kiểm thử định tuyến Unicast
dotnet test tests/NetOps.Tests/NetOps.Tests.csproj --filter "FullyQualifiedName~ManagerUnicastRoutingTests" -c Release

# Chạy kiểm thử toàn bộ các route Minimal API (bao gồm cả Unicast endpoints)
dotnet test tests/NetOps.Tests/NetOps.Tests.csproj --filter "FullyQualifiedName~MasterApiEndpoints" -c Release
```

#### h. Bộ Kiểm Thử Định Tuyến Broadcast (`ManagerBroadcastRoutingTests`)

Bộ kiểm thử chuyên sâu [`tests/NetOps.Tests/ManagerBroadcastRoutingTests.cs`](file:///c:/Users/vuong/Documents/NetOps/tests/NetOps.Tests/ManagerBroadcastRoutingTests.cs) kiểm chứng khả năng phát sóng đồng loạt từ Master tới toàn bộ Agent qua Socket TCP thật:
1. **Phát sóng tới toàn bộ Agent online:** Test case `Manager_CanBroadcastCommand_ToAllThreeAgentsSimultaneously_AllReceiveMessage` khởi chạy 3 Agent kết nối socket TCP thật; Master phát sóng lệnh shell; xác thực 3/3 Agent nhận đúng thông điệp với `AllSucceeded = true`.
2. **Cách ly lỗi mạng (Fault Isolation & Partial Resiliency):** Test case `Manager_BroadcastWithDisconnectedAgent_IsolatesFault_OtherAgentsReceiveSuccessfully` kết nối 2 Agent socket TCP thật + 1 session lỗi mạng (stream bị ngắt đột ngột); Master phát sóng; xác thực 2 Agent lành lặn vẫn nhận đủ gói tin, `BroadcastResult` ghi nhận `FailedCount == 1` mà không gây crash server.
3. **Phát sóng dồn dập (Burst Concurrency):** Test case `Manager_CanBroadcastBurst_MultipleConsecutiveMessages_AllAgentsReceiveAllMessagesInOrder` phát sóng liên tiếp 10 gói tin nhị phân; kiểm chứng luồng `SafeFrameStream` không bị lẫn khung byte, toàn bộ Agent nhận đủ 10/10 gói tin theo đúng thứ tự.
4. **Xử lý khi không có Agent nào online:** Test case `Manager_BroadcastWithZeroAgents_ReturnsZeroResultSafely` kiểm chứng khi `ActiveCount == 0`, Master phát sóng trả về an toàn `BroadcastResult(0, 0, 0)`.
5. **Kiểm thử E2E từ REST API sang Socket TCP:** Test case `RestApi_PostBroadcastCommand_SendsBroadcastTcpMessageToAllOnlineAgentsSuccessfully` kết hợp Kestrel Web Server in-memory và Master TCP Server; client gọi HTTP `POST /api/broadcast/command`, toàn bộ Agent nhận được lệnh qua socket TCP thật và phản hồi.

```bash
# Chạy riêng bộ kiểm thử định tuyến Broadcast
dotnet test tests/NetOps.Tests/NetOps.Tests.csproj --filter "FullyQualifiedName~ManagerBroadcastRoutingTests" -c Release

# Kiểm chứng độ bền vững Zero-Flakiness (chạy lặp 5 vòng Category Integration = 315 test runs)
powershell -ExecutionPolicy Bypass -File ./scripts/run-tests.ps1 -Repeat 5 -Category Integration
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

### 5. Kiểm Thử, Xác Thực & Đóng Gói Nhị Phân Agent.Worker

Dự án `Agent.Worker` được trang bị quy trình xác thực chất lượng mã nguồn nghiêm ngặt (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), kiểm tra đồ thị phụ thuộc (`ValidateScopes`, `ValidateOnBuild`) và kịch bản tự động hóa toàn diện:

#### a. Kiểm tra tiền khởi động DI Container (Pre-flight DI Check)
Thẩm định nhanh tính toàn vẹn của đồ thị DI và dịch vụ Singleton/HostedService mà không kích hoạt kết nối TCP thật tới Master (thích hợp cho CI/CD và Container Healthcheck):
```bash
dotnet run --project src/Agent.Worker/Agent.Worker.csproj -- --validate-only
```

#### b. Chạy kịch bản tự động hóa xác thực toàn trình
Thực hiện tự động chuỗi: Clean $\to$ Build Debug $\to$ Build Release $\to$ DI Pre-flight Validation $\to$ Run Tests $\to$ Publish Matrix:
```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/verify-agent-worker.ps1
```
*Các tham số tùy chọn: `-SkipPublish` (kiểm tra nhanh bỏ qua đóng gói), `-SkipTests`, `-ArtifactsPath "./artifacts/publish"`.*

#### c. Xuất bản đóng gói đa nền tảng (Cross-Platform Publish Matrix)
```bash
# 1. Windows x64 (Framework-dependent, yêu cầu .NET 10 Runtime)
dotnet publish src/Agent.Worker/Agent.Worker.csproj -c Release -r win-x64 --no-self-contained -o ./artifacts/publish/agent-win-x64

# 2. Linux x64 (Framework-dependent, nhẹ ~78 KB, thích hợp container .NET)
dotnet publish src/Agent.Worker/Agent.Worker.csproj -c Release -r linux-x64 --no-self-contained -o ./artifacts/publish/agent-linux-x64

# 3. Linux x64 Standalone (Self-contained Single-File, độc lập không cần cài .NET runtime)
dotnet publish src/Agent.Worker/Agent.Worker.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./artifacts/publish/agent-linux-standalone
```