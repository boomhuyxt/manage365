# Kế hoạch nghỉ phép v2 — chờ duyệt

Chỉ lập kế hoạch, chưa sửa source ứng dụng, chạy migration hoặc triển khai. Bản này thay thế `leave-request-api-plan.md`.

## 1. Phạm vi đề xuất cụ thể

Đã chốt theo yêu cầu: nghỉ một ca/một ngày/nhiều ngày; xét từng lý do; chỉ Admin duyệt. Những chính sách khác bên dưới là đề xuất cần duyệt.

| Hạng mục | Thực hiện đợt này | Đầu ra nghiệm thu |
|---|---|---|
| API | Danh mục lý do, preview, tạo/xem/hủy, duyệt/từ chối/thu hồi, phân quyền, audit | Swagger, DTO, SQL, test |
| Phân ca | Xác định ca nghỉ, bảo vệ lịch sử, chặn xếp ca xung đột, điều chỉnh coverage | Lịch và số nhân viên sẵn sàng đúng |
| Chấm công | Kiểm tra nghỉ có phép với check-in có ca/không ca; bảo vệ công đã ghi | Không ghi đè công hoặc vừa nghỉ vừa chấm công cùng khoảng |
| Mobile Flutter | Tạo đơn ba chế độ, lịch sử/chi tiết, hủy Pending, hiển thị nghỉ trên lịch | Nhân viên gửi và theo dõi đơn trên app |
| Web Manage365 | Trang Admin lọc/xem tác động/duyệt/từ chối/thu hồi có điều kiện | Luồng xử lý đơn hoàn chỉnh |
| Chuẩn bị tính lương | Snapshot ca, phút nghỉ theo ngày, phân loại xử lý lương, dữ liệu đối soát | Dữ liệu truy vết được; chưa tính tiền |

Đề xuất làm trọn luồng app → API → web Admin trong đợt này để nghiệm thu thực tế. Phạm vi này cần bạn duyệt trước khi code. Chưa bao gồm engine tính lương, sổ phép năm, tự tính chế độ bảo hiểm, nghỉ một phần ca/nửa ngày, upload hồ sơ, push/email hoặc điều chỉnh công đã ghi. Nghỉ dài hạn được hỗ trợ ngay ở mức đơn và phê duyệt.

Căn cứ mã nguồn: backend có JWT và `AppRoles`, `Routes/API/Shifts/`, `Repositories/Shifts/PostgresShiftSchedulingRepository*.cs`, `Repositories/Attendance/PostgresShiftAttendanceRepository.cs`; đã có khóa nhân viên trong đường ghi lịch/chấm công. Mobile nằm ở `C:/Users/ASUS/StudioProjects/retail-management-app/retail-management-app/mobile`, dùng `lib/core/auth_service.dart`, `schedule_service.dart`, `api_config.dart`. Nhân viên hiện có loại và lương cơ bản giờ; không suy diễn đã có đầy đủ cơ chế tính lương tháng/phép tồn.

## 2. Tách thời gian, lý do và cách xử lý lương

- `unit`: Shift, Day, DateRange — xác định thời gian nghỉ.
- `reasonCode`: Personal, FamilyEmergency, Sickness, Maternity, Other — lý do, luôn kèm giải trình.
- `payrollTreatment`: Unresolved, PaidLeave, UnpaidLeave, ExternalBenefitReview — phân loại do Admin chọn, không phải quyền nhân viên tự khai.

“Việc gia đình” không tự động là không lương. “Thai sản” không tự trừ phép năm hoặc tự tính tiền chế độ. Phân loại PaidLeave cũng chưa chứng minh còn số dư phép. Quyền lợi và mức hưởng cần chính sách được xác nhận khi triển khai module lương; kế hoạch này không đặt ra quy định pháp lý.

## 3. Ma trận trường hợp

| Trường hợp | Đầu vào chính | Phạm vi và kết quả |
|---|---|---|
| PartTime nghỉ một ca | Shift + shiftAssignmentId | Nghỉ đúng ca của mình, ca khác không giao giờ vẫn đi làm |
| PartTime làm hai ca và nghỉ cả ngày | Day + date | Tất cả ca giao ngày đó; cộng phút theo lịch thật, không mặc định một ca |
| FullTime nghỉ một ngày | Day + date | Như trên, không mặc định một ngày luôn bằng 8 giờ |
| Nghỉ nhiều ngày vì gia đình/ốm/thai sản | DateRange + startDate/endDate | Bảo vệ cả khoảng nghỉ, kể cả ngày chưa phân ca; Admin xét giải trình |
| Xin bổ sung do khẩn cấp | Một trong ba dạng + isEmergency + lateSubmissionReason | Có thể gửi để xét; không tự duyệt, không ghi đè công |
| Ngày chưa có lịch | Day/DateRange | Vẫn được gửi và duyệt; sau duyệt chặn xếp ca, đánh dấu thiếu cơ sở tính lương |

Cả PartTime và FullTime đều được chọn ba đơn vị phù hợp, không giới hạn cứng theo loại nhân viên. Shift phải thuộc chính chủ và chưa hủy. Day đúng một ngày; DateRange từ hai ngày liên tiếp trở lên. Không hỗ trợ ngày rời rạc trong một đơn; tạo đơn riêng.

Giới hạn đề xuất: giải trình 10–2.000 ký tự; ghi chú bắt buộc 1–2.000 ký tự. Bỏ giới hạn chung 31 ngày. Trên 30 ngày cần Admin ghi nhận xem xét dài hạn; tối đa 366 ngày/đơn là giới hạn kỹ thuật, không phải hạn mức quyền nghỉ. Các ngưỡng này có cấu hình và phiên bản chính sách.

Đơn bình thường gửi trước khi khoảng nghỉ bắt đầu. Đơn khẩn cấp được nộp bổ sung tối đa 30 ngày từ thời điểm bắt đầu, bắt buộc giải thích nộp muộn; Admin ghi rõ căn cứ khi chấp nhận. Đơn cùng ngày nhưng sau 00:00 thuộc trường hợp nộp bổ sung nếu chọn nghỉ nguyên ngày. Có công thực tế trong phạm vi thì không duyệt qua luồng nghỉ phép; cần điều chỉnh công riêng. Khi có khóa kỳ công/lương, phải bổ sung kiểm tra kỳ đã khóa trước khi cho duyệt bổ sung.

## 4. Ràng buộc với lịch và chấm công

**Quy ước thời gian:** ngày nghiệp vụ theo Việt Nam UTC+7; khoảng nửa mở `[start, end)`. Shift lấy giờ thật từ ca, client không tự nhập giờ. Day là 00:00 đến 00:00 hôm sau; DateRange đến 00:00 sau ngày cuối. Thời điểm thao tác lưu UTC.

**Ca qua đêm:** Shift 22:00–06:00 bao trọn ca. Day/DateRange có thể giao một phần ca bắt đầu hôm trước. Đề xuất nghỉ toàn ca giao khoảng vì chưa hỗ trợ nghỉ một phần ca; preview phải hiển thị phần mở rộng và Admin xác nhận. Ví dụ nghỉ ngày 03/11 có thể gồm ca 22:00 ngày 02/11–06:00 ngày 03/11. Phạm vi chặn thực tế là hợp của khoảng xin nghỉ và toàn bộ ca ảnh hưởng, gộp các khoảng giao nhau; không âm thầm bỏ phần qua đêm ngoài ngày đã chọn.

**Pending:** chưa thay đổi lịch/check-in. Chặn đơn Pending/Approved trùng phạm vi thực tế của cùng nhân viên; hai ca không giao nhau trong một ngày vẫn được xin riêng. Preview khi tạo không giữ chỗ. Khi lịch thay đổi, kiểm tra lại tác động lúc duyệt; xung đột mới làm thao tác duyệt thất bại. Nếu ca mục tiêu của đơn Shift bị đổi giờ/hủy, yêu cầu nhân viên hủy đơn cũ và tạo lại theo lịch mới.

**Duyệt nguyên đơn:** kiểm tra lại tài khoản, đơn khác, lịch và công dưới transaction. Nếu một phần bị xung đột thì trả 409 cho cả đơn; chưa duyệt một phần. Lưu trạng thái, snapshot, liên kết ca và audit trong cùng transaction.

**Công đã phát sinh:** không duyệt nếu ca mục tiêu đã check-in hoặc công thực tế giao phạm vi, gồm công không ca và phiên chưa checkout. Không xóa/cắt giờ/ghi công giả để duyệt đơn. Checkout vẫn được phép để kết thúc phiên đang mở; nếu phát hiện phiên giao nghỉ đã duyệt do dữ liệu cũ/ngoại lệ thì đánh dấu cần đối soát, không tự sửa giờ.

**Approved:** giữ phân công gốc và đánh dấu nghỉ riêng, không biến thành ca Cancelled. Chặn check-in có ca/không ca trong thời gian nghỉ; chặn cả check-in sớm vào ca đã được đánh dấu nghỉ. Ca bên ngoài phạm vi vẫn làm bình thường. Coverage không tính nhân sự nghỉ vào số người sẵn sàng; hiển thị thiếu người nhưng không tự tìm người thay.

**Bảo vệ lịch:** không sửa giờ/hủy/xóa ca gắn với Approved; muốn đổi phải thu hồi hợp lệ trước. Tạo/sửa/batch/copy/đổi nhân viên phải cùng kiểm tra nghỉ Approved; rà soát sửa mẫu ca nếu tác động tới ca đã phân. Dùng chung thứ tự khóa nhân viên giữa nghỉ phép, phân ca, check-in để tránh cuộc đua tạo dữ liệu mâu thuẫn.

## 5. Quyền và vòng đời

Nhân viên, kể cả tài khoản Manager, chỉ tạo/xem/hủy đơn của mình. Chỉ Admin xem danh sách toàn hệ thống, preview xét duyệt, duyệt/từ chối/thu hồi và chọn phân loại lương. Manager không có quyền duyệt trong module mới. Không thay đổi quyền phân ca hiện có.

Admin không tự duyệt đơn của mình; cần Admin khác. JWT xác định nhân viên, backend kiểm tra trạng thái hoạt động hiện tại; không nhận employeeId hoặc status từ body tạo. Lịch chung chỉ hiện trạng thái nghỉ, không lộ lý do riêng tư.

```text
Pending → Approved | Rejected | Cancelled
Approved → Revoked (Admin, trước khi bắt đầu nghỉ)
```

Nhân viên hủy Pending; đổi nội dung bằng hủy rồi tạo mới. Từ chối bắt buộc lý do. Admin thu hồi Approved chỉ trước khi phạm vi nghỉ thực tế bắt đầu, không có công liên quan, bắt buộc lý do; giữ audit/snapshot, gỡ tác động nghỉ và khôi phục khả dụng của ca gốc. Không sửa/xóa vật lý đơn đã xử lý.

Gia hạn nghỉ bằng đơn mới liền kề không chồng. Nghỉ dài hạn muốn đi làm sớm sau khi đã bắt đầu cần luồng điều chỉnh riêng ở đợt sau; bản đầu không cho sửa trực tiếp thời gian đã duyệt. Đây là giới hạn cần duyệt rõ trước triển khai.

## 6. Chuẩn bị cho tính lương sau này

Lưu riêng số ngày lịch, phút nghỉ theo lịch và phút làm thực tế. Không lấy số ngày nhân 8; không cộng nghỉ vào giờ chấm công; không tự trừ tiền thêm trên phần giờ vốn không được trả.

Snapshot khi duyệt gồm từng assignmentId, giờ bắt đầu/kết thúc, ngày nghiệp vụ, phút theo lịch, loại nhân viên, policyVersion, người duyệt và thời điểm. Ca qua đêm tách phút theo ngày địa phương để tổng hợp tháng không trùng. Dùng phút nguyên, không làm tròn giờ sớm; nếu ca sau này có nghỉ giữa giờ không lương thì cần quy tắc bổ sung trước khi dùng phút này làm giờ được trả.

Lưu payrollTreatment và `payrollReadiness=NeedsReview`. Trong bản đầu mọi đơn còn cần module lương đối soát hạn mức/đơn giá/quy tắc; Unresolved không được tự thành UnpaidLeave. PaidLeave là phân loại dự kiến; ExternalBenefitReview là cần xét chế độ riêng, không tự tính tiền.

Ngày chưa có lịch: scheduledLeaveMinutes bằng 0, scheduleBasis=Missing/Partial/Available theo mức có dữ liệu; không kết luận lương bằng 0. FullTime tính lương tháng sau này cần lịch chuẩn/hợp đồng, không chỉ dựa ca đã xếp.

Đợt này có API Admin đọc dữ liệu nghỉ theo kỳ. Đợt lương sau bổ sung sổ phép, đơn giá/chính sách có hiệu lực theo thời gian, khóa kỳ và bút toán điều chỉnh; phải tích hợp khóa kỳ vào cả duyệt bổ sung/thu hồi trước khi dùng thực tế để chốt lương.

## 7. Đầu vào/đầu ra API

JWT bắt buộc, prefix `/api/leave-requests`, JSON camelCase. Thay đổi trạng thái nhận version để chống thao tác trên dữ liệu cũ. POST tạo dùng Idempotency-Key UUID theo người dùng/endpoint: cùng key và payload trả kết quả cũ, khác payload trả 409; kết quả lưu cùng transaction.

| Method/path | Input | Output thành công/quyền |
|---|---|---|
| GET `/options` | Không | 200: units, reasonCodes, giới hạn; payrollTreatments nếu Admin |
| POST `/preview` | Body tạo đơn | 200: khoảng thực tế, ca ảnh hưởng, tổng phút, cảnh báo/xung đột, previewToken; chính chủ |
| POST `/` | Body tạo + previewToken | 201 + Location + DTO Pending; chính chủ |
| GET `/me` | status?, unit?, fromDate?, toDate?, page, pageSize | 200 danh sách của mình |
| GET `/{id}` | ID dương | 200 chi tiết/audit; chính chủ hoặc Admin |
| POST `/{id}/cancel` | version, note? | 200 DTO Cancelled; chính chủ Pending |
| GET `/` | Bộ lọc trên + employeeId?, departmentId?, reasonCode?, payrollTreatment? | 200 danh sách; Admin |
| GET `/{id}/review-preview` | ID | 200 tác động hiện tại, version, previewToken; Admin |
| POST `/{id}/approve` | version, previewToken, payrollTreatment, reviewNote?, longLeaveAcknowledged, expandedCoverageAcknowledged | 200 DTO Approved; Admin |
| POST `/{id}/reject` | version, reason | 200 DTO Rejected; Admin |
| POST `/{id}/revoke` | version, reason | 200 DTO Revoked; Admin, đủ điều kiện |
| GET `/payroll-summary` | fromDate, toDate, employeeId?, page, pageSize | 200 dòng nghỉ theo ngày và cờ đối soát; Admin |

Preview token do server phát hành, gắn người dùng/đơn, payload, chính sách và dấu vết lịch liên quan, hết hạn sau 10 phút. Khi commit đọc lại dữ liệu dưới khóa; nếu khác preview trả 409 và yêu cầu xem lại. Không tin client tự gửi tổng giờ hoặc ca ảnh hưởng. reviewNote bắt buộc cho nghỉ trên 30 ngày hoặc nộp bổ sung; xác nhận phần mở rộng bắt buộc khi có ca qua đêm vượt khoảng ngày đã chọn.

**Body theo ba dạng loại trừ nhau:**

```json
{
  "unit": "Shift",
  "shiftAssignmentId": 501,
  "reasonCode": "Personal",
  "reason": "Xin nghỉ ca chiều để giải quyết công việc gia đình",
  "isEmergency": false
}
```

```json
{
  "unit": "Day",
  "date": "2026-11-03",
  "reasonCode": "FamilyEmergency",
  "reason": "Gia đình có việc đột xuất, xin nghỉ tất cả ca trong ngày",
  "isEmergency": false
}
```

```json
{
  "unit": "DateRange",
  "startDate": "2026-11-01",
  "endDate": "2027-03-31",
  "reasonCode": "Maternity",
  "reason": "Xin nghỉ theo kế hoạch cá nhân đã trình bày với quản lý",
  "isEmergency": false
}
```

Khoảng ngày chỉ minh họa, không quy định thời gian hưởng chế độ. Preview dùng body trên; POST tạo thêm previewToken. Nộp bổ sung thêm lateSubmissionReason, server tự xác định nộp muộn; không thể bỏ cờ để vượt kiểm tra. Shift gửi kèm date/startDate/endDate hoặc Day/DateRange gửi assignmentId đều trả 400. Không nhận employeeId/status/payrollTreatment trong body tạo.

**DTO:** id, version, employeeId, employeeName, employeeTypeSnapshot (chốt khi duyệt; Pending dùng thông tin hiện tại được ghi nhãn), unit, reasonCode, reason, requestedPeriod, effectiveIntervals, shiftAssignmentId?, calendarDays?, scheduledLeaveMinutes, affectedAssignmentCount, scheduleBasis, isEmergency, isLateSubmission, lateSubmissionReason?, status, payrollTreatment, payrollReadiness, createdAtUtc, updatedAtUtc, reviewedBy?, reviewedAtUtc?, reviewNote?, cancelledAtUtc?, revokedAtUtc?. Shift có calendarDays=null để không quy đổi ngầm thành ngày phép.

Chi tiết thêm affectedAssignments và history. Pending hiển thị tác động hiện tại; Approved đọc snapshot. Danh sách `{ items, page, pageSize, totalCount, totalPages }`, page >= 1, pageSize 1–100, mặc định 1/20; sắp xếp createdAtUtc DESC, id DESC. Lọc ngày theo giao phạm vi nghỉ thực tế, hỗ trợ một đầu mốc, nếu đủ hai mốc thì fromDate <= toDate. payroll-summary giới hạn tối đa 366 ngày/truy vấn, trả dòng Approved theo ngày trong khoảng lọc, không tính Revoked; không phải bảng lương đã chốt.

**Lỗi:** ProblemDetails/ValidationProblemDetails gồm status/title/detail/instance, code và errors theo field. 400: invalid_unit_fields, invalid_date_range, validation_error; 401: unauthorized; 403: forbidden, employee_inactive, self_review_not_allowed; 404: leave_request_not_found/shift_not_found, áp dụng cả đối tượng ngoài quyền đọc; 409: leave_overlap, attendance_conflict, schedule_changed, preview_expired, stale_version, invalid_leave_status, approved_leave_conflict, idempotency_conflict. Không lộ lý do của người khác trong thông báo xung đột.

## 8. Database và vị trí thay đổi

- don_nghi_phep: dữ liệu đơn, unit/lý do/ngày/ca, trạng thái, version, phân loại lương, metadata; FK nhân viên/người duyệt.
- don_nghi_phep_khoang: khoảng thực tế đã gộp phục vụ kiểm tra giao; Pending phải tính lại nếu lịch thay đổi.
- don_nghi_phep_ca: snapshot ca ảnh hưởng, unique đơn/assignment; don_nghi_phep_ngay: phân bổ phút theo ngày.
- lich_su_don_nghi_phep: actor/action/trạng thái cũ-mới/ghi chú/thời gian; không sửa/xóa qua API thường.
- Lưu idempotency và preview có expiry/version; CHECK enum/ngày/field theo unit, FK RESTRICT bảo vệ lịch sử, index nhân viên/khoảng và trạng thái/ngày tạo. Rà soát đường xóa nhân viên/ca không cascade mất lịch sử.
- Transaction khóa nhân viên theo cùng thứ tự giữa các module; đổi trạng thái bằng điều kiện status + version; snapshot/audit cùng commit hoặc rollback. Batch nhiều nhân viên khóa theo ID tăng dần.
- Backend mới: Routes/API/LeaveRequests/, Repositories/LeaveRequests/, DI Program.cs; SQL database/, tài liệu leave-request-api.md.
- Sửa repository phân ca/chấm công, DTO và coverage; giữ tương thích client bằng trường nghỉ riêng.
- Web: thêm trang Admin, action/menu; cập nhật Views/Home/Schedule.cshtml và wwwroot/js/schedule.js. Quyền server độc lập với nút UI.
- Mobile: thêm lib/core/leave_request_service.dart, màn hình tạo/danh sách/chi tiết, menu và trạng thái lịch; dùng lại auth refresh và ApiConfig. Loading/empty/error, chống bấm lặp, tải lại khi 409. Ghi repository mobile ngoài workspace cần quyền môi trường khi thực thi.

## 9. Trình tự sau khi duyệt

1. Chốt phạm vi và chính sách mục 11; cố định contract và tình huống nghiệm thu.
2. Viết migration/constraint và dữ liệu test cô lập; xác minh chạy mới/chạy lại.
3. Viết tính khoảng nghỉ/ca ảnh hưởng/phút theo ngày, preview và validation; test ranh giới thời gian.
4. Viết repository/service, audit, idempotency, snapshot và transaction; kiểm thử đồng thời trên PostgreSQL.
5. Viết controller, JWT/Admin-only, DTO/Swagger và API đối soát lương.
6. Liên kết mọi đường phân ca/chấm công/coverage, bảo vệ lịch sử và kiểm tra hồi quy.
7. Tích hợp app/web theo phạm vi duyệt, hoàn thiện tài liệu local/API/SQL.
8. Kiểm thử toàn luồng cuối, sửa lỗi, demo và bàn giao bằng chứng thực tế.

## 10. Kiểm thử sau triển khai

| Tình huống | Kết quả mong đợi |
|---|---|
| PartTime có ca 08–12 và 17–21, nghỉ ca chiều | 240 phút nghỉ theo lịch; ca sáng vẫn check-in được |
| Cùng nhân viên nghỉ cả ngày | Hai ca nghỉ, 480 phút theo lịch |
| FullTime có ca 09–17, nghỉ ngày | 480 phút theo lịch; không tự tạo 8 giờ công |
| Nghỉ nhiều ngày gồm ngày chưa xếp ca | Bảo vệ toàn khoảng; phút chỉ từ lịch thật; đánh dấu thiếu cơ sở lương |
| Maternity trên 31 ngày | Được xét theo quy tắc; không tự trừ phép hoặc tính chế độ |
| Ca 22–06 qua tháng | Preview rõ mở rộng; 120/360 phút phân bổ đúng hai ngày/tháng |
| Hai ca không giao trong một ngày, hai khoảng sát nhau | Không bị chặn nhầm; đơn Day trùng một ca Pending/Approved bị 409 |
| Cuối tháng/năm, năm nhuận, UTC sát nửa đêm Việt Nam | Ngày và phút chính xác theo UTC+7 |
| Xin bổ sung khẩn cấp chưa có công | Pending trong cửa sổ cho phép; duyệt cần giải trình |
| Nộp muộn thiếu giải trình/quá cửa sổ | 400, không lưu đơn mới |
| Ca đã làm một phần hoặc có phiên không ca giao thời gian | Duyệt trả 409; công giữ nguyên, checkout vẫn được phép |
| Pending rồi lịch/công thay đổi | Preview cũ không duyệt được; 409 và tải lại |
| Approved rồi tạo/sửa/batch/copy ca giao thời gian | Mọi đường ghi đều bị chặn đúng hợp đồng |
| Check-in sớm ca nghỉ, check-in không ca trong nghỉ | Bị chặn; ca ngoài phạm vi không bị ảnh hưởng |
| Employee/Manager gọi API Admin | 403; không đọc được đơn người khác |
| Admin tự duyệt, token hết hạn, tài khoản khóa | 403/401 tương ứng; DB không đổi |
| Retry cùng Idempotency-Key, cùng key khác body | Cùng kết quả không trùng đơn; khác body 409 |
| Hai create trùng, hai approve, approve đồng thời cancel | Chỉ một thay đổi hợp lệ commit, không audit trùng |
| Approve đồng thời xếp ca/check-in | Không tồn tại nghỉ và làm việc mâu thuẫn |
| Thu hồi trước/sau bắt đầu, thu hồi lặp | Trước bắt đầu hợp lệ khôi phục ca; còn lại 409; snapshot giữ nguyên |
| Unresolved/PaidLeave/ExternalBenefitReview | Phân loại đúng, NeedsReview; chưa tạo tiền/khẳng định phép tồn |
| Migration chạy lại, lỗi giữa transaction | Không mất dữ liệu, không trạng thái/audit nửa chừng |
| Mobile mất mạng sau tạo, bấm lặp, 401, reload | Không trùng đơn; trạng thái lấy từ server, báo lỗi rõ |

Tầng test: xUnit repository fake cho nghiệp vụ/controller; HTTP integration cho JWT/binding/JSON; PostgreSQL integration riêng cho SQL/concurrency. Clock kiểm soát được để test ngày. LEAVE_TEST_CONNECTION chỉ trỏ DB local cô lập và xác minh trước reset; không dùng DB thật. Test skip phải báo rõ, không tính là đã kiểm chứng.

Lệnh backend: dotnet restore manage365.slnx; dotnet build manage365.slnx; dotnet test manage365.Tests/manage365.Tests.csproj --collect:"XPlat Code Coverage". Chạy API bằng dotnet run --project manage365.csproj --launch-profile http. Mobile chạy flutter analyze, flutter test và emulator trỏ API local; kiểm tra URL lưu trong ApiConfig. Chạy hồi quy auth, shifts, attendance và schedule mobile.

Nghiệm thu trên DB test với PartTime hai ca, FullTime, Manager và Admin: app gửi → web preview/duyệt → app tải lại → thử check-in/xếp ca bị chặn → đối chiếu snapshot/phút theo ngày. Thử thêm từ chối/hủy/thu hồi/nghỉ dài hạn. Bàn giao source, SQL, API docs, ảnh UI, HTTP request-response đã che token, báo cáo lệnh chạy và số pass/fail/skip, lỗi đã sửa và hạn chế chưa xác minh. Chưa chạy các test tính năng trong giai đoạn lập kế hoạch này.

## 11. Các đề xuất cần duyệt

1. Làm trọn API + phân ca/chấm công + app gửi đơn + web Admin duyệt + dữ liệu đối soát lương; chưa tính tiền/sổ phép.
2. Ca qua đêm giao ngày xin nghỉ: nghỉ cả ca sau preview và xác nhận của Admin.
3. Nghỉ dài: trên 30 ngày cần xác nhận thêm; giới hạn kỹ thuật 366 ngày/đơn, thay cho chặn 31 ngày.
4. Khẩn cấp: nộp bổ sung trong 30 ngày, bắt buộc giải trình, không ghi đè công.
5. Phân loại lương do Admin chọn, cho phép Unresolved; còn NeedsReview đến khi có module/chính sách lương.
6. Admin thu hồi trước khi bắt đầu; rút ngắn sau khi đã bắt đầu nghỉ và sửa công để đợt sau.

Các ngưỡng và quy tắc đề xuất có thể điều chỉnh khi duyệt. Chỉ sửa tài liệu ở bước hiện tại.
