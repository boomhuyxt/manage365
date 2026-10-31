"""Boundary checks for migration 016; rolled back on the isolated local clone."""
import os, pathlib, subprocess

env = os.environ.copy()
env.update(PGHOST="127.0.0.1", PGPORT="55439", PGUSER="audit_owner",
           PGPASSWORD="", PGDATABASE="manage365_ble_shop_test", PGSSLMODE="disable")
query = """
BEGIN;
INSERT INTO ca_lam(id,ngay_lam,loai_ca,gio_bat_dau,gio_ket_thuc,qua_dem)
VALUES (900030,(now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date-1,'Overnight','22:00','06:00',true),
       (900031,(now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date+1,'Tomorrow','08:00','17:00',false);
INSERT INTO dang_ky_ca(id,id_ca_lam,id_nhan_vien,trang_thai,shop_owner_id)
VALUES (900030,900030,900008,'Approved',900001), (900031,900031,900008,'Approved',900001);
DO $$ BEGIN
  IF NOT EXISTS(SELECT 1 FROM ble_scheduled_employees WHERE scanner_id='BLE-TEST-OWNER' AND assignment_id=900030)
    THEN RAISE EXCEPTION 'Yesterday overnight shift must be a candidate'; END IF;
  IF EXISTS(SELECT 1 FROM ble_scheduled_employees WHERE assignment_id=900031)
    THEN RAISE EXCEPTION 'Tomorrow shift must not grant access today'; END IF;
  UPDATE dang_ky_ca SET trang_thai='Cancelled' WHERE id=900030;
  IF EXISTS(SELECT 1 FROM ble_scheduled_employees WHERE assignment_id=900030)
    THEN RAISE EXCEPTION 'Cancelled shift must not grant access'; END IF;
  UPDATE dang_ky_ca SET trang_thai='Approved',shop_owner_id=900002 WHERE id=900030;
  IF EXISTS(SELECT 1 FROM ble_scheduled_employees WHERE scanner_id='BLE-TEST-OWNER' AND assignment_id=900030)
    THEN RAISE EXCEPTION 'Other shop shift must not grant access'; END IF;
  UPDATE dang_ky_ca SET shop_owner_id=900001 WHERE id=900030;
  UPDATE nhan_vien SET trang_thai='Inactive' WHERE id=900008;
  IF EXISTS(SELECT 1 FROM ble_scheduled_employees WHERE assignment_id=900030)
    THEN RAISE EXCEPTION 'Locked employee must not grant access'; END IF;
  UPDATE nhan_vien SET trang_thai='Active' WHERE id=900008;
  UPDATE nhan_vien SET trang_thai='Inactive' WHERE id=900001;
  IF EXISTS(SELECT 1 FROM ble_scheduled_employees WHERE scanner_id='BLE-TEST-OWNER')
    THEN RAISE EXCEPTION 'Locked shop owner must not grant access'; END IF;
END $$;
ROLLBACK;
"""
result = subprocess.run([r"C:\Program Files\PostgreSQL\18\bin\psql.exe", "-X", "-q", "-v", "ON_ERROR_STOP=1"],
                        input=query, text=True, encoding="utf-8", capture_output=True, env=env, timeout=30)
if result.returncode:
    raise RuntimeError(result.stderr)
print("PASS: six SQL scope boundaries (overnight, tomorrow, cancelled, other shop, employee locked, owner locked); rolled back.")
