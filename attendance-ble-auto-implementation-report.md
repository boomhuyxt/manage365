# Báo cáo triển khai chấm công BLE tự động

Ngày 28/10/2026

## Kết quả

Đã nối luồng BLE vào/ra ca vào app Android, agent kiosk Windows, backend và PostgreSQL. Nhân viên bật “Bật tự động” một lần; app lưu lựa chọn, quảng bá BLE khi chạy nền với dịch vụ foreground và thông báo cố định. Mở app lại sẽ khôi phục chế độ này. Đăng xuất hoặc tắt tự động sẽ dừng phát và thu hồi credential. Kiosk Windows có script cài chạy khi tài khoản Windows đăng nhập.

Check-in tiếp tục tự động khi kiosk nhận đủ tín hiệu, thời gian hiện diện, đúng nhân viên/cửa hàng và ca hợp lệ. Khi beacon vắng ít nhất 5 phút trong khi kiosk vẫn khỏe và đang quét, backend tự ghi giờ ra theo lần cuối xác nhận thấy beacon. Bản ghi được gắn NeedsReview, có nguồn beacon_absence; chủ shop cần đối soát. Khoảng chờ cấu hình bằng Attendance:Ble:AutoCheckoutAbsenceMinutes, từ 2 đến 30 phút, mặc định 5. Mất heartbeat kiosk, kiosk báo lỗi hoặc điện thoại vẫn được nhìn thấy thì sweep không đóng ca. QR + GPS vẫn dùng được khi BLE lỗi.

## Thay đổi chính

- Routes/API/Attendance/BleAutoCheckoutService.cs: sweep mỗi 30 giây, chỉ đóng lượt BLE còn mở, yêu cầu heartbeat kiosk gần đây và gán nhân viên đúng kiosk; tránh đóng trùng bằng khóa hàng và unique evidence.
- docs/sql/015_ble_automatic_checkout.sql: thêm nguồn checkout có kiểm tra giá trị và index cho lượt BLE còn mở.
- mobile/lib/core/ble_attendance_service.dart: lưu lựa chọn tự động, khởi động/dừng dịch vụ nền Android, khôi phục quảng bá khi app được mở lại.
- mobile/android/app/src/main/kotlin/com/example/mobile/MainActivity.kt và BlePresenceService.kt: Android foreground service cùng thông báo trạng thái, quyền thông báo và channel Flutter.
- mobile/lib/dash_board_screen.dart, mobile/lib/home_screen.dart, mobile/lib/main.dart: nút bật một lần, hiện trạng thái checkout cần đối soát, dừng khi đăng xuất/hết phiên. iPhone hiển thị rõ BLE nền chưa được kiosk Windows hỗ trợ ổn định.
- tools/BleScannerAgent/Install-BLE-Autostart.ps1, Start-BLE.cmd, build-package.ps1, README.md: cài khởi động scanner khi người dùng Windows đăng nhập; ZIP kiosk được build lại với helper và hướng dẫn mới.
- Views/Shared/_BleKioskPanel.cshtml: cập nhật hướng dẫn cấu hình một lần cho chủ shop và nhân viên.
- wwwroot/js/dashboard.js: tô nổi bật lượt tự checkout cần đối soát trong danh sách chấm công trực tiếp của chủ shop/admin.
- manage365.csproj: loại output build/test khỏi content web để tránh lỗi đường dẫn copy lặp; chỉ ZIP scanner được đưa vào output.
- manage365.Tests/ble-auto-checkout-smoke.py: kiểm tra truy vấn sweep trên database dùng riêng cho test.

## Database

Đã sao lưu trước khi áp dụng migration 015:

artifacts/ble-ui/public-before-015-20261028-174020.dump (131,918 byte).

Migration đã áp dụng vào database đang cấu hình cho ứng dụng. Kiểm tra sau migration xác nhận có cột checkout_source, vẫn có 27 bảng public và số dòng của các bảng không đổi. Kết quả nằm tại artifacts/ble-ui/migration-015-result.json. Đây là migration bổ sung; không gộp hay xóa bảng.

## Kiểm thử

- Backend build sang thư mục tạm riêng: đạt, 0 warning, 0 error.
- Flutter test: 36/36 đạt.
- Flutter APK debug: build thành công.
- Flutter analyze với --no-fatal-infos: không có lỗi; còn 15 thông tin/cảnh báo phong cách trong codebase, gồm các mục có sẵn.
- xUnit: 152 test, 0 lỗi, 0 thất bại, 19 test cơ sở dữ liệu bị bỏ qua do thiếu biến kết nối test chuyên biệt.
- Kiểm thử sweep trực tiếp trên PostgreSQL cô lập: 4 điều kiện đạt — kiosk khỏe tự đóng sau khi vắng beacon, ghi nguồn kiểm toán, không đóng khi kiosk hỏng hoặc nhân viên vẫn hiện diện, không tạo checkout trùng.
- Build ZIP agent Windows: thành công.

## Giới hạn đã xác minh

Windows Application Control chặn nạp assembly backend mới từ output tạm trong lúc thử khởi động API tích hợp (FileLoadException, mã 0x800711C7). Vì vậy API integration hiện chưa chạy được trên bản code mới; build và kiểm thử SQL của sweep đã chạy độc lập. Không tắt chính sách bảo vệ máy. Server đang chạy tại PID 9140 vẫn là tiến trình cũ; cần dừng server bằng Ctrl+C rồi chạy lại dotnet run --project manage365.csproj --launch-profile http để nạp backend mới.

Chưa có điện thoại thật kết nối trong phiên này nên chưa xác nhận quảng bá nền khi khóa màn hình trên từng hãng Android. Foreground service cải thiện thời gian chạy nhưng force-stop, tắt Bluetooth, chính sách tiết kiệm pin của hãng hoặc Windows kiosk ngủ/mất mạng vẫn có thể làm mất tín hiệu. Với iOS, app chỉ đảm bảo phát BLE khi đang mở; khi ẩn nền dùng QR dự phòng. Mất beacon không chứng minh chắc chắn nhân viên đã rời cửa hàng, vì vậy giờ ra tự động vẫn mang trạng thái NeedsReview.

APK debug: C:\Users\ASUS\StudioProjects\retail-management-app\retail-management-app\mobile\build\app\outputs\flutter-apk\app-debug.apk.
