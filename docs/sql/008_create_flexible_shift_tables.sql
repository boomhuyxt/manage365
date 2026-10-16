-- Migration 008: Tạo bảng mẫu ca làm (mau_ca_lam) và nâng cấp cấu trúc phân ca linh động
-- Ngày thực hiện: 2026-10-10

BEGIN;

CREATE SEQUENCE IF NOT EXISTS public.mau_ca_lam_id_seq;

CREATE TABLE IF NOT EXISTS public.mau_ca_lam (
    id BIGINT PRIMARY KEY DEFAULT nextval('public.mau_ca_lam_id_seq'),
    ma_mau_ca VARCHAR(50) NOT NULL UNIQUE,
    ten_mau_ca VARCHAR(150) NOT NULL,
    gio_bat_dau TIME NOT NULL,
    gio_ket_thuc TIME NOT NULL,
    qua_dem BOOLEAN NOT NULL DEFAULT FALSE,
    mau_sac VARCHAR(30) NOT NULL DEFAULT '#f97316',
    mo_ta TEXT NULL,
    trang_thai VARCHAR(50) NOT NULL DEFAULT 'Active',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE SEQUENCE IF NOT EXISTS public.ca_lam_id_seq;

CREATE TABLE IF NOT EXISTS public.ca_lam (
    id BIGINT PRIMARY KEY DEFAULT nextval('public.ca_lam_id_seq'),
    ngay_lam DATE NOT NULL,
    loai_ca VARCHAR(150) NOT NULL,
    gio_bat_dau TIME NOT NULL,
    gio_ket_thuc TIME NOT NULL,
    qua_dem BOOLEAN NOT NULL DEFAULT FALSE,
    id_mau_ca BIGINT NULL REFERENCES public.mau_ca_lam(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE SEQUENCE IF NOT EXISTS public.dang_ky_ca_id_seq;

CREATE TABLE IF NOT EXISTS public.dang_ky_ca (
    id BIGINT PRIMARY KEY DEFAULT nextval('public.dang_ky_ca_id_seq'),
    id_ca_lam BIGINT NOT NULL REFERENCES public.ca_lam(id) ON DELETE CASCADE,
    id_nhan_vien BIGINT NOT NULL REFERENCES public.nhan_vien(id) ON DELETE CASCADE,
    trang_thai VARCHAR(50) NOT NULL DEFAULT 'Approved',
    ghi_chu TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ux_dang_ky_ca_ca_nhan_vien UNIQUE (id_ca_lam, id_nhan_vien)
);

-- Bổ sung cột nếu bảng đã tồn tại từ trước
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = 'public' AND table_name = 'ca_lam' AND column_name = 'id_mau_ca'
    ) THEN
        ALTER TABLE public.ca_lam ADD COLUMN id_mau_ca BIGINT NULL REFERENCES public.mau_ca_lam(id) ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = 'public' AND table_name = 'dang_ky_ca' AND column_name = 'ghi_chu'
    ) THEN
        ALTER TABLE public.dang_ky_ca ADD COLUMN ghi_chu TEXT NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = 'public' AND table_name = 'dang_ky_ca' AND column_name = 'created_at'
    ) THEN
        ALTER TABLE public.dang_ky_ca ADD COLUMN created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = 'public' AND table_name = 'dang_ky_ca' AND column_name = 'updated_at'
    ) THEN
        ALTER TABLE public.dang_ky_ca ADD COLUMN updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
    END IF;
END $$;

-- Các index hỗ trợ truy vấn lịch tuần, tháng và kiểm tra trùng ca
CREATE INDEX IF NOT EXISTS ix_ca_lam_ngay_lam ON public.ca_lam(ngay_lam);
CREATE INDEX IF NOT EXISTS ix_ca_lam_mau_ca ON public.ca_lam(id_mau_ca);
CREATE INDEX IF NOT EXISTS ix_dang_ky_ca_nhan_vien_trang_thai ON public.dang_ky_ca(id_nhan_vien, trang_thai);
CREATE INDEX IF NOT EXISTS ix_dang_ky_ca_ca_lam ON public.dang_ky_ca(id_ca_lam);

-- Seed các mẫu ca chuẩn cho chuỗi bán lẻ
INSERT INTO public.mau_ca_lam (id, ma_mau_ca, ten_mau_ca, gio_bat_dau, gio_ket_thuc, qua_dem, mau_sac, mo_ta, trang_thai)
VALUES
    (nextval('public.mau_ca_lam_id_seq'), 'CA_SANG', 'Ca Sáng (06:00 - 14:00)', '06:00:00', '14:00:00', FALSE, '#f59e0b', 'Ca sáng tiêu chuẩn phục vụ mở cửa và bán hàng', 'Active'),
    (nextval('public.mau_ca_lam_id_seq'), 'CA_CHIEU', 'Ca Chiều (14:00 - 22:00)', '14:00:00', '22:00:00', FALSE, '#3b82f6', 'Ca chiều tiêu chuẩn bán hàng và chốt doanh thu ngày', 'Active'),
    (nextval('public.mau_ca_lam_id_seq'), 'CA_DEM', 'Ca Đêm (22:00 - 06:00)', '22:00:00', '06:00:00', TRUE, '#8b5cf6', 'Ca đêm trực quầy 24/7 và nhập kho sáng sớm (+30% phụ cấp)', 'Active'),
    (nextval('public.mau_ca_lam_id_seq'), 'CA_PARTTIME_TRUA', 'Part-time Trưa (10:30 - 14:30)', '10:30:00', '14:30:00', FALSE, '#10b981', 'Ca tăng cường giờ cao điểm ăn trưa', 'Active'),
    (nextval('public.mau_ca_lam_id_seq'), 'CA_PARTTIME_TOI', 'Part-time Tối (17:30 - 21:30)', '17:30:00', '21:30:00', FALSE, '#ec4899', 'Ca tăng cường giờ cao điểm mua sắm buổi tối', 'Active')
ON CONFLICT (ma_mau_ca) DO NOTHING;

COMMIT;
