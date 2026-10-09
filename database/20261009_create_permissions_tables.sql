-- Migration: Tạo bảng quyen (permissions) và vai_tro_quyen (role_permissions)
-- Ngày thực hiện: 2026-10-09

BEGIN;

CREATE SEQUENCE IF NOT EXISTS public.quyen_id_seq;

CREATE TABLE IF NOT EXISTS public.quyen (
    id BIGINT PRIMARY KEY DEFAULT nextval('public.quyen_id_seq'),
    ma_quyen VARCHAR(100) NOT NULL UNIQUE,
    ten_quyen VARCHAR(200) NOT NULL,
    nhom_quyen VARCHAR(100) NOT NULL DEFAULT 'System',
    mo_ta TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.vai_tro_quyen (
    id_vai_tro BIGINT NOT NULL REFERENCES public.vai_tro(id) ON DELETE CASCADE,
    id_quyen BIGINT NOT NULL REFERENCES public.quyen(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (id_vai_tro, id_quyen)
);

CREATE INDEX IF NOT EXISTS ix_quyen_nhom ON public.quyen(nhom_quyen);
CREATE INDEX IF NOT EXISTS ix_vai_tro_quyen_vai_tro ON public.vai_tro_quyen(id_vai_tro);

-- Chèn dữ liệu quyền mẫu (Seed permissions)
INSERT INTO public.quyen (id, ma_quyen, ten_quyen, nhom_quyen, mo_ta, created_at, updated_at)
VALUES
    -- Nhóm Quản lý Nhân viên
    (nextval('public.quyen_id_seq'), 'USER_VIEW', 'Xem danh sách và chi tiết nhân viên', 'Users', 'Quyền xem thông tin hồ sơ nhân viên', NOW(), NOW()),
    (nextval('public.quyen_id_seq'), 'USER_MANAGE', 'Quản lý nhân viên (Thêm/Sửa/Xóa/Khóa)', 'Users', 'Toàn quyền thêm, chỉnh sửa, vô hiệu hóa tài khoản nhân sự', NOW(), NOW()),

    -- Nhóm Quản lý Vai trò & Phân quyền
    (nextval('public.quyen_id_seq'), 'ROLE_VIEW', 'Xem danh sách vai trò', 'Roles', 'Xem danh sách các vai trò hệ thống', NOW(), NOW()),
    (nextval('public.quyen_id_seq'), 'ROLE_MANAGE', 'Quản lý vai trò & cấp quyền', 'Roles', 'Thêm sửa xóa vai trò và gán quyền cho vai trò', NOW(), NOW()),

    -- Nhóm Chấm công
    (nextval('public.quyen_id_seq'), 'ATTENDANCE_CHECKIN', 'Chấm công ca làm việc', 'Attendance', 'Chấm công vào/ra ca bằng mã QR và GPS', NOW(), NOW()),
    (nextval('public.quyen_id_seq'), 'ATTENDANCE_VIEW', 'Xem lịch sử chấm công', 'Attendance', 'Xem nhật ký vào ra của bản thân hoặc cửa hàng', NOW(), NOW()),
    (nextval('public.quyen_id_seq'), 'ATTENDANCE_MANAGE', 'Quản trị ca & địa điểm chấm công', 'Attendance', 'Cấu hình tọa độ cửa hàng, duyệt ca chấm công', NOW(), NOW()),

    -- Nhóm Báo cáo
    (nextval('public.quyen_id_seq'), 'REPORT_VIEW', 'Xem báo cáo & bảng lương', 'Reports', 'Xem doanh số, thống kê chấm công và lương', NOW(), NOW()),

    -- Nhóm Cấu hình Hệ thống
    (nextval('public.quyen_id_seq'), 'SYSTEM_CONFIG', 'Cấu hình hệ thống', 'System', 'Quyền quản trị cấp cao hệ thống', NOW(), NOW())
ON CONFLICT (ma_quyen) DO NOTHING;

-- Phân quyền mặc định cho các vai trò hiện hữu:
-- 1. Admin: Đầy đủ tất cả các quyền
INSERT INTO public.vai_tro_quyen (id_vai_tro, id_quyen, created_at)
SELECT vt.id, q.id, NOW()
FROM public.vai_tro vt
CROSS JOIN public.quyen q
WHERE LOWER(vt.ten_vai_tro) = 'admin'
ON CONFLICT (id_vai_tro, id_quyen) DO NOTHING;

-- 2. Manager: Các quyền xem, quản lý ca, chấm công và báo cáo
INSERT INTO public.vai_tro_quyen (id_vai_tro, id_quyen, created_at)
SELECT vt.id, q.id, NOW()
FROM public.vai_tro vt
JOIN public.quyen q ON q.ma_quyen IN ('USER_VIEW', 'ROLE_VIEW', 'ATTENDANCE_CHECKIN', 'ATTENDANCE_VIEW', 'ATTENDANCE_MANAGE', 'REPORT_VIEW')
WHERE LOWER(vt.ten_vai_tro) = 'manager'
ON CONFLICT (id_vai_tro, id_quyen) DO NOTHING;

-- 3. Employee: Quyền chấm công và xem nhật ký của chính mình
INSERT INTO public.vai_tro_quyen (id_vai_tro, id_quyen, created_at)
SELECT vt.id, q.id, NOW()
FROM public.vai_tro vt
JOIN public.quyen q ON q.ma_quyen IN ('ATTENDANCE_CHECKIN', 'ATTENDANCE_VIEW')
WHERE LOWER(vt.ten_vai_tro) = 'employee'
ON CONFLICT (id_vai_tro, id_quyen) DO NOTHING;

COMMIT;
