# Báo cáo rà soát và tối ưu PostgreSQL Manage365

Ngày thực hiện: **26/10/2026**. Migration đã áp dụng lên PostgreSQL được cấu hình trong `.env` của web Manage365 lúc khoảng **14:24 UTC+7**.

## 1. Kết quả đã thực hiện

| Hạng mục | Trước | Sau |
| --- | ---: | ---: |
| Bảng trong schema `public` | 31 | 22 |
| Chỉ mục trong schema `public` | 95 | 68 |
| Bảng rỗng không có chức năng sử dụng đã xóa | — | 9 |
| Chỉ mục trùng trên các bảng giữ lại đã xóa | — | 5 |
| Hàm chỉ phục vụ module đã bỏ đã xóa | — | 4 |

27 chỉ mục giảm gồm **22 chỉ mục thuộc 9 bảng bị xóa** và **5 chỉ mục trùng** trên bảng còn dùng. Tổng kích thước quan hệ trong `public` ghi nhận từ 1.794.048 xuống 1.490.944 byte tại hai lần đo; số này bao gồm bảng/chỉ mục/TOAST và có thể thay đổi theo hoạt động ứng dụng.

Toàn bộ 22 bảng giữ lại đã được so sánh số dòng và dấu kiểm tra nội dung trước/sau DDL trong cùng transaction `REPEATABLE READ`: **không thay đổi dữ liệu**. Đã chạy `ANALYZE` trên 22 bảng để cập nhật thống kê cho bộ lập kế hoạch truy vấn.

Giữ nguyên các schema `auth`, `storage`, `realtime`, `extensions`, `vault`, `familysafety` và các đối tượng hệ thống. Đã đối chiếu tên/loại quan hệ ngoài `public` trước/sau: không thay đổi. Bảng PostGIS như `extensions.spatial_ref_sys` là thành phần thư viện, không phải bảng dư của ứng dụng.

## 2. Phạm vi và căn cứ rà soát

- Backend hiện tại, bao gồm thay đổi chưa commit: repositories, controllers, routes, cấu hình khởi động và SQL khởi tạo.
- Web: Razor views, JavaScript phục vụ trang đăng nhập, phân ca, chấm công, nghỉ phép và quản trị quyền; thư mục giao diện `ui`.
- Flutter: `C:/Users/ASUS/StudioProjects/retail-management-app/retail-management-app/mobile/lib`, các màn hình và service gọi API. Không thấy Flutter truy cập trực tiếp bảng PostgreSQL/Supabase; dữ liệu đi qua backend.
- Database thật: số dòng chính xác, khóa ngoại, định nghĩa cột/chỉ mục, view, routine, trigger, policy RLS, publication, phụ thuộc trong catalog. Không có schema `cron` hoặc `pgagent` trên database kiểm tra.
- Các hàm SQL/PLpgSQL cũng được kiểm tra nội dung vì tham chiếu trong chuỗi SQL không phải lúc nào cũng có trong `pg_depend`.

Việc một bảng không hiện trên UI hoặc đang có 0 dòng **không đủ** để quyết định xóa. Các API ít được dùng như Permissions/Departments vẫn có chức năng truy cập dữ liệu và phải giữ bảng.

Phạm vi kết luận là hai dự án được cung cấp và đối tượng trong database hiện tại; không phải bằng chứng rằng mọi phần mềm bên ngoài đều đã được kiểm tra.

## 3. Các bảng đã xóa

Tất cả các bảng sau có **0 dòng**, không có tham chiếu trong mã chạy của hai dự án và không có khóa ngoại từ bảng giữ lại trỏ vào. Các trigger nội bộ của nhóm này được xóa cùng bảng; không dùng `CASCADE` để xóa phụ thuộc bên ngoài.

| Bảng | Chức năng cũ/chưa triển khai |
| --- | --- |
| `bang_luong` | Bảng lương cũ chưa có API sử dụng |
| `bao_hiem` | Bảo hiểm chưa có API sử dụng |
| `chi_tiet_hoa_don` | Chi tiết hóa đơn |
| `don_dat_hang` | Đơn đặt hàng |
| `hoa_don` | Hóa đơn |
| `khuyen_mai` | Khuyến mãi |
| `loai_san_pham` | Loại sản phẩm |
| `nhap_kho` | Nhập kho |
| `san_pham` | Sản phẩm |

Các hàm đã bỏ cùng module: `check_bao_hiem_fulltime()`, `tinh_lai_hoa_don(bigint)`, `trg_fn_cap_nhat_tong_hoa_don()`, `trg_fn_hoa_don_doi_khuyen_mai()`. Hàm `set_updated_at()` dùng chung cho bảng đang hoạt động được giữ lại.

**Lưu ý về lương:** `bang_luong` và `bao_hiem` là cấu trúc trống chưa được ứng dụng sử dụng. API `/api/leave-requests/payroll-summary` đang tính từ các bảng nghỉ phép, không đọc hai bảng này; đã kiểm tra API trả 200 sau migration. Khi triển khai module trả lương/bảo hiểm thực tế, cần tạo schema phù hợp hoặc khôi phục cấu trúc cũ bằng script rollback, không giả định hai bảng vẫn tồn tại.

## 4. Các bảng giữ lại

Số dòng dưới đây là ảnh chụp tại thời điểm bắt đầu audit, không phải số đếm cập nhật liên tục.

| Bảng | Số dòng | Chức năng đang sử dụng |
| --- | ---: | --- |
| `nhan_vien` | 14 | Tài khoản, thông tin nhân viên, tên shop, phân quyền |
| `vai_tro` | 5 | Danh mục vai trò Admin / Manager / User và vai trò hiện có |
| `quyen` | 9 | Danh mục quyền chi tiết qua Permissions API |
| `vai_tro_quyen` | 17 | Liên kết nhiều quyền với nhiều vai trò |
| `phong_ban` | 3 | Phòng ban, lọc nhân sự và lịch làm việc |
| `cong_cu_phong_ban` | 12 | Công cụ/chức năng theo phòng ban, Departments API |
| `mau_ca_lam` | 5 | Mẫu ca tái sử dụng |
| `ca_lam` | 19 | Ca làm cụ thể theo ngày và giờ |
| `dang_ky_ca` | 19 | Phân công nhân viên vào ca |
| `cham_cong` | 13 | Giờ vào/ra, lịch sử cá nhân và quản trị |
| `dia_diem_cham_cong` | 1 | Cấu hình địa điểm, bán kính GPS cửa hàng |
| `vi_tri_cham_cong` | 23 | Bằng chứng vị trí khi vào/ra ca |
| `lich_su_dia_diem_cham_cong` | 39 | Lịch sử thay đổi cấu hình địa điểm |
| `don_nghi_phep` | 7 | Đơn nghỉ phép và trạng thái duyệt |
| `don_nghi_phep_khoang` | 7 | Khoảng nghỉ, kiểm tra giao nhau với ca/chấm công |
| `don_nghi_phep_ca` | 0 | Ca chịu ảnh hưởng của đơn nghỉ; vẫn được truy vấn dù đang rỗng |
| `don_nghi_phep_ngay` | 7 | Ngày nghỉ và dữ liệu đối soát lương |
| `lich_su_don_nghi_phep` | 15 | Lịch sử tạo/duyệt/từ chối/thu hồi đơn |
| `don_nghi_phep_idempotency` | 7 | Chống gửi lặp yêu cầu tạo đơn |
| `notifications` | 6 | Thông báo trong ứng dụng, trạng thái đã đọc |
| `password_reset_codes` | 3 | Mã xác minh và token đặt lại mật khẩu qua email |
| `user_refresh_tokens` | 134 | Phiên đăng nhập, làm mới và thu hồi token |

Bằng chứng mã nguồn theo từng bảng, gồm đường dẫn và số dòng lúc rà soát, được lưu tại [database-table-usage.json](database-table-usage.json). Đây là ảnh chụp trước khi thêm báo cáo/migration, để tránh nhầm chính tài liệu audit thành chức năng đang sử dụng.

## 5. Vì sao chưa gộp các bảng đang dùng

| Nhóm | Lý do giữ cấu trúc riêng |
| --- | --- |
| `mau_ca_lam`, `ca_lam`, `dang_ky_ca` | Mẫu giờ, ca theo ngày và phân công nhân viên là ba thực thể khác nhau; một ca có thể được nhiều nhân viên tham gia. |
| Các bảng `don_nghi_phep*` và lịch sử đơn | Đơn chính, khoảng giờ, ca ảnh hưởng, ngày đối soát, lịch sử và khóa chống gửi lặp có quan hệ/vòng đời khác nhau. Gộp dễ nhân bản số liệu hoặc mất ràng buộc. |
| `cham_cong`, `vi_tri_cham_cong`, `dia_diem_cham_cong`, lịch sử địa điểm | Bản ghi công, bằng chứng GPS mỗi hành động, cấu hình cửa hàng và lịch sử cấu hình phục vụ các mục đích riêng. |
| `vai_tro`, `quyen`, `vai_tro_quyen` | Cần quan hệ nhiều–nhiều giữa vai trò và quyền. |
| `password_reset_codes`, `user_refresh_tokens` | Mã đặt lại mật khẩu và phiên đăng nhập có thời hạn, cách xác minh, thu hồi khác nhau. |

Không có bằng chứng cần gộp các bảng này để cải thiện truy vấn hiện tại. Đợt này giảm cấu trúc dư và chi phí bảo trì chỉ mục; không thay đổi mô hình nghiệp vụ đang chạy.

## 6. Chỉ mục và lỗi truy vấn đã sửa

| Chỉ mục đã bỏ | Chỉ mục giữ lại có cùng khóa tìm kiếm |
| --- | --- |
| `idx_ca_lam_ngay` | `ix_ca_lam_ngay_lam` |
| `idx_dang_ky_ca_ca_lam` | `ix_dang_ky_ca_ca_lam` |
| `idx_cham_cong_dang_ky` | `uq_cham_cong_dang_ky` — UNIQUE |
| `ix_user_refresh_tokens_token_hash` | `user_refresh_tokens_token_hash_key` — UNIQUE |
| `ix_vi_tri_cham_cong_attendance` | `ux_vi_tri_cham_cong_action` — UNIQUE |

Migration kiểm tra catalog tại thời điểm chạy: bảng, phương thức index, khóa, collation, thứ tự, biểu thức, predicate và trạng thái index thay thế. Nếu cấu trúc đã khác so với giả định hoặc index phục vụ constraint/replication thì không xóa. Đã sửa SQL khởi tạo và `PostgresRefreshTokenRepository` để tránh tạo lại các chỉ mục dư tương ứng.

Trong quá trình kiểm tra **trước khi xóa bảng**, Statistics API trả 503 khi bộ lọc nhân viên/phòng ban là null. Đã tái hiện thông báo PostgreSQL `could not determine data type of parameter $3`; sửa `PostgresReportingRepository` để gán rõ `NpgsqlDbType.Bigint` cho `employeeId` và `departmentId`. Phiên bản kiểm tra trả 200 cả trước và sau migration.

## 7. Kết quả kiểm chứng

### Migration và bảo toàn dữ liệu

- Có bản sao lưu custom-format của toàn bộ schema/data `public`; đã kiểm tra SHA-256 và đọc danh mục bằng `pg_restore --list`.
- Khôi phục 12 bảng không có dữ liệu không gian vào PostgreSQL tạm trên `127.0.0.1:55439`: 9 bảng cần bỏ và 3 bảng cha `nhan_vien`, `vai_tro`, `phong_ban`.
- Thử thành công xóa 9 bảng; giữ dữ liệu 14 nhân viên; chạy lại migration; khôi phục cấu trúc, hàm, trigger, constraint, chỉ mục và ACL bằng script rollback thực tế.
- Thử có dữ liệu mới trong bảng, view phụ thuộc và hàm tham chiếu qua SQL: migration từ chối và không xóa dở dang.
- Thử dọn index trên database mẫu: giữ ràng buộc UNIQUE; từ chối khi index đã đổi thứ tự khóa; khôi phục index thành công.
- Chạy thử cả hai migration trên database thật và `ROLLBACK`, sau đó mới áp dụng trong một transaction có kiểm tra nội dung 22 bảng giữ lại. Không dùng `DROP ... CASCADE` trên database thật.
- Sau commit: đủ 22 bảng được mã nguồn tham chiếu; 9 bảng dư không còn; 5 index trùng không còn; các index thay thế vẫn còn.

Bản sao cục bộ dùng để kiểm tra migration là **bản sao chọn lọc**, không phải bản sao đầy đủ của ứng dụng có PostGIS.

### API trên database thật

23/23 GET API trả **HTTP 200** sau migration, dùng phiên Admin đang lưu trong emulator và backend kiểm tra tạm chạy tại `127.0.0.1:5296`:

- `/api/auth/me`
- `/api/shift-assignments/employees?page=1&pageSize=100`
- `/api/shift-assignments/schedule?startDate=2026-10-19&endDate=2026-11-01`
- `/api/shift-templates`
- `/api/attendance/history?limit=20`
- `/api/attendance/admin/snapshot?limit=50`
- `/api/attendance/admin/history?fromDate=2026-10-01&toDate=2026-10-31`
- `/api/attendance-locations/STORE-01`
- `/api/attendance-qr/kiosk?storeCode=STORE-01`
- `/api/leave-requests/options`
- `/api/leave-requests/me`
- `/api/leave-requests`
- `/api/leave-requests/payroll-summary?fromDate=2026-10-01&toDate=2026-10-31`
- `/api/dashboard/summary?date=2026-10-26`
- `/api/statistics/attendance?fromDate=2026-10-01&toDate=2026-10-31&groupBy=day`
- `/api/notifications`
- `/api/notifications/unread-count`
- `/api/users`
- `/api/roles`
- `/api/permissions`
- `/api/permissions/me`
- `/api/departments`
- `/api/departments/my-tools`

Đây là kiểm tra HTTP có xác thực và truy vấn database thật. Chưa kiểm tra lại toàn bộ thao tác ghi từ UI, QR/GPS trên thiết bị thật hoặc gửi SMTP; không gửi email hay tạo dữ liệu chấm công/đơn nghỉ thử vào database đang sử dụng.

### Build và test tự động

- Build phiên bản backend sửa đổi thành công trong thư mục đầu ra riêng, tránh file thực thi đang chạy bị khóa.
- Lần chạy suite ban đầu: **122 passed, 1 failed, 18 skipped**. Ca lỗi `AssignRole_ReturnsOk_WhenRoleExists` gửi thiếu tên shop theo ràng buộc hiện có; đã bổ sung `ShopName = "Test Shop"` vào dữ liệu test.
- Sau chỉnh sửa, Windows Application Control chặn assembly test mới (`0x800711C7`), nên **chưa xác nhận suite chạy lại thành công**. Không tắt hoặc thay đổi chính sách bảo vệ của Windows.
- 18 test database yêu cầu database/port thử nghiệm riêng chưa được chạy trong suite này. Các kiểm tra SQL/migration ở trên được chạy độc lập và đã thành công.

### EXPLAIN ANALYZE

Đã lưu `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` cho 5 truy vấn đại diện. Thời gian dưới đây là một lần đo trên dữ liệu nhỏ, trước/sau có khác biệt cache và thống kê; **không phải benchmark tải hoặc bằng chứng cải thiện p95/p99**.

| Truy vấn đại diện | Trước (ms) | Sau (ms) |
| --- | ---: | ---: |
| employees | 0.263 | 0.155 |
| schedule | 1.375 | 0.082 |
| attendance | 0.726 | 0.073 |
| leave_review | 3.586 | 0.088 |
| notifications | 0.91 | 0.054 |

Không thêm index mới chỉ để ép Index Scan trên bảng ít dữ liệu. Việc bỏ bảng trống chủ yếu làm schema gọn hơn; việc bỏ chỉ mục trùng giảm chi phí lưu trữ và cập nhật chỉ mục khi ghi dữ liệu.

## 8. Các file thay đổi và khôi phục

- [010_remove_unused_empty_tables.sql](sql/010_remove_unused_empty_tables.sql): xóa nhóm bảng/hàm đã rà soát, kiểm tra dữ liệu và phụ thuộc, timeout khóa 5 giây, transaction.
- [011_remove_duplicate_indexes.sql](sql/011_remove_duplicate_indexes.sql): so sánh catalog trước khi xóa 5 index dư.
- [010_restore_unused_tables.sql](sql/rollback/010_restore_unused_tables.sql): phục hồi cấu trúc 9 bảng trống, 4 hàm, constraint, index, trigger và ACL từ backup. Không phục hồi dữ liệu phát sinh sau audit hoặc trạng thái sequence lịch sử; dùng backup đầy đủ khi cần phục hồi chính xác hơn.
- [011_restore_duplicate_indexes.sql](sql/rollback/011_restore_duplicate_indexes.sql): phục hồi 5 index dư nếu cần.
- `Repositories/Auth/PostgresRefreshTokenRepository.cs`, `database/20261009_create_user_refresh_tokens.sql`, `docs/sql/005_add_user_refresh_tokens.sql`, `docs/sql/002_add_attendance_geofence.sql`: bỏ lệnh khởi tạo index trùng.
- `Repositories/PostgresReportingRepository.cs`: khai báo kiểu tham số nullable để Statistics API hoạt động.
- `manage365.Tests/UsersControllerTests.cs`: bổ sung tên shop cho dữ liệu test phân quyền Manager.

Backup và bằng chứng chi tiết nằm ngoài Git tại:

`D:/LT_Mobile_QLDA/web_app/manage365/artifacts/db-audit/`

File quan trọng: `public-before.dump`, `backup-manifest.json`, `usage.json`, `constraints.json`, `dependencies.json`, `applied.json`, `api-after.json`, `plans-before.json`, `plans-after.json`. Backup có dữ liệu tài khoản/token dạng lưu trong database; cần giữ ở nơi riêng tư, không đưa lên Git hoặc gửi công khai.

SHA-256 của `public-before.dump`:

`58a93e0ce694473d6ba5bafc176715c4d993fd54302f46106f23040ce4d45f07`

Khôi phục cấu trúc bằng psql với kết nối đã cấu hình an toàn, bật dừng khi lỗi và chạy trong một transaction:

```powershell
& 'C:/Program Files/PostgreSQL/18/bin/psql.exe' -X -v ON_ERROR_STOP=1 --single-transaction -f docs/sql/rollback/010_restore_unused_tables.sql
```

Chỉ chạy rollback khi cần khôi phục module cũ và bảng chưa được tạo lại. Script yêu cầu bảng cha `nhan_vien` còn tồn tại và các role Supabase tương ứng; đã thử khôi phục trên bản sao. Không khôi phục đè toàn bộ `public-before.dump` lên database đang dùng vì có thể làm mất thay đổi mới; nên phục hồi archive đầy đủ vào database riêng trước.

## 9. Việc còn cần làm khi chạy phiên bản mới

**Migration đã áp dụng, không cần chạy lại.** Backend đang chạy cổng 5295 vẫn là tiến trình cũ. Cần khởi động lại backend từ mã hiện tại để nạp sửa Statistics và lệnh khởi tạo index đã điều chỉnh. Backend cổng 5296 chỉ phục vụ kiểm chứng, sẽ được dừng sau khi hoàn tất.

Sau khi chính sách Windows cho phép chạy assembly test, chạy lại `dotnet test manage365.Tests/manage365.Tests.csproj` để xác nhận toàn bộ suite. Module trả lương/bảo hiểm/bán hàng chưa triển khai cần migration riêng khi bắt đầu phát triển.
