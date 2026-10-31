# BLE: tự nhận diện nhân viên theo cửa hàng và ca làm

> Luồng cài bộ quét đã được nâng cấp sang cài một lần và Bật/Tắt trên web. Xem `attendance-ble-one-time-setup-report.md` cho hướng dẫn hiện hành; phần thiết lập ZIP dưới đây ghi lại phiên bản trước.

## Kết quả

Đã bỏ bước **Gán nhân viên vào kiosk**. Chủ shop chỉ phân ca như bình thường; hệ thống tự xác định nhân viên được nhận diện tại kiosk của shop từ ca đã phân.

Mô hình hiện tại của dự án là **một tài khoản chủ shop đại diện một cửa hàng**. Khóa liên kết là ID tài khoản phụ trách, không phải tên hiển thị, tên nhân viên hay phòng ban. Nhiều kiosk của cùng chủ shop thuộc cùng cửa hàng; chưa triển khai nhiều chi nhánh độc lập cho một chủ shop.

## Luồng xử lý

1. **Chủ shop phân ca trên web/app:** backend lấy ID từ phiên đăng nhập và ghi `dang_ky_ca.shop_owner_id`. Không cần chọn shop hoặc gán lại nhân viên ở kiosk.
2. **Admin phân ca:** chọn shop phụ trách. Sửa ca đã có shop giữ nguyên liên kết nếu không yêu cầu thay đổi. Sao chép tuần giữ shop của ca nguồn; chủ shop chỉ sao chép ca đã thuộc shop mình.
3. **Tạo kiosk một lần:** chủ shop dùng shop của mình; Admin có thể chọn chủ shop để tạo kiosk thay. Đổi mã kết nối/tải lại bộ quét không đổi chủ sở hữu kiosk.
4. **Nhân viên đăng nhập app, bật BLE và cấp quyền cần thiết:** bộ quét gửi tín hiệu lên API. API đối chiếu credential → nhân viên → ca → chủ shop của kiosk.
5. **Check-in:** chỉ chọn ca thuộc đúng shop, kiểm tra khung giờ, tài khoản, nghỉ phép, tín hiệu và thời gian hiện diện. Ca hợp lệ hôm nay, ca qua đêm từ hôm trước và ca đang chấm công được tra cứu tự động; ca ngày mai không cấp quyền hôm nay.
6. **Checkout:** kiểm tra lại ca thuộc shop của kiosk trước khi ghi dữ liệu. Luồng tự checkout hiện có cũng dùng quan hệ này. Khi mất beacon đủ thời gian và kiosk vẫn khỏe, bản ghi được đánh dấu `NeedsReview`; mất beacon không chứng minh chắc chắn nhân viên đã rời cửa hàng.
7. **Hủy ca / khóa tài khoản:** danh sách tự cập nhật; một bản ghi gán kiosk cũ không thể cấp quyền chấm công. QR tiếp tục là phương án dự phòng.

Danh sách trên web là **nhân viên có ca hôm nay hoặc ca đang mở**, không phải khẳng định tất cả đang đứng tại cửa hàng. Cột tín hiệu và kết quả phản ánh quan sát thực tế từ bộ quét.

## Thay đổi trong mã nguồn

| File/nhóm file | Thay đổi |
|---|---|
| `docs/sql/016_shift_shop_ble_eligibility.sql` | Thêm cột `shop_owner_id`, FK, index và view `ble_scheduled_employees`; không xóa bảng hoặc đổi dữ liệu lịch sử. |
| `Repositories/Shifts/ShopScopeRepository.cs` | Danh sách shop theo tài khoản; Manager chỉ dùng shop mình, Admin phải chỉ định shop cho ca mới. |
| `PostgresShiftSchedulingRepository.cs`, `.Writes.cs` | Đọc/ghi quan hệ shop, bảo vệ sửa ca khác shop, giữ shop khi sao chép tuần. |
| `ShiftContracts.cs`, `ShiftAssignmentsController.cs` | Bổ sung `shopOwnerId` và `GET /api/shift-assignments/shops`. |
| `BleScannerRepository.cs` | Đếm và liệt kê nhân viên từ ca; cho Admin chọn chủ shop khi tạo kiosk. |
| `BleAttendanceService.cs` | Thay kiểm tra gán thủ công bằng ca của shop ở nhận tín hiệu, trạng thái, chọn ca và transaction chấm công. |
| `BleAutoCheckoutService.cs` | Kiểm tra đúng phân công tại shop của kiosk khi tự checkout. |
| `BleAttendanceController.cs` | Loại bỏ API PUT/DELETE gán/gỡ nhân viên khỏi kiosk. |
| `Views/Shared/_BleKioskPanel.cshtml`, `wwwroot/js/ble-kiosk.js` | Bỏ nút gán/gỡ, tự hiện danh sách theo ca, giải thích luồng mới. |
| `Views/Home/Schedule.cshtml`, `wwwroot/js/schedule.js` | Chủ shop được chọn sẵn và khóa lựa chọn shop; Admin được chọn. Sửa lỗi reset form làm mất lựa chọn shop. |
| App: `lib/core/schedule_service.dart`, `lib/schedule_screen.dart` | Gửi/đọc `shopOwnerId`; Admin chọn shop khi phân ca hoặc xác nhận ca cũ. Manager không cần chọn. |
| `tools/BleScannerAgent/README.md` | Cập nhật hướng dẫn trong bộ quét ZIP, bỏ bước gán nhân viên. |

`ble_scanner_employees` được giữ để lưu chẩn đoán tín hiệu gần nhất, **không còn là nguồn cấp quyền**. Không cần xóa bảng này để dùng luồng mới.

## Ca cũ và dữ liệu hiện có

**Migration 016 đã áp dụng vào PostgreSQL đang cấu hình.** Đã tạo và kiểm tra bản sao lưu `artifacts/ble-ui/public-before-016-20261028-235228.dump` (132.726 byte) trước khi chạy. Sau migration vẫn có **27 bảng**, số dòng từng bảng không thay đổi. Kết quả kiểm tra lưu tại `artifacts/ble-ui/migration-016-result.json`.

Trước thay đổi, ca làm không lưu shop hoặc người phân ca. Vì vậy không thể suy ra chính xác shop cho ca lịch sử.

- Migration để `shop_owner_id` của ca cũ là `NULL`; những ca đó chưa được phép BLE.
- Với ca **chưa chấm công**, mở sửa ca, xác nhận cửa hàng rồi lưu. Chủ shop được tự chọn shop của mình; Admin chọn đúng shop.
- Ca đã có chấm công vẫn bị khóa sửa để bảo vệ lịch sử. Hoàn tất ca đang mở bằng luồng QR hiện có; phân ca tiếp theo với shop đúng.
- Không dùng danh sách gán kiosk cũ để tự nhận sở hữu ca lịch sử.
- Không đổi dữ liệu sang mô hình cửa hàng riêng biệt trên toàn hệ thống: danh sách nhân sự/lịch tổng hợp hiện có vẫn giữ phạm vi đọc cũ. Đây là quan hệ shop cho phân ca và xác thực BLE.

## Kiểm thử đã chạy

- Build backend: thành công, không lỗi.
- xUnit: **152 đạt, 8 bỏ qua**. Đã bật 11 test PostgreSQL về phân ca trên database kiểm thử riêng. Tám test DB nghỉ phép chưa bật `LEAVE_TEST_CONNECTION`.
- `ble-shop-http-smoke.py`: **44 kiểm tra API đạt** trên backend thật kết nối PostgreSQL bản sao ở localhost:55439. Bao gồm xác thực, tự lấy shop, chặn giả mạo shop, ca hủy, ca chưa có shop, hai nhân viên cùng ca, check-in/checkout, retry, tự checkout và tải bộ quét.
- `ble-shop-admin-smoke.py`: **5 kiểm tra bổ sung đạt** về Admin tạo kiosk/phân ca cho shop đã chọn, quyền chủ shop trên ca đó và sao chép tuần giữ shop đồng thời từ chối nhân viên đã khóa.
- `ble-shop-scope-sql.py`: **6 ranh giới dữ liệu đạt**, transaction rollback sau kiểm tra: qua đêm, ngày mai, ca hủy, khác shop, nhân viên khóa, chủ shop khóa.
- `ble-auto-checkout-smoke.py`: **4 kiểm tra SQL đạt**, gồm kiosk hỏng và nhân viên vẫn còn tín hiệu không bị tự checkout.
- Playwright: **8 kiểm tra UI BLE đạt**, thêm kiểm tra form phân ca của Manager/Admin; không ghi nhận lỗi JavaScript. Có kiểm tra bố cục 390px.
- Flutter: **36 test đạt**, analyze không có lỗi; còn các thông báo mức info về style/deprecation. APK debug đã build thành công.
- Bộ quét Windows ZIP đã được đóng gói lại với hướng dẫn mới.

Chưa kiểm chứng bằng điện thoại và Bluetooth vật lý tại cửa hàng. Kiểm thử API dùng tín hiệu BLE giả lập để xác minh nghiệp vụ; không chứng minh độ ổn định quảng bá nền khi OS dừng app, điện thoại hết pin hoặc chất lượng sóng tại cửa hàng.

## Chạy thử

1. Sau khi migration 016 được áp dụng, khởi động lại backend bằng `dotnet run --project manage365.csproj --launch-profile http`; mở `http://localhost:5295` và tải lại trang.
2. Đăng nhập chủ shop, tạo một ca hôm nay cho nhân viên thử. Với ca cũ chưa chấm công, mở và lưu lại để xác nhận shop.
3. Tại trang chấm công, chọn kiosk thuộc đúng chủ shop. Nhân viên có ca sẽ tự xuất hiện; không còn nút gán.
4. Nếu chưa thiết lập máy quét, tải ZIP, giải nén và chạy `Start-BLE.cmd` trên máy cửa hàng. Máy quét và điện thoại phải dùng cùng backend.
5. Cài APK mới, đăng nhập nhân viên, bật chấm công BLE/cấp quyền. Đứng gần kiosk trong khung giờ check-in và ít nhất 20 giây; theo dõi kết quả trên app/web.
6. Thử tài khoản không có ca ở shop này: không được tự check-in, kể cả trước đây từng được gán kiosk.
7. Thử checkout gần kiosk; thử mất tín hiệu đủ thời gian cấu hình để kiểm tra trạng thái cần đối soát. Khi BLE không sẵn sàng, dùng QR.

APK: `C:\Users\ASUS\StudioProjects\retail-management-app\retail-management-app\mobile\build\app\outputs\flutter-apk\app-debug.apk`.
