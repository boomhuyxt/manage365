# RETAIL365 (MANAGE365) - HỆ THỐNG QUẢN LÝ BÁN LẺ & CHẤM CÔNG THÔNG MINH

> Hệ thống quản lý bán lẻ toàn diện kết hợp Web Admin Portal & Backend API (.NET 10) và Ứng dụng di động dành cho nhân viên (Flutter), tích hợp chấm công đa tầng bảo mật cao với **Mã QR Động (HMAC)** và **Định vị GPS Geofencing**.

---

## MỤC LỤC
1. [Tổng Quan Hệ Thống](#1-tổng-quan-hệ-thống)
2. [Kiến Trúc Kỹ Thuật](#2-kiến-trúc-kỹ-thuật)
3. [Công Nghệ & Thư Viện Sử Dụng](#3-công-nghệ--thư-viện-sử-dụng)
4. [Các Phân Hệ & Nghiệp Vụ Cốt Lõi](#4-các-phân-hệ--nghiệp-vụ-cốt-lõi)
5. [Danh Sách RESTful API Endpoints](#5-danh-sách-restful-api-endpoints)
6. [Hướng Dẫn Cài Đặt & Khởi Chạy (Step-by-Step)](#6-hướng-dẫn-cài-đặt--khởi-chạy-step-by-step)
   - [Yêu cầu tiên quyết](#61-yêu-cầu-tiên-quyết)
   - [Bước 1: Cấu hình Cơ sở dữ liệu (PostgreSQL / Supabase)](#62-bước-1-cấu-hình-cơ-sở-dữ-liệu-postgresql--supabase)
   - [Bước 2: Cấu hình Biến môi trường Backend (.env)](#63-bước-2-cấu-hình-biến-môi-trường-backend-env)
   - [Bước 3: Khởi chạy Backend Web API & Admin](#64-bước-3-khởi-chạy-backend-web-api--admin)
   - [Bước 4: Chạy Kiểm thử Tự động (Unit Tests)](#65-bước-4-chạy-kiểm-thử-tự-động-unit-tests)
   - [Bước 5: Cài đặt và Khởi chạy Mobile App (Flutter)](#66-bước-5-cài-đặt-và-khởi-chạy-mobile-app-flutter)
7. [Mã Lỗi Hệ Thống & Hướng Dẫn Xử Lý](#7-mã-lỗi-hệ-thống--hướng-dẫn-xử-lý)
8. [Lộ Trình Phát Triển Tiếp Theo](#8-lộ-trình-phát-triển-tiếp-theo)

---

## 1. TỔNG QUAN HỆ THỐNG

Dự án **Retail365 (Manage365)** được xây dựng nhằm giải quyết bài toán quản trị vận hành bán lẻ chuỗi cửa hàng, quản lý nhân sự, lịch làm việc và đặc biệt là hệ thống **chấm công chống gian lận đa tầng**:
- **Backend API & Web Admin (`manage365`)**: Xử lý logic nghiệp vụ tập trung, cấp phát xác thực JWT, phục vụ giao diện quản trị Web cho Quản lý / Chủ cửa hàng và màn hình Kiosk hiển thị mã QR động.
- **Mobile App Nhân viên (`retail-management-app`)**: Ứng dụng di động Flutter giúp nhân viên xem lịch ca, quét mã QR Kiosk, tự động thu thập tọa độ GPS và gửi yêu cầu Check-in / Check-out an toàn.
- **Cơ chế chống gian lận**:
  - Mã QR động đổi liên tục mỗi 30-35s và được ký bằng thuật toán HMAC-SHA256 (chống chụp ảnh mã QR gửi cho người khác chấm công hộ).
  - Tọa độ GPS Geofencing được tính toán trực tiếp trên server qua công thức **Haversine** hoặc **PostGIS**; chặn định vị giả lập (Fake GPS), kiểm tra độ chính xác (accuracy <= 30m) và giới hạn độ trễ thời gian (age <= 60s).

---

## 2. KIẾN TRÚC KỸ THUẬT

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                             POSTGRESQL (SUPABASE)                                │
│       - Bảng nhân sự, ca làm, đăng ký ca, chấm công                              │
│       - PostGIS: Tọa độ cửa hàng (dia_diem_cham_cong), audit log                 │
└────────────────────────┬────────────────────────────────┬────────────────────────┘
                         │                                │
                         ▼                                ▼
┌──────────────────────────────────────────────────────────────────────────────────┐
│                    BACKEND WEB API & ADMIN PORTAL (.NET 10)                      │
│                                (manage365)                                       │
│                                                                                  │
│  [Web Admin cho Quản lý / Chủ Cửa Hàng]    [RESTful API Engine]                  │
│  - Dashboard thống kê & Giám sát hôm nay    - JWT Authentication & RBAC           │
│  - Kiosk sinh mã QR Chấm công động         - Dynamic QR HMAC-SHA256 Signing      │
│  - Cấu hình vị trí GPS & Geofence cửa hàng - PostGIS / Haversine Distance Calc    │
│  - Quản trị ca làm & Lịch sử chấm công     - Password Reset via SMTP Gmail       │
│                                            - Rate Limiter & Security Protection   │
└────────────────────────────────────────────────┬─────────────────────────────────┘
                                                 │
                                                 │ HTTPS / JSON (Bearer Token)
                                                 ▼
┌──────────────────────────────────────────────────────────────────────────────────┐
│                   RETAIL MANAGEMENT MOBILE APP (FLUTTER)                         │
│                    (retail-management-app - Dành cho Nhân viên)                  │
│                                                                                  │
│  - Đăng nhập / Đăng xuất, Lưu trữ phiên làm việc an toàn (JWT Token)            │
│  - Quên mật khẩu qua mã OTP gửi về Email cá nhân                                 │
│  - Quét mã QR Kiosk bằng Camera (Mobile Scanner)                                │
│  - Lấy tọa độ GPS thời gian thực (Geolocator) gửi xác thực vị trí đứng           │
│  - Tra cứu lịch làm việc phân ca, lịch sử chấm công ra/vào                       │
└──────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. CÔNG NGHỆ & THƯ VIỆN SỬ DỤNG

### 3.1 Backend API & Web Admin (`manage365`)
- **Nền tảng**: .NET 10.0 (ASP.NET Core Web API & Razor MVC).
- **Thư viện chính**:
  - `Microsoft.AspNetCore.Authentication.JwtBearer` (`10.0.12`): Xác thực người dùng bằng Bearer Token, phân quyền theo Claims (`sub`, `email`, `role`).
  - `Npgsql` (`10.0.3`): Driver kết nối PostgreSQL tốc độ cao.
  - `Lyntai.Storage.Postgres` (`3.5.1`): Quản lý kết nối và tối ưu truy vấn cơ sở dữ liệu.
  - `Swashbuckle.AspNetCore` (`10.2.3`): Tự động sinh tài liệu Swagger UI OpenAPI tương tác.
  - `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` (`1.23.0`): Hỗ trợ đóng gói Docker Container Linux.
  - `System.Security.Cryptography` (Core): HMAC-SHA256 cho chữ ký mã QR động; PBKDF2 (100.000 rounds salt) băm mật khẩu bảo mật tuyệt đối.
  - `Microsoft.AspNetCore.RateLimiting` (Core): Giới hạn tần suất gọi API (ngăn chặn brute-force và DDoS).
  - `System.Net.Mail` (Core): Gửi mã OTP xác thực quên mật khẩu qua SMTP SSL/TLS.
  - `xUnit`, `Moq`, `FluentAssertions`: Bộ kiểm thử tự động hóa Unit Test (19/19 Tests Passed).

### 3.2 Mobile App Nhân viên (`retail-management-app`)
- **Nền tảng**: Flutter (Dart SDK).
- **Thư viện chính**:
  - `mobile_scanner` (`^7.4.2`): Tích hợp camera quét mã QR tốc độ cao.
  - `geolocator` (`^14.1.1`): Thu thập tọa độ GPS (Latitude, Longitude, Accuracy, Mock Status).
  - `http` (`^1.2.2`): Giao tiếp RESTful API qua HTTP/HTTPS.
  - `shared_preferences` (`^2.3.2`): Lưu trữ phiên làm việc, JWT Token an toàn trên máy.
  - `qr_flutter` (`^4.1.0`): Render mã QR hiển thị trên ứng dụng.
  - `intl` (`^0.20.3`): Định dạng ngày giờ, tiền tệ và số liệu hiển thị.
  - `font_awesome_flutter` & `cupertino_icons`: Bộ icon giao diện hiện đại.

---

## 4. CÁC PHÂN HỆ & NGHIỆP VỤ CỐT LÕI

### 4.1 Module 1: Xác thực & Phân quyền (Authentication & RBAC)
- **Đăng ký tài khoản (`/api/auth/register`)**: Mặc định gắn role `Employee`, chuẩn hóa email, kiểm tra trùng lặp.
- **Băm mật khẩu an toàn**: Sử dụng thuật toán chuẩn **PBKDF2** với Salt 128-bit, 100.000 vòng lặp (vượt trội so với MD5/SHA thông thường).
- **Đăng nhập & Cấp JWT (`/api/auth/login`)**: Cấp phát Token chứa User ID, Email, Họ tên, Role (`Admin`, `Manager`, `Staff`).
- **Thông tin tài khoản (`/api/auth/me`)**: Trích xuất từ JWT Claims phục vụ kiểm tra phiên làm việc.
- **Bảo mật Rate Limiting**: Giới hạn 5 requests/phút trên mỗi IP để ngăn chặn dò quét tài khoản.

### 4.2 Module 2: Quên Mật Khẩu Qua Email SMTP (Password Reset)
- **Yêu cầu OTP (`/api/auth/password-reset/request` hoặc `/api/auth/forgot-password`)**:
  - Sinh mã OTP ngẫu nhiên 6 chữ số.
  - Lưu mã dưới dạng băm kèm thời hạn hiệu lực (10-15 phút).
  - Gửi email định dạng HTML chuyên nghiệp qua máy chủ Google Gmail SMTP bảo mật.
- **Xác thực mã OTP (`/api/auth/password-reset/verify-code`)**:
  - Kiểm tra tính hợp lệ, thời gian hết hạn và số lần nhập sai (chống Brute-force).
  - Cấp **Reset Token** dùng một lần được bảo vệ bằng ASP.NET Core Data Protection (hết hạn sau 5 phút).
- **Đặt lại mật khẩu (`/api/auth/password-reset/reset`)**:
  - Giải mã và xác thực Reset Token, cập nhật mật khẩu mới băm PBKDF2 vào cơ sở dữ liệu.

### 4.3 Module 3: Chấm Công Đa Tầng (Dynamic QR + GPS Geofencing)
- **Kiosk sinh mã QR Động (`/api/attendance-kiosk/generate-qr` hoặc `/api/attendance-qr/kiosk`)**:
  - Sinh chuỗi payload gồm `storeCode`, `timestamp`, `nonce` và chữ ký số **HMAC-SHA256**.
  - Mã QR tự động hết hạn sau **30-35 giây** (TTL), chống hành vi chụp ảnh màn hình gửi cho nhân viên khác chấm công hộ.
- **Xác thực mã QR (`/api/attendance/verify-qr`)**:
  - Kiểm tra tính toàn vẹn chữ ký HMAC và độ trễ thời gian.
  - Tự động quét ca làm việc trong ngày của nhân viên; hỗ trợ cả ca đã xếp lịch và chấm công linh hoạt chưa xếp ca (`shiftAssignmentId: null`).
- **Xác thực GPS Geofence (`/api/attendance/submit`)**:
  - Tọa độ GPS được tính toán khoảng cách thực tế trên Server bằng PostGIS (`ST_Distance`) hoặc công thức lượng giác **Haversine Formula**.
  - Kiểm tra bán kính hợp lệ của cửa hàng (mặc định 50m - 100m).
  - Bắt buộc kiểm tra độ chính xác GPS (`MaxLocationAccuracyMeters <= 30m`), độ trễ thời gian (`MaxLocationAgeSeconds <= 60s`) và cờ `isMocked == false` để loại trừ việc giả lập GPS (Fake GPS).
- **Lịch sử chấm công (`/api/attendance/history`)**: Cho phép nhân viên tra cứu lịch sử vào/ra ca và tổng giờ làm thực tế.

### 4.4 Module 4: Quản Trị Vị Trí Cửa Hàng & Audit Log (Store Geofence Admin)
- **Cấu hình GPS (`GET / PUT /api/attendance-locations/{storeCode}`)**:
  - Admin/Quản lý có thể cấu hình và cập nhật tọa độ tâm (Vĩ độ, Kinh độ) và bán kính cho phép (`radius_meters`) của từng điểm cửa hàng.
- **Nhật ký chỉnh sửa (Audit Trail)**:
  - Mọi thao tác thay đổi vị trí cửa hàng đều được tự động ghi nhận vào bảng `lich_su_dia_diem_cham_cong` (ai sửa, thời gian nào, tọa độ cũ và tọa độ mới).

### 4.5 Module 5: Web Admin Portal & Kiosk Mode (ASP.NET Razor MVC)
- Giao diện Admin quản lý nhân sự, ca làm việc, giám sát thời gian thực danh sách nhân viên đang trong ca.
- Chế độ **Kiosk Mode toàn màn hình**: Tự động gọi API lấy mã QR động và render làm mới sau mỗi 30 giây phục vụ đặt tại quầy thu ngân/cửa ra vào.

---

## 5. DANH SÁCH RESTFUL API ENDPOINTS

| Phương thức | Đường dẫn Endpoint | Quyền hạn | Mô tả chức năng |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/auth/register` | Public | Đăng ký tài khoản nhân viên mới |
| `POST` | `/api/auth/login` | Public | Đăng nhập hệ thống, cấp Access Token JWT |
| `GET` | `/api/auth/me` | User (`Bearer`) | Lấy thông tin cá nhân và quyền hạn hiện tại |
| `POST` | `/api/auth/password-reset/request` | Public | Gửi mã OTP 6 số qua email SMTP Gmail |
| `POST` | `/api/auth/password-reset/verify-code` | Public | Xác thực mã OTP và nhận Reset Token |
| `POST` | `/api/auth/password-reset/reset` | Public | Đặt lại mật khẩu mới bằng Reset Token |
| `GET` / `POST` | `/api/attendance-kiosk/generate-qr`<br>*(hoặc `/api/attendance-qr/kiosk`)* | Admin / Kiosk | Sinh payload mã QR động kèm chữ ký HMAC |
| `POST` | `/api/attendance/verify-qr` | Staff (`Bearer`) | Quét và kiểm tra tính hợp lệ của mã QR |
| `POST` | `/api/attendance/submit` | Staff (`Bearer`) | Gửi xác nhận Check-in / Check-out (QR + GPS) |
| `GET` | `/api/attendance/my-shifts` | Staff (`Bearer`) | Lấy danh sách ca làm việc được phân công |
| `GET` | `/api/attendance/history` | Staff (`Bearer`) | Tra cứu lịch sử chấm công của cá nhân |
| `GET` | `/api/attendance/admin/snapshot` | Admin (`Bearer`) | Thống kê số lượng và danh sách chấm công hôm nay |
| `GET` | `/api/attendance-locations/{storeCode}` | Admin (`Bearer`) | Lấy thông tin cấu hình tọa độ GPS của cửa hàng |
| `PUT` | `/api/attendance-locations/{storeCode}` | Admin (`Bearer`) | Cập nhật tọa độ GPS và bán kính Geofence |

---

## 6. HƯỚNG DẪN CÀI ĐẶT & KHỞI CHẠY (STEP-BY-STEP)

### 6.1 Yêu cầu tiên quyết
- **Hệ điều hành**: Windows 10/11, macOS hoặc Linux.
- **Backend**:
  - [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) trở lên.
  - [Docker Desktop](https://www.docker.com/) (nếu chạy qua Docker Compose).
- **Cơ sở dữ liệu**:
  - PostgreSQL phiên bản 15+ (Khuyến nghị sử dụng **Supabase Database** đã hỗ trợ sẵn PostGIS).
- **Mobile App**:
  - [Flutter SDK](https://docs.flutter.dev/get-started/install) (phiên bản ổn định 3.x).
  - Android Studio / Xcode và máy ảo hoặc thiết bị thật có bật quyền Camera & Vị trí.

---

### 6.2 Bước 1: Cấu hình Cơ sở dữ liệu (PostgreSQL / Supabase)

Mở giao diện SQL Editor trên Supabase hoặc pgAdmin và thực thi lần lượt các file script SQL theo đúng thứ tự sau:

1. **Khởi tạo bảng Quên mật khẩu OTP**:
   - File: `database/20260927_create_password_reset_codes.sql`
   - Tạo bảng `password_reset_codes` lưu trữ mã băm OTP, số lần thử và thời gian hết hạn.
2. **Cho phép Chấm công linh hoạt (chưa xếp ca)**:
   - File: `docs/sql/001_allow_unassigned_attendance.sql`
   - Bổ sung `id_nhan_vien`, `ngay_cham_cong` vào bảng `cham_cong`, chuyển `id_dang_ky_ca` về nullable.
3. **Kích hoạt PostGIS và Khởi tạo bảng Vị trí Geofence**:
   - File: `docs/sql/002_add_attendance_geofence.sql`
   - Kích hoạt extension `postgis` trong schema `extensions`.
   - Tạo bảng `dia_diem_cham_cong` và `vi_tri_cham_cong` (lưu bằng chứng GPS cho mỗi lần Check-in/Check-out).
4. **Cấu hình tọa độ GPS cửa hàng thực tế**:
   - Tham khảo: `docs/sql/003_configure_store_location.example.sql`
   - Chạy lệnh cập nhật tọa độ thực tế của cửa hàng (Lưu ý: hàm `ST_MakePoint` nhận `(longitude, latitude)`):
     ```sql
     INSERT INTO public.dia_diem_cham_cong (
         store_code, ten_dia_diem, latitude, longitude, location, ban_kinh_met, is_active
     )
     VALUES (
         'STORE-01',
         'Retail365 Flagship Store',
         10.776889,
         106.700897,
         extensions.ST_SetSRID(extensions.ST_MakePoint(106.700897, 10.776889), 4326)::extensions.geography,
         50,
         TRUE
     )
     ON CONFLICT (store_code) DO UPDATE
     SET latitude = EXCLUDED.latitude,
         longitude = EXCLUDED.longitude,
         location = EXCLUDED.location,
         ban_kinh_met = EXCLUDED.ban_kinh_met,
         is_active = TRUE,
         updated_at = NOW();
     ```
5. **Khởi tạo bảng Audit Log lịch sử sửa đổi tọa độ**:
   - File: `docs/sql/004_add_store_location_audit.sql`
   - Tạo bảng `lich_su_dia_diem_cham_cong` phục vụ giám sát thay đổi vị trí.

---

### 6.3 Bước 2: Cấu hình Biến môi trường Backend (.env)

Trong thư mục gốc `manage365`, sao chép file cấu hình mẫu `.env.example` thành `.env`:

```powershell
# Trên PowerShell (Windows)
Copy-Item .env.example .env

# Trên Linux / macOS Bash
cp .env.example .env
```

Chỉnh sửa nội dung file `.env` với các tham số phù hợp:

```dotenv
# Chuỗi kết nối PostgreSQL (Supabase Connection String)
ConnectionStrings__DefaultConnection=Host=aws-0-ap-northeast-2.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.your_project_id;Password=YOUR_DATABASE_PASSWORD;SSL Mode=Require

# Khóa bí mật ký JWT Token (ít nhất 32 ký tự ngẫu nhiên)
Jwt__Key=your_super_secret_jwt_key_at_least_32_bytes_long_123456

# Khóa bí mật ký mã QR Chấm công HMAC-SHA256 (ít nhất 32 ký tự ngẫu nhiên)
AttendancePolicy__QrHmacSecret=your_secure_qr_hmac_secret_key_at_least_32_bytes_long

# Chính sách xác thực vị trí GPS
AttendancePolicy__MaxLocationAccuracyMeters=30
AttendancePolicy__MaxLocationAgeSeconds=60

# Khóa băm mã OTP xác thực quên mật khẩu
PasswordReset__HashKey=your_separate_password_reset_hash_key_32_bytes

# Cấu hình tài khoản gửi Email SMTP Gmail
Smtp__Host=smtp.gmail.com
Smtp__Port=587
Smtp__EnableSsl=true
Smtp__Username=your-account@gmail.com
Smtp__Password=YOUR_GMAIL_APP_PASSWORD_WITHOUT_SPACES
Smtp__FromEmail=your-account@gmail.com
Smtp__FromName=Retail365
```

> [!NOTE]
> Để lấy `Smtp__Password`, bạn truy cập tài khoản Google -> Quản lý tài khoản -> Bảo mật -> Bật Xác minh 2 bước -> **Mật khẩu ứng dụng (App Passwords)** -> Tạo mật khẩu 16 chữ số và dán liền không khoảng trắng.

---

### 6.4 Bước 3: Khởi chạy Backend Web API & Admin

#### Cách A: Chạy trực tiếp bằng .NET CLI (Khuyến nghị khi phát triển)
```powershell
# Di chuyển vào thư mục project manage365
cd manage365

# Khôi phục các thư viện NuGet
dotnet restore

# Biên dịch dự án
dotnet build

# Khởi chạy máy chủ
dotnet run
```
Sau khi khởi chạy thành công:
- **Giao diện Web Admin**: `http://localhost:5000` (hoặc cổng cấu hình trên console).
- **Tài liệu kiểm thử Swagger API**: `http://localhost:5000/swagger`.

#### Cách B: Chạy thông qua Docker Compose
```powershell
# Build và chạy container trong chế độ nền
docker compose up --build -d

# Xem log hoạt động của container
docker compose logs -f
```

---

### 6.5 Bước 4: Chạy Kiểm thử Tự động (Unit Tests)

Hệ thống có sẵn bộ Unit Test tự động hóa bảo đảm tính chính xác của các thuật toán mã hóa và Geofence:

```powershell
dotnet test
```

**Kết quả kiểm thử đạt 100% Pass (19/19 Tests Passed)**:
- `AttendanceGeofenceServiceTests`: Kiểm thử tính toán khoảng cách Haversine, độ chính xác GPS, độ trễ thời gian và phát hiện tọa độ giả lập.
- `PasswordResetServiceTests`: Kiểm thử quy trình sinh OTP, xác thực mã đúng/sai, xử lý mã hết hạn và cấp token.
- `PasswordResetTokenProtectorTests`: Kiểm thử giải mã & bảo vệ an toàn Token qua Data Protection.
- `SmtpCredentialTests` & `LocalEnvFileTests`: Kiểm tra đọc biến môi trường an toàn.

---

### 6.6 Bước 5: Cài đặt và Khởi chạy Mobile App (Flutter)

Mở dự án ứng dụng di động tại thư mục `retail-management-app`:

#### 1. Cài đặt các thư viện phụ thuộc:
```bash
flutter pub get
```

#### 2. Cấu hình Quyền (Permissions):
- **Android (`android/app/src/main/AndroidManifest.xml`)**:
  ```xml
  <uses-permission android:name="android.permission.CAMERA" />
  <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
  <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
  ```
- **iOS (`ios/Runner/Info.plist`)**:
  ```xml
  <key>NSCameraUsageDescription</key>
  <string>Ứng dụng cần quyền Camera để quét mã QR chấm công.</string>
  <key>NSLocationWhenInUseUsageDescription</key>
  <string>Ứng dụng cần định vị GPS để xác thực vị trí bạn đang có mặt tại cửa hàng.</string>
  ```

#### 3. Cấu hình Base URL kết nối Backend:
- Nếu chạy trên **Android Emulator**: Đặt Base URL là `http://10.0.2.2:5000`.
- Nếu chạy trên **iOS Simulator**: Đặt Base URL là `http://localhost:5000`.
- Nếu chạy trên **Thiết bị thật (Real Device)**: Đặt Base URL theo địa chỉ IP mạng nội bộ của máy tính chạy backend (Ví dụ: `http://192.168.1.15:5000`).

#### 4. Khởi chạy ứng dụng:
```bash
flutter run
```

---

## 7. MÃ LỖI HỆ THỐNG & HƯỚNG DẪN XỬ LÝ

| Mã lỗi (`ErrorCode`) | Nguyên nhân | Hướng khắc phục |
| :--- | :--- | :--- |
| `location_required` | Client không gửi kèm dữ liệu tọa độ GPS khi submit chấm công. | Kiểm tra lại quyền vị trí trên app, đảm bảo `geolocator` đã lấy được tọa độ. |
| `location_invalid` | Tọa độ GPS không hợp lệ (Vĩ độ nằm ngoài [-90, 90] hoặc Kinh độ ngoài [-180, 180]). | Đảm bảo thiết bị đã bật định vị vệ tinh GPS rõ ràng. |
| `location_inaccurate` | Độ chính xác GPS của thiết bị kém hơn ngưỡng cho phép (`accuracyMeters > 30m`). | Di chuyển lại gần cửa sổ hoặc ra khu vực thoáng sóng GPS để tăng độ chính xác. |
| `location_too_old` | Tọa độ GPS được lấy cách thời điểm gửi quá lâu (`> 60 giây`). | App cần lấy tọa độ GPS mới ngay tại thời điểm nhân viên nhấn nút xác nhận chấm công. |
| `mock_location_detected` | Thiết bị đang bật ứng dụng giả lập GPS (Fake GPS). | Tắt ứng dụng Fake GPS và chế độ Mock Location trong Tùy chọn nhà phát triển. |
| `outside_geofence` | Khoảng cách từ nhân viên tới cửa hàng vượt quá bán kính quy định (ví dụ > 50m). | Nhân viên cần có mặt trực tiếp tại cửa hàng để tiến hành chấm công. |
| `attendance_location_not_found` | Mã cửa hàng trong mã QR (`storeCode`) chưa được cấu hình tọa độ trong database. | Admin chạy script cấu hình vị trí cho cửa hàng trong bảng `dia_diem_cham_cong`. |
| `qr_expired` / `invalid_signature` | Mã QR Kiosk đã quá hạn 30s hoặc chuỗi payload bị can thiệp. | Kiosk tự động làm mới mã QR hoặc nhấn làm mới trên màn hình Kiosk để quét lại. |

---

## 8. LỘ TRÌNH PHÁT TRIỂN TIẾP THEO

1. **Phân hệ Mobile App (Flutter)**:
   - Hoàn thiện giao diện hiển thị lịch làm việc phân ca theo dạng Weekly Timeline trực quan.
   - Tích hợp thông báo đẩy (Firebase Cloud Messaging - FCM) nhắc nhở nhân viên trước ca trực 15 phút.
   - Hỗ trợ lưu trữ lịch sử chấm công offline và đồng bộ tự động khi có kết nối mạng.
2. **Phân hệ Backend & Web Admin (.NET 10)**:
   - Module Báo cáo tổng hợp: Xuất bảng công, tổng giờ làm thực tế ra file Excel/PDF phục vụ tính lương.
   - Xây dựng quy trình phê duyệt đơn xin nghỉ phép, xin đi muộn và đổi ca trực tuyến cho Quản lý cửa hàng.
   - Tích hợp kết nối WebSocket / SignalR để Dashboard Admin cập nhật trạng thái ra/vào của nhân viên theo thời gian thực (Real-time).

---

*Tài liệu được trích xuất và tổng hợp chuẩn hóa từ báo cáo kiến trúc hệ thống Retail365 (Manage365).*
