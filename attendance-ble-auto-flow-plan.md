# Kế hoạch sửa luồng chấm công BLE tự động

Ngày: 28/10/2026. Trạng thái: đề xuất, chưa triển khai thay đổi trong kế hoạch này.

Mục tiêu: nhân viên thiết lập quyền một lần; trong điều kiện thiết bị được hỗ trợ, vào cửa hàng được ghi nhận vào ca mà không mở màn hình chấm công. Rời cửa hàng được xử lý theo trạng thái xác minh, không coi mọi lần mất Bluetooth là kết thúc ca. QR tiếp tục dùng dự phòng.

## Luồng sử dụng và giới hạn

- Chủ shop thiết lập một lần: gán kiosk vào cửa hàng, gán nhân viên, hiệu chỉnh tín hiệu tại cửa/quầy, bật agent tự khởi động. Web quản lý; agent Windows thực hiện quét BLE.
- Nhân viên đăng nhập, cấp quyền và bật chế độ tự động một lần. Dịch vụ BLE tồn tại độc lập với màn hình, có thông báo trạng thái; đăng xuất/khóa tài khoản sẽ thu hồi quyền. Không yêu cầu nhập mã kiosk hay URL API hằng ngày.
- Vào cửa hàng: nhận diện token ngắn hạn → kiểm tra tài khoản/cửa hàng/ca/nghỉ phép → tín hiệu ổn định → ghi check-in duy nhất → thông báo app và web. Không dùng địa chỉ MAC làm danh tính.
- Rời cửa hàng: `PRESENT → EXIT_PENDING → CHECKED_OUT` khi có bằng chứng rời vùng bổ sung đáng tin cậy và ca đủ điều kiện. Nếu thiếu bằng chứng: `NEEDS_REVIEW`, giữ giờ ra trống. Có thể bổ sung geofence với quyền phù hợp; GPS không đủ chính xác thì yêu cầu xác nhận. Heartbeat còn sống không tự chứng minh nhân viên đã rời cửa hàng.
- Ngưỡng thí điểm: hiện diện 20–30 giây, chờ mất tín hiệu 3–5 phút; phải hiệu chỉnh trên thiết bị thật. Không quy RSSI thành bán kính chính xác. Lưu riêng lastSeen, thời điểm nghi rời, thời điểm bằng chứng rời vùng và thời điểm xác nhận; không tự lấy lastSeen làm giờ ra.
- Android là phạm vi thí điểm đầu tiên. Foreground service cần quyền và thông báo theo phiên bản Android; không bảo đảm chạy sau force-stop. iPhone phát UUID ở nền không tương thích với cách Windows scanner hiện nhận diện; cần thử kiến trúc beacon tại cửa hàng → iPhone nhận diện, hoặc giữ QR. Chưa cam kết tự động nền trên iOS.

## Các bước thực hiện

1. [ ] **Chốt chính sách và thử thiết bị trước:** kiểm tra Android thực tế, Bluetooth adapter Windows, tắt màn hình và tiết kiệm pin; chọn cách xác minh rời cửa hàng. Đạt khi ghi được tín hiệu nền ổn định và phân biệt được ca thử rời cửa hàng với lỗi thiết bị.
2. [ ] **Tách BLE khỏi màn hình Flutter:** sửa `mobile/lib/core/ble_attendance_service.dart`, `mobile/lib/dash_board_screen.dart`, vòng đời đăng nhập và Android service/manifest. Service quản lý quảng bá, xoay credential, retry và trạng thái quyền; không phụ thuộc Dart timer của màn hình. Đạt khi chuyển tab/khóa màn hình vẫn nhận diện, đăng xuất thì dừng và thu hồi credential.
3. [ ] **Làm kiosk hoạt động liên tục:** sửa `tools/BleScannerAgent/Program.cs` và quy trình cài đặt để hỗ trợ tự chạy, phục hồi adapter, heartbeat, hàng đợi có giới hạn. Đạt khi lỗi mạng/Bluetooth hiển thị đúng và khởi động lại không nhân đôi sự kiện; dữ liệu cũ không bị coi là hiện diện hiện tại.
4. [ ] **Thêm phiên hiện diện bền vững:** sửa `BleAttendanceService.cs`, `BleAttendancePolicy.cs`, contracts/controller và repository; worker xử lý hết hạn, khóa đồng thời theo nhân viên/ca. Dự kiến migration kế tiếp thêm phiên hiện diện (employee, scanner/store, shift, state, timestamps, evidence, version), tận dụng bảng evidence hiện có. Chốt schema sau rà soát; không xóa bảng cũ. Đạt khi restart backend giữ đúng trạng thái, token xoay không tạo phiên/ca mới.
5. [ ] **Hoàn thiện quy tắc vào/ra và QR chung:** kiểm tra quyền sở hữu shop, tài khoản khóa, nghỉ phép, ca qua đêm/ca gãy/ca giao nhau; sự kiện QR và BLE dùng chung giao dịch/idempotency. Đạt khi nhiều nhân viên cùng ca được ghi độc lập, mỗi phân ca chỉ có bản ghi hợp lệ, không tự đóng ca do kiosk hỏng hay đến giờ kết thúc lịch.
6. [ ] **Cập nhật UI:** `_BleKioskPanel.cshtml`, `ble-kiosk.js`, `ble-kiosk.css` và app hiển thị Sẵn sàng/Trong ca/Nghi rời/Mất kết nối/Cần xác minh; thêm hàng đợi chủ shop đối soát có lịch sử chỉnh sửa. Đạt khi nhân viên thấy kết quả, lý do chưa chấm công và nút QR dự phòng; chỉ quản lý đúng phạm vi được đối soát.
7. [ ] **Kiểm thử rồi triển khai:** bổ sung xUnit, API/PostgreSQL, Flutter và Playwright; thử hai điện thoại thật đi vào/ra, khóa màn hình, mạng yếu, Bluetooth tắt, hết pin, agent dừng và restart. Chạy thử chế độ chỉ ghi nhận đề xuất trước khi bật tự động; sao lưu, migration thử trên bản sao, cờ bật/tắt tính năng, chính sách dọn observations và báo cáo bằng chứng. Đạt khi không ghi trùng/sai giờ ra, QR hồi quy đạt, mọi test bị bỏ qua/giới hạn thiết bị được nêu rõ.

## Tình huống bắt buộc kiểm tra

| Tình huống | Xử lý mong đợi |
|---|---|
| Đúng ca, đi vào và ở ổn định | Check-in một lần; nhiều nhân viên được vào cùng lúc |
| Đi ngang, ngoài giờ hoặc không có ca | Không ghi công; giải thích trên app |
| Đến muộn / ca qua đêm | Ghi giờ thực tế, gắn đúng phân ca và ngày nghiệp vụ |
| Có nhiều ca hợp lệ giao nhau | Yêu cầu chọn/xác minh ca, không tự đoán |
| Ra ngoài ngắn rồi quay lại | Hủy nghi rời trong thời gian chờ, không tạo công mới |
| Rời cửa hàng khi kết thúc ca | Tự ghi ra chỉ khi đạt chính sách bằng chứng; nếu không thì đối soát |
| Rời sớm / giờ nghỉ / ca gãy | Phân biệt nghỉ tạm với kết thúc ca; chuyển xác nhận khi chưa rõ |
| Hết pin, force-stop, tắt Bluetooth, kiosk/mạng hỏng | Báo mất khả năng xác minh; không tự ghi check-out |
| Đã duyệt nghỉ, tài khoản khóa, sai cửa hàng | Chặn ghi công mới theo quyền và phạm vi; giữ lịch sử |
| QR xen BLE, gửi lại batch, mạng phục hồi | Không ghi trùng hoặc lùi giờ bằng chứng cũ |

Nguồn giới hạn nền: [Android BLE background](https://developer.android.com/develop/connectivity/bluetooth/ble/background), [Apple Core Bluetooth background](https://developer.apple.com/library/archive/documentation/NetworkingInternetWeb/Conceptual/CoreBluetooth_concepts/CoreBluetoothBackgroundProcessingForIOSApps/PerformingTasksWhileYourAppIsInTheBackground.html).
