# Đánh giá tinh gọn các bảng nghỉ phép

Ngày rà soát: 26/10/2026. Phạm vi: mã backend/web hiện tại, Flutter `retail-management-app/mobile/lib` và truy vấn chỉ đọc PostgreSQL. **Chưa thay đổi cấu trúc hoặc dữ liệu nghiệp vụ trong lần đánh giá này.**

## Kết luận

Có thể thiết kế lại từ **6 xuống 5 bảng** bằng cách chuyển hai mốc thời gian của `don_nghi_phep_khoang` sang `don_nghi_phep`. Đây là ứng viên hợp lý nhất theo ba loại đơn đang triển khai; cần sửa đồng bộ backend, chuyển dữ liệu và kiểm thử trước khi bỏ bảng cũ.

Giao diện web/app có thể giữ nguyên nếu backend giữ nguyên URL, JSON trả về, mã lỗi và quy tắc nghiệp vụ. Xóa bảng trực tiếp khi mã hiện tại còn truy vấn bảng sẽ gây lỗi; việc gộp không tự động làm truy vấn nhanh hơn.

## 1. Dữ liệu thật được kiểm tra

| Bảng | Số dòng | Ý nghĩa |
| --- | ---: | --- |
| `don_nghi_phep` | 7 | Đơn chính, loại nghỉ, lý do, trạng thái, version, thông tin duyệt/lương |
| `don_nghi_phep_khoang` | 7 | Khoảng thời gian nghỉ có hiệu lực |
| `don_nghi_phep_ca` | 0 | Bản chụp các ca bị ảnh hưởng |
| `don_nghi_phep_ngay` | 7 | Phân bổ phút nghỉ theo ngày, kể cả ngày chưa có ca |
| `lich_su_don_nghi_phep` | 15 | Lịch sử thao tác trên đơn |
| `don_nghi_phep_idempotency` | 7 | Lưu kết quả để xử lý yêu cầu tạo đơn gửi lại |

- Mỗi đơn hiện có đúng **1 khoảng nghỉ**; không có đơn thiếu khoảng hoặc có nhiều khoảng.
- Cả 7 đơn đều thuộc loại `Day`, `schedule_basis = Missing`; không có ca ảnh hưởng nhưng vẫn có ngày nghỉ.
- Trạng thái: 3 Approved, 1 Cancelled, 2 Rejected, 1 Revoked.
- Không có chênh lệch giữa tổng phút trên đơn và tổng phút ở bảng ngày trong dữ liệu hiện tại.
- Đây là tập dữ liệu nhỏ và chỉ bao phủ nghỉ một ngày chưa có ca; không thể dựa vào nó để kết luận các trường hợp Shift/DateRange/qua đêm đã được kiểm thử.

## 2. Tác động theo từng bảng

### `don_nghi_phep`: giữ làm bảng chính

Tất cả loại đơn Shift/Day/DateRange và lý do Personal/FamilyEmergency/Sickness/Maternity/Other đã dùng chung bảng này. Không có bảng riêng cho mỗi lý do nghỉ. Bảng chứa trạng thái, phiên bản chống xử lý đồng thời, phương án lương, người duyệt và thông tin yêu cầu ban đầu.

### `don_nghi_phep_khoang`: có thể chuyển vào bảng chính

Trong `ComputePreviewCalculationsAsync` hiện tại:

- Shift tạo một khoảng từ giờ bắt đầu đến giờ kết thúc ca.
- Day tạo khoảng cả ngày rồi hợp nhất với các ca giao với ngày đó.
- DateRange tạo khoảng liên tục từ đầu ngày bắt đầu đến đầu ngày sau ngày kết thúc, rồi hợp nhất với các ca giao với khoảng này.

Các ca được thêm đều giao với khoảng nền, nên kết quả hợp nhất vẫn là một khoảng liên tục. Đây là căn cứ từ thuật toán, ngoài quan sát 7 đơn hiện có.

Đề xuất thêm hai cột `gio_bat_dau_nghi_utc`, `gio_ket_thuc_nghi_utc` kiểu `timestamptz` vào đơn chính, chuyển giá trị từ bảng khoảng và giữ ràng buộc bắt đầu < kết thúc. Phải lưu **khoảng có hiệu lực thực tế**, không chỉ đổi `ngay_nghi` thành 00:00–24:00, vì ca qua đêm có thể mở rộng phạm vi sang ngày khác.

Các phần backend bắt buộc sửa:

- Tạo đơn, đọc danh sách/chi tiết, xem trước duyệt và cập nhật bản chụp lúc duyệt.
- Kiểm tra đơn trùng, kiểm tra ca đã đổi giờ, kiểm tra chấm công xung đột.
- Kiểm tra xung đột khi phân ca và khi nhân viên vào/ra ca.
- Thu hồi đơn trước thời điểm nghỉ bắt đầu; lọc danh sách theo khoảng ngày.
- SQL khởi tạo trong `EnsureTablesAsync`, migration và chỉ mục liên quan.

JSON `effectiveIntervals` vẫn trả về danh sách một phần tử để web/app cũ tiếp tục dùng. Không thay đổi giờ, timezone, quy tắc giao nhau hay phiên bản đơn.

Nếu sau này hỗ trợ một đơn gồm nhiều khoảng nghỉ rời nhau (ví dụ nghỉ sáng thứ Hai và chiều thứ Tư trong cùng đơn), mô hình hai cột sẽ không đủ. Khi đó cần giữ/đưa lại bảng khoảng hoặc tạo các đơn riêng.

### `don_nghi_phep_ca`: nên giữ

Một đơn có thể ảnh hưởng nhiều ca. Bảng lưu tên ca, ngày/giờ, qua đêm, phút theo lịch và liên kết phân công. Nó đang được phân ca, chấm công, Dashboard và Statistics sử dụng để nhận diện ca nghỉ đã duyệt; không thể bỏ chỉ vì hiện có 0 dòng.

Thay bằng dữ liệu lấy lại từ lịch hiện tại có thể làm thay đổi ý nghĩa bản chụp khi lịch được chỉnh sửa. Nhét danh sách ca vào JSON trong đơn chính sẽ đòi hỏi viết lại join/kiểm tra, khó giữ khóa ngoại và ràng buộc không trùng phân công bằng cấu trúc hiện tại.

### `don_nghi_phep_ngay`: có thể tính lại, nhưng chưa đề xuất bỏ

`CalculateDayAllocations` tính phút theo ngày từ **bản chụp ca**, tách ca qua đêm qua mốc 00:00; `IncludeUnscheduledDays` bổ sung ngày trong yêu cầu với 0 phút nếu chưa có ca.

Về kỹ thuật có thể thay bảng bằng truy vấn/view tính toán từ `don_nghi_phep_ca` và ngày yêu cầu trên đơn chính. Tuy nhiên phải sửa truy vấn chi tiết và toàn bộ `/api/leave-requests/payroll-summary`, bao gồm tổng số bản ghi, phân trang, thứ tự ngày, phân loại lương và các ngày 0 phút. Không được tính lại từ lịch làm hiện tại.

Ví dụ ca 22:00 ngày 31/10 đến 06:00 ngày 01/11 phải phân 120 phút cho tháng 10 và 360 phút cho tháng 11. Dùng toàn bộ số phút cho ngày bắt đầu sẽ làm sai báo cáo tháng. Nếu chỉ JOIN các ca mà bỏ bước thêm ngày chưa phân ca, **7 đơn hiện có sẽ không còn dòng ngày để đối soát**.

Bỏ bảng này có thể giảm thêm từ 5 xuống 4 bảng nhưng tăng tính toán mỗi lần báo cáo; chưa có benchmark và đối chiếu đầy đủ để khuyến nghị thực hiện.

### `lich_su_don_nghi_phep`: giữ

Trạng thái hiện tại không thay thế được chuỗi tạo/duyệt/từ chối/thu hồi, người thao tác và thời điểm. Web đọc `detail.history` để trình bày lịch sử. Gộp vào JSON làm thay đổi cách ghi lịch sử và kiểm soát đồng thời mà không có lợi ích được đo trong dự án này.

### `don_nghi_phep_idempotency`: giữ

Flutter gửi `Idempotency-Key` khi tạo đơn. Backend lưu request hash và response để trả lại kết quả cũ khi app gửi lại do mạng chậm; đổi nội dung với cùng khóa phải bị từ chối. Bỏ bảng mà không thay thế cơ chế này có thể tạo đơn trùng hoặc làm yêu cầu gửi lại thất bại.

Có thể phát triển kho idempotency dùng chung cho nhiều API trong tương lai, nhưng đó là chuyển trách nhiệm sang bảng khác, không loại bỏ nhu cầu lưu dữ liệu này.

## 3. Ảnh hưởng đến web và Flutter

| Luồng | Nếu chuyển bảng khoảng đúng cách | Nếu chỉ xóa bảng khoảng |
| --- | --- | --- |
| Nhân viên xem trước/tạo/xem/hủy đơn | Có thể giữ nguyên UI và JSON | Truy vấn/ghi dữ liệu lỗi |
| Web xem trước duyệt/duyệt/từ chối/thu hồi | Giữ nguyên hành vi nếu cập nhật mọi truy vấn | Luồng xử lý có thể trả lỗi hoặc thiếu dữ liệu nếu khởi tạo lại bảng rỗng |
| Phân ca, kiểm tra trùng nghỉ | Phải thay nguồn đọc khoảng thời gian | Mất khả năng kiểm tra hoặc API lỗi |
| Chấm công trong thời gian nghỉ được duyệt | Phải giữ điều kiện giao nhau và khóa đồng thời | Có nguy cơ bỏ chặn nếu backend đọc bảng được tạo lại nhưng không có dữ liệu |
| Đối soát lương | Giữ dữ liệu bảng ngày/ca và JSON hiện tại | Chi tiết đơn có thể lỗi; bỏ thêm bảng ngày còn làm lỗi báo cáo |
| Dashboard/Statistics | Giữ bảng ca và truy vấn liên quan | Xóa/gộp bảng ca sẽ cần sửa thêm thống kê vắng/nghỉ |

**Phát hiện riêng ở Flutter:** `home_screen.dart` đang đưa tài khoản quản lý vào `AdminLeaveManagementScreen`. File này khởi tạo `_leaveRequests = []` và duyệt/từ chối bằng `setState`, chưa gọi API. Màn nhân viên `leave_requests_screen.dart` có gọi API qua `LeaveRequestService`; web duyệt đơn cũng có gọi API thật. Vì vậy chưa thể nói màn duyệt đơn Admin trên app đã đồng bộ với web; gộp bảng không tự hoàn thiện phần kết nối còn thiếu này.

## 4. Phương án triển khai đề xuất nếu chọn 6 → 5 bảng

1. Backup, kiểm tra mọi đơn có đúng một khoảng; dừng chuyển nếu phát hiện ngoại lệ. Lưu cả đơn đã hủy/từ chối/thu hồi.
2. Thêm hai cột nullable vào đơn chính; backfill và đối chiếu mốc thời gian của từng đơn. Tạo ràng buộc/chỉ mục thích hợp sau khi kiểm tra dữ liệu.
3. Sửa repository và khởi tạo schema để hỗ trợ schema mới. Trong thời gian còn backend cũ, cần giai đoạn tương thích/ghi đồng bộ hoặc ngừng ghi trong cửa sổ triển khai; không để hai nguồn dữ liệu lệch nhau.
4. Giữ nguyên hợp đồng API, đặc biệt `effectiveIntervals`, `affectedAssignments`, `dayAllocations`, `history`, `version` và lỗi nghiệp vụ.
5. Kiểm thử trên bản sao rồi chạy kiểm tra web/app với backend mới. Đo truy vấn trước/sau; không suy luận tốc độ chỉ từ số bảng.
6. Chỉ bỏ bảng khoảng sau khi mọi tiến trình backend đã nâng cấp, dữ liệu đồng nhất và không còn code khởi động tạo lại/đọc bảng cũ. Chuẩn bị rollback khôi phục khoảng từ hai cột đã chuyển.

Các ca kiểm thử bắt buộc: nghỉ một ca; nghỉ một ngày nhiều ca; nhiều ngày đủ/thiếu lịch; không có ca; ca qua đêm và qua tháng; đơn trùng; lịch đổi sau preview; không tự duyệt; hai người duyệt đồng thời; chấm công đồng thời với duyệt; gửi lại cùng khóa idempotency; duyệt/từ chối/hủy/thu hồi; tổng phút và phân trang đối soát lương; quyền Admin/Manager/User; dữ liệu đơn cũ.

## 5. Bằng chứng mã nguồn

- [Schema 6 bảng](../database/20261022_create_leave_requests_tables.sql).
- [Tính khoảng nghỉ và đọc báo cáo](../Repositories/LeaveRequests/PostgresLeaveRequestRepository.Queries.cs): `ComputePreviewCalculationsAsync`, `GetDetailByIdAsync`, `GetPayrollSummaryAsync`.
- [Phân bổ ca qua đêm theo ngày](../Repositories/LeaveRequests/PostgresLeaveRequestRepository.Calculations.cs): `CalculateDayAllocations`.
- [Ngày chưa có lịch và thay bản chụp khi duyệt](../Repositories/LeaveRequests/PostgresLeaveRequestRepository.Validation.cs): `IncludeUnscheduledDays`, `ReplaceSnapshotAsync`, `EnsureOriginalShiftAsync`.
- [Tạo/duyệt/thu hồi và idempotency](../Repositories/LeaveRequests/PostgresLeaveRequestRepository.Writes.cs).
- [Chấm công](../Repositories/Attendance/PostgresShiftAttendanceRepository.cs), [phân ca](../Repositories/Shifts/PostgresShiftSchedulingRepository.cs), [báo cáo](../Repositories/PostgresReportingRepository.cs).
- [Hợp đồng JSON](../Routes/API/LeaveRequests/LeaveRequestContracts.cs), [web duyệt nghỉ](../wwwroot/js/leave-requests.js).
- Bằng chứng truy vấn chỉ đọc: `artifacts/db-audit/leave-review.json` (ngoài Git).

Lần rà soát này không chạy migration hay kiểm thử ghi dữ liệu. Kết luận 6 → 5 là phương án kỹ thuật dựa trên mã và dữ liệu hiện tại, chưa phải thay đổi đã triển khai/đã vượt qua kiểm thử hồi quy.
