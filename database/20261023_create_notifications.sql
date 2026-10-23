CREATE TABLE IF NOT EXISTS public.notifications (
    id BIGSERIAL PRIMARY KEY,
    employee_id BIGINT NOT NULL REFERENCES public.nhan_vien(id) ON DELETE CASCADE,
    type VARCHAR(80) NOT NULL,
    title VARCHAR(200) NOT NULL,
    message VARCHAR(1000) NOT NULL,
    data JSONB NULL,
    idempotency_key VARCHAR(160) NOT NULL UNIQUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    read_at_utc TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS ix_notifications_employee_created
    ON public.notifications(employee_id, created_at_utc DESC, id DESC);

CREATE INDEX IF NOT EXISTS ix_notifications_employee_unread
    ON public.notifications(employee_id, created_at_utc DESC)
    WHERE read_at_utc IS NULL;
