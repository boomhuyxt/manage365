const { chromium } = require('../artifacts/ble-ui/node_modules/playwright');
const crypto = require('node:crypto');
const enc = x => Buffer.from(JSON.stringify(x)).toString('base64url');
const now = Math.floor(Date.now()/1000);
const payload=enc({alg:'HS256',typ:'JWT'})+'.'+enc({sub:'900001',name:'BLE Test',email:'ble-900001@example.invalid',
  'http://schemas.microsoft.com/ws/2008/06/identity/claims/role':'Manager',iss:'ble-ui-test',aud:'ble-ui-test',nbf:now-10,exp:now+3600});
const jwt=payload+'.'+crypto.createHmac('sha256','ble-ui-isolated-testing-key-2026-only-123456789').update(payload).digest('base64url');
(async()=>{
  const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  try {
    const page=await browser.newPage({viewport:{width:1440,height:1000}});
    const errors=[]; page.on('pageerror',e=>errors.push(e.message));
    await page.addInitScript(jwt=>{
      localStorage.setItem('accessToken',jwt);
      localStorage.setItem('user',JSON.stringify({id:900001,role:'Manager',displayName:'BLE Test'}));
    },jwt);
    const scanner='BLE-UI-CONTROL-'+Date.now();
    const registration=await page.request.post('http://127.0.0.1:5296/api/attendance/ble/scanners',{
      headers:{Authorization:'Bearer '+jwt},data:{scannerId:scanner,storeCode:'UI-CONTROL'}});
    if(!registration.ok()) throw new Error('Fixture registration failed');
    const secret=(await registration.json()).scannerToken;
    const report=async state=>{
      const response=await page.request.post('http://127.0.0.1:5296/api/attendance/ble/scanner-control/'+scanner,{
        headers:{'X-Scanner-Token':secret},data:{state,message:'UI simulated agent',version:'2.0'}});
      if(!response.ok()) throw new Error('Simulated acknowledgement failed');
    };
    const refresh=async text=>{ await page.locator('#bleRefresh').click(); await page.waitForFunction(text=>document.getElementById('bleHealth').textContent===text,text); };
    await page.goto('http://127.0.0.1:5296');
    await page.locator('#bleScanner:not([disabled])').selectOption(scanner);
    await page.waitForFunction(()=>document.getElementById('bleHealth').textContent==='Cần kích hoạt bộ quét mới');
    if(!await page.locator('#bleSwitch').isDisabled()) throw new Error('Legacy/uninstalled agent should request activation');
    await report('running'); await refresh('Đang quét');
    await page.locator('#bleSwitch:not([disabled])').uncheck();
    await page.waitForFunction(()=>document.getElementById('bleHealth').textContent==='Đang tắt · chờ máy xác nhận');
    await page.locator('#bleSwitch:not([disabled])').waitFor();
    await report('stopped'); await refresh('Đã tắt BLE');
    await page.locator('#bleSwitch:not([disabled])').check();
    await page.waitForFunction(()=>document.getElementById('bleHealth').textContent==='Đang bật · chờ máy xác nhận');
    await page.locator('#bleSwitch:not([disabled])').waitFor();
    await report('running'); await refresh('Đang quét');
    await report('error'); await refresh('Bộ quét cần kiểm tra');
    await page.route('**/api/attendance/ble/scanners',async route=>{
      const response=await route.fetch(), body=await response.json();
      body.scanners=body.scanners.map(s=>s.scannerId===scanner?{...s,lastSeenAt:new Date(Date.now()-120000).toISOString(),online:false}:s);
      await route.fulfill({response,json:body});
    });
    await refresh('Mất kết nối bộ quét');
    await page.unroute('**/api/attendance/ble/scanners');
    await report('running'); await refresh('Đang quét');
    await page.route('**/api/attendance/ble/scanners',route=>route.fulfill({status:503,contentType:'application/problem+json',body:JSON.stringify({title:'Test API unavailable'})}));
    await page.locator('#bleSwitch:not([disabled])').uncheck();
    await page.waitForFunction(()=>document.getElementById('bleHealth').textContent==='Không tải được trạng thái BLE');
    if(!await page.locator('#bleSwitch').isDisabled()) throw new Error('Failed status refresh must disable further toggles');
    if(errors.length) throw new Error(errors.join('; '));
    await page.screenshot({path:'artifacts/ble-ui/control-switch-desktop.png',fullPage:true});
    console.log('PASS: nine control UI states, including failed status refresh after toggling; no JS errors.');
  } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
