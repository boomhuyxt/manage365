-- The old global unique index prevented multiple employees sharing a shift
-- name/date and left cancelled rows blocking replacement assignments.
-- Same-employee overlapping shifts remain checked by the scheduling API.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

DO $$
DECLARE legacy_index OID := to_regclass('public.uq_ca_lam_ngay_loai');
DECLARE legacy_constraint TEXT;
BEGIN
    SELECT conname INTO legacy_constraint
    FROM pg_constraint
    WHERE conrelid = 'public.ca_lam'::regclass
      AND conname = 'uq_ca_lam_ngay_loai';

    IF legacy_constraint IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conrelid = 'public.ca_lam'::regclass
              AND conname = legacy_constraint
              AND contype = 'u'
              AND pg_get_constraintdef(oid) = 'UNIQUE (ngay_lam, loai_ca)'
        ) THEN
            RAISE EXCEPTION 'Constraint uq_ca_lam_ngay_loai has an unexpected definition; no change was made.';
        END IF;

        EXECUTE format('ALTER TABLE public.ca_lam DROP CONSTRAINT %I', legacy_constraint);
        RETURN;
    END IF;

    IF legacy_index IS NULL THEN RETURN; END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_index i
        WHERE i.indexrelid = legacy_index
          AND i.indrelid = 'public.ca_lam'::regclass
          AND i.indisunique AND i.indisvalid
          AND i.indnatts = 2 AND i.indnkeyatts = 2
          AND i.indexprs IS NULL AND i.indpred IS NULL
          AND ARRAY(
              SELECT a.attname
              FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum, position)
              JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
              ORDER BY k.position
          ) = ARRAY['ngay_lam', 'loai_ca']::name[]
    ) OR EXISTS (SELECT 1 FROM pg_constraint WHERE conindid = legacy_index) THEN
        RAISE EXCEPTION 'Index uq_ca_lam_ngay_loai has an unexpected definition; no change was made.';
    END IF;

    DROP INDEX public.uq_ca_lam_ngay_loai;
END $$;

COMMIT;
