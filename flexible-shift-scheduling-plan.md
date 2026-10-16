# Kế Hoạch & Báo Cáo Triển Khai API Phân Ca Làm Linh Động Cho Retail365

> **Trạng thái**: Đã hoàn thành triển khai mã nguồn (Backend API, Repository, Migrations, Swagger UI & Unit Tests).  
> **Bộ kiểm thử tự động**: 83/83 Tests Pass (100%).  
> **Môi trường**: .NET 10.0 Web API, PostgreSQL (Supabase), Swagger OpenAPI v1.

---

## 1. Mục Tiêu Nghiệp Vụ (Goal)

Chuyển đổi hoàn toàn cơ chế phân ca từ 3 ca cố định (Sáng 06:00-14:00, Chiều 14:00-22:00, Đêm 22:00-06:00) sang hệ thống **Phân ca linh động (Flexible Shifts)** phục vụ chuỗi cửa hàng bán lẻ:
- **Thư viện Ca Mẫu (Shift Templates)**: Quản lý dễ dàng cấu hình các mẫu ca phổ biến (Full-time, Part-time, Ca cao điểm) với màu sắc nhận diện riêng trên UI Web/Mobile.
- **Phân Ca Tùy Biến Trực Tiếp (Custom Shifts)**: Cho phép xếp giờ linh động tùy ý (ví dụ: `11:15` đến `15:45`) cho từng nhân viên theo ngày mà không bị bó buộc vào ca cố định.
- **Tương Thích Ngược Liền Mạch**: Tích hợp trực tiếp với luồng Chấm công QR Động kết hợp GPS Geofencing (`/api/attendance/verify-qr`, `/api/attendance/submit`) thông qua `id_dang_ky_ca`.

---

## 2. Giải Quyết Vấn Đề Ràng Buộc Thời Gian (Core Architecture Decision)

### Câu hỏi: "12h tan ca thì có nên ràng buộc 12h sẽ vào ca tiếp theo hay không?"
**Kết luận: KHÔNG NÊN ràng buộc cứng nối tiếp (12:00 tan ca → đúng 12:00 vào ca tiếp theo).**

### 3 Lý do thực tế chuỗi bán lẻ:
1. **Giao thoa ca & Chốt két tiền mặt (Handover & Cash Closing):**
   - Thu ngân khi hết ca cần thời gian kiểm tiền két (`TOOL_SHIFT_CASH_CLOSING`), đối chiếu biên lai và ký biên bản giao nhận tài sản.
   - Hệ thống bắt buộc phải hỗ trợ **ca có gối đầu (Overlapping Shifts)** từ 15–30 phút (ví dụ Ca 1: `06:00 – 14:15`, Ca 2: `13:45 – 22:00`) để 2 nhân viên cùng có mặt đếm tiền két mà quầy thu ngân không bị bỏ trống.
2. **Khung giờ cao điểm & Ca Part-time (Peak Hours):**
   - Khách mua hàng đông vào các khung giờ nhất định (11:30 – 13:30 và 18:00 – 20:30). Cần điều động nhân viên part-time làm ca ngắn 4 tiếng (ví dụ: `11:00 – 15:00` hoặc `17:30 – 21:30`) hoạt động song song với các nhân viên ca chính. Nếu ép ca nối tiếp nhau, hệ thống sẽ chặn không cho xếp ca tăng cường.
3. **Cửa hàng không hoạt động 24/7:**
   - Nhiều chi nhánh chỉ mở từ `07:00` đến `22:00`. Khoảng từ `22:00` đến `07:00` hôm sau đóng cửa nên không có nhân viên trực. Ràng buộc nối tiếp liên tục sẽ báo lỗi sai.

### Cơ chế 2 tầng ràng buộc được cài đặt:
| Loại ràng buộc | Quy tắc kiểm tra | Mức độ xử lý |
| :--- | :--- | :--- |
| **Trùng ca của 1 nhân viên** | Cùng 1 nhân viên không được xếp 2 ca chồng chéo giờ nhau trong cùng ngày | ⛔ **Chặn lỗi (Hard Error - 409 Conflict)** |
| **Khung giờ hợp lệ** | Giờ kết thúc phải sau giờ bắt đầu (nếu kết thúc hôm sau phải bật `qua_dem = true`) | ⛔ **Chặn lỗi (Hard Error - 400 Bad Request)** |
| **Thời gian nghỉ ngơi giữa 2 ca** | Khoảng cách giữa ca hôm trước và ca hôm sau của 1 nhân viên $< 8$ tiếng | ⚠️ **Cảnh báo (Soft Warning trong response)** |
| **Lỗ hổng nhân sự** | Cửa hàng mở cửa nhưng ngày/khung giờ đó chưa có nhân viên nào trực | 💡 **Cảnh báo lỗ hổng (Coverage Warning)** |

---

## 3. Chi Tiết Các Công Việc Đã Thực Hiện

### 3.1 Cơ sở dữ liệu & Database Migrations
Đã tạo và đồng bộ 2 tệp migration:
- [`docs/sql/008_create_flexible_shift_tables.sql`](file:///D:/LT_Mobile_QLDA/web_app/manage365/docs/sql/008_create_flexible_shift_tables.sql)
- [`database/20261010_create_flexible_shift_tables.sql`](file:///D:/LT_Mobile_QLDA/web_app/manage365/database/20261010_create_flexible_shift_tables.sql)

**Nội dung thực thi SQL:**
1. **Tạo bảng `public.mau_ca_lam` (Shift Templates)**:
   - Các cột: `id` (BIGSERIAL), `ma_mau_ca` (VARCHAR 50 UNIQUE), `ten_mau_ca` (VARCHAR 150), `gio_bat_dau` (TIME), `gio_ket_thuc` (TIME), `qua_dem` (BOOLEAN), `mau_sac` (VARCHAR 30), `mo_ta` (TEXT), `trang_thai` (VARCHAR 50), `created_at`, `updated_at`.
2. **Nâng cấp bảng `public.ca_lam`**:
   - Thêm cột `id_mau_ca BIGINT NULL REFERENCES public.mau_ca_lam(id) ON DELETE SET NULL`.
   - Bổ sung chỉ mục `ix_ca_lam_ngay_lam`, `ix_ca_lam_mau_ca`.
3. **Nâng cấp bảng `public.dang_ky_ca`**:
   - Thêm cột `ghi_chu TEXT NULL`, `created_at`, `updated_at`.
   - Bổ sung chỉ mục `ix_dang_ky_ca_nhan_vien_trang_thai`, `ix_dang_ky_ca_ca_lam`.
4. **Seed dữ liệu 5 mẫu ca bán lẻ chuẩn**:
   - `CA_SANG`: 06:00 – 14:00 (Màu cam `#f59e0b`)
   - `CA_CHIEU`: 14:00 – 22:00 (Màu xanh dương `#3b82f6`)
   - `CA_DEM`: 22:00 – 06:00 (Màu tím `#8b5cf6`, qua đêm, phụ cấp +30%)
   - `CA_PARTTIME_TRUA`: 10:30 – 14:30 (Màu xanh ngọc `#10b981`, cao điểm trưa)
   - `CA_PARTTIME_TOI`: 17:30 – 21:30 (Màu hồng `#ec4899`, cao điểm tối)

---

### 3.2 Tầng Hợp Đồng Dữ Liệu (Contracts & DTOs)
Đã tạo tệp [`Routes/API/Shifts/ShiftContracts.cs`](file:///D:/LT_Mobile_QLDA/web_app/manage365/Routes/API/Shifts/ShiftContracts.cs) gồm:
- `ShiftTemplateDto`: DTO thông tin mẫu ca kèm thời lượng ca (`DurationHours`).
- `CreateShiftTemplateRequest` & `UpdateShiftTemplateRequest`: Cung cấp thông tin ca mẫu.
- `ShiftAssignmentDto`: Thông tin ca làm được phân công, liên kết tên nhân viên, mã nhân viên (`NV-xxxx`), phòng ban, trạng thái chấm công (`ShiftAttendanceBriefDto`).
- `AssignShiftRequest`: Cho phép chọn `ShiftTemplateId` HOẶC tự nhập `CustomStartTime`, `CustomEndTime`, `CustomShiftName`.
- `UpdateShiftAssignmentRequest`: Hỗ trợ đổi giờ, đổi người, đổi trạng thái.
- `BatchAssignShiftRequest` & `BatchAssignShiftResult`: Phân ca hàng loạt cho nhiều người/nhiều ngày.
- `ValidateShiftConflictRequest` & `ValidateShiftConflictResponse`: Kiểm tra trước trùng ca realtime trên giao diện.
- `WeeklyScheduleResponse`: Ma trận lịch tuần kèm danh sách cảnh báo thiếu quân số (`CoverageWarnings`).
- `CopyWeeklyScheduleRequest`: Sao chép toàn bộ ca của tuần nguồn sang tuần đích.

---

### 3.3 Tầng Xử Lý Nghiệp Vụ & Repository
Đã tạo:
- [`Repositories/Shifts/IShiftSchedulingRepository.cs`](file:///D:/LT_Mobile_QLDA/web_app/manage365/Repositories/Shifts/IShiftSchedulingRepository.cs)
- [`Repositories/Shifts/PostgresShiftSchedulingRepository.cs`](file:///D:/LT_Mobile_QLDA/web_app/manage365/Repositories/Shifts/PostgresShiftSchedulingRepository.cs)

**Các giải thuật xử lý cốt lõi:**
1. **Khởi tạo tự động (`EnsureTablesAndSeedAsync`)**:
   - Tự động chạy DDL và seed mẫu ca khi Backend khởi động, đảm bảo ứng dụng luôn sẵn sàng mà không cần chạy SQL thủ công.
2. **Kiểm tra xung đột trùng giờ (`ValidateConflictAsync`)**:
   - Chuyển đổi khung giờ về thời điểm tuyệt đối $[S, E)$ trên trục thời gian:
     $$S = \text{shiftDate} + \text{startTime}$$
     $$E = (\text{isOvernight} ? \text{shiftDate} + 1\text{ ngày} : \text{shiftDate}) + \text{endTime}$$
   - Hai ca trùng giờ nhau khi và chỉ khi: $S_{\text{mới}} < E_{\text{cũ}} \land S_{\text{cũ}} < E_{\text{mới}}$.
   - Nếu không trùng giờ, tiếp tục tính khoảng nghỉ giữa 2 ca: nếu $\text{gap} < 8$ tiếng, tự động phát sinh thông điệp cảnh báo sức khỏe nhân viên.
3. **Phân ca linh hoạt (`AssignShiftAsync`)**:
   - Nếu truyền `ShiftTemplateId`: Tự động nạp cấu hình giờ từ bảng `mau_ca_lam`.
   - Nếu truyền giờ tùy biến: Xác thực giờ và tạo bản ghi ca linh động.
   - Ghi nhận vào `public.ca_lam` và `public.dang_ky_ca`.
4. **Sao chép lịch tuần (`CopyWeeklyScheduleAsync`)**:
   - Tự động tính toán độ lệch ngày ($\Delta d = \text{targetStartDate} - \text{sourceStartDate}$) và nhân bản toàn bộ lịch tuần trước sang tuần mới chỉ với 1 lệnh API.

---

### 3.4 Tầng Web API Controllers & Swagger OpenAPI
Đã xây dựng 2 Controllers chuyên biệt:

#### 1. `ShiftTemplatesController.cs` (Route: `api/shift-templates`)
- `GET /api/shift-templates`: Danh sách mẫu ca (Public/Staff).
- `GET /api/shift-templates/{id}`: Chi tiết mẫu ca.
- `POST /api/shift-templates`: Tạo mẫu ca mới (`[Authorize(Roles = "Admin,Manager")]`).
- `PUT /api/shift-templates/{id}`: Cập nhật mẫu ca (`[Authorize(Roles = "Admin,Manager")]`).
- `DELETE /api/shift-templates/{id}`: Chuyển mẫu ca sang `Inactive`.

#### 2. `ShiftAssignmentsController.cs` (Route: `api/shift-assignments`)
- `GET /api/shift-assignments/schedule`: Lấy ma trận lịch theo tuần/tháng (có bộ lọc phòng ban, nhân viên).
- `GET /api/shift-assignments/{id}`: Chi tiết ca phân công.
- `POST /api/shift-assignments`: Phân ca mới (hỗ trợ ca mẫu + ca tùy biến, validate trùng giờ).
- `PUT /api/shift-assignments/{id}`: Cập nhật ca phân công.
- `DELETE /api/shift-assignments/{id}`: Hủy phân ca.
- `POST /api/shift-assignments/batch`: Phân ca hàng loạt.
- `POST /api/shift-assignments/copy-week`: Sao chép lịch tuần.
- `POST /api/shift-assignments/validate`: Kiểm tra xung đột trước khi lưu (phục vụ UI).

#### Cấu hình Swagger trong `Program.cs`:
- Bổ sung XML Documentation Comments trên toàn bộ Controllers và DTOs.
- Phân nhóm Tags: `Shift Templates - Mẫu Ca Làm Việc` và `Shift Scheduling - Phân Ca Linh Động`.
- Cấu hình Swagger UI hỗ trợ thử nghiệm trực tiếp tại `/swagger` với cơ chế xác thực JWT Bearer Token.

---

### 3.5 Kiểm Thử Tự Động (Unit Testing)
Đã xây dựng bộ kiểm thử hoàn chỉnh tại [`manage365.Tests/ShiftSchedulingTests.cs`](file:///D:/LT_Mobile_QLDA/web_app/manage365/manage365.Tests/ShiftSchedulingTests.cs) gồm 7 kịch bản:
1. `GetTemplates_ReturnsActiveTemplates_Successfully`: Đảm bảo nạp đúng danh sách ca mẫu.
2. `CreateTemplate_ReturnsCreated_WhenValid`: Đảm bảo tạo thành công ca mẫu hợp lệ.
3. `CreateTemplate_ReturnsBadRequest_WhenTimesInvalid`: Đảm bảo chặn ca có giờ kết thúc trước giờ bắt đầu (nếu không bật qua đêm).
4. `AssignShift_ReturnsCreated_WhenValid`: Đảm bảo phân ca thành công khi không có xung đột.
5. `AssignShift_ReturnsConflict_WhenSameEmployeeHasOverlappingShift`: Đảm bảo chặn mã lỗi `409 Conflict` khi cùng 1 nhân viên bị xếp 2 ca chồng chéo giờ nhau.
6. `AssignShift_Succeeds_WhenShiftsDoNotOverlap_ForSameEmployee`: Đảm bảo cho phép phân ca gãy (ca sáng 6h-10h, ca tối 18h-22h) cho cùng một người.
7. `ValidateConflict_DetectsShortRestGap_Warning`: Đảm bảo phát hiện và trả về cảnh báo nếu khoảng nghỉ giữa 2 ca $< 8$ tiếng.
8. `BatchAssignShifts_ExecutesMultipleAssignments`: Đảm bảo phân ca hàng loạt cho nhiều nhân sự.
9. `GetSchedule_ReturnsMatrix_AndCoverageWarnings`: Đảm bảo hiển thị ma trận lịch tuần và cảnh báo ngày thiếu nhân sự.

**Kết quả chạy `dotnet test`**:
```
Passed!  - Failed: 0, Passed: 83, Skipped: 0, Total: 83, Duration: 100 ms - manage365.Tests.dll (net10.0)
```
*(Bao gồm 74 tests cũ + 9 tests mới cho phân ca linh động, tỷ lệ thành công 100%)*

---

## 4. Hướng Dẫn Kiểm Thử Trực Tiếp Trên Swagger UI

1. **Khởi động Backend**:
   ```powershell
   dotnet run --launch-profile http
   ```
2. **Truy cập Swagger UI**:
   - Địa chỉ: `http://localhost:5295/swagger` (hoặc `https://localhost:7037/swagger`).
3. **Đăng nhập và lấy Bearer Token**:
   - Gửi yêu cầu tại `POST /api/auth/login`.
   - Copy chuỗi `accessToken`, nhấn nút **Authorize** ở góc trên Swagger và nhập `Bearer {token}`.
4. **Kiểm thử các kịch bản thực tế**:
   - **Kịch bản 1**: Gọi `GET /api/shift-templates` để xem 5 mẫu ca bán lẻ chuẩn vừa được seed.
   - **Kịch bản 2**: Gọi `POST /api/shift-assignments` phân ca mẫu `CA_SANG` cho nhân viên ID 1 vào ngày mai.
   - **Kịch bản 3**: Thử gọi tiếp `POST /api/shift-assignments` phân ca tùy biến `10:00 - 15:00` cùng ngày cho nhân viên ID 1 $\rightarrow$ Backend sẽ trả về mã lỗi `409 Conflict` kèm thông báo trùng giờ chi tiết.
   - **Kịch bản 4**: Gọi `POST /api/shift-assignments/validate` kiểm tra tính hợp lệ của ca dự kiến trước khi lưu.
   - **Kịch bản 5**: Gọi `GET /api/shift-assignments/schedule` để xem ma trận phân ca và cảnh báo thiếu nhân sự trong tuần.
