# Quy Ước Đóng Góp & Phối Hợp Làm Việc Nhóm

Tài liệu này quy định quy trình làm việc trên GitHub cho tất cả thành viên Nhóm 5 trong suốt quá trình phát triển đồ án **NetOps**, nhằm hạn chế tối đa xung đột mã nguồn (merge conflict) và duy trì chất lượng dự án.

---

## 1. Branching Strategy

Dự án áp dụng mô hình phân nhánh dựa trên Git Flow rút gọn:

```
[main] ──────────────────────────● Release v1.0 ────>
          \                     /
[develop]  ●───────●───────────● (Nhánh tích hợp chính)
            \     / \         /
[feature/*]  ●───●   ●───────● (Nhánh tính năng riêng)
```

- **`main`:** Nhánh chứa mã nguồn ổn định nhất, sẵn sàng demo hoặc bàn giao. Chỉ nhận merge thông qua Pull Request từ `develop` hoặc hotfix đã được kiểm thử kỹ lưỡng.
- **`develop`:** Nhánh tích hợp chính của cả nhóm. Mọi tính năng sau khi hoàn thành sẽ được tạo Pull Request để merge vào đây.
- **Nhánh tính năng / sửa lỗi:** Luôn rẽ nhánh từ `develop` mới nhất:
  - `feature/<tên-tính-năng>`: Phát triển chức năng mới (vd: `feature/tcp-ping-pong`, `feature/metrics-collector`, `feature/terminal-ui`).
  - `fix/<tên-lỗi>`: Sửa lỗi phát sinh (vd: `fix/socket-reconnect-loop`).
  - `docs/<tên-tài-liệu>`: Cập nhật tài liệu kỹ thuật, báo cáo đồ án (vd: `docs/architecture-update`).
  - `test/<nội-dung-test>`: Bổ sung bộ kiểm thử tự động (vd: `test/framing-stress-test`).

---

## 2. Conventional Commits

Mỗi commit cần có thông điệp rõ ràng, tuân thủ định dạng chuẩn quốc tế:

```
<loại>(<phạm vi>): <mô tả ngắn gọn bằng tiếng Anh hoặc tiếng Việt>
```

### Các tiền tố (Types):
- **`feat:`** Thêm tính năng mới (vd: `feat(network): implement length-prefix frame encoder`).
- **`fix:`** Sửa lỗi (vd: `fix(agent): handle connection timeout on restart`).
- **`docs:`** Soạn thảo hoặc sửa đổi tài liệu (vd: `docs: update system architecture diagram`).
- **`test:`** Viết thêm hoặc cập nhật bài kiểm thử (vd: `test: add unit tests for message framing`).
- **`refactor:`** Tái cấu trúc mã nguồn không thay đổi hành vi (vd: `refactor(contracts): organize message DTOs`).
- **`chore:`** Thay đổi cấu hình build, thêm package, dọn dẹp file (vd: `chore(solution): add xunit test project`).

### Quy tắc commit:
1. Không commit các thư mục tạm: `bin/`, `obj/`, `.vs/`, file log cá nhân (đã được cấu hình trong `.gitignore`).
2. Chia nhỏ commit theo từng đơn vị công việc logic, tránh gom toàn bộ code 1 tuần vào 1 commit khổng lồ.

---

## 3. Avoid Merge Conflict

Để tránh conflict khi nhiều thành viên cùng làm việc:
1. **Trước khi bắt đầu code mỗi ngày:**
   ```bash
   git checkout develop
   git pull origin develop
   ```
2. **Cập nhật nhánh feature của bạn với develop thường xuyên:**
   ```bash
   git checkout feature/your-feature
   git merge develop
   # hoặc: git rebase develop
   ```
3. **Giải quyết conflict sớm tại máy cá nhân (Local):** Nếu có conflict, giải quyết ngay trên máy mình trước khi đẩy (push) lên GitHub.

---

## 4. PR Workflow

1. **Chuẩn bị trước khi mở PR:**
   - Đảm bảo dự án build thành công: `dotnet build NetOps.slnx`
   - Đảm bảo toàn bộ test đều pass: `dotnet test NetOps.slnx`
2. **Mở Pull Request:**
   - Chọn base branch là `develop`.
   - Tiêu đề PR phản ánh đúng nội dung (tuân theo Conventional Commits).
   - Điền đầy đủ thông tin theo mẫu **Pull Request Template** được cung cấp.
3. **Review & Merge:**
   - Cần ít nhất **1 thành viên khác trong nhóm** review và phê duyệt (Approve).
   - Khi có ý kiến cần chỉnh sửa, tiếp tục commit lên nhánh feature; PR sẽ tự động cập nhật.
   - Ưu tiên phương thức **Squash and merge** hoặc **Merge commit** để giữ lịch sử git gọn gàng.