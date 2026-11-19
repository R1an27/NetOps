# Cấp danh tính cho NetOps Agent

Mỗi máy dùng một OpenID Connect client riêng trong realm `netops`. Client ID là ID đã lưu của Agent. Secret dùng để xin access token từ Keycloak. Server đọc `client_id` trong token rồi đối chiếu với máy đang đăng ký.

## 1. Lấy ID của máy

Trên Windows, chạy `NetOps.Agent.exe --show-id` bằng tài khoản sẽ chạy Agent. Trên Linux, bộ cài in ID và chờ cấp secret. Thao tác này dùng lại `agent-id.txt` của bản cũ. Với một nhóm máy đang hoạt động, hãy cấp credential cho từng ID trước khi nâng cấp Server và Agent.

## 2. Cấp quyền trong Keycloak

1. Trong realm `netops`, mở **Clients → netops-backend → Roles**. Bản import mới có sẵn client role `NetOpsAgent`. Với realm đang chạy, tạo role này một lần. Keycloak giữ nguyên realm đã có khi khởi động với `--import-realm`.
2. Tạo một **OpenID Connect client** với **Client ID** trùng chính xác ID ở bước 1. Bật **Client authentication** và **Service accounts roles**. Tắt các luồng đăng nhập người dùng, gồm **Standard flow** và **Direct access grants**.
3. Trong **Service account roles**, gán client role **netops-backend → NetOpsAgent** cho tài khoản dịch vụ của máy. Giữ client scopes `roles` và `service_account` ở chế độ **Default**. Scope đầu đưa role và audience `netops-backend` vào token, scope sau đưa `client_id` vào token. Nếu đã tắt **Full scope allowed**, cho phép role này trong scope của client thiết bị.
4. Trong **Credentials**, lấy **Client secret** và chuyển riêng cho người quản lý máy tương ứng.

## 3. Lưu secret và khởi động Agent

Lưu duy nhất giá trị secret vào file văn bản `client-secret.txt`, cạnh `agent-id.txt`. Agent đọc lại file mỗi lần xin token.

| Môi trường | Vị trí và cách khởi động |
| --- | --- |
| Windows | Mặc định dùng `%LOCALAPPDATA%\NetOps\Agent\`. Giới hạn quyền đọc file cho tài khoản chạy Agent và quản trị viên. Khởi động lại `NetOps.Agent.exe`. |
| Linux dùng bộ cài | Dùng `/var/lib/netops-agent/client-secret.txt`. Chép từ file do quản trị viên cung cấp bằng `sudo install -o netops-agent -g netops-agent -m 600 /duong-dan-rieng/client-secret.txt /var/lib/netops-agent/client-secret.txt`, rồi chạy `sudo systemctl start netops-agent.service`. |

Nếu đã đặt `NETOPS_DATA_DIR`, dùng thư mục đó. Khi dùng Hub riêng qua đối số hoặc `NETOPS_HUB_URL`, đặt thêm `NETOPS_TOKEN_URL` tới endpoint `https://<keycloak>/realms/netops/protocol/openid-connect/token`. Với systemd, ghi biến này dưới dạng `NETOPS_TOKEN_URL=...` trong `/etc/netops-agent.conf` rồi khởi động lại service. HTTP chỉ dùng được trên loopback khi phát triển.

## Thu hồi quyền một máy

Vô hiệu hóa client của máy đó trong Keycloak để dừng cấp token mới. Server đóng kết nối khi token hiện tại hết hạn. Với realm mẫu, token có hiệu lực 5 phút kể từ lúc cấp. Khi xoay secret cho một máy vẫn được phép hoạt động, thay nội dung `client-secret.txt` bằng secret mới.

Một máy vẫn được cấp quyền sẽ tự kết nối lại. Nếu một lệnh đang trả kết quả đúng lúc đổi phiên, phần output gửi trong khoảng ngắt kết nối có thể bị mất.
