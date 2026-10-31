"""Run after ble-shop-http-smoke.py; same isolated database/backend only."""
import datetime, pathlib, runpy

helpers = runpy.run_path(str(pathlib.Path(__file__).with_name("ble-shop-http-smoke.py")))
request, token, sql, check = (helpers[name] for name in ("request", "token", "sql", "check"))
admin, owner, other = token(900003, "Admin"), token(900001, "Manager"), token(900002, "Manager")
code, kiosk = request("/api/attendance/ble/scanners", admin, "POST",
    {"scannerId": "BLE-TEST-ADMIN-OTHER", "storeCode": "STORE-02", "shopOwnerId": 900002})
check(code == 200 and sql("SELECT owner_employee_id FROM ble_scanners WHERE scanner_id='BLE-TEST-ADMIN-OTHER'") == "900002",
      "Admin creates kiosk for selected shop, not its own account")
employees = request("/api/attendance/ble/scanners/BLE-TEST-ADMIN-OTHER/overview", other)[1]["employees"]
check(any(e["employeeId"] == 900006 for e in employees), "Other shop automatically receives its own scheduled employee")
today = datetime.date.fromisoformat(sql("SELECT (now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date"))
tomorrow = (today + datetime.timedelta(days=1)).isoformat()
code, shift = request("/api/shift-assignments", admin, "POST",
    {"employeeId": 900008, "shiftDate": tomorrow, "customShiftName": "Admin chosen shop",
     "customStartTime": "08:00", "customEndTime": "12:00", "shopOwnerId": 900002})
check(code in (200,201) and shift["shopOwnerId"] == 900002, "Admin assignment persists selected shop")
check(request(f"/api/shift-assignments/{shift['assignmentId']}", other, "DELETE")[0] in (200,204),
      "Selected shop owner can manage the Admin-created assignment")
monday = today - datetime.timedelta(days=today.weekday())
target = monday + datetime.timedelta(days=7)
# Only synthetic fixtures in the hard-coded disposable database, to permit retries.
sql(f"DELETE FROM dang_ky_ca d USING ca_lam cl WHERE cl.id=d.id_ca_lam AND d.id_nhan_vien IN (900004,900005,900007) AND cl.ngay_lam>='{target.isoformat()}' AND cl.ngay_lam<'{(target+datetime.timedelta(days=7)).isoformat()}'")
code, copied = request("/api/shift-assignments/copy-week", owner, "POST",
    {"sourceStartDate": monday.isoformat(), "targetStartDate": target.isoformat()})
# The preceding HTTP test intentionally locked 900007. Copy must reject that row.
check(code == 200 and copied["successCount"] == 2 and copied["failedCount"] == 1
      and "900007" in copied["errors"][0]
      and all(s["shopOwnerId"] == 900001 for s in copied["createdAssignments"]),
      "Copy-week preserves shop ownership and rejects the locked employee")
print("PASS: five additional Admin/shop/copy-week assertions.")
