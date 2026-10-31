-- BLE attendance support. QR attendance tables and rows remain unchanged.
CREATE TABLE IF NOT EXISTS public.ble_scanners (
    scanner_id text PRIMARY KEY,
    store_code varchar(64) NOT NULL,
    scanner_token_hash char(64) NOT NULL UNIQUE,
    is_active boolean NOT NULL DEFAULT true,
    last_seen_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS public.ble_presence_credentials (
    id uuid PRIMARY KEY,
    employee_id bigint NOT NULL REFERENCES public.nhan_vien(id) ON DELETE CASCADE,
    device_id varchar(128) NOT NULL,
    token_hash char(64) NOT NULL UNIQUE,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_ble_presence_employee_expiry
    ON public.ble_presence_credentials(employee_id, expires_at DESC)
    WHERE revoked_at IS NULL;

-- Explicit binding prevents a valid employee token from being accepted by a kiosk belonging to another shop.
CREATE TABLE IF NOT EXISTS public.ble_scanner_employees (
    scanner_id text NOT NULL REFERENCES public.ble_scanners(scanner_id) ON DELETE CASCADE,
    employee_id bigint NOT NULL REFERENCES public.nhan_vien(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(scanner_id, employee_id)
);

CREATE TABLE IF NOT EXISTS public.ble_presence_observations (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    credential_id uuid NOT NULL REFERENCES public.ble_presence_credentials(id) ON DELETE CASCADE,
    scanner_id text NOT NULL REFERENCES public.ble_scanners(scanner_id) ON DELETE RESTRICT,
    batch_id uuid NOT NULL,
    sequence bigint NOT NULL,
    observed_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    rssi_dbm smallint NOT NULL CHECK (rssi_dbm BETWEEN -127 AND 20),
    UNIQUE(scanner_id, batch_id, sequence)
);
CREATE INDEX IF NOT EXISTS ix_ble_observations_presence_window
    ON public.ble_presence_observations(credential_id, scanner_id, received_at DESC);

CREATE TABLE IF NOT EXISTS public.ble_attendance_evidence (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    attendance_id bigint NOT NULL REFERENCES public.cham_cong(id) ON DELETE CASCADE,
    action varchar(16) NOT NULL CHECK (action IN ('CHECK_IN', 'CHECK_OUT')),
    scanner_id text NOT NULL REFERENCES public.ble_scanners(scanner_id) ON DELETE RESTRICT,
    credential_id uuid NOT NULL REFERENCES public.ble_presence_credentials(id) ON DELETE RESTRICT,
    observed_at timestamptz NOT NULL,
    stable_presence_seconds integer NOT NULL CHECK (stable_presence_seconds >= 0),
    median_rssi_dbm smallint NOT NULL,
    idempotency_key uuid UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(attendance_id, action)
);
CREATE INDEX IF NOT EXISTS ix_ble_evidence_scanner_created
    ON public.ble_attendance_evidence(scanner_id, created_at DESC);
