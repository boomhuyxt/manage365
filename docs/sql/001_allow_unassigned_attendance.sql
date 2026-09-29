BEGIN;

ALTER TABLE public.cham_cong
    ADD COLUMN IF NOT EXISTS id_nhan_vien BIGINT,
    ADD COLUMN IF NOT EXISTS ngay_cham_cong DATE;

UPDATE public.cham_cong AS cc
SET id_nhan_vien = dkc.id_nhan_vien,
    ngay_cham_cong = cl.ngay_lam
FROM public.dang_ky_ca AS dkc
JOIN public.ca_lam AS cl ON cl.id = dkc.id_ca_lam
WHERE cc.id_dang_ky_ca = dkc.id
  AND (cc.id_nhan_vien IS NULL OR cc.ngay_cham_cong IS NULL);

ALTER TABLE public.cham_cong
    ALTER COLUMN id_dang_ky_ca DROP NOT NULL,
    ALTER COLUMN id_nhan_vien SET NOT NULL,
    ALTER COLUMN ngay_cham_cong SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'cham_cong_id_nhan_vien_fkey'
          AND conrelid = 'public.cham_cong'::regclass
    ) THEN
        ALTER TABLE public.cham_cong
            ADD CONSTRAINT cham_cong_id_nhan_vien_fkey
            FOREIGN KEY (id_nhan_vien)
            REFERENCES public.nhan_vien(id)
            ON DELETE CASCADE;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_cham_cong_unassigned_employee_day
    ON public.cham_cong (id_nhan_vien, ngay_cham_cong)
    WHERE id_dang_ky_ca IS NULL;

CREATE INDEX IF NOT EXISTS ix_cham_cong_employee_day
    ON public.cham_cong (id_nhan_vien, ngay_cham_cong DESC);

COMMIT;