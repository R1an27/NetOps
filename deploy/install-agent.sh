#!/bin/bash
# Script cài đặt / nâng cấp / gỡ cài đặt NetOps Agent trên Linux (Ubuntu/Debian)
# Cách dùng phổ biến (chạy đúng 1 lệnh từ máy Linux bất kỳ):
# - Cài mới / nâng cấp : curl -fsSL https://raw.githubusercontent.com/R1an27/NetOps/main/deploy/install-agent.sh | sudo bash
# - Gỡ cài đặt         : curl -fsSL https://raw.githubusercontent.com/R1an27/NetOps/main/deploy/install-agent.sh | sudo bash -s -- --uninstall

# set -euo pipefail là "chế độ an toàn" của Bash:
#   -e : dừng ngay lập tức nếu có bất kỳ lệnh nào bị lỗi (tránh chạy tiếp gây hỏng hệ thống)
#   -u : báo lỗi ngay nếu gõ nhầm tên biến chưa được khai báo
#   -o pipefail : nếu 1 chuỗi lệnh nối bằng dấu | bị lỗi ở bước giữa, vẫn tính là lỗi
set -euo pipefail

# Cấu hình cố định của Agent trên máy Linux
INSTALL_DIR="/opt/netops-agent"          # Nơi đặt file chương trình NetOps.Agent
DATA_DIR="/var/lib/netops-agent"         # Nơi lưu agent-id.txt (giữ nguyên ID khi nâng cấp bản mới)
SERVICE_FILE="/etc/systemd/system/netops-agent.service"
SERVICE_USER="netops-agent"              # Tài khoản hệ thống quyền thấp dùng riêng để chạy Agent
ASSET_NAME="netops-agent-linux-x64"      # Tên file binary đã build sẵn trên GitHub Releases

# Địa chỉ tải binary và địa chỉ Server Hub mặc định.
# Cú pháp ${BIEN:-gia_tri_mac_dinh} nghĩa là: nếu biến môi trường chưa được đặt thì lấy giá trị mặc định.
RELEASE_URL="${NETOPS_DOWNLOAD_URL:-https://github.com/R1an27/NetOps/releases/latest/download}"
HUB_URL="${NETOPS_HUB_URL:-https://api.r1antech.online/hubs/agents}"

# [1] Kiểm tra quyền quản trị (root)
# $EUID là mã số định danh của người đang chạy lệnh (root luôn có EUID = 0).
# Cần quyền root vì script sẽ ghi file vào /opt, /var/lib và /etc/systemd/system.
if [ "$EUID" -ne 0 ]; then
  echo "LOI: Vui long chay bang quyen root (them 'sudo' phia truoc)."
  exit 1
fi

# [2] Chế độ gỡ cài đặt (khi người dùng truyền tham số --uninstall)
# ${1:-} là tham số dòng lệnh đầu tiên (trả về rỗng nếu không truyền gì, tránh lỗi do set -u).
if [ "${1:-}" = "--uninstall" ]; then
  echo "Dang go cai dat NetOps Agent..."
  # Tắt service đang chạy và hủy chế độ tự khởi động cùng máy (|| true để không báo lỗi nếu chưa từng cài)
  systemctl disable --now netops-agent.service 2>/dev/null || true
  rm -f "$SERVICE_FILE"
  systemctl daemon-reload
  rm -rf "$INSTALL_DIR"
  echo "Da go xong! (Thu muc du lieu $DATA_DIR duoc giu lai; chay 'sudo rm -rf $DATA_DIR' neu muon xoa han.)"
  exit 0
fi

# Nếu người dùng truyền URL Server ở tham số đầu tiên thì ghi đè HUB_URL mặc định.
if [ -n "${1:-}" ]; then
  HUB_URL="$1"
fi

# [3] Kiểm tra kiến trúc CPU & lấy file thực thi NetOps.Agent
# Hiện tại bản phát hành được build cho chip 64-bit Intel/AMD (x86_64).
if [ "$(uname -m)" != "x86_64" ]; then
  echo "LOI: Hien chi ho tro Linux x86_64 (may nay la $(uname -m))."
  exit 1
fi

# Tạo 1 thư mục tạm trong /tmp để tải file, và cài bẫy (trap) tự xóa thư mục tạm đó khi script kết thúc
# (dù cài thành công hay bị lỗi giữa chừng cũng không bao giờ để lại file rác).
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

# Nếu cùng thư mục đang đứng đã có sẵn file ./NetOps.Agent -> dùng luôn (tiện khi test offline).
# Ngược lại -> tự tải bản mới nhất từ GitHub Releases giống cách các agent bên thứ 3 hoạt động.
if [ -f "./NetOps.Agent" ]; then
  echo "Dang dung file ./NetOps.Agent co san trong thu muc hien tai..."
  BINARY_PATH="./NetOps.Agent"
else
  echo "Dang tai $ASSET_NAME tu $RELEASE_URL..."
  # curl -fsSL:
  #   -f : báo lỗi ngay nếu server trả mã 404/500 (không lưu trang HTML báo lỗi vào file binary)
  #   -sS: ẩn thanh tiến trình rườm rà nhưng vẫn hiện rõ thông báo nếu bị lỗi mạng
  #   -L : tự động đi theo link chuyển hướng (GitHub Releases luôn redirect sang CDN lưu file)
  curl -fsSL "$RELEASE_URL/$ASSET_NAME" -o "$TMP_DIR/$ASSET_NAME"

  # Nếu Release có đính kèm file SHA256SUMS -> tự đối chiếu mã băm để đảm bảo file không bị lỗi hoặc bị chỉnh sửa.
  #   sed -i 's/\r$//' : xóa ký tự xuống dòng kiểu Windows (\r) nếu file SHA256SUMS được tạo từ máy Windows
  #   sha256sum --ignore-missing -c : chỉ kiểm tra file đã tải về trong thư mục tạm, bỏ qua các file OS khác
  if curl -fsSL "$RELEASE_URL/SHA256SUMS" -o "$TMP_DIR/SHA256SUMS" 2>/dev/null; then
    sed -i 's/\r$//' "$TMP_DIR/SHA256SUMS"
    (cd "$TMP_DIR" && sha256sum --ignore-missing -c SHA256SUMS)
  fi

  BINARY_PATH="$TMP_DIR/$ASSET_NAME"
fi

# [4] Tạo user quyền thấp & chép file vào đúng vị trí hệ thống
# Không nên để Agent chạy bằng quyền root; tạo riêng 1 tài khoản hệ thống không cho đăng nhập shell.
# Lệnh "id -u ... || useradd ..." nghĩa là: nếu user chưa tồn tại thì mới tạo mới.
id -u "$SERVICE_USER" >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"

mkdir -p "$INSTALL_DIR" "$DATA_DIR"
# install -m 755 vừa copy file binary vừa cấp quyền chạy (rwxr-xr-x) trong 1 bước duy nhất.
install -m 755 "$BINARY_PATH" "$INSTALL_DIR/NetOps.Agent"
# Chỉ duy nhất user netops-agent được phép đọc/ghi thư mục chứa agent-id.txt (chmod 700).
chown -R "$SERVICE_USER:$SERVICE_USER" "$DATA_DIR"
chmod 700 "$DATA_DIR"

# [5] Đăng ký systemd service để Agent chạy ngầm và tự bật lại khi khởi động máy
# Cú pháp "cat <<EOF > file" dùng để ghi toàn bộ nội dung từ đây cho tới dòng "EOF" vào file cấu hình service.
cat <<EOF > "$SERVICE_FILE"
[Unit]
Description=NetOps Manager - Background Agent Service
# Đợi hệ thống có mạng sẵn sàng rồi mới khởi động Agent
After=network-online.target
Wants=network-online.target

[Service]
# Dùng Type=simple (KHÔNG dùng Type=notify) vì NetOps.Agent là ứng dụng console bình thường,
# không gửi tín hiệu sd_notify; dùng Type=notify sẽ khiến systemd treo chờ 90 giây rồi tự tắt.
Type=simple
User=$SERVICE_USER
Group=$SERVICE_USER
ExecStart=$INSTALL_DIR/NetOps.Agent
WorkingDirectory=$INSTALL_DIR
# Truyền đường dẫn lưu agent-id.txt và địa chỉ Server vào biến môi trường cho Agent đọc
Environment="NETOPS_DATA_DIR=$DATA_DIR"
Environment="NETOPS_HUB_URL=$HUB_URL"
# Nếu Agent gặp lỗi bị thoát, tự động khởi động lại sau 5 giây
Restart=always
RestartSec=5
SyslogIdentifier=netops-agent

[Install]
WantedBy=multi-user.target
EOF

# [6] Nạp cấu hình, bật tự chạy khi mở máy và khởi động service ngay
systemctl daemon-reload
systemctl enable netops-agent.service
systemctl restart netops-agent.service

echo ""
echo "Cai dat NetOps Agent thanh cong!"
echo "  - Xem trang thai : sudo systemctl status netops-agent --no-pager"
echo "  - Xem log live   : sudo journalctl -u netops-agent -f"
echo "  - Go cai dat     : curl -fsSL https://raw.githubusercontent.com/R1an27/NetOps/main/deploy/install-agent.sh | sudo bash -s -- --uninstall"
