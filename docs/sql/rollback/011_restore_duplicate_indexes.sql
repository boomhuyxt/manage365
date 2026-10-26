-- Optional rollback: restores redundant indexes only; no data changes.
BEGIN;
SET LOCAL lock_timeout='5s';
CREATE INDEX IF NOT EXISTS idx_ca_lam_ngay ON public.ca_lam USING btree (ngay_lam);
CREATE INDEX IF NOT EXISTS idx_cham_cong_dang_ky ON public.cham_cong USING btree (id_dang_ky_ca);
CREATE INDEX IF NOT EXISTS idx_dang_ky_ca_ca_lam ON public.dang_ky_ca USING btree (id_ca_lam);
CREATE INDEX IF NOT EXISTS ix_user_refresh_tokens_token_hash ON public.user_refresh_tokens USING btree (token_hash);
CREATE INDEX IF NOT EXISTS ix_vi_tri_cham_cong_attendance ON public.vi_tri_cham_cong USING btree (id_cham_cong, hanh_dong);
COMMIT;
