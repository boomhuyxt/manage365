BEGIN;
-- A shop currently belongs to one Manager account. Never infer historical ownership
-- from employee name, department, or old kiosk bindings.
ALTER TABLE public.dang_ky_ca ADD COLUMN IF NOT EXISTS shop_owner_id BIGINT NULL
    REFERENCES public.nhan_vien(id) ON DELETE RESTRICT;
CREATE INDEX IF NOT EXISTS ix_dang_ky_ca_shop_employee
    ON public.dang_ky_ca(shop_owner_id, id_nhan_vien, id_ca_lam)
    WHERE COALESCE(trang_thai, 'Approved') IN ('Approved', 'Pending');

-- Current/overnight assignments, plus an open attendance that still needs checkout.
-- Diagnostics in ble_scanner_employees no longer grant attendance permission.
CREATE OR REPLACE VIEW public.ble_scheduled_employees AS
SELECT DISTINCT s.scanner_id, d.id_nhan_vien AS employee_id, d.id AS assignment_id
FROM public.ble_scanners s
JOIN public.dang_ky_ca d ON d.shop_owner_id = s.owner_employee_id
JOIN public.ca_lam cl ON cl.id = d.id_ca_lam
JOIN public.nhan_vien employee ON employee.id = d.id_nhan_vien
JOIN public.nhan_vien owner ON owner.id = s.owner_employee_id
WHERE COALESCE(d.trang_thai, 'Approved') IN ('Approved', 'Pending')
  AND COALESCE(employee.trang_thai, 'Active') = 'Active'
  AND COALESCE(owner.trang_thai, 'Active') = 'Active'
  AND (cl.ngay_lam = (now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date
       OR (cl.qua_dem AND cl.ngay_lam = (now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date - 1)
       OR EXISTS (SELECT 1 FROM public.cham_cong cc WHERE cc.id_dang_ky_ca=d.id
                  AND cc.gio_vao IS NOT NULL AND cc.gio_ra IS NULL));
COMMIT;
