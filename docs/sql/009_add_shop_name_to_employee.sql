-- Stores the display name of the shop assigned to each Shop Owner account.
-- Safe to run repeatedly on PostgreSQL.
ALTER TABLE public.nhan_vien
    ADD COLUMN IF NOT EXISTS ten_cua_hang VARCHAR(200);

COMMENT ON COLUMN public.nhan_vien.ten_cua_hang IS
    'Shop display name shown in the header for accounts with the Manager role.';
