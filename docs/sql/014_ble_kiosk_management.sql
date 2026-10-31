-- Apply after 013. Additive migration: no attendance data is removed.
ALTER TABLE public.ble_scanners ADD COLUMN IF NOT EXISTS owner_employee_id bigint REFERENCES public.nhan_vien(id);
ALTER TABLE public.ble_scanners ADD COLUMN IF NOT EXISTS scanner_state varchar(24) NOT NULL DEFAULT 'unknown';
ALTER TABLE public.ble_scanners ADD COLUMN IF NOT EXISTS scanner_message varchar(300);
ALTER TABLE public.ble_scanner_employees ADD COLUMN IF NOT EXISTS last_observed_at timestamptz;
ALTER TABLE public.ble_scanner_employees ADD COLUMN IF NOT EXISTS last_rssi_dbm smallint;
ALTER TABLE public.ble_scanner_employees ADD COLUMN IF NOT EXISTS last_result_code varchar(80);
ALTER TABLE public.ble_scanner_employees ADD COLUMN IF NOT EXISTS last_result_message varchar(300);
CREATE INDEX IF NOT EXISTS ix_ble_scanners_owner ON public.ble_scanners(owner_employee_id);

-- The existing attendance constraint only accepts QR/NFC; allow BLE as well.
ALTER TABLE public.cham_cong DROP CONSTRAINT IF EXISTS cham_cong_phuong_thuc_cham_cong_check;
ALTER TABLE public.cham_cong ADD CONSTRAINT cham_cong_phuong_thuc_cham_cong_check
    CHECK (phuong_thuc_cham_cong IN ('QR', 'NFC', 'BLE'));
