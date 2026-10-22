# Kế hoạch sửa phân ca web và đồng bộ app

Ngày: 19/10/2026. Trạng thái: chờ người dùng duyệt, chưa triển khai.

## Kết quả rà soát mã nguồn
- Backend `Repositories/Shifts/PostgresShiftSchedulingRepository.cs` đã lọc xung đột theo `employeeId`; luồng ghi khóa theo nhân viên. Chưa tái hiện lỗi trên bản chạy hoặc kiểm tra constraint/trigger của database đang dùng, nên chưa xác nhận nguyên nhân chặn nhân viên khác.
- Web `wwwroot/js/schedule.js` gọi API chung. Working tree đã có thay đổi chưa commit về chọn nhân viên, bộ chọn giờ 24 giờ và một test dùng fake; cần giữ nguyên và đánh giá tiếp, không coi là đã nghiệm thu.
- App thực tế ở `C:/Users/ASUS/StudioProjects/retail-management-app/retail-management-app/mobile`. `lib/schedule_screen.dart` chỉ khóa lưu khi trùng ca của cùng nhân viên và đã dùng `show24HourWheelTimePicker`.
- `lib/core/schedule_service.dart` chỉ lấy trang nhân viên đầu tiên; `validateConflict` chưa kiểm tra HTTP status trước khi đọc kết quả. App đã tải lại khi trở về foreground; web hiện có nút tải lại. Hai client cùng route nhưng chưa xác minh cùng máy chủ/database thực tế.

## Quy tắc dự kiến
- Cho phép chọn nhiều nhân viên trong một lần phân ca trên web và app, dùng chung ngày/khung giờ hoặc mẫu ca; lưu assignment riêng cho từng người để sửa/hủy độc lập.
- Nhân viên A làm 02:00–23:00 không cản nhân viên B làm 08:00–17:00 hoặc 02:00–23:00 cùng ngày.
- Ca gãy được hiểu là nhiều khoảng làm việc rời nhau của cùng nhân viên trong ngày, ví dụ 08:00–12:00 và 17:00–21:00. Form cho phép thêm khoảng giờ và áp dụng cho các nhân viên được chọn; mỗi khoảng là một assignment, khoảng nghỉ không được tính vào thời gian có người làm.
- Chỉ chặn ca chồng thời gian của cùng một nhân viên, kể cả qua đêm; cho phép hai ca nối tiếp; bỏ qua ca đã hủy và bản ghi đang sửa.
- Hiện nhắc nhở nhỏ ngay trong form, không dùng hộp thoại xác nhận và không chặn lưu vì nhân viên khác làm đồng thời. Hiển thị số người hiện có và số người dự kiến sau lưu theo từng khoảng giờ; có thể mở danh sách tên để đối chiếu.
- Số người phải tính theo employeeId duy nhất trên từng đoạn thời gian có số người thay đổi, không lấy tổng mọi người giao với cả ca. Ví dụ A 08:00–12:00, B 10:00–14:00: 08:00–10:00 có 1 người, 10:00–12:00 có 2, 12:00–14:00 có 1. Dùng khoảng [bắt đầu, kết thúc), xử lý qua ngày, bỏ ca hủy, thay bản ghi đang sửa bằng dữ liệu dự kiến và không đếm lặp người được chọn.
- Thống kê lấy từ server cho đầy đủ khoảng thời gian đang chọn, không dựa vào lịch tuần hoặc danh sách đang bị lọc trên giao diện; giới hạn theo phạm vi quản lý được phép. Khi tải lỗi, báo chưa lấy được số lượng, không hiển thị 0 giả; phản hồi cũ không được ghi đè lựa chọn mới. Con số là dự kiến tại lúc tải, kiểm tra xung đột vẫn thực hiện lại khi lưu.
- Giờ nhập/hiển thị là 00:00–23:59, không AM/PM. Giữ múi giờ nghiệp vụ Việt Nam UTC+7; định dạng 24 giờ không đồng nghĩa chuyển lịch sang UTC.
- Đồng bộ qua API/database chung: cập nhật sau lưu, tải lại, mở lại màn hình hoặc quay lại ứng dụng/tab; chưa đưa đồng bộ đẩy thời gian thực vào phạm vi.

## Các bước sau khi duyệt
1. **Tái hiện và xác định nguyên nhân:** kiểm tra thao tác web tạo ca A rồi B, request `employeeId`, phản hồi validate/save, lỗi trình duyệt, phiên bản JS/API và schema/trigger thực tế. Chỉ đọc môi trường đang dùng; tạo dữ liệu thử trên môi trường riêng.
2. **Sửa tại lớp gây lỗi:** rà soát validate/create/update/batch/copy và ràng buộc SQL; chỉ thay logic/schema nếu có bằng chứng sai. Giữ kiểm tra theo nhân viên và transaction chống hai yêu cầu ghi trùng. Nếu cần migration, soạn script kèm kiểm tra trước/sau và phương án hoàn tác.
3. **API và thông báo nhân sự:** bổ sung API xem trước số nhân viên theo từng khoảng giờ, nhận các phân công dự kiến và bản ghi đang sửa; dùng chung trên web/app, kiểm tra quyền quản lý. Tận dụng batch API cho nhiều người/nhiều khoảng; hiển thị rõ mục thành công/thất bại và chỉ thử lại mục thất bại, chia yêu cầu theo giới hạn 200 mục hiện có.
4. **Hoàn thiện web:** form chọn nhiều nhân viên, thêm/bớt khoảng giờ cho ca gãy, giờ 24 giờ và nhắc số người ngay dưới khung giờ; cập nhật nhắc nhở khi đổi ngày/giờ/nhân viên. Kiểm tra thay đổi đang có; hoàn thiện tạo/sửa/hủy và thông báo xung đột theo từng người.
5. **Thống nhất app:** hỗ trợ cùng form nhiều nhân viên/ca gãy, cùng nhắc số người và giờ 24 giờ; tải đủ trang nhân viên, kiểm tra HTTP status của validate, dùng validation server và hỗ trợ tạo/sửa/hủy. File chính: `lib/schedule_screen.dart`, `lib/core/schedule_service.dart`, `lib/widgets/wheel_time_picker.dart`.
6. **Đồng bộ dữ liệu:** đối chiếu API_BASE_URL và môi trường web; thống nhất employeeId/assignmentId, ngày, giờ, trạng thái; tải lại khi quay lại màn hình/tab mà không ghi đè form đang nhập; báo rõ lỗi tải sau lưu và tránh tạo trùng khi thử lại.
7. **Kiểm chứng cuối:** build/test .NET, kiểm thử PostgreSQL riêng và HTTP thực tế; kiểm tra JavaScript, Flutter analyze/test và thao tác trình duyệt/thiết bị. Ghi rõ phần bị môi trường chặn. Không dùng kết quả test cũ làm kết quả lần này.

## Tiêu chí nghiệm thu
| Tình huống | Kỳ vọng |
|---|---|
| A 02:00–23:00, B 08:00–17:00 hoặc 02:00–23:00 | Lưu được cả hai |
| Chọn A/B/C và một khung giờ trong cùng lần lưu | Tạo đủ 3 phân công, sửa/hủy từng người độc lập |
| Ca gãy 08:00–12:00 và 17:00–21:00 | Lưu được, không tính khoảng nghỉ 12:00–17:00 là đang làm |
| A 08:00–12:00, B 10:00–14:00 | Nhắc 1/2/1 người theo đúng 3 khoảng; không chặn lưu |
| Đổi lựa chọn nhanh, lọc lịch, ca qua tuần, ca hủy, sửa ca | Nhắc đúng theo dữ liệu server và lựa chọn mới nhất, không đếm lặp |
| Một người xung đột trong nhóm; lỗi mạng sau batch | Báo từng kết quả, tải lại đối chiếu trước khi thử lại; không nhân đôi ca đã lưu |
| A có 02:00–23:00, thêm ca 08:00–17:00 cho A | Chặn, chỉ rõ ca xung đột |
| A 22:00–06:00 hôm sau, thêm 05:00–08:00 hôm sau | Chặn với A, cho phép với B |
| Hai ca nối tiếp; ca hủy; sửa bản ghi hiện tại | Không báo trùng sai; cảnh báo nghỉ ngắn không phải lỗi chặn |
| Hai yêu cầu cùng lúc; batch/copy nhiều nhân viên | Không tạo ca trùng cùng người; không chặn nhầm người khác |
| Web tạo/sửa/hủy → app tải lại và ngược lại | Cùng ID, ngày, giờ, trạng thái; nhân viên chỉ thấy lịch được phép |
| 00:00, 02:00, 12:00, 14:00, 23:59 và qua đêm | Hiển thị đúng 24 giờ, không lệch ngày |

Chỉ tạo tài liệu kế hoạch ở bước này; giữ nguyên mọi thay đổi mã nguồn có sẵn. Việc ghi vào repository app ngoài workspace có thể cần cấp quyền công cụ khi triển khai.
