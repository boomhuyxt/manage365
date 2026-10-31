"""BLE integration test against the isolated local clone; never connects to the configured/live DB.
Requires backend on 127.0.0.1:5296 with the test JWT configuration documented in the BLE report.
"""
import base64, hashlib, hmac, json, os, pathlib, subprocess, time, urllib.request, urllib.error, uuid, datetime
ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/ble-ui'
BASE = 'http://127.0.0.1:5296'
KEY = b'ble-ui-isolated-testing-key-2026-only-123456789'
PSQL = r'C:\Program Files\PostgreSQL\18\bin\psql.exe'
def sql(query):
    env=os.environ.copy(); env.update(PGPASSWORD='', PGSSLMODE='disable')
    p=subprocess.run([PSQL,'-h','127.0.0.1','-p','55439','-U','audit_owner','-d','manage365_ble_shop_test','-X','-qAt','-v','ON_ERROR_STOP=1'], input=query, text=True, encoding='utf-8',capture_output=True,env=env,timeout=30)
    if p.returncode: raise RuntimeError(p.stderr)
    return p.stdout.strip()
def token(id,role):
    def enc(obj): return base64.urlsafe_b64encode(json.dumps(obj,separators=(',',':')).encode()).rstrip(b'=')
    data=enc({'alg':'HS256','typ':'JWT'})+b'.'+enc({'sub':str(id),'email':f'ble-{id}@example.invalid','name':f'BLE Test {id}','http://schemas.microsoft.com/ws/2008/06/identity/claims/role':role,'iss':'ble-ui-test','aud':'ble-ui-test','nbf':int(time.time())-10,'exp':int(time.time())+3600})
    return (data+b'.'+base64.urlsafe_b64encode(hmac.new(KEY,data,hashlib.sha256).digest()).rstrip(b'=')).decode()
def request(path,who=None,method='GET',body=None,secret=None):
    headers={'Content-Type':'application/json'}
    if who: headers['Authorization']='Bearer '+who
    if secret: headers['X-Scanner-Token']=secret
    req=urllib.request.Request(BASE+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method)
    try: response=urllib.request.urlopen(req,timeout=90)
    except urllib.error.HTTPError as e: response=e
    raw=response.read()
    if response.headers.get_content_type() in ('application/zip','application/octet-stream'): return response.status,raw
    return response.status,json.loads(raw) if raw else None
results=[]
def check(condition,name):
    if not condition: raise AssertionError(name)
    results.append(name); print('PASS:',name,flush=True)
def event(items,state='running'):
    return {'batchId':str(uuid.uuid4()),'observedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(), 'observations':[{'advertisementToken':c['advertisementToken'],'rssiDbm':r,'sequence':i} for i,(c,r) in enumerate(items)],'scannerState':state}
def seed(credential,scanner='BLE-TEST-OWNER',rssi=-55):
    sql(f"INSERT INTO ble_presence_observations(credential_id,scanner_id,batch_id,sequence,observed_at,received_at,rssi_dbm) SELECT '{credential['credentialId']}','{scanner}','{uuid.uuid4()}',i,now()-make_interval(secs=>i*5),now()-make_interval(secs=>i*5),{rssi} FROM generate_series(1,5) i;")
if __name__=='__main__':
    # Known synthetic rows in an isolated database only. Existing clone employees/attendance are untouched.
    sql("DELETE FROM ble_attendance_evidence WHERE scanner_id LIKE 'BLE-TEST-%'; DELETE FROM ble_presence_observations WHERE scanner_id LIKE 'BLE-TEST-%'; DELETE FROM ble_scanner_employees WHERE scanner_id LIKE 'BLE-TEST-%'; DELETE FROM ble_scanners WHERE scanner_id LIKE 'BLE-TEST-%'; DELETE FROM cham_cong WHERE id_nhan_vien BETWEEN 900001 AND 900010; DELETE FROM dang_ky_ca WHERE id_nhan_vien BETWEEN 900001 AND 900010; DELETE FROM ca_lam WHERE id BETWEEN 900001 AND 900010;")
    for id,role in [(900001,32),(900002,32),(900003,31),(900004,1),(900005,1),(900006,1),(900007,1),(900008,1)]:
        sql(f"INSERT INTO nhan_vien(id,id_vai_tro,ho_ten,email,mat_khau,loai_nhan_vien,trang_thai) VALUES({id},{role},'BLE Test {id}','ble-{id}@example.invalid','test-only','FullTime','Active') ON CONFLICT(id) DO UPDATE SET trang_thai='Active',id_vai_tro=EXCLUDED.id_vai_tro;")
    sql("INSERT INTO ca_lam(id,ngay_lam,loai_ca,gio_bat_dau,gio_ket_thuc) VALUES(900001,(now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date,'BLE test shift','00:00','23:59'); INSERT INTO dang_ky_ca(id,id_nhan_vien,id_ca_lam,trang_thai) VALUES (900004,900004,900001,'Approved'),(900005,900005,900001,'Approved'),(900007,900007,900001,'Approved');")
    owner=token(900001,'Manager'); other=token(900002,'Manager'); admin=token(900003,'Admin'); employee=token(900004,'Employee'); second=token(900005,'Employee')
    path='/api/attendance/ble'
    check(request(path+'/scanners')[0]==401,'Anonymous cannot configure kiosk')
    check(request(path+'/scanners',employee)[0]==403,'Employee cannot configure kiosk')
    status,registered=request(path+'/scanners',owner,'POST',{'scannerId':'BLE-TEST-OWNER','storeCode':'STORE-01'})
    check(status==200,'Owner creates kiosk'); secret=registered['scannerToken']
    check(request(path+'/scanners/BLE-TEST-OWNER/overview',other)[0]==404,'Other owner cannot inspect kiosk')
    check(request(path+'/scanners',other,'POST',{'scannerId':'BLE-TEST-OWNER','storeCode':'STEAL'})[0]==403,'Other owner cannot rotate or take kiosk')
    check(request(path+'/scanners/BLE-TEST-OWNER/overview',admin)[0]==200,'Admin can inspect owner kiosk')
    sql("UPDATE dang_ky_ca SET shop_owner_id=900001 WHERE id IN (900004,900005,900007)")
    check(request(path+'/scanners/BLE-TEST-OWNER/overview',owner)[1]['employees'].__len__()==3,'Scheduled employees appear without kiosk bindings')
    check(request(path+'/scanners/BLE-TEST-OWNER/employees/900004',owner,'PUT')[0] in (404,405),'Manual binding endpoint removed')
    shops=request('/api/shift-assignments/shops',owner)[1]
    check(len(shops)==1 and shops[0]['ownerId']==900001,'Manager shop choices restricted to own shop')
    today=sql("SELECT (now() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date")
    assignment={'employeeId':900008,'shiftDate':today,'customShiftName':'Scoped shift','customStartTime':'00:00','customEndTime':'23:59'}
    code,created=request('/api/shift-assignments',owner,'POST',assignment)
    check(code in (200,201) and created['shopOwnerId']==900001,'Manager assignment automatically stamps own shop')
    check(request('/api/shift-assignments',owner,'POST',dict(assignment,shopOwnerId=900002))[0] in (400,409),'Manager cannot forge another shop')
    check(request('/api/shift-assignments/'+str(created['assignmentId']),other,'PUT',{'note':'steal'})[0] in (400,409),'Other shop cannot take assignment on update')
    check(request('/api/shift-assignments',admin,'POST',assignment)[0]==400,'Admin must select shop for new assignment')
    check(request('/api/shift-assignments/'+str(created['assignmentId']),owner,'DELETE')[0] in (200,204),'Manager can cancel own scoped assignment')
    check(all(e['employeeId']!=900008 for e in request(path+'/scanners/BLE-TEST-OWNER/overview',owner)[1]['employees']),'Cancelled shift removes automatic kiosk eligibility')
    sql("INSERT INTO ble_scanner_employees(scanner_id,employee_id) VALUES('BLE-TEST-OWNER',900006)")
    sql("INSERT INTO dang_ky_ca(id,id_nhan_vien,id_ca_lam,trang_thai) VALUES(900006,900006,900001,'Approved')")
    check(all(e['employeeId']!=900006 for e in request(path+'/scanners/BLE-TEST-OWNER/overview',owner)[1]['employees']),'Unscoped historical shift plus old binding cannot grant access')
    sql("UPDATE dang_ky_ca SET shop_owner_id=900002 WHERE id=900006")
    check(all(e['employeeId']!=900006 for e in request(path+'/scanners/BLE-TEST-OWNER/overview',owner)[1]['employees']),'Other shop shift cannot grant access')
    check(request(path+'/scanners/BLE-TEST-OWNER/employees/900006',other,'PUT')[0] in (404,405),'Removed binding API cannot bypass scope')
    check(request(path+'/scanners/BLE-TEST-OWNER/package',other,'POST',{'apiBaseUrl':BASE})[0]==404,'Cross-owner secret package blocked')
    check(request(path+'/scanners/BLE-TEST-OWNER/package',owner,'POST',{'apiBaseUrl':'javascript:alert(1)'})[0]==400,'Package rejects invalid URL')
    _,c1=request(path+'/presence-credentials',employee,'POST',{'deviceId':'test-phone-1'})
    _,c2=request(path+'/presence-credentials',second,'POST',{'deviceId':'test-phone-2'})
    _,unbound=request(path+'/presence-credentials',token(900006,'Employee'),'POST',{'deviceId':'unbound'})
    check(request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([]),secret='incorrect')[0]==401,'Invalid scanner secret rejected')
    check(request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([(unbound,-50)]),secret=secret)[1]['errorCode']=='ble_credential_invalid','Unbound phone rejected')
    check(request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([(c1,-50)]),secret=secret)[1]['errorCode']=='ble_dwell_not_met','Brief presence does not check in')
    seed(c1); seed(c2)
    batch=event([(c1,-50),(c2,-50)])
    status,data=request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=batch,secret=secret)
    check(status==200 and data['success'], 'Stable presence checks in through actual SQL')
    check(sql('SELECT count(*) FROM cham_cong WHERE id_nhan_vien IN (900004,900005) AND phuong_thuc_cham_cong=\'BLE\'')=='2','Two employees in one batch both checked in')
    request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=batch,secret=secret)
    check(sql('SELECT count(*) FROM cham_cong WHERE id_nhan_vien IN (900004,900005)')=='2','Replay does not duplicate attendance')
    _,status=request(path+'/me/status',employee)
    check(status['scannerOnline'] and status['bleReady'] and status['scannerId']=='BLE-TEST-OWNER','Employee status matches its kiosk')
    _,status=request(path+'/me/status',token(900006,'Employee'))
    check(not status['scannerOnline'] and status['scannerId'] is None,'Unbound employee never sees unrelated online kiosk')
    sql(f"UPDATE ble_presence_observations SET rssi_dbm=-100 WHERE credential_id='{c1['credentialId']}'")
    checkout={'shiftAssignmentId':900004,'credentialId':c1['credentialId'],'presenceToken':c1['advertisementToken'],'idempotencyKey':str(uuid.uuid4()),'scannerId':'BLE-TEST-OWNER'}
    check(request(path+'/checkout',employee,'POST',checkout)[0]==422,'Checkout rejects weak signal')
    sql(f"UPDATE ble_presence_observations SET rssi_dbm=-50 WHERE credential_id='{c1['credentialId']}'")
    code,data=request(path+'/checkout',employee,'POST',checkout)
    check(code==200 and data['success'],'Checkout records time and BLE evidence')
    check(request(path+'/checkout',employee,'POST',checkout)[0]==200,'Checkout retry is idempotent')
    check(sql("SELECT count(*) FROM ble_attendance_evidence WHERE action='CHECK_OUT' AND scanner_id='BLE-TEST-OWNER'")=='1','Checkout retry has one evidence row')
    _,c7=request(path+'/presence-credentials',token(900007,'Employee'),'POST',{'deviceId':'auto-checkout-phone'})
    seed(c7)
    auto_status,auto_data=request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([(c7,-50)]),secret=config['scannerToken'] if 'config' in locals() else secret)
    check(auto_status==200 and auto_data['success'],'Auto-checkout test employee checks in')
    sql(f"UPDATE cham_cong SET gio_vao=now()-interval '3 minutes',updated_at=now()-interval '3 minutes' WHERE id_nhan_vien=900007 AND gio_ra IS NULL; UPDATE ble_presence_observations SET observed_at=now()-interval '3 minutes',received_at=now()-interval '3 minutes' WHERE credential_id='{c7['credentialId']}'; UPDATE ble_attendance_evidence SET observed_at=now()-interval '3 minutes' WHERE attendance_id IN (SELECT id FROM cham_cong WHERE id_nhan_vien=900007) AND action='CHECK_IN';")
    checkout_deadline=time.monotonic()+45
    while time.monotonic()<checkout_deadline and sql("SELECT count(*) FROM ble_attendance_evidence WHERE action='CHECK_OUT' AND checkout_source='beacon_absence' AND attendance_id IN (SELECT id FROM cham_cong WHERE id_nhan_vien=900007)")!='1':
        request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([]),secret=secret)
        time.sleep(5)
    check(sql("SELECT count(*) FROM ble_attendance_evidence WHERE action='CHECK_OUT' AND checkout_source='beacon_absence' AND attendance_id IN (SELECT id FROM cham_cong WHERE id_nhan_vien=900007)")=='1','Healthy kiosk auto-checks-out after configured beacon absence')
    check(sql("SELECT count(*) FROM cham_cong WHERE id_nhan_vien=900007 AND gio_ra IS NOT NULL AND trang_thai='NeedsReview'")=='1','Automatic absence checkout is flagged for manager review')
    request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([]),secret=secret)
    time.sleep(2)
    check(sql("SELECT count(*) FROM ble_attendance_evidence WHERE action='CHECK_OUT' AND checkout_source='beacon_absence' AND attendance_id IN (SELECT id FROM cham_cong WHERE id_nhan_vien=900007)")=='1','Repeated sweeps do not duplicate automatic checkout')
    request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([],state='error'),secret=secret)
    _,data=request(path+'/scanners',owner)
    check(not data['scanners'][0]['online'],'Broken Bluetooth heartbeat does not show online')
    request(path+'/scanners/BLE-TEST-OWNER/active',owner,'PUT',{'isActive':False})
    check(request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([]),secret=secret)[0]==401,'Paused kiosk cannot send attendance')
    request(path+'/scanners/BLE-TEST-OWNER/active',owner,'PUT',{'isActive':True})
    code,zipdata=request(path+'/scanners/BLE-TEST-OWNER/package',owner,'POST',{'apiBaseUrl':BASE})
    check(code==200,'Configured Windows package downloads')
    import zipfile,io
    with zipfile.ZipFile(io.BytesIO(zipdata)) as z:
        config=json.loads(z.read('ble-scanner.json'))
        check('BleScannerAgent.exe' in z.namelist() and 'Start-BLE.cmd' in z.namelist(),'Package contains Windows executable and launcher')
        check(config['scannerId']=='BLE-TEST-OWNER' and config['apiBaseUrl']==BASE,'Package configuration matches kiosk/backend')
    check(request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([]),secret=secret)[0]==401,'Old scanner secret revoked after download')
    check(request(path+'/scanner-events/BLE-TEST-OWNER',method='POST',body=event([]),secret=config['scannerToken'])[0]==200,'New package secret authenticates')
    sql("UPDATE nhan_vien SET trang_thai='Inactive' WHERE id=900007")
    check(request(path+'/presence-credentials',token(900007,'Employee'),'POST',{'deviceId':'locked'})[0]==401,'Locked account cannot request BLE credentials')
    OUT.mkdir(exist_ok=True)
    (OUT/'shop-http-results.json').write_text(json.dumps({'passed':len(results),'cases':results},indent=2),encoding='utf-8')
    print(f'PASSED {len(results)} BLE integration assertions')
