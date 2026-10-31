# BLE: cài đặt một lần, Bật/Tắt trên web

## Kết quả

- Trang chấm công có công tắc **Bật chấm công BLE**. Không tải bộ quét lại hoặc đổi mã kết nối khi bật/tắt hằng ngày.
- Kiosk chưa dùng agent 2.0 hiển thị **Kích hoạt BLE trên máy này**. Web không còn yêu cầu nhập địa chỉ API hay giải nén ZIP trong luồng cài đặt thông thường.
- Chủ shop tải một file `Retail365-BLE-Setup.exe`, mở và xác nhận cài đặt. Cửa hàng, kiosk và máy chủ được gắn sẵn vào file.
- Bộ cài thiết lập khởi động cùng tài khoản Windows và chạy agent không có cửa sổ console. Nhân viên vẫn được nhận diện theo shop/ca làm, không gán từng người.

Việc cài phần mềm vẫn cần người dùng mở file và xác nhận trên Windows. Web không tự cài phần mềm hoặc tự cấp quyền Bluetooth. Trạng thái hiển thị là của **kiosk đang chọn**, không phải bằng chứng bộ quét đã cài trên chính máy đang mở trình duyệt.

## Luồng hoạt động

1. **Kích hoạt lần đầu:** chọn kiosk thuộc shop → tải bộ cài → mở EXE trên chính máy đang mở trang web → xác nhận cài → bật Bluetooth.
2. **Agent khởi động:** đọc cấu hình đã gắn sẵn; xác thực bằng mã riêng của kiosk. Mỗi kiosk/backend chỉ có một instance agent 2.0 cho tài khoản Windows đó.
3. **Điều khiển:** agent gọi `POST /api/attendance/ble/scanner-control/{scannerId}` khoảng 5–6 giây/lần. Endpoint kiểm tra mã kiosk và tài khoản chủ shop, kể cả khi BLE đã tắt.
4. **Tắt BLE:** web cập nhật trạng thái yêu cầu; API từ chối quan sát chấm công ngay. Agent nhận lệnh, dừng Bluetooth watcher, xóa hàng đợi và báo `stopped`. Web hiển thị chờ xác nhận trước khi báo “Đã tắt BLE”.
5. **Bật lại:** agent vẫn kết nối nên nhận lệnh bật bằng cùng mã, kiểm tra adapter/Bluetooth rồi khởi động watcher. Chỉ báo “Đang quét” khi nhận xác nhận `running` còn mới.
6. **Lỗi/mất mạng:** agent dừng quét nếu không lấy được lệnh máy chủ và thử lại; web phân biệt lỗi bộ quét với mất kết nối. QR vẫn là phương án dự phòng.
7. **Bảo vệ khi khôi phục:** mỗi lần bật lại, mất kết nối lâu hoặc lỗi Bluetooth bắt đầu một phiên quét mới. Auto-checkout chỉ được dùng quan sát của nhân viên trong phiên mới; không lấy tín hiệu cũ trước lúc tắt để suy ra nhân viên đã rời cửa hàng.

Nếu sau khi khôi phục không nhận lại beacon của nhân viên đang có ca mở, không tự kết luận giờ tan ca từ tín hiệu cũ; cần dùng luồng QR/đối soát hiện có.

## Bộ cài

- Gồm template EXE tự chứa .NET runtime, ZIP agent và cấu hình riêng được nối bằng footer định dạng xác định.
- Kiểm tra cấu hình, kích thước, đường dẫn ZIP và file bắt buộc trước khi giải nén.
- Cài vào `%LOCALAPPDATA%\Retail365\BLE\installs\<kiosk-identity>\<version-id>`; không yêu cầu sửa thư mục hệ thống.
- Shortcut tự chạy nằm trong Startup của tài khoản Windows hiện tại, tên `Retail365 BLE <identity>.lnk`.
- Log nằm tại `%LOCALAPPDATA%\Retail365\BLE\logs`; file log lớn được đổi sang bản `.previous` ở lần khởi động tiếp theo.
- Cài lại chỉ dừng agent có đường dẫn thuộc thư mục cài của đúng kiosk/backend đó. Các agent khác không bị dừng.
- Tải lại bộ cài thay mã kiosk, vì vậy chỉ dùng **Cài lại / thay máy** khi cần. File tải chứa thông tin xác thực thiết bị; không đưa lên GitHub.
- Nếu dùng `localhost`, phải cài trên máy đang chạy backend. Với máy kiosk khác, mở web qua địa chỉ LAN/HTTPS truy cập được rồi tải bộ cài từ máy đó.

Muốn bỏ tự chạy: mở `shell:startup` và xóa shortcut `Retail365 BLE ...` của kiosk tương ứng. Công tắc Tắt trên web chỉ dừng quét, vẫn giữ agent kết nối để có thể bật lại.

## File đã thay đổi

| File | Nội dung |
|---|---|
| `Views/Shared/_BleKioskPanel.cshtml`, `wwwroot/js/ble-kiosk.js` | Công tắc hằng ngày, trạng thái chờ xác nhận, luồng tải bộ cài không nhập API. |
| `BleAttendanceController.cs` | API tải installer tự cấu hình từ địa chỉ request; endpoint điều khiển agent; cờ sẵn sàng bộ cài. |
| `BleScannerRepository.cs`, `BleAttendanceContracts.cs` | Phiên bản agent, lệnh bật/tắt, trạng thái xác nhận, thời điểm bắt đầu phiên quét. |
| `BleAutoCheckoutService.cs` | Không dùng quan sát từ phiên quét trước sau khi khôi phục agent 2.0. |
| `tools/BleScannerAgent/Program.cs` | Poll điều khiển, dừng/bật watcher, dừng khi mất điều khiển, instance lock và log nền. |
| `tools/BleScannerAgent/Installer/` | Bộ cài Windows, kiểm tra payload, cài theo user, Startup shortcut và chạy agent nền. |
| `tools/BleScannerAgent/build-package.ps1` | Build agent ZIP và installer template. |
| `manage365.csproj` | Đóng gói cả ZIP và template installer khi publish. |
| `docs/sql/017_ble_agent_control.sql` | Thêm `agent_version`; không thêm bảng. |
| `docs/sql/018_ble_scan_session.sql` | Thêm `scan_started_at`; không thêm bảng. |

API ZIP cũ vẫn giữ để tương thích kiểm thử/công cụ kỹ thuật; giao diện thông thường dùng EXE. Agent cũ cần nâng cấp một lần để công tắc thực sự điều khiển Bluetooth trên máy.

Migration **017 và 018 đã áp dụng** vào PostgreSQL đang cấu hình, sau khi sao lưu từng bước. Database vẫn có **27 bảng**, số dòng mỗi bảng không đổi. Bản sao lưu và kết quả kiểm tra:

- `artifacts/ble-ui/public-before-017-20261029-112354.dump` — 135.232 byte; `migration-017-result.json`.
- `artifacts/ble-ui/public-before-018-20261029-112928.dump` — 135.290 byte; `migration-018-result.json`.

## Kiểm thử

- Build backend, agent Windows và installer: thành công.
- xUnit: **170 test tổng; 162 đạt, 8 bỏ qua**. Có 10 trường hợp bộ cài (footer, cấu hình, đường dẫn, tải thiếu) và 11 test PostgreSQL phân ca. Tám test nghỉ phép chưa bật kết nối DB riêng.
- **44 kiểm tra API BLE** trước đó chạy lại và đạt: phân quyền, shop/ca, check-in, checkout, retry, tự checkout và tải ZIP tương thích. Không kiểm thử quét QR bằng camera trong lượt này.
- **22 kiểm tra điều khiển/bộ cài đạt:** quyền chủ shop, bí mật kiosk, tắt vẫn nhận lệnh bật, chặn quan sát khi tắt, lỗi/offline không báo đang quét, cấu hình tự động và xoay mã khi cài lại. File EXE tải thật đã chạy chế độ `--verify-package` thành công, không thực hiện cài đặt.
- **7 kiểm tra phiên quét đạt** qua API thật: bắt đầu, giữ phiên, tắt, chờ xác nhận bật, khôi phục, lỗi Bluetooth và mất heartbeat.
- **7 kiểm tra SQL auto-checkout đạt**, bao gồm không dùng dữ liệu trước lúc tắt và không suy ra vắng mặt khi chưa xác nhận phiên mới.
- Playwright: **8 kiểm tra UI BLE**, thêm **9 trạng thái công tắc**; không có lỗi JavaScript. Đã kiểm tra giao diện 390px. Khi cập nhật công tắc xong nhưng tải lại trạng thái bị lỗi, UI giữ thông báo lỗi thay vì khẳng định lại trạng thái cũ.
- Đã phát hiện và sửa lỗi tính sai chiều dài footer bộ cài (15 thay vì 16 byte) trước khi xác minh EXE thành công.

Tất cả dữ liệu thử ở database bản sao localhost:55439, backend thử ở localhost:5296. Không cài agent thử vào Startup thật của người dùng. App Flutter không thay đổi trong lượt này nên không chạy lại bộ test Flutter.

**Giới hạn:** chưa kiểm thử thao tác cài tương tác, khởi động lại Windows hoặc Bluetooth trên điện thoại thật. Windows Application Control từng chặn một lần chạy EXE thử (4551); lần xác minh tiếp theo đã đạt, không sửa/tắt chính sách bảo vệ. Không thể bảo đảm chính sách của mọi máy sẽ cho phép các bộ cài chưa ký. Khi máy chặn, cần quản trị máy kiểm tra/cho phép đúng bản cài, không tắt bảo vệ toàn máy.

## Dùng thử

1. Khởi động lại backend và tải lại trang chấm công (Ctrl+F5).
2. Chọn kiosk, bấm **Kích hoạt BLE trên máy này** → **Tải bộ cài BLE cho Windows**.
3. Mở EXE vừa tải và xác nhận cài; bật Bluetooth. Đợi trạng thái **Đang quét**.
4. Tắt công tắc: đợi **Đã tắt BLE**. Bật lại: đợi **Đang quét**; không tải lại bộ cài.
5. Dùng tài khoản nhân viên có ca đúng shop, cấp quyền BLE trên app, đứng gần kiosk để thử chấm công. Nếu lỗi, dùng QR và xem trạng thái/log.
6. Lần đăng nhập Windows sau, agent tự chạy theo shortcut. Máy cần còn thức, có mạng và Bluetooth; khi tắt BLE trên web, agent chỉ chờ lệnh.

## Sửa lỗi giao diện cũ chạy với JavaScript mới

Ảnh lỗi `Cannot set properties of null (setting 'checked')` được xác nhận trên tiến trình Manage365 ở cổng 5295 khởi động lúc 09:58. HTTP trả về Razor cũ (có `bleDownload`, không có `bleSwitch`), trong khi file JavaScript trên đĩa đã là phiên bản có công tắc. JavaScript bị lỗi lúc gắn sự kiện/hiển thị trạng thái nên màn hình đứng ở “Đang tải”; bấm làm mới lại gặp lỗi DOM. Đây không phải bằng chứng API mất kết nối.

- Đã bổ sung marker `data-ble-ui-version="2"` và kiểm tra các phần tử cần thiết trước khi khởi tạo JavaScript. Trang cũ hiển thị hướng dẫn đồng bộ phiên bản, không đăng ký các handler không tương thích.
- Trang Index trả `Cache-Control: no-store,no-cache`.
- Đã dừng đúng tiến trình Manage365 cũ, build và khởi động lại ứng dụng tại `http://localhost:5295`. HTTP sau khởi động có công tắc, nút cài đặt và marker phiên bản mới.
- `manage365.Tests/ble-ui-version.spec.cjs`: kiểm tra lại cả HTML cũ thực tế và HTML mới với JS hiện tại, đều không có ngoại lệ JavaScript; giao diện mới khởi tạo công tắc đúng. API được mock trong kiểm thử này, không ghi dữ liệu chấm công thật.
- Người dùng đang mở tab cũ chỉ cần Ctrl+F5 để lấy giao diện mới.
