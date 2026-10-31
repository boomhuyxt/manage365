"""Uses the isolated localhost:5296 / localhost:55439 BLE fixture only.
The downloaded installer runs in verification-only mode: no install/startup changes.
"""
import hashlib, io, json, pathlib, runpy, struct, subprocess, zipfile

root = pathlib.Path(__file__).resolve().parents[1]
helpers = runpy.run_path(str(pathlib.Path(__file__).with_name('ble-shop-http-smoke.py')))
request, token, sql, check = (helpers[n] for n in ('request','token','sql','check'))
owner, other, employee = token(900001,'Manager'), token(900002,'Manager'), token(900004,'Employee')
base = '/api/attendance/ble'
scanner = 'BLE-CONTROL-TEST'
code, registered = request(base+'/scanners',owner,'POST',{'scannerId':scanner,'storeCode':'CONTROL'})
check(code==200,'Create isolated control fixture')
secret = registered['scannerToken']
def control(state='stopped', credential=None):
    return request(base+'/scanner-control/'+scanner,method='POST',
        body={'state':state,'message':'synthetic test agent','version':'2.0'},secret=credential or secret)

check(control(credential='invalid')[0]==401,'Control requires the kiosk secret')
check(request(base+'/scanner-control/'+scanner,method='POST',body={'state':'running','version':'2.0'})[0]==401,'Anonymous control without secret rejected')
check(request(base+f'/scanners/{scanner}/active',employee,'PUT',{'isActive':False})[0]==403,'Employee cannot toggle kiosk')
check(request(base+f'/scanners/{scanner}/active',other,'PUT',{'isActive':False})[0]==404,'Other shop cannot toggle kiosk')
check(control()[1]['enabled'],'Enabled command delivered to authenticated agent')
control('running')
summary=next(s for s in request(base+'/scanners',owner)[1]['scanners'] if s['scannerId']==scanner)
check(summary['online'] and summary['agentVersion']=='2.0','Confirmed running agent is displayed online')
request(base+f'/scanners/{scanner}/active',owner,'PUT',{'isActive':False})
summary=next(s for s in request(base+'/scanners',owner)[1]['scanners'] if s['scannerId']==scanner)
check(not summary['online'] and summary['scannerState']=='running','Toggle-off preserves unacknowledged agent state; never fakes stopped acknowledgement')
check(not control('running')[1]['enabled'],'Disabled kiosk still receives authenticated stop command')
check(not control('stopped')[1]['enabled'],'Stopped agent can remain connected while disabled')
check(request(base+'/scanner-events/'+scanner,method='POST',body=helpers['event']([]),secret=secret)[0]==401,'Disabled kiosk observations remain rejected')
request(base+f'/scanners/{scanner}/active',owner,'PUT',{'isActive':True})
check(control()[1]['enabled'],'Same secret resumes after toggle-on; no reinstallation')
control('error')
summary=next(s for s in request(base+'/scanners',owner)[1]['scanners'] if s['scannerId']==scanner)
check(not summary['online'],'Bluetooth error never reports scanning')
sql(f"UPDATE ble_scanners SET last_seen_at=now()-interval '2 minutes',scanner_state='running' WHERE scanner_id='{scanner}'")
summary=next(s for s in request(base+'/scanners',owner)[1]['scanners'] if s['scannerId']==scanner)
check(not summary['online'],'Stale connection never reports scanning')
check(request(base+f'/scanners/{scanner}/installer',employee,'POST')[0]==403,'Employee cannot download configured installer')
check(request(base+f'/scanners/{scanner}/installer',other,'POST')[0]==404,'Other shop cannot download configured installer')
code, binary = request(base+f'/scanners/{scanner}/installer',owner,'POST')
check(code==200 and binary[:2]==b'MZ' and binary[-8:]==b'R365BLE2','Download is a Windows EXE with configured payload')
size=struct.unpack('<q',binary[-16:-8])[0]
with zipfile.ZipFile(io.BytesIO(binary[-16-size:-16])) as z:
    config=json.loads(z.read('ble-scanner.json'))
    check(config['apiBaseUrl']=='http://127.0.0.1:5296' and config['scannerId']==scanner,'Installer automatically receives current web server and selected kiosk')
    check('BleScannerAgent.exe' in z.namelist(),'Installer contains the complete Windows agent')
check(control()[0]==401,'Installer replacement revokes old connection secret')
check(control(credential=config['scannerToken'])[0]==200,'Embedded configuration authenticates successfully')
out=root/'artifacts/ble-ui/control-installer-test.exe'
out.write_bytes(binary)
try:
    verified=subprocess.run([str(out),'--verify-package',str(out)],timeout=45,
        creationflags=subprocess.CREATE_NO_WINDOW)
except OSError as error:
    if error.winerror != 4551: raise
    (root/'artifacts/ble-ui/control-http-results.json').write_text(json.dumps({
        'passed':len(helpers['results']),'cases':helpers['results'],
        'blocked':'Native installer execution blocked by Windows Application Control (4551). No policy bypass attempted.'},indent=2))
    print('BLOCKED: Windows Application Control prevents executing the downloaded installer; API/payload checks passed.')
    raise SystemExit(2)
check(verified.returncode==0,'Actual downloaded self-contained EXE verifies its payload without installing')
(root/'artifacts/ble-ui/control-http-results.json').write_text(json.dumps({'passed':len(helpers['results']),'cases':helpers['results']},indent=2))
print(f"PASS: {len(helpers['results'])} control/installer checks.")
