-- Migration: Tạo bảng phong_ban, cong_cu_phong_ban và liên kết nhan_vien
-- Ngày thực hiện: 2026-10-09

BEGIN;

CREATE SEQUENCE IF NOT EXISTS public.phong_ban_id_seq;

CREATE TABLE IF NOT EXISTS public.phong_ban (
    id BIGINT PRIMARY KEY DEFAULT nextval('public.phong_ban_id_seq'),
    ma_phong_ban VARCHAR(50) NOT NULL UNIQUE,
    ten_phong_ban VARCHAR(150) NOT NULL,
    mo_ta TEXT NULL,
    trang_thai VARCHAR(50) NOT NULL DEFAULT 'Active',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE SEQUENCE IF NOT EXISTS public.cong_cu_phong_ban_id_seq;

CREATE TABLE IF NOT EXISTS public.cong_cu_phong_ban (
    id BIGINT PRIMARY KEY DEFAULT nextval('public.cong_cu_phong_ban_id_seq'),
    id_phong_ban BIGINT NOT NULL REFERENCES public.phong_ban(id) ON DELETE CASCADE,
    ma_cong_cu VARCHAR(100) NOT NULL UNIQUE,
    ten_cong_cu VARCHAR(200) NOT NULL,
    icon VARCHAR(100) NULL,
    route_path VARCHAR(200) NULL,
    thu_tu_hien_thi INT NOT NULL DEFAULT 0,
    trang_thai VARCHAR(50) NOT NULL DEFAULT 'Active',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Thêm cột id_phong_ban vào bảng nhan_vien nếu chưa có
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'nhan_vien'
          AND column_name = 'id_phong_ban'
    ) THEN
        ALTER TABLE public.nhan_vien
            ADD COLUMN id_phong_ban BIGINT NULL REFERENCES public.phong_ban(id) ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_cong_cu_phong_ban ON public.cong_cu_phong_ban(id_phong_ban);
CREATE INDEX IF NOT EXISTS ix_nhan_vien_phong_ban ON public.nhan_vien(id_phong_ban);

-- Seed dữ liệu phòng ban
INSERT INTO public.phong_ban (id, ma_phong_ban, ten_phong_ban, mo_ta, trang_thai, created_at, updated_at)
VALUES
    (nextval('public.phong_ban_id_seq'), 'WAREHOUSE', 'Bộ phận Kho vận', 'Quản lý kho hàng, xuất nhập và kiểm kê tồn kho', 'Active', NOW(), NOW()),
    (nextval('public.phong_ban_id_seq'), 'POS_SALES', 'Bộ phận Bán hàng / Thu ngân', 'Bán hàng quầy POS, in hóa đơn và thanh toán', 'Active', NOW(), NOW()),
    (nextval('public.phong_ban_id_seq'), 'HR_ADMIN', 'Bộ phận Nhân sự & Quản trị', 'Xếp ca làm việc, chấm công và tính lương', 'Active', NOW(), NOW())
ON CONFLICT (ma_phong_ban) DO NOTHING;

-- Seed dữ liệu công cụ theo phòng ban
-- 1. Kho vận
INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_STOCK_IN', 'Nhập kho hàng hóa', 'move_to_inbox', '/warehouse/stock-in', 1, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'WAREHOUSE'
ON CONFLICT (ma_cong_cu) DO NOTHING;

INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_STOCK_OUT', 'Xuất kho & Điều chuyển', 'outbox', '/warehouse/stock-out', 2, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'WAREHOUSE'
ON CONFLICT (ma_cong_cu) DO NOTHING;

INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_INVENTORY_AUDIT', 'Kiểm kê & Soát tồn', 'fact_check', '/warehouse/inventory-audit', 3, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'WAREHOUSE'
ON CONFLICT (ma_cong_cu) DO NOTHING;

INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_BARCODE_SCAN', 'Quét mã vạch kiểm tra', 'qr_code_scanner', '/warehouse/barcode-scan', 4, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'WAREHOUSE'
ON CONFLICT (ma_cong_cu) DO NOTHING;

-- 2. Bán hàng / Thu ngân
INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_POS_CHECKOUT', 'Quầy bán hàng POS', 'point_of_sale', '/pos/checkout', 1, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'POS_SALES'
ON CONFLICT (ma_cong_cu) DO NOTHING;

INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_INVOICE_HISTORY', 'Lịch sử & In lại hóa đơn', 'receipt_long', '/pos/invoices', 2, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'POS_SALES'
ON CONFLICT (ma_cong_cu) DO NOTHING;

INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_PROMOTION_LOOKUP', 'Tra cứu khuyến mãi', 'loyalty', '/pos/promotions', 3, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'POS_SALES'
ON CONFLICT (ma_cong_cu) DO NOTHING;

INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_SHIFT_CASH_CLOSING', 'Chốt ca két tiền', 'payments', '/pos/cash-closing', 4, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'POS_SALES'
ON CONFLICT (ma_cong_cu) DO NOTHING;

-- 3. Nhân sự & Quản trị
INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_ATTENDANCE_KIOSK', 'Quản lý chấm công & QR', 'access_time', '/attendance/manage', 1, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'HR_ADMIN'
ON CONFLICT (ma_cong_cu) DO NOTHING;

INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_SHIFT_SCHEDULING', 'Xếp lịch phân ca', 'calendar_month', '/schedule/manage', 2, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'HR_ADMIN'
ON CONFLICT (ma_cong_cu) DO NOTHING;

INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_EMPLOYEE_LIST', 'Hồ sơ nhân viên', 'badge', '/employees/list', 3, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'HR_ADMIN'
ON CONFLICT (ma_cong_cu) DO NOTHING;

INSERT INTO public.cong_cu_phong_ban (id, id_phong_ban, ma_cong_cu, ten_cong_cu, icon, route_path, thu_tu_hien_thi, trang_thai, created_at, updated_at)
SELECT nextval('public.cong_cu_phong_ban_id_seq'), pb.id, 'TOOL_PAYROLL_REPORT', 'Báo cáo lương', 'attach_money', '/payroll/summary', 4, 'Active', NOW(), NOW()
FROM public.phong_ban pb WHERE pb.ma_phong_ban = 'HR_ADMIN'
ON CONFLICT (ma_cong_cu) DO NOTHING;

-- Tự động gán nhân viên chưa có phòng ban vào phòng ban mặc định
UPDATE public.nhan_vien nv
SET id_phong_ban = (SELECT id FROM public.phong_ban WHERE ma_phong_ban = 'POS_SALES' LIMIT 1)
WHERE nv.id_phong_ban IS NULL;

COMMIT;
