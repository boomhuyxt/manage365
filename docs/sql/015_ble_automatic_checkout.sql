-- Records why a BLE checkout was made. Existing/manual evidence remains unchanged.
ALTER TABLE public.ble_attendance_evidence
    ADD COLUMN IF NOT EXISTS checkout_source varchar(32) NOT NULL DEFAULT 'employee';

ALTER TABLE public.ble_attendance_evidence
    DROP CONSTRAINT IF EXISTS ble_attendance_evidence_checkout_source_check;
ALTER TABLE public.ble_attendance_evidence
    ADD CONSTRAINT ble_attendance_evidence_checkout_source_check
    CHECK (checkout_source IN ('employee', 'beacon_absence'));

CREATE INDEX IF NOT EXISTS ix_ble_auto_checkout_open_attendance
    ON public.cham_cong(updated_at)
    WHERE phuong_thuc_cham_cong='BLE' AND gio_vao IS NOT NULL AND gio_ra IS NULL;
