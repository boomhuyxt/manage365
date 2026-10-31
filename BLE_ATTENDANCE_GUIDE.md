# Tài liệu tổng hợp chấm công BLE — Retail365 / Manage365

Cập nhật: **31/10/2026**. Phạm vi: web ASP.NET Core, app Flutter, bộ quét Windows và PostgreSQL.

Tài liệu này tổng hợp các tài liệu BLE đã có, phân biệt kế hoạch ban đầu với hành vi triển khai và bổ sung các sửa lỗi ngày 30/10. Các số liệu kiểm thử, sao lưu và migration cũ là kết quả được báo cáo tại thời điểm thực hiện, không phải kết quả kiểm tra lại database hay thiết bị ngày 31/10.

## 1. Các tài liệu nguồn

| Tài liệu | Nội dung được tổng hợp |
|---|---|
| `attendance-ble-auto-flow-plan.md` — Kế hoạch luồng tự động | Mục tiêu, tình huống thực tế, tiêu chí và phần còn cần hoàn thiện |
| `attendance-ble-auto-implementation-report.md` — Báo cáo triển khai tự động | Android foreground service, auto-checkout, migration 015 |
| `attendance-ble-shop-auto-implementation.md` — Nhận diện theo shop và ca | Quan hệ cửa hàng, phân ca, phân quyền, migration 016 |
| `attendance-ble-one-time-setup-report.md` — Cài đặt một lần | Bộ cài EXE, công tắc web, phiên quét, migration 017–018, lỗi giao diện |
| [README bộ quét Windows](tools/BleScannerAgent/README.md) | Cài đặt, vận hành, log, build và tương thích thiết bị |

Bốn tài liệu BLE ở thư mục gốc đã được hợp nhất vào file này và không còn giữ thành các file riêng. Tên file cũ trong bảng chỉ dùng để ghi nhận nguồn nội dung. README trong bộ quét Windows vẫn được giữ cho hướng dẫn đi kèm công cụ. File này là tài liệu tổng hợp chính, đặt tại thư mục gốc repository.

## 2. Kiến trúc và trách nhiệm

```mermaid
sequenceDiagram
    participant App as App nhân viên
    participant API as Manage365 API
    participant Kiosk as Agent Windows
    participant DB as PostgreSQL
    participant Web as Web chủ shop
    App->>API: Xin credential bằng phiên đăng nhập và deviceId
    API->>DB: Lưu credential, hash token, thời hạn
    API-->>App: credentialId, token, expiresAtUtc, refreshAfterSeconds
    App-->>Kiosk: Quảng bá UUID BLE chứa token
    Kiosk->>API: Batch quan sát token, RSSI, sequence và thời gian
    API->>DB: Kiểm tra nhân viên, ca, shop, tín hiệu; ghi công và evidence
    App->>API: Đọc trạng thái chấm công
    Web->>API: Đọc trạng thái kiosk và danh sách ghi nhận
```

- **App** đăng nhập, xin credential, phát BLE và hiển thị kết quả. Không dùng địa chỉ MAC làm danh tính nhân viên.
- **Agent Windows** quét BLE thật, gửi quan sát và heartbeat. Máy phải có Bluetooth LE, có mạng và còn thức.
- **Web** quản lý kiosk, bật/tắt, phân ca, xem kết quả. Chỉ mở web và bật Bluetooth trên máy tính chưa đủ: cần agent cài trên máy cửa hàng.
- **Backend** xác thực và quyết định ghi công; app hoặc agent không tự quyết định giờ công hợp lệ.
- **PostgreSQL** lưu chấm công chung cùng thông tin chứng minh BLE. QR + GPS tiếp tục là phương án dự phòng.

RSSI phản ánh cường độ tín hiệu, không phải bán kính hoặc vị trí chính xác. BLE có thể bị ảnh hưởng bởi vật cản, phần cứng và chuyển tiếp tín hiệu.

## 3. Nhận diện nhân viên theo cửa hàng

Mô hình hiện tại: một tài khoản chủ shop đại diện một cửa hàng. Khóa liên kết là ID chủ shop, không phải tên shop, tên nhân viên hoặc phòng ban. Nhiều kiosk của cùng chủ shop cùng thuộc cửa hàng đó; chưa có mô hình nhiều chi nhánh độc lập cho một chủ shop.

1. Chủ shop phân ca: backend ghi `dang_ky_ca.shop_owner_id` từ tài khoản đăng nhập.
2. Admin phân ca: chọn chủ shop phụ trách. Sửa ca và sao chép tuần phải giữ/kiểm tra quan hệ shop.
3. Kiosk thuộc một chủ shop. Backend đối chiếu credential → nhân viên → phân ca → chủ shop của kiosk.
4. View `ble_scheduled_employees` xác định quyền nhận diện từ ca hôm nay, ca qua đêm liên quan và ca đang mở. Ca ngày mai không cấp quyền hôm nay.
5. Không cần gán từng nhân viên vào kiosk. `ble_scanner_employees` còn dùng cho chẩn đoán, không còn là nguồn cấp quyền.

Danh sách nhân viên trên web biểu thị người có ca phù hợp hoặc ca đang mở; chỉ cột tín hiệu/quan sát mới phản ánh việc kiosk nhìn thấy điện thoại.

**Ca lịch sử:** migration 016 để shop của ca cũ là `NULL` vì không có đủ dữ liệu suy ra chủ sở hữu. Ca chưa chấm công cần mở sửa và lưu đúng shop. Ca đã có chấm công bị khóa sửa: dùng QR để hoàn tất theo luồng hiện có, phân ca tiếp theo đúng shop. Không lấy quan hệ gán kiosk cũ để tự sửa lịch sử.

## 4. Thiết lập một lần và sử dụng hằng ngày

### Chủ shop / máy kiosk

1. Chạy backend đã có các migration BLE cần thiết. Mở trang chấm công bằng địa chỉ máy kiosk truy cập được.
2. Chọn hoặc tạo kiosk thuộc đúng shop.
3. Chọn **Kích hoạt BLE trên máy này**, tải `Retail365-BLE-Setup.exe` rồi mở và xác nhận cài trên máy cửa hàng.
4. Bật Bluetooth, giữ máy thức và kết nối mạng. Đợi web xác nhận **Đang quét**.
5. Hằng ngày dùng công tắc **Bật chấm công BLE**; không cần tải lại bộ cài.

Bộ cài có sẵn địa chỉ backend, kiosk và thông tin xác thực. Nó cài dưới `%LOCALAPPDATA%\Retail365\BLE\installs`, tạo shortcut `Retail365 BLE <identity>.lnk` trong Startup của tài khoản Windows và chạy không có console. Tự chạy xảy ra khi tài khoản Windows đăng nhập.

Agent kiểm tra lệnh điều khiển khoảng 5–6 giây/lần, gửi heartbeat khoảng 20 giây/lần. Tắt trên web khiến API từ chối quan sát; agent nhận lệnh rồi dừng watcher, xóa hàng đợi và báo `stopped`. Agent vẫn chờ lệnh bật. Web phải phân biệt yêu cầu bật/tắt với trạng thái máy đã xác nhận.

Lỗi điều khiển/mạng khiến agent dừng quét và thử lại. Sau bật lại, lỗi Bluetooth hoặc mất kết nối lâu, agent 2.0 mở phiên quét mới. Quan sát từ phiên cũ không được dùng để suy ra vắng mặt sau phục hồi.

**Cài lại / thay máy** xoay mã kiosk và làm mã cũ mất hiệu lực; file tải chứa bí mật thiết bị, không đưa lên GitHub. Cài lại chỉ dừng agent thuộc đúng kiosk/backend. Muốn bỏ tự khởi động, mở `shell:startup` và xóa shortcut tương ứng. Log: `%LOCALAPPDATA%\Retail365\BLE\logs`; log lớn có thể chuyển sang `.previous` lúc khởi động tiếp theo.

ZIP và `Start-BLE.cmd` là phương án kỹ thuật cũ còn được giữ. Luồng thông thường dùng EXE. Web không tự cài phần mềm hoặc tự cấp quyền Bluetooth; trạng thái hiển thị là của kiosk được chọn, không chứng minh agent nằm trên máy đang mở trình duyệt.

### Nhân viên

1. Cấu hình app tới cùng backend mà kiosk sử dụng và đăng nhập.
2. Mở Chấm công, chọn **Bật tự động**, cấp quyền Bluetooth/thông báo được yêu cầu.
3. Đứng gần kiosk, đúng ca và đúng shop. Theo dõi kết quả ghi nhận trên app/web.
4. Khi tan ca, bấm **Check-out** gần kiosk; dùng QR khi BLE không sẵn sàng.

Android lưu lựa chọn tự động và dùng foreground service với thông báo. Mở lại app sẽ khôi phục; tắt tự động hoặc đăng xuất sẽ dừng/thu hồi theo luồng hiện có. Tuy nhiên việc xoay credential vẫn có timer tại dashboard; foreground service không đồng nghĩa toàn bộ luồng Dart đã độc lập với màn hình hoặc chạy nền được bảo đảm.

## 5. Luồng check-in

1. App gửi `deviceId` tới API bằng Bearer token.
2. API xác nhận tài khoản đang hoạt động, cấp UUID quảng bá ngẫu nhiên, lưu hash và thời hạn. Credential hiện có hạn 2 phút, gợi ý làm mới sau 60 giây.
3. App quảng bá token qua BLE. Agent gửi batch có `batchId`, `observedAtUtc`, danh sách token/RSSI/sequence và trạng thái scanner.
4. API kiểm tra mã kiosk, trạng thái kiosk, độ mới của batch và credential, tài khoản, cửa hàng, phân ca và điều kiện chấm công.
5. Ngưỡng hiện hành: RSSI tối thiểu **−72 dBm**, hiện diện tối thiểu **20 giây**, tối thiểu **5 quan sát**. Chỉ ghi khi chọn được ca hợp lệ.
6. Ghi chấm công và evidence; tránh ghi trùng. App và web đọc lại kết quả.

Nhiều nhân viên được nhận diện độc lập trong cùng ca. Nếu có nhiều ca hợp lệ giao nhau, hệ thống không tự đoán: dùng luồng chọn ca/QR. Ca qua đêm phải gắn đúng phân ca; đến muộn được xét theo chính sách và thời gian thực tế.

## 6. Check-out chủ động và tự động

### Nhân viên bấm Check-out

App kiểm tra trạng thái phát BLE và thời hạn credential, lấy kiosk đang trực tuyến rồi gửi `shiftAssignmentId` nếu có, `credentialId`, `presenceToken`, `idempotencyKey`, `scannerId`.

API kiểm tra quyền của nhân viên, credential còn hiệu lực, quan sát gần đây tại đúng kiosk/shop và tín hiệu. Khoảng quan sát gần đây được truy vấn là 45 giây. Khi không chỉ định phân ca, phải xác định được đúng một ca đang mở đủ điều kiện. Gửi lại cùng idempotency key không tạo thêm lần ra ca. Khi thành công app dừng phát BLE theo triển khai hiện tại.

Nếu credential hết hạn hoặc còn không quá 15 giây, app cấp lại và báo đứng gần kiosk ít nhất 20 giây rồi bấm Check-out lại. Không coi việc cấp credential thành công là đã ghi giờ ra.

### Tự check-out khi mất beacon

Worker chạy khoảng 30 giây/lần. Với lượt vào ca bằng BLE còn mở, nếu không còn quan sát trong thời gian chờ, kiosk vẫn hoạt động và đúng phân công shop, worker có thể ghi ra:

- Chờ mặc định **5 phút**; cấu hình `Attendance:Ble:AutoCheckoutAbsenceMinutes`, giới hạn **2–30 phút**.
- Kiosk phải active, trạng thái `running`, heartbeat còn mới trong 60 giây.
- Với agent hỗ trợ phiên quét, quan sát phải thuộc phiên hiện tại.
- Ghi giờ ra bằng lần cuối thấy beacon, đặt `NeedsReview`, lưu `checkout_source=beacon_absence`.
- Dùng khóa hàng và ràng buộc evidence để tránh đóng trùng.

Mất beacon có thể do nhân viên rời cửa hàng, hết pin, mất quyền hoặc app bị dừng. Chủ shop phải đối soát. Đây là hành vi triển khai hiện tại; kế hoạch ban đầu đề xuất giữ giờ ra trống khi chưa đủ bằng chứng rời vùng, nên không được xem đề xuất đó là hành vi đã hoàn thành.

Nếu kiosk lỗi/offline hoặc chưa nhận lại beacon sau khi bắt đầu phiên quét mới, không lấy dữ liệu phiên cũ để tự đóng ca. Dùng QR/đối soát theo luồng hiện có.

## 7. API và dữ liệu

Các route dưới đây có tiền tố `/api/attendance/ble`:

| Method và route | Mục đích |
|---|---|
| `GET scanners` | Danh sách kiosk theo quyền |
| `POST scanners` | Tạo/đăng ký kiosk |
| `GET scanners/{scannerId}/overview` | Tổng quan kiosk và nhân viên |
| `PUT scanners/{scannerId}/active` | Yêu cầu bật/tắt |
| `POST scanners/{scannerId}/installer` | Tải bộ cài tự cấu hình |
| `POST scanners/{scannerId}/package` | ZIP kỹ thuật tương thích cũ |
| `POST scanner-control/{scannerId}` | Agent xác thực và lấy lệnh |
| `POST scanner-events/{scannerId}` | Agent gửi quan sát |
| `POST presence-credentials` | App xin credential bằng `deviceId` |
| `DELETE presence-credentials/{credentialId}` | Thu hồi credential |
| `GET me/status` | Trạng thái BLE của nhân viên |
| `POST checkout` | Check-out chủ động |

Route app dùng phiên đăng nhập; route quản trị kiểm tra quyền/phạm vi shop; agent sử dụng bí mật kiosk. Không có bước gán/gỡ từng nhân viên vào kiosk trong luồng mới.

| Bảng/view | Vai trò |
|---|---|
| `ble_scanners` | Kiosk, shop, thông tin xác thực, heartbeat, trạng thái, phiên quét |
| `ble_presence_credentials` | Nhân viên, thiết bị, hash token, thời hạn và thu hồi |
| `ble_presence_observations` | Quan sát tín hiệu từ kiosk |
| `ble_scanner_employees` | Chẩn đoán nhận diện gần nhất; không cấp quyền |
| `ble_attendance_evidence` | Bằng chứng check-in/out, nguồn và idempotency |
| `ble_scheduled_employees` (view) | Quyền nhận diện suy từ cửa hàng và ca |
| `dang_ky_ca`, `ca_lam`, `cham_cong`, `nhan_vien` | Phân ca, ca làm, công và nhân viên dùng chung |

Migration theo thứ tự: [013](docs/sql/013_add_ble_attendance.sql), [014](docs/sql/014_ble_kiosk_management.sql), [015](docs/sql/015_ble_automatic_checkout.sql), [016](docs/sql/016_shift_shop_ble_eligibility.sql), [017](docs/sql/017_ble_agent_control.sql), [018](docs/sql/018_ble_scan_session.sql). 015 bổ sung nguồn checkout; 016 bổ sung shop cho phân ca/view; 017 thêm phiên bản agent; 018 thêm thời điểm bắt đầu phiên quét.

Báo cáo trước ghi nhận các migration 015–018 đã áp dụng sau sao lưu, database khi đó có 27 bảng và số dòng không đổi sau từng migration. Không suy ra số bảng hiện tại từ số liệu này. Bản sao lưu nằm trong `artifacts/ble-ui/public-before-015-...dump` đến `public-before-018-...dump`; kết quả tương ứng là `migration-015-result.json` đến `migration-018-result.json`.

## 8. Các file triển khai chính

| File/nhóm file | Trách nhiệm |
|---|---|
| `Routes/API/Attendance/BleAttendanceController.cs` | Routes, quyền và tải bộ cài |
| `BleAttendanceService.cs`, `BleAttendancePolicy.cs`, `BleAttendanceContracts.cs` trong cùng thư mục | Credential, quan sát, chọn ca, check-out, trạng thái, contracts |
| `Routes/API/Attendance/BleAutoCheckoutService.cs` | Worker đóng ca cần đối soát khi vắng beacon |
| `Repositories/Attendance/BleScannerRepository.cs` | Dữ liệu kiosk và tổng quan |
| `Repositories/Shifts/ShopScopeRepository.cs` và repository phân ca | Phạm vi shop, lưu shop, sao chép tuần |
| `Views/Shared/_BleKioskPanel.cshtml`, `wwwroot/js/ble-kiosk.js`, `wwwroot/js/dashboard.js` | UI điều khiển kiosk và hiển thị kết quả |
| `tools/BleScannerAgent/Program.cs`, `Installer/`, `build-package.ps1` | Quét Windows, điều khiển, bộ cài và đóng gói |
| App `lib/core/ble_attendance_service.dart` | Credential, phát BLE, kiểm tra thời hạn, retry, checkout |
| App `lib/dash_board_screen.dart` | Nút BLE, timer, trạng thái, phục hồi khi app resumed |
| App `lib/main.dart`, `lib/home_screen.dart` | Vòng đời đăng nhập/đăng xuất |
| App `MainActivity.kt`, `BlePresenceService.kt`, manifest | Kênh Flutter–Android, foreground service và quyền |
| App `lib/core/schedule_service.dart`, `lib/schedule_screen.dart` | Đọc/gửi shop khi phân ca |

Gốc app trên máy phát triển: `C:\Users\ASUS\StudioProjects\retail-management-app\retail-management-app\mobile`.

## 9. Kết nối môi trường phát triển

Chạy backend từ repository web:

```powershell
dotnet run --project manage365.csproj --launch-profile http
```

Web trên chính máy backend: `http://localhost:5295`. Với điện thoại qua Wi-Fi, dùng `http://<IPv4-hiện-tại-của-PC>:5295`; hai máy phải truy cập được nhau, backend lắng nghe trên LAN và firewall cho phép TCP 5295. Dùng `ipconfig` để lấy IP mới; IP cũ trong trao đổi không phải cấu hình cố định.

Thử qua USB/ADB:

```powershell
adb devices
adb reverse tcp:5295 tcp:5295
adb reverse --list
```

Lúc này app dùng `http://127.0.0.1:5295`. Địa chỉ này chỉ tới API trên PC khi đường hầm còn hoạt động; rút USB hoặc đổi kết nối ADB có thể phải thiết lập lại. Một request thử không có Bearer token nhận HTTP 401 chứng minh đường truyền tới API, không chứng minh nghiệp vụ chấm công đã thành công.

Nếu cần tạo firewall rule trên máy phát triển, lệnh `New-NetFirewallRule` phải chạy trong PowerShell với quyền quản trị, không phải CMD. Không cần tắt bảo vệ toàn máy.

Build bộ cài Windows bằng `tools/BleScannerAgent/build-package.ps1` trước publish backend. Nếu kiosk nằm trên máy khác, tải bộ cài từ địa chỉ LAN/HTTPS mà kiosk truy cập được; `localhost` chỉ phù hợp khi backend chạy trên chính máy kiosk.

## 10. Lỗi đã gặp và cách xử lý

| Hiện tượng | Nguyên nhân/kiểm tra và hướng xử lý |
|---|---|
| `Connection refused`, URL `127.0.0.1:5295` | Kiểm tra backend và ADB reverse; dùng LAN khi không chạy qua USB |
| Không chạy được `dotnet run`, port đã dùng | Kiểm tra tiến trình giữ cổng, dừng đúng bản backend cũ trước khi chạy mới |
| Web đứng “Đang tải”, lỗi gán `checked` vào null | Razor cũ và JavaScript mới không khớp. Đã bổ sung marker phiên bản/kiểm tra DOM; restart backend và Ctrl+F5 |
| Không có nhân viên ở kiosk | Kiểm tra ca hôm nay/qua đêm, `shop_owner_id`, kiosk đúng shop và tài khoản active |
| Có RSSI nhưng chưa check-in | Kiểm tra thời gian hiện diện, số quan sát, ca hợp lệ, nghỉ phép, ca đang mở và thông báo backend |
| Ca cũ còn mở chặn vào ca mới | Đã giới hạn kiểm tra chặn ở lượt mở trong 24 giờ gần nhất. Không tự sửa hoặc đóng dữ liệu công lịch sử |
| Credential vừa cấp đã hết hạn | Đã quan sát điện thoại nhanh hơn server khoảng 3 phút ngày 30/10, vượt TTL 2 phút; app cũ so sánh trực tiếp hai đồng hồ |
| App xuống nền rồi không checkout | Timer có thể bị OS tạm dừng; đã thêm phục hồi khi resumed và xử lý credential hết hạn lúc bấm checkout |
| “Đã vượt quá số yêu cầu” / HTTP 429 | Tôn trọng `Retry-After`, chờ hết hạn; không bấm liên tục hoặc khởi động BLE nhiều lần |

**Sửa thời hạn ngày 30/10:** app dùng `expiresAtUtc` so với HTTP `Date` của server, trừ thời gian request và biên an toàn 2 giây; dùng `Stopwatch` theo dõi thời gian trôi qua. Khi không đọc được `Date`, dùng `refreshAfterSeconds` làm thời hạn dự phòng bảo thủ. Không phụ thuộc giờ tuyệt đối của điện thoại để kết luận credential đã hết hạn.

`refreshPresence()` chỉ xoay khi thời gian còn lại không quá 65 giây. Check-out yêu cầu còn hơn 15 giây; nếu không, app cấp lại rồi hướng dẫn chờ nhận diện. HTTP 429 được ghi thời gian chờ trong service để hạn chế gọi lại API trong thời gian bị giới hạn. Đây không phải thay đổi nới giới hạn server.

## 11. Tình huống thực tế và nghiệm thu

| Tình huống | Kết quả cần quan sát |
|---|---|
| Nhân viên đúng shop, đúng ca, đứng gần đủ lâu | Một check-in hợp lệ xuất hiện trên app/web/database |
| Hai nhân viên cùng ca | Mỗi người có dữ liệu riêng, không chặn nhau |
| Đi ngang hoặc tín hiệu yếu | Không ghi công trước khi đủ điều kiện |
| Sai shop, ca thiếu shop, ca hủy, tài khoản khóa | Không được cấp quyền ghi công mới qua quan hệ cũ |
| Ca qua đêm hoặc ca giao nhau | Gắn đúng ca; khi không xác định duy nhất thì dùng QR/chọn ca |
| Check-out gần kiosk | Đúng ca đang mở, một lần ghi ra, retry không nhân đôi |
| App nền quá 2 phút rồi trở lại | Credential được phục hồi; chờ kiosk nhận lại rồi checkout |
| Điện thoại lệch giờ ±3 phút | Credential mới không bị kết luận hết hạn chỉ vì giờ điện thoại |
| API trả 429 | App chờ theo phản hồi; không gửi liên tục |
| Rời vùng ngắn hơn thời gian chờ rồi quay lại | Không tự đóng ca vì lần mất tín hiệu ngắn |
| Mất beacon quá thời gian chờ, kiosk vẫn khỏe | Có thể tự đóng BLE với `NeedsReview` và nguồn `beacon_absence` |
| Kiosk lỗi/offline/tắt rồi bật lại | Không đóng từ bằng chứng phiên cũ; cần quan sát mới |
| Điện thoại hết pin/force-stop | Có thể giống rời vùng; giờ ra suy từ vắng beacon phải được đối soát |
| BLE lỗi, chuyển QR | QR vẫn dùng theo chính sách, kiểm tra không ghi trùng |

Trước thử thực tế: có ca hợp lệ thuộc đúng shop, kiosk `Đang quét`, app/backend cùng môi trường, tài khoản đang hoạt động và mạng truy cập được. Đối chiếu kết quả ghi công thực tế; chữ “Đang phát BLE” chỉ xác nhận phía app, không phải check-in thành công.

## 12. Kết quả kiểm thử đã được báo cáo và giới hạn

| Mốc | Kết quả lịch sử và phạm vi |
|---|---|
| Triển khai tự động 28/10 | Backend/APK/ZIP build đạt; Flutter 36/36; xUnit báo 152 test, 19 DB test bỏ qua; 4 kiểm tra sweep SQL đạt. API tích hợp lúc đó bị Application Control chặn |
| Tự nhận diện shop/ca | xUnit 152 đạt, 8 bỏ qua; 44 API + 5 Admin + 6 SQL phạm vi + 4 sweep + 8 UI đạt theo báo cáo; Flutter 36 đạt |
| Cài một lần, agent 2.0 | xUnit 170 tổng, 162 đạt, 8 bỏ qua; 44 API chạy lại, 22 điều khiển/bộ cài, 7 phiên quét, 7 SQL, 8 UI và 9 trạng thái công tắc đạt theo báo cáo |
| Sửa lỗi 30/10 gần nhất | BLE service analyze không báo vấn đề; APK debug build/cài điện thoại thành công. Test lệch đồng hồ chưa chạy được do Application Control chặn `flutter_tester.exe` |

Các bộ test ở các mốc khác nhau không cộng thành một tổng test duy nhất. Nhiều kiểm tra API dùng BLE giả lập trên database bản sao ở cổng 55439/backend 5296, không chứng minh radio hoặc chạy nền ổn định. Kiểm tra installer `--verify-package` không tương đương cài tương tác và khởi động Windows thật.

Chưa có xác nhận hoàn tất check-out vật lý sau bản sửa cuối trong phiên làm việc được ghi nhận. Không tắt Windows Application Control để chạy test; nếu cần, quản trị máy cho phép đúng executable bị chặn.

## 13. Giới hạn và phần còn cần hoàn thiện

- Android foreground service không bảo đảm tồn tại sau force-stop, hết pin hoặc mọi chế độ tiết kiệm pin của hãng. Timer xoay credential còn gắn với dashboard; việc tách toàn bộ BLE khỏi vòng đời màn hình vẫn là mục tiêu cần hoàn thiện.
- Với iPhone, các nguồn cũ ghi nhận quảng bá khi app mở; nhận diện nền bằng scanner Windows hiện chưa được bảo đảm. Dùng QR khi không nhận được tín hiệu.
- Mất beacon chưa phân biệt chắc chắn rời cửa hàng với lỗi điện thoại. Tự checkout hiện ghi giờ suy từ last-seen và yêu cầu đối soát; kế hoạch xác minh bằng chứng rời vùng/GPS bổ sung chưa được coi là đã triển khai.
- Mô hình phiên hiện diện bền vững độc lập, hàng đợi đối soát đầy đủ có lịch sử chỉnh sửa, chính sách dọn observations và kiểm chứng hai thiết bị thật xuyên suốt ngày làm việc là những mục cần đánh giá tiếp theo kế hoạch ban đầu.
- Khi triển khai bản mới: sao lưu database, kiểm tra migration trên bản sao, build agent/installer/backend/app phù hợp, khởi động đúng phiên bản, thử BLE và QR thực tế trước khi sử dụng dữ liệu cho nghiệp vụ tiếp theo.
