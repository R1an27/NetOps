# TODO

- [ ] Bổ sung sau chức năng chờ máy online: lưu lệnh khi Agent offline, gửi khi Agent kết nối lại trước hạn chờ và đánh dấu hết hạn nếu quá thời gian chờ.

## Phase 5: Bảo mật và phân quyền — để làm sau

Đã có code đăng nhập Desktop, xác thực JWT trên Server, chính sách `RunCommands` và `ReadMetrics`, gửi token qua gRPC/SignalR và kiểm tra chủ sở hữu kết nối nhận output. Cần chốt kết quả kiểm thử thực tế.

### 1. Xác thực Agent → Server

- [ ] Tạo danh tính máy trong Keycloak cho Agent.
- [ ] Cho Agent tự lấy token và gửi khi mở `ConnectStream()`.
- [ ] Yêu cầu JWT hợp lệ và quyền kết nối phù hợp tại `AgentHubService`.
- [ ] Bảo đảm Agent lấy được token mới khi cần kết nối lại.

### 2. Bảo vệ các điểm truy cập còn lại

- [ ] Xử lý endpoint mẫu `/weatherforecast` đang công khai: bỏ nếu không dùng hoặc yêu cầu JWT nếu giữ.
- [ ] Áp dụng quyền xem danh sách Agent cho các role NetOps tại Hub/`GetAgents()`, chặn tài khoản chưa được gán role phù hợp.

### 3. Hoàn tất kiểm thử Phase 5

- [ ] Viewer đọc metrics nhận `200 OK`.
- [ ] Viewer gửi lệnh bị từ chối với `PermissionDenied`.
- [ ] Operator và Admin gửi được lệnh và nhận output.
- [ ] Yêu cầu mới thiếu token, dùng token sai hoặc token hết hạn bị từ chối.
- [ ] Yêu cầu chỉ định kết nối nhận output thuộc tài khoản khác bị từ chối trước khi gửi vào queue.
- [ ] Agent thiếu token hoặc không có quyền không kết nối được stream.
- [ ] Hủy hoặc thất bại khi đăng nhập Desktop thì không sử dụng được chức năng quản trị.

### 4. Chốt cấu hình và tài liệu

- [ ] Export lại cấu hình Keycloak sau khi thêm cấu hình cho Agent.
- [ ] Xác nhận các tài khoản thử nghiệm đăng nhập được và được gán đúng role.
- [ ] Cập nhật checklist Phase 5 trong `tasks/todo.md` theo các mục đã kiểm thử đạt.

**Điểm tiếp tục:** hoàn tất phép thử Viewer đọc metrics nhận `200 OK`, sau đó chuyển sang xác thực Agent → Server.
