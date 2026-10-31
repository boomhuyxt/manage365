"""Validate acknowledged scan session boundaries on the isolated test backend."""
import pathlib, runpy
h=runpy.run_path(str(pathlib.Path(__file__).with_name('ble-shop-http-smoke.py')))
request, token, sql, check=(h[n] for n in ('request','token','sql','check'))
owner=token(900001,'Manager'); base='/api/attendance/ble'; scanner='BLE-CONTROL-SESSION'
status, registration=request(base+'/scanners',owner,'POST',{'scannerId':scanner,'storeCode':'SESSION'})
assert status==200
secret=registration['scannerToken']
def control(state):
    response=request(base+'/scanner-control/'+scanner,method='POST',body={'state':state,'version':'2.0'},secret=secret)
    assert response[0]==200
    return response[1]['enabled']
def started(): return sql(f"SELECT COALESCE(scan_started_at::text,'none') FROM ble_scanners WHERE scanner_id='{scanner}'")
control('running'); first=started()
check(first!='none','Running acknowledgement establishes scan session')
control('running'); check(started()==first,'Continuous heartbeats preserve current session')
request(base+f'/scanners/{scanner}/active',owner,'PUT',{'isActive':False})
check(started()=='none' and not control('stopped'),'Turning off clears the scan session while control remains available')
request(base+f'/scanners/{scanner}/active',owner,'PUT',{'isActive':True})
check(started()=='none' and control('stopped'),'Turning on waits for actual scanning acknowledgement')
control('running'); check(started() not in ('none',first),'Resumed scanner starts a new session')
control('error'); check(started()=='none','Bluetooth/control error invalidates the scan session')
control('running'); before=started()
sql(f"UPDATE ble_scanners SET last_seen_at=now()-interval '2 minutes' WHERE scanner_id='{scanner}'")
control('running'); check(started()!=before,'Recovered stale connection starts a new session')
print('PASS: seven control-session assertions.')
