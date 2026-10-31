"""Exercise the auto-checkout SQL against only the disposable 55439 clone."""
import os, pathlib, re, subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
DB = "manage365_ble_shop_test"
PSQL = r"C:\Program Files\PostgreSQL\18\bin\psql.exe"
env = os.environ.copy()
env.update(PGHOST="127.0.0.1", PGPORT="55439", PGUSER="audit_owner",
           PGPASSWORD="", PGDATABASE=DB, PGSSLMODE="disable")
if env["PGHOST"] != "127.0.0.1" or env["PGPORT"] != "55439" or DB != "manage365_ble_shop_test":
    raise RuntimeError("Refusing to run outside the isolated BLE test database.")

def sql(statement):
    result = subprocess.run([PSQL, "-X", "-qAt", "-v", "ON_ERROR_STOP=1"],
                            input=statement, text=True, encoding="utf-8",
                            capture_output=True, env=env, timeout=30)
    if result.returncode:
        raise RuntimeError(result.stderr)
    return result.stdout.strip()

def expect(statement, value, message):
    actual = sql(statement)
    if actual != value:
        raise AssertionError(f"{message}: expected {value}, got {actual}")
    print("PASS:", message, flush=True)

sql("""
DELETE FROM ble_attendance_evidence WHERE scanner_id LIKE 'AUTO-%';
DELETE FROM ble_presence_observations WHERE scanner_id LIKE 'AUTO-%';
DELETE FROM ble_scanner_employees WHERE scanner_id LIKE 'AUTO-%';
DELETE FROM ble_scanners WHERE scanner_id LIKE 'AUTO-%';
DELETE FROM cham_cong WHERE id_nhan_vien BETWEEN 900021 AND 900023;
DELETE FROM dang_ky_ca WHERE id_nhan_vien BETWEEN 900021 AND 900023;
DELETE FROM ca_lam WHERE id=900021;
DELETE FROM nhan_vien WHERE id BETWEEN 900021 AND 900023;
INSERT INTO nhan_vien(id,id_vai_tro,ho_ten,email,mat_khau,loai_nhan_vien,trang_thai)
SELECT n,1,'BLE Auto Test '||n,'auto-'||n||'@example.invalid','test-only','FullTime','Active'
FROM generate_series(900021,900023) n;
INSERT INTO ca_lam(id,ngay_lam,loai_ca,gio_bat_dau,gio_ket_thuc)
VALUES (900021,(now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date,'Auto test','00:00','23:59');
INSERT INTO dang_ky_ca(id,id_nhan_vien,id_ca_lam,trang_thai)
VALUES (900021,900021,900021,'Approved'),(900022,900022,900021,'Approved'),(900023,900023,900021,'Approved');
UPDATE dang_ky_ca SET shop_owner_id=900021 WHERE id BETWEEN 900021 AND 900023;
INSERT INTO ble_scanners(scanner_id,store_code,scanner_token_hash,owner_employee_id,scanner_state,last_seen_at)
VALUES ('AUTO-HEALTHY','AUTO-STORE',repeat('a',64),900021,'running',now()),
       ('AUTO-BROKEN','AUTO-STORE',repeat('b',64),900021,'error',now()),
       ('AUTO-PRESENT','AUTO-STORE',repeat('c',64),900021,'running',now());
INSERT INTO ble_scanner_employees(scanner_id,employee_id)
VALUES ('AUTO-HEALTHY',900021),('AUTO-BROKEN',900022),('AUTO-PRESENT',900023);
INSERT INTO ble_presence_credentials(id,employee_id,device_id,token_hash,expires_at)
VALUES ('00000000-0000-4000-8000-000000000021',900021,'test',repeat('d',64),now()+interval '1 day'),
       ('00000000-0000-4000-8000-000000000022',900022,'test',repeat('e',64),now()+interval '1 day'),
       ('00000000-0000-4000-8000-000000000023',900023,'test',repeat('f',64),now()+interval '1 day');
INSERT INTO cham_cong(id_dang_ky_ca,id_nhan_vien,ngay_cham_cong,gio_vao,trang_thai,phuong_thuc_cham_cong,created_at,updated_at)
VALUES (900021,900021,(now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date,now()-interval '2 hours','CheckedIn','BLE',now(),now()),
       (900022,900022,(now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date,now()-interval '2 hours','CheckedIn','BLE',now(),now()),
       (900023,900023,(now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date,now()-interval '2 hours','CheckedIn','BLE',now(),now());
INSERT INTO ble_presence_observations(credential_id,scanner_id,batch_id,sequence,observed_at,received_at,rssi_dbm)
VALUES ('00000000-0000-4000-8000-000000000021','AUTO-HEALTHY','00000000-0000-4000-8000-000000000121',1,now()-interval '6 minutes',now()-interval '6 minutes',-55),
       ('00000000-0000-4000-8000-000000000022','AUTO-BROKEN','00000000-0000-4000-8000-000000000122',1,now()-interval '6 minutes',now()-interval '6 minutes',-55),
       ('00000000-0000-4000-8000-000000000023','AUTO-PRESENT','00000000-0000-4000-8000-000000000123',1,now()-interval '10 seconds',now()-interval '10 seconds',-55);
INSERT INTO ble_attendance_evidence(attendance_id,action,scanner_id,credential_id,observed_at,stable_presence_seconds,median_rssi_dbm)
SELECT cc.id,'CHECK_IN',scanner.scanner_id,cred.id,now()-interval '6 minutes',30,-55
FROM cham_cong cc JOIN ble_scanner_employees se ON se.employee_id=cc.id_nhan_vien
JOIN ble_scanners scanner ON scanner.scanner_id=se.scanner_id
JOIN ble_presence_credentials cred ON cred.employee_id=cc.id_nhan_vien
WHERE cc.id_nhan_vien BETWEEN 900021 AND 900023;
""")

source = (ROOT / "Routes/API/Attendance/BleAutoCheckoutService.cs").read_text(encoding="utf-8")
match = re.search(r'const string sql = """\s*(.*?)\s*""";', source, re.S)
if not match:
    raise RuntimeError("Could not find the production sweep query.")
sweep = match.group(1).replace("@absenceGrace", "interval '2 minutes'")
sql(sweep)
expect("SELECT count(*) FROM cham_cong WHERE id_nhan_vien=900021 AND gio_ra IS NOT NULL AND trang_thai='NeedsReview'", "1", "healthy kiosk closes only the absent employee and flags review")
expect("SELECT checkout_source FROM ble_attendance_evidence e JOIN cham_cong c ON c.id=e.attendance_id WHERE c.id_nhan_vien=900021 AND e.action='CHECK_OUT'", "beacon_absence", "automatic checkout has an auditable source")
expect("SELECT count(*) FROM cham_cong WHERE id_nhan_vien IN (900022,900023) AND gio_ra IS NULL", "2", "broken kiosk and still-present employee stay checked in")
sql(sweep)
expect("SELECT count(*) FROM ble_attendance_evidence e JOIN cham_cong c ON c.id=e.attendance_id WHERE c.id_nhan_vien=900021 AND e.action='CHECK_OUT'", "1", "repeated sweep does not duplicate checkout")
sql("""
DELETE FROM ble_attendance_evidence WHERE action='CHECK_OUT' AND attendance_id IN (SELECT id FROM cham_cong WHERE id_nhan_vien=900021);
UPDATE cham_cong SET gio_ra=NULL,so_gio_thuc_te=NULL,trang_thai='CheckedIn' WHERE id_nhan_vien=900021;
UPDATE ble_scanners SET agent_version='2.0',scan_started_at=now(),last_seen_at=now() WHERE scanner_id='AUTO-HEALTHY';
""")
sql(sweep)
expect("SELECT count(*) FROM cham_cong WHERE id_nhan_vien=900021 AND gio_ra IS NULL", "1", "resuming BLE never uses pre-pause observations for automatic checkout")
sql("UPDATE ble_scanners SET scan_started_at=NULL WHERE scanner_id='AUTO-HEALTHY'")
sql(sweep)
expect("SELECT count(*) FROM cham_cong WHERE id_nhan_vien=900021 AND gio_ra IS NULL", "1", "unconfirmed scan session cannot infer employee absence")
sql("UPDATE ble_scanners SET scan_started_at=now()-interval '10 minutes',last_seen_at=now() WHERE scanner_id='AUTO-HEALTHY'")
sql(sweep)
expect("SELECT count(*) FROM cham_cong WHERE id_nhan_vien=900021 AND gio_ra IS NOT NULL", "1", "fresh observations from the current scan session can establish absence")
print("PASS: seven BLE auto-checkout SQL checks completed.")
