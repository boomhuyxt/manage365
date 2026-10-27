-- Restore the legacy restriction only if there are no duplicate shift names
-- on a day. Existing allowed shared shifts make this rollback intentionally fail.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'public.ca_lam'::regclass AND conname = 'uq_ca_lam_ngay_loai'
    ) OR EXISTS (
        SELECT 1 FROM pg_index
        WHERE indexrelid = to_regclass('public.uq_ca_lam_ngay_loai')
    ) THEN RETURN; END IF;

    IF EXISTS (
        SELECT 1 FROM public.ca_lam
        GROUP BY ngay_lam, loai_ca
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot restore uq_ca_lam_ngay_loai: shared shift assignments already exist.';
    END IF;

    ALTER TABLE public.ca_lam ADD CONSTRAINT uq_ca_lam_ngay_loai UNIQUE (ngay_lam, loai_ca);
END $$;

COMMIT;
