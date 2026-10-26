-- Remove only identical non-unique access paths; preserve unique constraints.
-- Compare catalog definitions at execution time to reject schema drift.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
DO $audit$
DECLARE pair record;
BEGIN
    FOR pair IN SELECT * FROM (VALUES
        ('idx_ca_lam_ngay','ix_ca_lam_ngay_lam'),
        ('idx_dang_ky_ca_ca_lam','ix_dang_ky_ca_ca_lam'),
        ('idx_cham_cong_dang_ky','uq_cham_cong_dang_ky'),
        ('ix_user_refresh_tokens_token_hash','user_refresh_tokens_token_hash_key'),
        ('ix_vi_tri_cham_cong_attendance','ux_vi_tri_cham_cong_action')
    ) AS pairs(remove_name, keep_name) LOOP
        IF to_regclass(format('public.%I', pair.remove_name)) IS NULL THEN CONTINUE; END IF;
        IF NOT EXISTS (
            SELECT 1 FROM pg_index old JOIN pg_index kept ON old.indrelid = kept.indrelid
            JOIN pg_class oc ON oc.oid=old.indexrelid
            JOIN pg_class kc ON kc.oid=kept.indexrelid
            WHERE old.indexrelid = to_regclass(format('public.%I', pair.remove_name))
              AND kept.indexrelid = to_regclass(format('public.%I', pair.keep_name))
              AND NOT old.indisunique AND NOT old.indisprimary
              AND NOT old.indisreplident AND NOT old.indisclustered
              AND kept.indisvalid AND kept.indisready AND kept.indislive
              AND oc.relam = kc.relam AND old.indnatts = kept.indnatts
              AND old.indnkeyatts = kept.indnkeyatts AND old.indkey = kept.indkey
              AND old.indclass = kept.indclass AND old.indcollation = kept.indcollation
              AND old.indoption = kept.indoption
              AND old.indexprs::text IS NOT DISTINCT FROM kept.indexprs::text
              AND old.indpred::text IS NOT DISTINCT FROM kept.indpred::text
              AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conindid=old.indexrelid)
        ) THEN
            RAISE EXCEPTION 'Index % is not safely covered by %', pair.remove_name, pair.keep_name;
        END IF;
        EXECUTE format('DROP INDEX public.%I RESTRICT', pair.remove_name);
    END LOOP;
END;
$audit$;
COMMIT;
