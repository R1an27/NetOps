# Tích hợp kênh Agent–Server qua SignalR trên nhánh `main`

Quyết định ngày 15/11/2026: dùng SignalR/WebSockets qua Cloudflare Tunnel (`/hubs/agents`) thay cho gRPC duplex stream `AgentHub.ConnectStream` để Agent không cần OpenVPN khi kết nối từ ngoài mạng trường.

Nhánh PoC `feat/signalr-agent-poc` đã kiểm chứng kết nối thực tế từ Agent Windows (`DESKTOP-T5Q6AC0`, tắt OpenVPN) tới container `netops-server` qua `https://api.r1antech.online/hubs/agents`. Bản tích hợp này đưa thay đổi SignalR và cấu hình N00 lên nền `main` mới nhất, giữ nguyên các tính năng hiện có của nhóm.

## Phạm vi tích hợp trên `main`

- `Directory.Build.props`: chốt version chung `0.1.0-preview.1` (N00).
- `src/NetOps.AppHost/NetOps.AppHost.csproj`: nâng `Aspire.AppHost.Sdk` và `Aspire.Hosting.RabbitMQ` lên `13.6.5` (khắc phục cảnh báo `NU1903` ở `13.6.4`), giữ nguyên `AppHost.cs`.
- `src/NetOps.Shared/AgentContracts.cs` & `src/NetOps.Shared/CommandContracts.cs`: hợp đồng dữ liệu Agent–Server và Desktop–Server dưới dạng POCO cho SignalR JSON; đã gỡ bỏ hoàn toàn gRPC và `netops.proto`.
- `src/NetOps.Server/Program.cs`, `AgentHubService.cs`, `NetOpsHub.cs`, `AgentConnectionManager.cs`, `CommandConsumer.cs`:
  - Map `AgentHubService` tại `/hubs/agents` (thay cho gRPC `AgentHub`) và đưa `SubmitCommand` trực tiếp lên `NetOpsHub` (`/hubs/netops`, giữ `[Authorize(Policy = "RunCommands")]`), không còn `CommandGrpcService`.
  - Bảo vệ phiên kết nối theo `ConnectionId` (`TryUpdate` / `TryRemove(KeyValuePair)`), chống giả danh `AgentId` khi gửi `Heartbeat` và `SendCommandOutput`.
  - Giữ `_knownAgents` và `GetAgents()` cho `NetOpsHub`; chuyển `CommandConsumer` từ gRPC stream sang `IHubContext<AgentHubService>.Clients.Client(...).SendAsync("ReceiveCommand", ...)`.
- `src/NetOps.Agent/Program.cs`, `SystemInfoService.cs`, `CommandExecutor.cs`:
  - Kết nối WebSockets tới `/hubs/agents` (mặc định `https://api.r1antech.online/hubs/agents`, vẫn hỗ trợ ghi đè qua đối số CLI hoặc `NETOPS_HUB_URL`), kiểm tra HTTPS (chỉ cho phép HTTP loopback), tự thử lại sau 5 giây, dừng sạch bằng `Ctrl+C`.
  - Giữ nguyên `SystemInfoService.GetAgentId()`, `MetricsExportService.InitMetrics(...)` và `CommandExecutor` (gửi output qua callback `SendCommandOutput`, kill process tree khi timeout).
- `src/NetOps.Desktop/Form1.cs`, `Form1.Designer.cs`: cấu hình mặc định Server API tại `https://api.r1antech.online` (`SubmitCommand` và nhận output qua `/hubs/netops`, `MetricsApiClient`) và Keycloak Authority tại `https://auth.r1antech.online/realms/netops`.
