BEGIN;
-- A resumed/healthy scanner must see the employee again before inferring absence.
ALTER TABLE public.ble_scanners ADD COLUMN IF NOT EXISTS scan_started_at TIMESTAMPTZ NULL;
COMMIT;
