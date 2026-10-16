# Kế hoạch hoàn thiện phân ca Web và Mobile

Ngày: 15/10/2026. Trạng thái: **Đã triển khai và chờ nghiệm thu Flutter trên máy đích**.

Mục tiêu: giữ bố cục lịch tuần hiện có, lấy nhân viên thật, lưu phân ca vào PostgreSQL và hiển thị nhất quán trên `retail-management-app`. Kết quả kiểm tra hiện trạng nằm trong [báo cáo kiểm thử](docs/shift-scheduling-review-2026-10-15.md).

## Phạm vi và quy tắc đề xuất

- Admin/Manager quản lý lịch; Employee chỉ xem lịch cá nhân. Nếu cần xem lịch đồng đội, phải chốt phạm vi và trường dữ liệu được phép xem.
- Một nhân viên có thể có nhiều ca/ngày nếu không trùng giờ; xét cả ca qua đêm. Nghỉ dưới 8 giờ là cảnh báo theo logic hiện có, không tự động cấm.
- Phân biệt **chưa phân ca**, **ca đã hủy** và **nghỉ phép**; chưa có nghiệp vụ nghỉ phép thì không dùng ô trống để suy ra nghỉ phép.
- Dùng ngày `yyyy-MM-dd`, giờ `HH:mm`, lịch theo múi giờ Việt Nam. Giữ dữ liệu chấm công khi hủy ca; quy định việc sửa ca đã chấm công.
- Chưa triển khai gửi Zalo/SMS, đổi ca hoặc KPI phủ kiosk trong đợt này; ẩn/vô hiệu hóa thông báo và số liệu giả tương ứng.

## Công việc và tiêu chí hoàn thành

| Bước | Thay đổi cụ thể | Cách nghiệm thu |
|---|---|---|
| 1 | Chốt đường dẫn mobile, DB test, tài khoản Admin/Manager/Employee; kiểm tra schema và migration `008` trên DB test. | Có dữ liệu mẫu cô lập, cấu hình hai client cùng API và cách dọn dữ liệu test. |
| 2 | Sửa validation tại `Routes/API/Shifts/`: ngày/giờ, nhân viên tồn tại/hoạt động, mẫu ca Active, trạng thái và phạm vi đọc theo người đăng nhập. | Input sai trả 400/404/409 nhất quán; kiểm tra 401/403 qua HTTP; Employee không đọc lịch người khác theo chính sách đã chốt. |
| 3 | Sửa `PostgresShiftSchedulingRepository`: transaction, chống ghi trùng đồng thời, sửa ca dùng chung không ảnh hưởng người khác, hủy ca an toàn, sao chép đúng giờ đã lưu và bỏ ca hủy. | Test PostgreSQL chứng minh rollback, không sinh bản ghi mồ côi/trùng ca và không mất chấm công. |
| 4 | Bổ sung API danh sách nhân viên dành cho phân ca, ví dụ `GET /api/shift-assignments/employees`, gồm ID, mã, tên, phòng ban, trạng thái, phân trang/tìm kiếm; không trả lương. | Có cả nhân viên chưa được phân ca; lọc phòng ban đúng; không đánh đồng chức danh với vai trò đăng nhập. |
| 5 | Thay dữ liệu cố định trong `Views/Home/Schedule.cshtml` và `wwwroot/js/schedule.js`; tải nhân viên, mẫu ca và lịch tuần bằng Bearer token. | Tuần hiện tại/ngày hiện tại đúng; chuyển tuần, tìm kiếm và lọc kết hợp; tải lại trang vẫn giữ dữ liệu server. |
| 6 | Hoàn thiện modal thêm/sửa/hủy, ca mẫu/custom/qua đêm, nhiều ca trong một ô; gọi `validate` trước lưu, server vẫn kiểm tra khi ghi. | Chỉ báo thành công sau response thành công; hiển thị conflict/cảnh báo; chống nhấn lưu lặp; có trạng thái tải, rỗng, lỗi và hết phiên. |
| 7 | Nối phân hàng loạt/sao chép tuần, thống kê từ dữ liệu thật, in tuần đang chọn; hiển thị rõ thành công một phần. | Có danh sách lỗi theo ca, không báo toàn bộ thành công khi có lỗi; bỏ KPI chưa đủ dữ liệu tính. |
| 8 | Đối chiếu DTO, token, ngày/giờ và `assignmentId` trong mobile; tải lại lịch sau thay đổi, đồng bộ luồng chấm công. | Web tạo/sửa/hủy → app cập nhật đúng sau refresh; kiểm thử ca qua đêm và nhiều ca/ngày. |
| 9 | Chạy unit, integration PostgreSQL/HTTP, browser E2E, Flutter analyze/test và nghiệm thu liên ứng dụng. | Đính kèm kết quả, ảnh/video và dữ liệu trước/sau; toàn bộ lỗi quan trọng đóng trước khi bàn giao. |

Thứ tự: 1 → 2–4 → 5–7 → 8 → 9. Đã hoàn tất API, giao diện Web, kiểm tra PostgreSQL cô lập và đồng bộ mã nguồn Flutter. Không đổi schema và không áp dụng migration trên cơ sở dữ liệu thật.

## Kết quả triển khai ngày 15/10/2026

- **Hoàn tất:** validation, phân quyền dữ liệu, API danh sách nhân sự, lưu trong transaction, khóa đồng thời theo nhân viên, hủy mềm và bảo vệ ca đã chấm công; lịch Web/API thật; màn hình Flutter dùng API lịch ca.
- **Đã kiểm chứng trên PostgreSQL test:** ca tùy chỉnh, phủ nhân viên chưa xếp ca, xung đột đồng thời, rollback, ca qua đêm, sửa ca dùng chung, hủy an toàn, mẫu ngừng sử dụng, sao chép giữ giờ đã lưu và batch báo lỗi từng ca. Kết quả đầu: 103 test đạt.
- **Đã kiểm chứng lại sau đó:** `dotnet build` thành công; 94 test đạt, 9 integration test PostgreSQL bỏ qua khi Docker tự dừng. Test này trước đó đã đạt khi container chạy.
- **Web:** `node --check wwwroot/js/schedule.js` và smoke harness với DOM/API giả xác nhận tải nhân viên/ca/lịch, kiểm tra conflict trước khi POST, lưu phân ca và tải lại lịch.
- **Mobile:** các tệp đã áp dụng vào checkout do người dùng chỉ định; `dart format` chạy được. `dart analyze` chỉ báo hai góp ý style không chặn biên dịch. Không chạy được `flutter analyze`/`flutter test` vì Windows chặn khởi chạy Flutter bằng Application Control.
- **Chưa nghiệm thu:** chạy Flutter tests/E2E và xác nhận tương tác trên thiết bị/emulator thật; container test PostgreSQL đã dừng giữa lượt kiểm thử cuối.

## Điều kiện duyệt hoàn tất

- Nhân viên thật xuất hiện dù chưa có ca; mọi thay đổi vẫn tồn tại sau refresh và xuất hiện trên app.
- Ca chồng giờ, qua nửa đêm, ca nối tiếp, hai người lưu đồng thời và dữ liệu sai đều có kết quả đúng.
- Quyền truy cập, hủy ca có chấm công, batch lỗi một phần và sao chép tuần được kiểm chứng trên DB test.
- Hoàn tất ma trận kiểm thử trong báo cáo; không coi 83 unit test hiện có là bằng chứng hoàn thành E2E.
