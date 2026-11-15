# Tích hợp kênh Agent–Server qua SignalR trên nhánh `main`

Quyết định ngày 15/11/2026: dùng SignalR/WebSockets qua Cloudflare Tunnel (`/hubs/agents`) thay cho gRPC duplex stream `AgentHub.ConnectStream` để Agent không cần OpenVPN khi kết nối từ ngoài mạng trường.

Nhánh PoC `feat/signalr-agent-poc` đã kiểm chứng kết nối thực tế từ Agent Windows (`DESKTOP-T5Q6AC0`, tắt OpenVPN) tới container `netops-server` qua `https://api.r1antech.online/hubs/agents`. Bản tích hợp này đưa thay đổi SignalR và cấu hình N00 lên nền `main` mới nhất, giữ nguyên các tính năng hiện có của nhóm.

## Phạm vi tích hợp trên `main`

- `Directory.Build.props`: chốt version chung `0.1.0-preview.1` (N00).
- `src/NetOps.AppHost/NetOps.AppHost.csproj`: nâng `Aspire.AppHost.Sdk` và `Aspire.Hosting.RabbitMQ` lên `13.6.5` (khắc phục cảnh báo `NU1903` ở `13.6.4`), giữ nguyên `AppHost.cs`.
- `src/NetOps.Shared/Protos/netops.proto` & `src/NetOps.Shared/AgentContracts.cs`: tách hợp đồng Agent–Server (`RegisterAgentRequest`, `RegisterAgentResponse`, `HeartbeatMessage`, `CommandRequest`) sang POCO cho SignalR JSON; giữ `CommandService`, `SubmitCommandRequest/Response`, `WatchCommandRequest` và `CommandOutput` trong `netops.proto` cho Desktop gRPC và SignalR `/hubs/netops`.
- `src/NetOps.Server/Program.cs`, `AgentHubService.cs`, `AgentConnectionManager.cs`, `CommandConsumer.cs`:
  - Map `AgentHubService` tại `/hubs/agents` (thay cho gRPC `AgentHub`).
  - Bảo vệ phiên kết nối theo `ConnectionId` (`TryUpdate` / `TryRemove(KeyValuePair)`), chống giả danh `AgentId` khi gửi `Heartbeat` và `SendCommandOutput`.
  - Giữ `_knownAgents` và `GetAgents()` cho `NetOpsHub`; chuyển `CommandConsumer` từ gRPC stream sang `IHubContext<AgentHubService>.Clients.Client(...).SendAsync("ReceiveCommand", ...)`.
- `src/NetOps.Agent/Program.cs`, `SystemInfoService.cs`, `CommandExecutor.cs`:
  - Kết nối WebSockets tới `/hubs/agents` (hỗ trợ đối số CLI hoặc `NETOPS_HUB_URL`, mặc định `https://localhost:7027/hubs/agents`), kiểm tra HTTPS (chỉ cho phép HTTP loopback), tự thử lại sau 5 giây, dừng sạch bằng `Ctrl+C`.
  - Giữ nguyên `SystemInfoService.GetAgentId()`, `MetricsExportService.InitMetrics(...)` và `CommandExecutor` (gửi output qua callback `SendCommandOutput`, kill process tree khi timeout).
- `tests/NetOps.Tests/`: thêm bộ test xUnit kiểm tra `AgentConnectionManager`, `AgentHubService` (đăng ký, heartbeat, cô lập phiên cũ, đẩy lệnh `ReceiveCommand`, bảo vệ `SendCommandOutput`) và `CommandExecutor`.
