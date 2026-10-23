# API nghỉ phép và hướng dẫn kiểm chứng

## Phạm vi đã nối

Nhân viên gửi đơn tại app → Cá nhân → Đơn nghỉ phép. Admin mở web `/LeaveRequests` để xem chi tiết, lịch sử, preview tác động và duyệt/từ chối/thu hồi. Lịch hiển thị nghỉ đã duyệt; phân ca, coverage và check-in kiểm tra nghỉ phép. Chưa tính tiền lương hoặc số dư phép.

Ba đơn vị: `Shift` (một assignment của chính chủ), `Day` (nguyên ngày), `DateRange` (ít nhất hai ngày liên tiếp, tối đa 366 ngày). Cả PartTime/FullTime đều dùng được. Ngày theo Việt Nam UTC+7, timestamp UTC. Ca qua đêm giao ngày nghỉ được bao trọn và cần Admin xác nhận mở rộng.

Lý do: Personal, FamilyEmergency, Sickness, Maternity, Other; giải trình 10–2.000 ký tự. Nghỉ trên 30 ngày cần xác nhận và ghi chú. Nộp bổ sung tối đa 30 ngày, cờ khẩn cấp và giải thích ít nhất 10 ký tự. Các giới hạn hiện ở `GetOptionsAsync` và validation repository; chưa có giao diện thay đổi chính sách.

## Gọi API

Prefix `/api/leave-requests`, header `Authorization: Bearer <token>`. Employee/Manager chỉ xem và thao tác đơn của mình. Admin xét duyệt nhưng không tự duyệt/từ chối đơn mình. Endpoint kiểm tra thêm trạng thái tài khoản và quyền Admin hiện tại trong DB.

| Method | Path | Input / output |
|---|---|---|
| GET | `/options` | Danh mục, giới hạn; lựa chọn phân loại lương chỉ cho Admin |
| POST | `/preview` | Body tạo → previewToken, khoảng thực tế, ca ảnh hưởng, phút, cảnh báo |
| POST | `/` | Body tạo + previewToken → 201, Location, LeaveRequestDto |
| GET | `/me` | status/unit/fromDate/toDate/page/pageSize → danh sách của mình |
| GET | `/{id}` | `{ request, affectedAssignments, dayAllocations, history }` |
| POST | `/{id}/cancel` | `{ version, note? }` → DTO Cancelled, chỉ Pending |
| GET | `/` | Admin; thêm employeeId/departmentId/reasonCode/payrollTreatment |
| GET | `/{id}/review-preview` | Admin; preview mới và existingVersion |
| POST | `/{id}/approve` | version/previewToken/payrollTreatment/reviewNote/longLeaveAcknowledged/expandedCoverageAcknowledged |
| POST | `/{id}/reject` | Admin; `{ version, reason }` |
| POST | `/{id}/revoke` | Admin; `{ version, reason }`, trước khi nghỉ bắt đầu và không có công liên quan |
| GET | `/payroll-summary` | Admin; fromDate/toDate/employeeId/page/pageSize; tối đa 366 ngày mỗi truy vấn |

Danh sách: `{ items, page, pageSize, totalCount, totalPages }`. Page từ 1; pageSize 1–100. Lọc ngày theo giao khoảng thực tế; bộ lọc sai trả 400.

Ví dụ body preview nghỉ một ca:

```json
{
  "unit": "Shift",
  "shiftAssignmentId": 501,
  "reasonCode": "Personal",
  "reason": "Giải quyết công việc gia đình",
  "isEmergency": false
}
```

Nghỉ ngày dùng `unit: "Day", date: "2026-11-03"`; nhiều ngày dùng `unit: "DateRange", startDate, endDate`. Không gửi field của đơn vị khác. Nộp bổ sung thêm `isEmergency: true, lateSubmissionReason`. Không nhận employeeId/status/phân loại lương từ nhân viên.

Gọi POST tạo với body trên thêm previewToken. Nên gửi `Idempotency-Key` là UUID và giữ nguyên khi retry do timeout. Cùng key/nội dung trả lại đơn cũ kể cả preview đã dùng; cùng key/nội dung khác trả 409. PreviewToken không tham gia hash idempotency. Khóa và kết quả idempotency lưu cùng transaction với đơn.

Ví dụ duyệt:

```json
{
  "version": 1,
  "previewToken": "token-tu-review-preview",
  "payrollTreatment": "Unresolved",
  "reviewNote": "Đã xem xét kế hoạch nghỉ",
  "longLeaveAcknowledged": false,
  "expandedCoverageAcknowledged": true
}
```

Preview giữ trong memory cache 10 phút, gắn người dùng/đơn/người duyệt. Restart ứng dụng làm mất preview; lấy lại preview. Triển khai nhiều instance cần cache chung hoặc sticky session. Mọi commit đọc lại lịch dưới khóa nhân viên; khác preview trả `schedule_changed`. Day/DateRange được lấy preview mới để duyệt; Shift đã đổi giờ cần hủy đơn và tạo lại.

## Ràng buộc dữ liệu và lương

- Pending chưa chặn lịch/chấm công; Pending/Approved chồng thời gian bị từ chối khi gửi/duyệt.
- Duyệt thay snapshot cũ bằng lịch tại thời điểm duyệt trong cùng transaction. Lịch, khoảng nghỉ, phân bổ ngày và audit commit/rollback cùng nhau.
- Không duyệt nếu có công giao thời gian hoặc ca mục tiêu đã có check-in, kể cả phiên đang mở. Không xóa/sửa công để duyệt.
- Approved bảo vệ ca gốc; tạo/sửa/hủy/batch/copy bị chặn nếu xung đột. Coverage loại ca nghỉ khỏi nhân sự sẵn sàng.
- Check-in có ca/không ca bị chặn trong nghỉ; vào sớm ca nghỉ cũng bị chặn. Checkout phiên đang mở vẫn cho phép; nếu phát hiện công giao nghỉ Approved, giữ nguyên giờ và gắn trạng thái `NeedsReview` để đối soát.
- Snapshot chia phút ca qua đêm theo ngày địa phương. Ngày chưa có lịch vẫn có dòng 0 phút để đối soát; không suy ra tiền lương bằng 0.
- PaidLeave/UnpaidLeave/ExternalBenefitReview/Unresolved chỉ là phân loại Admin; tất cả còn `payrollReadiness=NeedsReview`. Không trừ phép hay tính tiền/chế độ tự động.
- Rút ngắn nghỉ đã bắt đầu, sửa công, khóa kỳ lương và sổ phép chưa thuộc phiên bản này.

## Lỗi

400 validation/date/unit/pagination; 401 chưa đăng nhập/token hết hạn; 403 sai quyền/tài khoản khóa/tự duyệt; 404 không tồn tại hoặc ngoài quyền đọc; 409 xung đột.

Các lỗi nghiệp vụ trả ProblemDetails với `code`: leave_overlap, schedule_changed, preview_expired, stale_version, attendance_conflict, invalid_leave_status, idempotency_conflict. Model validation dùng ValidationProblemDetails; 401/403 từ middleware có thể không có JSON, client cần fallback theo HTTP status.

## SQL và chạy local

`database/20261022_create_leave_requests_tables.sql` yêu cầu bảng nhân viên, phòng ban, ca làm/phân ca và chấm công hiện có. Program.cs đăng ký repository và gọi EnsureTablesAsync sau khởi tạo phân ca. Script tạo bảng/index có thể chạy lại. Kiểm tra log khởi tạo trước khi sử dụng API. Chưa áp dụng vào database triển khai trong lượt làm việc này.

Backend:

```powershell
dotnet build manage365.slnx --no-restore
dotnet run --project manage365.csproj --launch-profile http
```

Mobile: trỏ API local qua cấu hình ứng dụng; emulator Android thường dùng `http://10.0.2.2:5295`. ApiConfig có URL đã lưu có thể ghi đè URL build. Vào Cá nhân → Đơn nghỉ phép; chọn ca/ngày/khoảng ngày, preview rồi gửi. Admin duyệt trên web, tải lại danh sách/lịch app để xem trạng thái mới.

## Kiểm thử

Các test PostgreSQL opt-in, chỉ cho DB cô lập đúng tên/host/port; không đọc `.env` ứng dụng để reset dữ liệu.

```powershell
$env:LEAVE_TEST_CONNECTION='Host=127.0.0.1;Port=55440;Database=manage365_leave_test;Username=postgres'
$env:SHIFT_TEST_CONNECTION='Host=127.0.0.1;Port=55440;Database=manage365_shift_test;Username=postgres'
dotnet test manage365.Tests/manage365.Tests.csproj --no-restore
```

Ví dụ trên dành cho cluster test local đã chuẩn bị, không dùng thông tin đăng nhập production. Test DB sẽ truncate dữ liệu trong database test. Nếu thiếu biến môi trường, test DB bị skip và không được coi là đã kiểm chứng SQL.

Flutter: `flutter analyze --no-pub`, `flutter test --no-pub` từ thư mục mobile. Kiểm tra thêm trên thiết bị thật: gửi đơn → Admin duyệt → tải lại app → quét QR/GPS → xác nhận bị chặn đúng thời gian.
