# Báo cáo triển khai hoàn thiện API tags

**Ngày:** 2026-10-23  
**Repo:** Manage365 ASP.NET Core / PostgreSQL  
**Kết quả:** Đã triển khai các API và thay đổi xuyên suốt trong phạm vi kế hoạch. Build và test tự động thành công; kiểm thử PostgreSQL cần môi trường test riêng nên chưa chạy trong lượt này.

## Công việc đã thực hiện

| Tag | Thay đổi |
|---|---|
| **Attendance API** | Gắn nhóm Swagger `Attendance`; giới hạn snapshot, cấu hình vị trí cửa hàng, tìm địa chỉ và phát QR kiosk cho Admin/Manager. Sửa tổng `TotalToday`, `CheckedIn`, `Completed` bằng window aggregates trên toàn bộ dữ liệu trong ngày; danh sách chi tiết vẫn giới hạn theo `limit`. |
| **Dashboard API** | Thêm `GET /api/dashboard/summary?date=YYYY-MM-DD&recentLimit=20`. Trả ngày, ca được phân, đang chấm công, đã hoàn thành, số ca vắng, đơn nghỉ chờ duyệt, thời điểm cập nhật và danh sách chấm công gần nhất. `recentLimit` được giới hạn 1–100; bỏ `date` thì dùng ngày theo `AttendancePolicy:TimeZone`. Ca vắng chỉ tính khi ca đã kết thúc, không có bản ghi chấm công và không có đơn nghỉ ca được duyệt. Chỉ Admin/Manager truy cập. |
| **Statistics API** | Thêm `GET /api/statistics/attendance?fromDate=...&toDate=...&groupBy=day|week|month&employeeId=...&departmentId=...`. Trả các bucket có `scheduled`, `checkedIn`, `completed`, `absent`; giới hạn khoảng tối đa 366 ngày, từ chối ngày đảo/ngày thiếu, bộ nhóm không hỗ trợ và ID không hợp lệ. Chỉ Admin/Manager truy cập. |
| **Notification API** | Thêm `GET /api/notifications?unreadOnly=false&page=1&pageSize=20`, `GET /api/notifications/unread-count`, `PATCH /api/notifications/{id}/read` và `POST /api/notifications/read-all`. Danh tính lấy từ JWT `sub`; thao tác thông báo người khác trả 404. Phân trang tối đa 100 dòng và response có `Cache-Control: no-store`. |
| **Sự kiện thông báo** | Khi Admin duyệt hoặc từ chối đơn nghỉ, thông báo được ghi cùng transaction thay đổi trạng thái đơn; nếu transaction lỗi thì không phát sinh thông báo rời. `idempotency_key` duy nhất chống tạo lặp. Payload có mã đơn và trạng thái. Đã thêm bảng, index và script migration `database/20261023_create_notifications.sql`; repository cũng tự đảm bảo schema khi khởi động theo cách repo hiện tại. |
| **Xử lý Error/Exception** | Thêm exception handler dùng Problem Details cho lỗi toàn cục; body có `code` và `traceId`, không trả stack trace/SQL. Lỗi DB trả 503, timeout trả 504, unique/reference conflict trả 409, lỗi khác trả 500. Bật exception handler cả khi Swagger hoạt động; lỗi model validation và filter ca/đơn nghỉ cũng có `traceId`. |
| **Security** | Thay CORS `AllowAnyOrigin` bằng danh sách `Cors:AllowedOrigins` (mặc định rỗng). Swagger tắt mặc định, tự bật trong Development hoặc khi cấu hình `EnableSwagger=true`. Dashboard, Statistics và Notification không cho cache; endpoint quản trị Attendance kiểm tra vai trò. |
| **Rate Limit** | Thêm fixed-window giới hạn toàn cục 300 request/phút/IP và bucket Reporting 60 request/phút/người dùng đã đăng nhập. Các bucket auth, password reset, quét Attendance và tìm địa chỉ đang có tiếp tục được giữ. Khi vượt ngưỡng trả 429, `Retry-After`, `code=rate_limit_exceeded` và `traceId`; queue bằng 0. |
| **Tối ưu API / Query** | Dashboard và Statistics tính tổng hợp ở PostgreSQL, không tải toàn bộ bản ghi để đếm; danh sách gần nhất có giới hạn và thứ tự ổn định. Truy vấn báo cáo/thông báo/snapshot có timeout 15 giây và nhận `CancellationToken`. Không thêm index mới khi chưa có số đo `EXPLAIN` trên dữ liệu đại diện. |

## Hợp đồng đầu vào/đầu ra chính

Các endpoint mới sử dụng JSON camelCase theo cấu hình serializer ASP.NET Core. Các truy vấn ngày dùng `YYYY-MM-DD`; `fromDate` và `toDate` tính cả hai đầu. Statistics hỗ trợ bộ lọc nhân viên và phòng ban; dữ liệu ngoài quyền truy cập chỉ trả cho vai trò Admin/Manager. Notification không nhận `employeeId` từ client.

Response notification gồm `items`, `page`, `pageSize`, `totalCount`, `unreadCount`; từng item gồm `id`, `type`, `title`, `message`, `createdAtUtc`, `readAtUtc`, `data`. Đánh dấu một thông báo không thuộc nhân viên hiện tại trả 404 để không tiết lộ sự tồn tại của ID.

## File thay đổi/thêm mới

- Khởi tạo pipeline, CORS, Swagger, Problem Details, DI và rate limit: [Program.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Program.cs>), [appsettings.json](</D:/LT_Mobile_QLDA/web_app/manage365/appsettings.json>).
- Attendance và snapshot: [AttendanceShiftController.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/Attendance/AttendanceShiftController.cs>), [AttendanceLocationAdminController.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/Attendance/AttendanceLocationAdminController.cs>), [AttendanceKioskController.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/Attendance/AttendanceKioskController.cs>), [PostgresShiftAttendanceRepository.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Repositories/Attendance/PostgresShiftAttendanceRepository.cs>).
- Dashboard/Statistics: [DashboardController.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/DashboardController.cs>), [StatisticsController.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/StatisticsController.cs>), [ReportingContracts.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/ReportingContracts.cs>), [IReportingRepository.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Repositories/IReportingRepository.cs>), [PostgresReportingRepository.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Repositories/PostgresReportingRepository.cs>).
- Notifications: [NotificationsController.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/NotificationsController.cs>), [NotificationContracts.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/NotificationContracts.cs>), [INotificationRepository.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Repositories/INotificationRepository.cs>), [PostgresNotificationRepository.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Repositories/PostgresNotificationRepository.cs>), [migration](</D:/LT_Mobile_QLDA/web_app/manage365/database/20261023_create_notifications.sql>) và [PostgresLeaveRequestRepository.Writes.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Repositories/LeaveRequests/PostgresLeaveRequestRepository.Writes.cs>).
- Lỗi và kiểm thử: [ApiExceptionHandler.cs](</D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/Errors/ApiExceptionHandler.cs>), [ApiTagSecurityTests.cs](</D:/LT_Mobile_QLDA/web_app/manage365/manage365.Tests/ApiTagSecurityTests.cs>), [ApiExceptionHandlerTests.cs](</D:/LT_Mobile_QLDA/web_app/manage365/manage365.Tests/ApiExceptionHandlerTests.cs>), [ReportingApiTests.cs](</D:/LT_Mobile_QLDA/web_app/manage365/manage365.Tests/ReportingApiTests.cs>), [NotificationsControllerTests.cs](</D:/LT_Mobile_QLDA/web_app/manage365/manage365.Tests/NotificationsControllerTests.cs>), [LeaveRequestsDatabaseTests.cs](</D:/LT_Mobile_QLDA/web_app/manage365/manage365.Tests/LeaveRequestsDatabaseTests.cs>).

Repo đã có thay đổi chưa commit về Leave Requests, lập lịch ca và Attendance trước khi bắt đầu lượt này. Những thay đổi đó được giữ nguyên; riêng phần xử lý ghi thông báo được thêm vào transaction duyệt/từ chối đơn nghỉ để đảm bảo tính nhất quán.

## Kiểm thử và xác minh

- `dotnet build manage365.slnx --no-restore` — thành công, 0 lỗi. Build báo 28 cảnh báo analyzer xUnit về truyền `CancellationToken` trong test.
- `dotnet test manage365.Tests/manage365.Tests.csproj --no-restore` — 121 passed, 0 failed, 18 skipped, tổng 139. Các test bị skip là test cần PostgreSQL chuyên dụng.
- Kiểm thử mới xác nhận chính sách role, Problem Details an toàn có trace ID, giới hạn khoảng ngày/nhóm thống kê, clamp `recentLimit`, danh tính người dùng lấy từ JWT và 404 khi ID thông báo không thuộc người dùng.
- `git diff --check` và parse `appsettings.json` — thành công.

## Cấu hình và giới hạn còn lại

- Môi trường triển khai giao diện khác origin cần khai báo `Cors:AllowedOrigins` (ví dụ biến môi trường `Cors__AllowedOrigins__0`). Cấu hình mặc định không cho origin ngoài truy cập; ứng dụng same-origin không cần khai báo thêm.
- Có thể chạy script migration trước deploy; ứng dụng cũng gọi `EnsureTablesAsync` khi khởi động. Chưa chạy migration lên DB thật.
- Chưa chạy các test PostgreSQL vì môi trường này không có `LEAVE_TEST_CONNECTION` tới DB cô lập `127.0.0.1:55440/manage365_leave_test`. Cần chạy lại để xác nhận SQL snapshot, aggregate, migration và thông báo transaction trên PostgreSQL.
- Rate limiter của ASP.NET Core hiện giữ trạng thái trong bộ nhớ từng process; nếu triển khai nhiều instance, cần gateway hoặc limiter phân tán. Cấu hình trusted forwarded headers cho proxy cũng chưa có, nên giới hạn IP phụ thuộc `RemoteIpAddress` của kết nối.
- Chưa có benchmark p95/throughput hoặc `EXPLAIN (ANALYZE, BUFFERS)` vì không có dữ liệu DB đại diện. Đã tối ưu cấu trúc aggregate và giới hạn kết quả, nhưng chưa kết luận mức cải thiện hiệu năng bằng số đo.
- Giao diện dashboard hiện vẫn dùng `/api/attendance/admin/snapshot` để tương thích; endpoint mới `/api/dashboard/summary` đã sẵn sàng nhưng chưa chuyển JavaScript sang route mới. Push/FCM chưa triển khai; Notification MVP lưu và đọc thông báo trong ứng dụng.

## So sánh trước và sau 

Bảng dưới đây đối chiếu hiện trạng thể hiện trong ảnh trước khi triển khai với phần đã thay đổi trong repo. Các mục kiểm thử môi trường thật, benchmark hoặc chuyển đổi frontend được ghi riêng để phản ánh đúng phạm vi đã xác minh.

### Attendance API

- **Trước:** Đã có QR, check-in/out, GPS, lịch sử, kiểm tra nghỉ phép và snapshot. Quyền một số endpoint quản trị chưa chặt; tổng snapshot có thể thiếu khi danh sách vượt `limit`.
- **Sau:** Giới hạn cấu hình địa điểm, tìm địa chỉ, phát QR kiosk và snapshot cho Admin/Manager. Tổng snapshot tính trên toàn bộ bản ghi, danh sách chi tiết vẫn có giới hạn.
- **Còn lại:** Kiểm thử QR/GPS trên thiết bị thật và chạy kiểm thử SQL snapshot với PostgreSQL.

### Dashboard API

- **Trước:** Dashboard dùng `/api/attendance/admin/snapshot`, làm mới mỗi 5 giây; chưa có API tổng hợp riêng.
- **Sau:** Thêm `GET /api/dashboard/summary` với số ca, chấm công, ca vắng đã kết thúc, đơn nghỉ chờ duyệt và danh sách gần nhất có giới hạn.
- **Còn lại:** Frontend vẫn dùng snapshot cũ; cần kiểm thử tích hợp DB rồi mới chuyển sang API mới.

### Statistics API

- **Trước:** Có thống kê rời rạc, chưa có API báo cáo theo kỳ đầy đủ.
- **Sau:** Thêm `GET /api/statistics/attendance`, nhóm ngày/tuần/tháng, lọc nhân viên/phòng ban; giới hạn khoảng tối đa 366 ngày.
- **Còn lại:** Chưa kiểm thử truy vấn trên PostgreSQL thật hoặc benchmark dữ liệu lớn.

### Notification API

- **Trước:** Chưa có thông báo nội bộ; chỉ có email đặt lại mật khẩu.
- **Sau:** Có API danh sách, đếm chưa đọc, đánh dấu một/tất cả đã đọc. Thông báo duyệt/từ chối đơn nghỉ được ghi cùng transaction và giới hạn theo JWT nhân viên.
- **Còn lại:** Chưa triển khai push/FCM; cần chạy test DB để xác minh migration và transaction.

### Tối ưu API

- **Trước:** Đã có async, `CancellationToken`, phân trang và giới hạn ở một số API; chưa đo p95/p99.
- **Sau:** Dashboard/Statistics dùng aggregate SQL; giới hạn kết quả, timeout 15 giây, hỗ trợ hủy request; response riêng tư không cache.
- **Còn lại:** Chưa đo p95/p99 hoặc throughput để kết luận mức cải thiện hiệu năng.

### Xử lý Error/Exception

- **Trước:** Có `ProblemDetails` và filter riêng ở một số nhóm API; exception handler toàn cục phụ thuộc cấu hình Swagger.
- **Sau:** Có exception handler toàn cục, mã lỗi ổn định và `traceId`; chuẩn hóa validation; ánh xạ lỗi DB, timeout và conflict.
- **Còn lại:** Cần xác minh thêm qua kiểm thử tích hợp trên PostgreSQL.

### Optimize Query

- **Trước:** Có index, `LIMIT/OFFSET` và truy vấn nghỉ phép theo batch; chưa có bằng chứng `EXPLAIN ANALYZE`.
- **Sau:** Snapshot đếm toàn bộ bằng window aggregate; báo cáo tổng hợp tại PostgreSQL, không tải toàn bộ dữ liệu chỉ để đếm.
- **Còn lại:** Chưa chạy `EXPLAIN (ANALYZE, BUFFERS)` hoặc so sánh trước/sau trên dữ liệu đại diện; chưa thêm index mới khi chưa có số đo.

### Security

- **Trước:** Có JWT và một số phân quyền; CORS cho phép mọi origin; Swagger bật mặc định theo cấu hình.
- **Sau:** Siết API quản trị Attendance và API báo cáo theo Admin/Manager; CORS theo danh sách origin; Swagger tắt mặc định ngoài Development; API báo cáo/thông báo đặt `no-store`.
- **Còn lại:** Khi triển khai cross-origin cần khai báo origin được phép; cần rà soát cấu hình proxy nếu chạy sau load balancer.

### Rate Limit

- **Trước:** Đã giới hạn auth, password reset, quét chấm công và tìm địa chỉ.
- **Sau:** Thêm giới hạn toàn cục 300 request/phút/IP và Reporting 60 request/phút/người dùng; trả 429 kèm `Retry-After`, mã lỗi và `traceId`.
- **Còn lại:** Limiter lưu trong bộ nhớ từng instance; triển khai nhiều instance cần gateway hoặc limiter phân tán.

**Kết quả kiểm thử để báo cáo:** build thành công, 0 lỗi; test có 121 đạt, 0 lỗi và 18 bị bỏ qua do cần PostgreSQL test riêng chưa được cấu hình. Chưa chạy migration lên DB thật và chưa benchmark hiệu năng.
