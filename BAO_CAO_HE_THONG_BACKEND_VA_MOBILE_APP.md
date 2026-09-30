# BÁO CÁO TỔNG KẾT TIẾN ĐỘ VÀ KIẾN TRÚC HỆ THỐNG
## DỰ ÁN HỆ THỐNG QUẢN LÝ BÁN LẺ & CHẤM CÔNG RETAIL365 (MANAGE365)

---

- **Dự án**: **Manage365** (Web Admin & Backend API) & **Retail Management Mobile App** (App Nhân viên)
- **Nền tảng Backend / Admin**: .NET 10.0 (ASP.NET Core Web API & MVC Razor)
- **Nền tảng Mobile**: Flutter (Dart)
- **Cơ sở dữ liệu**: PostgreSQL (Supabase Database)
- **Trạng thái**: Hoàn thiện tích hợp luồng Xác thực, Quên mật khẩu SMTP, Chấm công QR Động kết hợp GPS Geofencing, Quản lý vị trí cửa hàng. Kiểm thử đạt **17/17 Unit Tests (100% Pass)**.

---

## 1. TỔNG QUAN KIẾN TRÚC 2 PHÂN HỆ

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                             POSTGRESQL (SUPABASE)                                │
└────────────────────────┬────────────────────────────────┬────────────────────────┘
                         │                                │
                         ▼                                ▼
┌──────────────────────────────────────────────────────────────────────────────────┐
│                    BACKEND WEB API & ADMIN PORTAL (.NET 10)                      │
│                                (manage365)                                       │
│                                                                                  │
│  [Web Admin cho Chủ Cửa Hàng]             [RESTful API Engine]                   │
│  - Dashboard & Báo cáo tổng quan          - JWT Authentication & Roles           │
│  - Cấu hình vị trí GPS & Geofence         - Dynamic QR & HMAC Validation         │
│  - Kiosk sinh mã QR Chấm công động        - GPS Distance Haversine Calculator    │
│  - Quản lý ca làm việc & nhân sự          - Password Reset via SMTP Email        │
└────────────────────────────────────────────────┬─────────────────────────────────┘
                                                 │
                                                 │ RESTful API (JSON / Bearer Token)
                                                 ▼
┌──────────────────────────────────────────────────────────────────────────────────┐
│                   RETAIL MANAGEMENT MOBILE APP (FLUTTER)                         │
│                    (C:\Users\ASUS\StudioProjects\retail-management-app)                │
│                                                                                  │
│  [Dành cho Nhân viên Cửa hàng]                                                   │
│  - Đăng nhập / Đăng xuất / Lưu phiên làm việc (JWT)                              │
│  - Quên mật khẩu qua mã OTP gửi về Email                                         │
│  - Quét mã QR Kiosk bằng Camera (Mobile Scanner)                                │
│  - Lấy tọa độ GPS thiết bị (Geolocator) gửi kèm để Check-in/Check-out             │
│  - Xem lịch làm việc phân ca, lịch sử chấm công cá nhân                         │
└──────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. CÁC THƯ VIỆN ĐÃ CÀI ĐẶT & VAI TRÒ XỬ LÝ LOGIC

### 2.1 Phân hệ Backend API & Web Admin (`manage365`)

| Tên Package / Thư viện | Phiên bản | Vai trò & Logic Nghiệp vụ Xử lý |
| :--- | :--- | :--- |
| **`Microsoft.AspNetCore.Authentication.JwtBearer`** | `10.0.12` | Xác thực người dùng bằng JWT Token. Đính kèm Claims (`sub`, `email`, `role`) để phân quyền giữa Admin/Chủ cửa hàng và Nhân viên. |
| **`Npgsql`** | `10.0.3` | Driver ADO.NET hiệu năng cao để kết nối và thực thi các câu lệnh SQL trên PostgreSQL (Supabase). |
| **`Lyntai.Storage.Postgres`** | `3.5.1` | Hỗ trợ quản lý kết nối và thao tác lưu trữ dữ liệu với PostgreSQL. |
| **`Swashbuckle.AspNetCore`** | `10.2.3` | Tự động sinh tài liệu Swagger UI OpenAPI v1 để kiểm thử API tương tác trực tiếp. |
| **`Microsoft.VisualStudio.Azure.Containers.Tools.Targets`** | `1.23.0` | Hỗ trợ Containerization, đóng gói Dockerfile chuẩn môi trường Linux Container. |
| **`System.Security.Cryptography`** (Built-in) | Core | - **HMAC-SHA256**: Ký và xác thực mã QR động chống làm giả.<br>- **PBKDF2 (Rfc2898DeriveBytes)**: Băm mật khẩu (100.000 rounds salt) an toàn tuyệt đối. |
| **`Microsoft.AspNetCore.RateLimiting`** (Built-in) | Core | Giới hạn tần suất gọi API (Rate Limiting) cho luồng đăng nhập/đăng ký (`auth`) và luồng quét chấm công (`attendance-scan`) chống spam và tấn công DoS. |
| **`System.Net.Mail (SmtpClient)`** (Built-in) | Core | Gửi mã OTP xác thực quên mật khẩu qua máy chủ SMTP Gmail đến hòm thư nhân viên. |
| **`xUnit`, `Moq`, `FluentAssertions`** (Tests) | `net10.0` | Bộ kiểm thử tự động (Unit Test) cho logic tính toán Geofence, Token Protector và Reset Password. |

---

### 2.2 Phân hệ Mobile App Nhân viên (`retail-management-app`)

| Tên Package / Plugin | Phiên bản | Vai trò & Logic Xử lý trên Ứng dụng Di động |
| :--- | :--- | :--- |
| **`mobile_scanner`** | `^7.4.2` | Tích hợp Camera quét mã QR chấm công tốc độ cao, hỗ trợ tự động nhận diện chuỗi payload QR từ Kiosk. |
| **`geolocator`** | `^14.1.1` | Truy xuất tọa độ GPS (Vĩ độ, Kinh độ, Độ chính xác `accuracy`) thời gian thực của nhân viên để backend xác thực vị trí đứng tại cửa hàng. |
| **`http`** | `^1.2.2` | Giao tiếp mạng HTTP/HTTPS với Backend API, gửi Header `Authorization: Bearer <token>`, xử lý Request/Response JSON. |
| **`shared_preferences`** | `^2.3.2` | Lưu trữ cục bộ an toàn: JWT Token, Refresh Token, Thông tin User và cài đặt phiên làm việc. |
| **`qr_flutter`** | `^4.1.0` | Hỗ trợ render mã QR trên màn hình ứng dụng di động khi cần trao đổi dữ liệu. |
| **`intl`** | `^0.20.3` | Định dạng ngày tháng, giờ vào ca, định dạng tiền tệ và hỗ trợ đa ngôn ngữ hiển thị. |
| **`font_awesome_flutter`**, **`cupertino_icons`** | Mới nhất | Bộ biểu tượng giao diện chuẩn iOS & Android chuyên nghiệp. |

---

## 3. CHI TIẾT CÁC MODULE VÀ LOGIC BACKEND ĐÃ THỰC HIỆN

### Module 1: Xác thực, Phân quyền & Quản lý Tài khoản (Authentication & RBAC)
- **Đăng ký / Đăng nhập (`/api/auth/register`, `/api/auth/login`)**:
  - Chuẩn hóa email, kiểm tra trùng lặp.
  - Băm mật khẩu bằng thuật toán **PBKDF2 với Salt 128-bit**, 100.000 vòng lặp.
  - Cấp phát **JWT Token** chứa User ID, Email, Display Name, Role (`Admin`, `Manager`, `Staff`).
- **Lấy thông tin người dùng (`/api/auth/me`)**:
  - Trích xuất thông tin định danh từ JWT Claims để xác thực phiên làm việc.
- **Bảo mật**: Áp dụng Rate Limiting tối đa 5 yêu cầu/phút cho mỗi IP với các API nhạy cảm.

---

### Module 2: Quên Mật khẩu An toàn qua Email SMTP (Password Reset via SMTP)
- **Yêu cầu cấp lại mật khẩu (`/api/auth/password-reset/request`)**:
  - Sinh mã OTP ngẫu nhiên 6 chữ số.
  - Lưu mã dưới dạng băm kèm thời gian hết hạn (15 phút).
  - Gửi email định dạng HTML đẹp mắt đến nhân viên thông qua SMTP Gmail bảo mật SSL/TLS.
- **Xác thực mã OTP (`/api/auth/password-reset/verify-code`)**:
  - Kiểm tra tính hợp lệ của mã, thời gian hết hạn và số lần thử (chống Brute-force).
  - Trả về **Reset Token được mã hóa an toàn** (Data Protection Protector).
- **Đặt lại mật khẩu mới (`/api/auth/password-reset/reset`)**:
  - Giải mã và xác thực Reset Token, tiến hành cập nhật mật khẩu mới đã được băm PBKDF2 vào cơ sở dữ liệu.

---

### Module 3: Chấm công Đa tầng: Dynamic QR Code + GPS Geofencing
- **Kiosk sinh mã QR Động (`/api/attendance-kiosk/generate-qr`)**:
  - Sinh chuỗi payload chứa `storeCode`, `timestamp`, `nonce` và **chữ ký số HMAC-SHA256**.
  - Mã QR tự động hết hạn sau **30 giây** (TTL), chống hành vi chụp ảnh màn hình gửi cho nhân viên khác chấm công hộ.
- **Xác thực QR Code (`/api/attendance/verify-qr`)**:
  - Giải mã và kiểm tra chữ ký số HMAC.
  - Tự động quét ca làm việc trong ngày của nhân viên, đưa ra gợi ý hành động (`CHECK_IN` hoặc `CHECK_OUT`), hỗ trợ cả trường hợp chưa được xếp ca trước.
- **Xác thực Tọa độ GPS Geofence (`/api/attendance/submit`)**:
  - Áp dụng công thức lượng giác **Haversine Formula** để tính khoảng cách thực tế giữa tọa độ GPS của thiết bị nhân viên và tọa độ tâm của cửa hàng.
  - Kiểm tra bán kính hợp lệ (mặc định cấu hình theo cửa hàng, ví dụ: 50m - 100m).
  - Kiểm tra độ chính xác của GPS (`MaxLocationAccuracyMeters <= 30m`) và độ trễ thời gian (`MaxLocationAgeSeconds <= 60s`) để loại trừ việc giả lập GPS (Fake GPS).
- **Lịch sử và Ca làm việc (`/api/attendance/history`, `/api/attendance/my-shifts`)**:
  - Truy vấn lịch sử ra/vào ca, thời gian chấm công và tính toán trạng thái đi muộn/về sớm.

---

### Module 4: Quản trị Vị trí Cửa hàng dành cho Admin (Store Geofence Admin)
- **Tra cứu cấu hình vị trí (`GET /api/attendance-locations/{storeCode}`)**:
  - Trả về tọa độ Vĩ độ (`latitude`), Kinh độ (`longitude`), Bán kính cho phép (`radius_meters`) và trạng thái kích hoạt của cửa hàng.
- **Cập nhật tọa độ cửa hàng (`PUT /api/attendance-locations/{storeCode}`)**:
  - Cho phép Admin cập nhật lại vị trí cửa hàng khi thay đổi địa điểm.
  - Tự động ghi lại lịch sử chỉnh sửa vào bảng **`store_location_audits`** (lưu lại ai sửa, vào lúc nào, tọa độ cũ và tọa độ mới).

---

### Module 5: Giao diện Web Admin & Kiosk (ASP.NET MVC)
- **Web Portal cho Admin/Chủ cửa hàng**:
  - Giao diện đăng nhập, Dashboard thống kê, danh sách nhân viên và quản lý điểm chấm công.
  - Trang hiển thị mã QR Chấm công toàn màn hình (Kiosk Mode) tự động làm mới mã sau mỗi chu kỳ.
- **Cơ chế lưu trữ Cookie / JWT**:
  - Hỗ trợ lưu trữ phiên đăng nhập mượt mà cho Admin trên trình duyệt.

---

## 4. BẢNG TỔNG HỢP CÁC ENDPOINT BACKEND API CHÍNH

| Phương thức | Đường dẫn Endpoint | Quyền hạn | Mô tả chức năng |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/auth/register` | Public | Đăng ký tài khoản người dùng mới |
| `POST` | `/api/auth/login` | Public | Đăng nhập hệ thống, cấp JWT Token |
| `GET` | `/api/auth/me` | User (`Bearer Token`) | Lấy thông tin cá nhân và vai trò |
| `POST` | `/api/auth/password-reset/request` | Public | Gửi mã OTP 6 số qua email SMTP |
| `POST` | `/api/auth/password-reset/verify-code` | Public | Xác thực mã OTP và nhận Reset Token |
| `POST` | `/api/auth/password-reset/reset` | Public | Đặt lại mật khẩu mới |
| `POST` | `/api/attendance-kiosk/generate-qr` | Admin / Kiosk | Sinh mã QR chấm công động có chữ ký HMAC |
| `POST` | `/api/attendance/verify-qr` | Staff (`Bearer Token`) | Quét và kiểm tra tính hợp lệ của mã QR |
| `POST` | `/api/attendance/submit` | Staff (`Bearer Token`) | Thực hiện Check-in / Check-out (QR + GPS) |
| `GET` | `/api/attendance/my-shifts` | Staff (`Bearer Token`) | Lấy danh sách ca làm việc được phân công |
| `GET` | `/api/attendance/history` | Staff (`Bearer Token`) | Lịch sử chấm công của nhân viên |
| `GET` | `/api/attendance-locations/{storeCode}` | Admin (`Bearer Token`) | Lấy cấu hình tọa độ GPS cửa hàng |
| `PUT` | `/api/attendance-locations/{storeCode}` | Admin (`Bearer Token`) | Cập nhật tọa độ GPS & Bán kính Geofence |

---

## 5. KẾT QUẢ KIỂM THỬ TỰ ĐỘNG (UNIT TESTS)

Dự án đã xây dựng bộ Unit Test tự động hóa hoàn chỉnh trong project [`manage365.Tests`](file:///d:/LT_Mobile_QLDA/web_app/manage365/manage365.Tests/manage365.Tests.csproj):

```
Passed!  - Failed: 0, Passed: 17, Skipped: 0, Total: 17, Duration: 345 ms
```

- ✅ **`AttendanceGeofenceServiceTests`**: Kiểm tra tính khoảng cách Haversine, ngưỡng bán kính, kiểm tra độ chính xác GPS và thời gian hợp lệ của thiết bị.
- ✅ **`PasswordResetServiceTests`**: Kiểm tra quy trình tạo OTP, kiểm tra mã sai, mã hết hạn và cấp token.
- ✅ **`PasswordResetTokenProtectorTests`**: Kiểm tra mã hóa và bảo vệ Token đặt lại mật khẩu.
- ✅ **`SmtpCredentialTests` & `LocalEnvFileTests`**: Kiểm tra tải cấu hình biến môi trường an toàn.

---

## 6. KẾ HOẠCH BƯỚC TIẾP THEO

1. **Phía Mobile App (Flutter)**:
   - Tiếp tục hoàn thiện UI Dashboard hiển thị ca làm việc theo tuần.
   - Tích hợp thông báo đẩy (Push Notification) nhắc nhở nhân viên trước giờ vào ca.
2. **Phía Backend / Admin (Web)**:
   - Bổ sung module Báo cáo xuất dữ liệu bảng chấm công ra file Excel/PDF phục vụ tính lương.
   - Hoàn thiện tính năng phê duyệt đơn xin nghỉ phép / đổi ca trực tuyến.
