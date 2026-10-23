# Bàn giao tiếp tục chức năng nghỉ phép — 2026-10-23

## Phần được kiểm tra và tiếp tục

Mã ban đầu đã có controller/repository, SQL, trang web và service Flutter; build được. Các test controller ban đầu dùng fake chưa kiểm chứng SQL hoặc transaction.

Các sửa chữa chính:

- Gửi lặp: kiểm tra hash và trả kết quả dưới khóa nhân viên, trước khi kiểm tra preview đã dùng; không trả nhầm đơn cũ cho payload khác.
- Preview: kiểm tra lại lịch bằng connection đang giữ khóa khi tạo/duyệt, kiểm tra thời gian nộp lại lúc commit; review token gắn Admin thực hiện.
- Snapshot: cập nhật khoảng nghỉ/ca/phút theo ngày từ lịch hiện tại lúc duyệt; Shift đổi giờ yêu cầu hủy/tạo lại. Kiểm tra lại trùng đơn trước commit.
- Đồng thời: thống nhất khóa nhân viên trước hàng đơn; chấm công không ca dùng cùng transaction/khóa; thu hồi sửa lỗi đọc TIMESTAMPTZ.
- Phân quyền: middleware JWT và Admin-only, kiểm tra tài khoản/quyền hiện tại trong DB; validation ngày/enum/phân trang, fallback NameIdentifier.
- Chấm công: ngăn check-in sớm vào ca nghỉ và check-in không ca trong nghỉ; không duyệt đè lên ca đã check-in hoặc phiên còn mở. Checkout giữ giờ thật, gắn NeedsReview khi giao nghỉ đã duyệt.
- Coverage loại người nghỉ; dữ liệu đối soát vẫn có ngày 0 phút chưa xếp ca, mọi NeedsReview được hiển thị cần đối soát.
- Web: phân trang, chi tiết/lịch sử cho các trạng thái, preview lấy version mới, báo cáo tải đủ trang và sửa ngày đầu/cuối tháng bị lệch UTC.
- Flutter: màn hình tạo ba kiểu nghỉ, preview khoảng/ca ảnh hưởng, gửi với UUID idempotency, danh sách/phân trang/chi tiết/hủy; menu Cá nhân; trạng thái nghỉ trên lịch và loại ca nghỉ khỏi coverage mobile.

## Bằng chứng đã chạy

| Kiểm tra | Kết quả |
|---|---|
| `dotnet build manage365.slnx --no-restore --verbosity quiet` | Thành công |
| `dotnet test ... --no-restore --collect:"XPlat Code Coverage"` với cả LEAVE_TEST_CONNECTION và SHIFT_TEST_CONNECTION | 129 passed, 0 failed, 0 skipped |
| PostgreSQL riêng localhost:55440 | Cả database nghỉ phép và phân ca chạy thật; migration EnsureTables chạy lại được |
| HTTP Kestrel với JWT ký thật | Thiếu/hết hạn token 401; Employee/Manager 403 khi gọi Admin; đọc đơn người khác 404; tài khoản khóa 403 |
| HTTP tạo/retry | 201 + Location, retry cùng nội dung trả cùng ID; khác nội dung 409 |
| Razor `/LeaveRequests` | Render HTTP thành công; kiểm tra phần tử danh sách và modal chi tiết |
| Node syntax checks | leave-requests.js và schedule.js hợp lệ |
| `git diff --check` | Không lỗi whitespace; Git có cảnh báo chuyển LF/CRLF theo cấu hình sẵn có |
| `flutter analyze --no-pub` | No issues found |
| `flutter test --no-pub` | 27 tests passed, gồm test form/nghỉ phép và hồi quy lịch/auth |

Các test nghỉ phép trên DB thực kiểm chứng: snapshot thay đổi theo lịch, idempotency đồng thời, payload khác, preview cũ, ca qua đêm phân bổ 120/360 phút, ngày chưa có lịch, công đã phát sinh, hai Admin duyệt đồng thời, phân quyền JWT, check-in hai đường bị chặn, coverage, thu hồi, nghỉ dài hạn và nộp bổ sung khẩn cấp.

Coverage artifact của lượt chạy này: `manage365.Tests/TestResults/02ea2d6b-c32e-421c-af07-ace584a51742/coverage.cobertura.xml`. Không áp dụng ngưỡng coverage phần trăm và không coi pass test là đã kiểm chứng mọi tình huống.

## Giới hạn kiểm chứng và môi trường

- Chưa chạy thao tác trực tiếp trên điện thoại/emulator với camera, GPS và QR kiosk thật.
- Chưa kiểm tra trực quan web bằng trình duyệt/screenshot; đã kiểm tra render Razor, syntax JS và HTTP/API.
- DB test kiểm chứng đường check-in bị chặn trước bước ghi PostGIS. Ghi tọa độ/check-out thực qua PostGIS, gồm trạng thái NeedsReview cho dữ liệu ngoại lệ cũ, chưa được chạy end-to-end trong lượt này.
- Chưa chạy migration vào DB ứng dụng/production, chưa deploy, chưa commit. Thay đổi đang ở working tree của cả hai repository.
- PostgreSQL test được tạo ở `%TEMP%/manage365-leave-pg` và đã dừng sau kiểm thử; dữ liệu test còn lại để tái chạy. Docker không hoạt động, không dùng Docker hay DB thật.
- Preview hiện lưu memory cache 10 phút; restart mất preview, nhiều instance cần cache chung/sticky session. Nhân viên phải tải lại preview nếu hết hạn/lịch đổi.
- Chính sách chưa có giao diện cấu hình; số dư phép, khóa kỳ và tính tiền lương cần module riêng. Nghỉ được duyệt không tự xác lập quyền hưởng lương/chế độ.

## Điểm vào sử dụng

- App: **Cá nhân → Đơn nghỉ phép**; chọn ca/ngày/khoảng ngày, giải trình, xem trước, gửi.
- Web Admin: **`/LeaveRequests`**; xem preview, duyệt/từ chối; thu hồi chỉ trước khi nghỉ.
- Chi tiết API, SQL prerequisites và cách chạy local/test: [leave-request-api.md](../leave-request-api.md).
