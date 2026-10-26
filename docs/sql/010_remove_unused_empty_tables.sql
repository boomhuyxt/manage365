-- Reviewed against both Manage365 and retail-management-app, 2026-10-26.
-- REQUIRED: back up public schema/data and review docs/database-optimization-report.md.
-- These legacy modules have no current application consumers. Abort if data or
-- new dependencies appeared since the audit. Never use CASCADE here.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

DO $audit$
DECLARE
    names text[] := ARRAY['bang_luong','bao_hiem','chi_tiet_hoa_don',
        'don_dat_hang','hoa_don','khuyen_mai','loai_san_pham','nhap_kho','san_pham'];
    routines text[] := ARRAY['check_bao_hiem_fulltime','tinh_lai_hoa_don',
        'trg_fn_cap_nhat_tong_hoa_don','trg_fn_hoa_don_doi_khuyen_mai'];
    item text;
    has_rows boolean;
BEGIN
    FOREACH item IN ARRAY names LOOP
        IF to_regclass(format('public.%I', item)) IS NOT NULL THEN
            EXECUTE format('LOCK TABLE public.%I IN ACCESS EXCLUSIVE MODE', item);
            EXECUTE format('SELECT EXISTS (SELECT 1 FROM public.%I)', item) INTO has_rows;
            IF has_rows THEN
                RAISE EXCEPTION 'Refusing to drop public.%: table contains data', item;
            END IF;
        END IF;
    END LOOP;

    IF EXISTS (
        SELECT 1 FROM pg_constraint fk
        JOIN pg_class target ON target.oid = fk.confrelid
        JOIN pg_class source ON source.oid = fk.conrelid
        WHERE fk.contype = 'f' AND target.relnamespace = 'public'::regnamespace
          AND target.relname = ANY(names)
          AND NOT (source.relnamespace = 'public'::regnamespace AND source.relname = ANY(names))
    ) THEN
        RAISE EXCEPTION 'A retained table references a removal candidate';
    END IF;

    -- SQL in PL/pgSQL strings is not always represented by pg_depend.
    IF EXISTS (
        SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE p.prokind IN ('f','p') AND n.nspname NOT IN ('pg_catalog','information_schema')
          AND NOT (n.nspname = 'public' AND p.proname = ANY(routines))
          AND EXISTS (SELECT 1 FROM unnest(names || routines) x(name)
                      WHERE p.prosrc ~* ('\m' || x.name || '\M'))
    ) THEN
        RAISE EXCEPTION 'Another database routine references a removal candidate';
    END IF;
END;
$audit$;

-- One statement allows dependencies within this explicitly reviewed group.
DROP TABLE IF EXISTS public.chi_tiet_hoa_don, public.hoa_don, public.nhap_kho,
    public.san_pham, public.loai_san_pham, public.khuyen_mai,
    public.don_dat_hang, public.bang_luong, public.bao_hiem RESTRICT;

DROP FUNCTION IF EXISTS public.trg_fn_cap_nhat_tong_hoa_don() RESTRICT;
DROP FUNCTION IF EXISTS public.trg_fn_hoa_don_doi_khuyen_mai() RESTRICT;
DROP FUNCTION IF EXISTS public.tinh_lai_hoa_don(bigint) RESTRICT;
DROP FUNCTION IF EXISTS public.check_bao_hiem_fulltime() RESTRICT;
-- public.set_updated_at() is shared by active tables and MUST be retained.
COMMIT;
