// Run after ble-http-smoke.py against the isolated local test backend on port 5296.
const { chromium } = require('../artifacts/ble-ui/node_modules/playwright');
const crypto = require('node:crypto');
const path = require('node:path');
const fs = require('node:fs');
const base = 'http://127.0.0.1:5296';
const out = path.resolve(__dirname, '../artifacts/ble-ui');
function token(id, role) {
  const enc = x => Buffer.from(JSON.stringify(x)).toString('base64url');
  const now = Math.floor(Date.now()/1000);
  const data = enc({alg:'HS256',typ:'JWT'})+'.'+enc({sub:String(id),email:`ble-${id}@example.invalid`,name:`BLE Test ${id}`,'http://schemas.microsoft.com/ws/2008/06/identity/claims/role':role,iss:'ble-ui-test',aud:'ble-ui-test',nbf:now-10,exp:now+3600});
  return data+'.'+crypto.createHmac('sha256','ble-ui-isolated-testing-key-2026-only-123456789').update(data).digest('base64url');
}
async function authenticate(page, id, role) {
  await page.addInitScript(({jwt,id,role}) => { localStorage.setItem('accessToken',jwt); localStorage.setItem('user',JSON.stringify({id,role,displayName:'BLE test'})); },{jwt:token(id,role),id,role});
}
(async()=>{
  const browser = await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  const context = await browser.newContext({viewport:{width:1440,height:1100},acceptDownloads:true});
  const page = await context.newPage(); const errors=[]; page.on('pageerror',e=>errors.push(e.message));
  const failures=[]; page.on('requestfailed',r=>failures.push({url:r.url(),error:r.failure()?.errorText}));
  page.on('response',r=>{if(r.status()>=400)failures.push({url:r.url(),status:r.status()});});
  try {
    await authenticate(page,900001,'Manager'); await page.goto(base); await page.locator('#bleCreate:not([disabled])').waitFor({timeout:30000});
    await page.locator('#bleCreate').click(); await page.locator('#bleNewId').fill('BLE-TEST-UI-'+Date.now());
    await page.locator('#bleNewOwner option[value="900001"]').waitFor({state:'attached'}); await page.locator('#bleSaveCreate:not([disabled])').click(); await page.locator('#bleCreateModal').waitFor({state:'hidden'});
    await page.locator('#bleConfigure:not([disabled])').click(); await page.locator('#bleConfigModal.show').waitFor();
    if(await page.locator('#bleBind').count()) throw new Error('Manual binding control still present');
    if(await page.locator('[data-ble-remove]').count()) throw new Error('Manual unbinding control still present');
    if(!await page.locator('#bleConfigModal .alert-info').isVisible()) throw new Error('Automatic shop eligibility explanation missing');
    if(await page.locator('#bleApiBase').count()) throw new Error('Setup still asks for API address');
    page.once('dialog',dialog=>dialog.accept());
    const downloaded=page.waitForEvent('download',{timeout:120000}); await page.locator('#bleInstall:not([disabled])').click();
    const download=await downloaded; await download.saveAs(path.join(out,'ui-downloaded-installer.exe'));
    await page.locator('#bleConfigFeedback.alert-success').waitFor();
    await page.screenshot({path:path.join(out,'ble-configuration-desktop.png'),animations:'disabled'});
    await page.locator('#bleConfigModal .modal-footer button').click();
    await page.locator('#bleConfigModal').waitFor({state:'hidden'});
    await page.locator('.modal-backdrop').waitFor({state:'detached'}); await page.evaluate(()=>window.scrollTo(0,0));
    await page.screenshot({path:path.join(out,'ble-dashboard-desktop.png'),fullPage:true,animations:'disabled'});
    await page.setViewportSize({width:390,height:844});
    const overflow=await page.locator('#blePanel').evaluate(n=>n.scrollWidth>n.clientWidth+1);
    if(overflow) throw new Error('BLE panel overflows narrow viewport');
    await page.locator('#bleConfigure').click(); await page.locator('#bleConfigModal.show').waitFor();
    await page.waitForFunction(()=>getComputedStyle(document.getElementById('bleConfigModal')).opacity==='1');
    await page.screenshot({path:path.join(out,'ble-configuration-mobile.png'),animations:'disabled'});
    const employee=await context.newPage(); await authenticate(employee,900004,'Employee'); await employee.goto(base);
    await employee.locator('#employeeView:not(.d-none)').waitFor();
    if(await employee.locator('#blePanel').isVisible()) throw new Error('BLE admin controls exposed to employee');
    const failure=await context.newPage(); await authenticate(failure,900001,'Manager');
    await failure.route('**/api/attendance/ble/scanners',route=>route.fulfill({status:503,contentType:'application/problem+json',body:JSON.stringify({title:'BLE chưa được cài đặt trên database. Cần áp dụng migration 013 và 014.'})}));
    await failure.goto(base); await failure.locator('#bleFeedback.alert-danger').waitFor();
    if(!(await failure.locator('#bleFeedback').innerText()).includes('migration 013')) throw new Error('Schema error not visible');
    if(errors.length) throw new Error('Browser errors: '+errors.join('; '));
    fs.writeFileSync(path.join(out,'shop-ui-results.json'),JSON.stringify({passed:['owner panel','create kiosk','automatic employees without binding','download configured Windows installer','desktop layout','390px layout','employee controls hidden','missing migration error'],browserErrors:errors},null,2));
    console.log('PASS: 8 UI checks; no browser JavaScript errors; screenshots saved in artifacts/ble-ui.');
  } catch (error) {
    await page.screenshot({path:path.join(out,'ui-failure.png'),fullPage:true});
    console.error(JSON.stringify({errors,failures,url:page.url(),feedback:await page.locator('#bleFeedback').textContent().catch(()=>null)},null,2));
    throw error;
  } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
