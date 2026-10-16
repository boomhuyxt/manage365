# Báo cáo kiểm tra phân ca — 15/10/2026

## Kết luận

**Trước triển khai:** Web dùng nhân sự/ca giả, không gọi API khi lưu, sai ngày khi chuyển tuần; backend chưa giới hạn dữ liệu cá nhân. Những lỗi này được tái hiện ở phần đánh giá ban đầu, rồi được xử lý theo [kế hoạch hoàn thiện](../shift-scheduling-completion-plan.md).

Đánh giá ban đầu dẫn đến kế hoạch triển khai được duyệt. Các vấn đề bên dưới đã được dùng làm regression checks trong phần implementation. Không ghi vào DB cấu hình của ứng dụng; PostgreSQL test dùng container local tách biệt.

## Kết quả thực thi

| Nhóm | Kết quả | Phạm vi bằng chứng |
|---|---|---|
| Build .NET | **Đạt, 0 warning / 0 error** | `dotnet build manage365.slnx --no-restore --verbosity quiet`. |
| Unit/regression test | **94 đạt, 9 bỏ qua** | 103 test tổng; 9 test DB đánh dấu skip khi không cấu hình `SHIFT_TEST_CONNECTION`. `shift-final-no-db.trx`. |
| PostgreSQL integration | **103 đạt khi container chạy** | Kiểm thử trực tiếp PostgreSQL local riêng; gồm transaction/rollback, nhân viên thật, concurrency, ca qua đêm, ca dùng chung, hủy mềm và sao chép. Docker Desktop tự dừng trước lượt chạy lại sau thay đổi chấm công nên không thể xác nhận lại lượt này. |
| Web JavaScript smoke | **6 bước xác nhận** | Node VM: tải API nhân viên/mẫu ca/lịch, kiểm tra conflict, lưu POST và tải lại. Kèm `node --check`. DOM giả không thay thế kiểm thử browser. |
| Flutter | **Dart analyze đạt, 0 lỗi; `flutter analyze`/`flutter test` bị hệ điều hành chặn** | Đã format và chạy `dart analyze` trên service, screen, test. Windows Application Control chặn Flutter ProcessStarter khi khởi động công cụ; cần chạy Flutter trên máy phát triển cho phép. Chưa nghiệm thu trên thiết bị thật. |

Lệnh và artifact cục bộ (thư mục `TestResults` được Git bỏ qua):

```powershell
dotnet build manage365.slnx --no-restore --verbosity quiet
dotnet test manage365.Tests/manage365.Tests.csproj --no-restore --logger "trx;LogFileName=shift-final-no-db.trx"
node --check wwwroot/js/schedule.js
node manage365.Tests/TestResults/shift-ui-smoke.cjs
dart analyze lib/core/schedule_service.dart lib/schedule_screen.dart test/schedule_test.dart
```

- Kết quả regression test: `manage365.Tests/TestResults/shift-final-no-db.trx`; PostgreSQL integration: `shift-implementation.trx`.
- Smoke test web: `manage365.Tests/TestResults/shift-ui-smoke.cjs` (artifact test cục bộ, bị Git bỏ qua).
- `flutter test` chưa được xác minh: Flutter báo Windows Application Control chặn khởi chạy tiến trình con. Không cài thêm phần mềm hay sửa policy máy.

## Lỗi đã tái hiện

| Mã | Ưu tiên | Thao tác / kết quả quan sát | Kỳ vọng |
|---|---|---|---|
| UI-01 | Cao | Gọi `handleSaveShift`: ô HTML đổi và toast thành công xuất hiện; **0 request** gửi đi. | POST/PUT thành công rồi tải lại dữ liệu server; lỗi mạng không báo thành công. |
| UI-02 | Cao | Chuyển tuần 38 → 39 → 40: cả tuần 39 và 40 đều hiện `22/09 - 28/09`. | Tính ngày thật, cập nhật tiêu đề/cột/modal và tải đúng khoảng lịch. |
| UI-03 | Vừa | Lọc `cashier` rồi tìm kiếm chuỗi rỗng: dòng `stock` hiện lại. | Kết hợp điều kiện tìm kiếm và bộ lọc. |
| API-01 | Cao | `AssignShift` với `ShiftDate=not-a-date`: `FormatException` thoát khỏi controller. | 400 ProblemDetails, không lỗi server. |
| API-02 | Cao | `CreateTemplate` với `StartTime=not-a-time`: `FormatException` thoát khỏi controller. | 400 ProblemDetails. |
| API-03 | Cao | `AssignShift` với `CustomStartTime=not-a-time`: `FormatException` thoát khỏi controller. | 400 ProblemDetails. |
| AUTH-01 | Cao, cần chốt chính sách | Claims Employee ID 101, yêu cầu lịch ID 202: controller trả 200 và dữ liệu của 202 từ stub. | Nếu chỉ cho xem lịch cá nhân: ép bộ lọc theo claims hoặc từ chối. |
| AUTH-02 | Cao, cần chốt chính sách | Employee ID 101 đọc chi tiết assignment thuộc ID 202: controller trả 200. | Nếu chỉ cho xem lịch cá nhân: trả 403/404. |

Hai kiểm tra đạt trong harness backend: khoảng ngày đảo ngược và thiếu EmployeeId đều trả 400. Harness không xác nhận mã HTTP thực tế của các ngoại lệ; cần test qua pipeline để xác nhận phản hồi cuối cùng.

## Bằng chứng qua mã nguồn và rủi ro cần integration test

- `Views/Home/Schedule.cshtml`: tên nhân viên, lựa chọn trong modal, ngày, KPI, chi nhánh và nhãn vai trò đều cố định. `wwwroot/js/schedule.js` không gọi API; `notifyStaff()` chỉ hiện toast gửi Zalo/SMS.
- `Routes/API/Shifts/ShiftAssignmentsController.cs`: lịch, chi tiết và validate chỉ có `[Authorize]`, chưa kiểm tra quyền sở hữu. Không thể dùng unit test gọi method trực tiếp để kết luận middleware đã chặn 401/403.
- `Repositories/Shifts/PostgresShiftSchedulingRepository.cs`, `AssignShiftAsync`/`UpdateAssignmentAsync`: kiểm tra xung đột và các lệnh ghi chạy tách rời, chưa có transaction/khóa bảo vệ. Cần kiểm chứng hai request đồng thời và lỗi ở lần ghi thứ hai.
- `UpdateAssignmentAsync` sửa `ca_lam` theo ShiftId. Dữ liệu cũ có thể dùng chung ca cho nhiều người; cần xác minh sửa một assignment có ảnh hưởng người khác hay không.
- `DeleteAssignmentAsync` lấy kết quả từ số hàng xóa `ca_lam`, không phải số assignment đã xóa. Cần test ca dùng chung, ca có chấm công, FK và trường hợp trả 404 dù assignment đã bị xóa; chưa chạy SQL thực tế.
- `GetScheduleAsync` xuất phát từ assignment, nên không trả nhân viên chưa có ca. Không lọc `Cancelled` trong truy vấn lịch; số nhân viên là số người có assignment, không phải tổng nhân sự. Coverage chỉ cảnh báo ngày không có bản ghi, chưa chứng minh đủ người theo giờ/kiosk.
- `CopyWeeklyScheduleAsync` truyền cả template ID và giờ đã lưu; `AssignShiftAsync` ưu tiên template ID. Nếu mẫu ca đã đổi giờ, sao chép có thể không giữ giờ tuần nguồn. Ca `Cancelled` cũng chưa được loại khỏi nguồn sao chép.
- Phân ca theo mẫu chưa kiểm tra `template.Status == Active`; chưa thấy kiểm tra nhân viên bị khóa trước khi ghi. API danh sách nhân viên hiện có phân trang nhưng DTO chưa có phòng ban/mã nhân viên dùng trong lịch.
- Fake repository tự cài lại logic, `UpdateAssignmentAsync` của fake không kiểm tra conflict, và copy-week của fake không lọc tuần nguồn. 9 test hiện tại không kiểm chứng SQL, rollback, concurrency, DELETE hoặc quy tắc copy-week trên PostgreSQL.

## Ma trận nghiệm thu sau triển khai

| Nhóm | Ca kiểm thử bắt buộc | Kết quả cần đạt |
|---|---|---|
| Nhân sự | Nhân viên thật, >1 trang, chưa có ca, bị khóa, trùng tên; tìm kiếm + phòng ban | Không mất nhân viên; định danh bằng ID; loại/chặn người không đủ điều kiện. |
| Ngày/tuần | Tuần hiện tại, qua tháng/năm, Chủ nhật, ngày Việt Nam gần 00:00 | Tiêu đề, cột, query và modal cùng khoảng ngày. |
| Phân ca | Mẫu/custom, nhiều ca/ngày, 22:00–06:00; 05:00 hôm sau; hai ca nối tiếp | Ca qua đêm và xung đột đúng; ca nối tiếp được phép; nghỉ ngắn có cảnh báo. |
| Validation | Ngày/giờ sai, thiếu dữ liệu, nhân viên không tồn tại, mẫu Inactive, trạng thái sai | Lỗi 4xx có mã rõ; không 500 và không ghi dở dang. |
| Ghi đồng thời | Hai request cùng nhân viên/giờ; lỗi giữa hai lệnh ghi | Chỉ một ca được lưu; rollback không để ca mồ côi. |
| Sửa/hủy | Sửa ca dùng chung, hủy ca chưa/đã chấm công, đọc lại sau hủy | Không ảnh hưởng người khác; giữ lịch sử chấm công; phản hồi đúng. |
| Batch/copy | Một phần xung đột, copy tuần trống/cùng tuần, mẫu đã đổi giờ, ca hủy | Chính sách rõ; số thành công/lỗi đúng; không tự đổi giờ nguồn. |
| Quyền | Chưa đăng nhập, Employee đọc/sửa người khác, Manager/Admin hợp lệ | 401/403 và phạm vi dữ liệu đúng qua HTTP thực tế. |
| UI | Tải/rỗng/lỗi, token hết hạn, nhấn lưu liên tiếp, tên chứa ký tự HTML, màn hình nhỏ | Không báo thành công giả, không chèn HTML từ dữ liệu người dùng; thao tác được trên desktop/mobile. |
| Liên ứng dụng | Web tạo/sửa/hủy → app refresh → chấm công bằng assignmentId | Hai client cùng dữ liệu; ca hủy không còn là ca hợp lệ để chấm công. |

Kế hoạch thực hiện để duyệt: [shift-scheduling-completion-plan.md](../shift-scheduling-completion-plan.md).
