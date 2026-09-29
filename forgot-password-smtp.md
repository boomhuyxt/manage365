# Quên mật khẩu qua SMTP

## Goal
Cho phép người dùng web và Flutter đặt lại mật khẩu bằng OTP gửi từ backend Manage365.

## Tasks
- [x] Tạo migration, OTP protector, repository và SMTP sender → Verify: unit tests và build backend chạy thành công.
- [x] Thêm API yêu cầu OTP, xác minh OTP và đặt mật khẩu mới → Verify: response không làm lộ email và token chỉ dùng một lần.
- [x] Thêm luồng ba bước trên web → Verify: nhập email, OTP và mật khẩu mới gọi đúng API.
- [x] Thêm luồng ba bước trên Flutter → Verify: widget test bao phủ điều hướng, loading và lỗi.
- [x] Cập nhật cấu hình mẫu và tài liệu → Verify: không có secret thật trong Git.
- [x] Chạy toàn bộ build, lint và test → Verify: không có lỗi mới.

## Done When
- [ ] Mật khẩu cũ không đăng nhập được và mật khẩu mới đăng nhập được sau khi dùng OTP hợp lệ.
- [ ] OTP hết hạn, sai hoặc đã dùng đều bị từ chối an toàn.

## Notes
SMTP credential chỉ được đặt qua User Secrets hoặc biến môi trường. App Password đã gửi trong hội thoại phải bị thu hồi.
