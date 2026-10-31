// Requires the isolated backend and data seeded by ble-shop-http-smoke.py.
const { chromium } = require('../artifacts/ble-ui/node_modules/playwright');
const crypto = require('node:crypto');
function token(id, role) {
  const enc = x => Buffer.from(JSON.stringify(x)).toString('base64url');
  const now = Math.floor(Date.now()/1000);
  const data = enc({alg:'HS256',typ:'JWT'})+'.'+enc({sub:String(id),email:`ble-${id}@example.invalid`,name:'BLE Test',
    'http://schemas.microsoft.com/ws/2008/06/identity/claims/role':role,iss:'ble-ui-test',aud:'ble-ui-test',nbf:now-10,exp:now+3600});
  return data+'.'+crypto.createHmac('sha256','ble-ui-isolated-testing-key-2026-only-123456789').update(data).digest('base64url');
}
(async () => {
  const browser = await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  try {
    for (const [id,role] of [[900001,'Manager'],[900003,'Admin']]) {
      const context = await browser.newContext();
      const page = await context.newPage();
      const errors = []; page.on('pageerror', e => errors.push(e.message));
      await page.addInitScript(jwt => localStorage.setItem('accessToken',jwt),token(id,role));
      await page.goto('http://127.0.0.1:5296/Schedule');
      await page.locator('#addShiftButton:not([disabled])').click();
      await page.locator('#shiftModal.show').waitFor();
      if (role === 'Manager') {
        if (await page.locator('#shiftShop').inputValue() !== '900001') throw new Error('Manager shop lost after form reset');
        if (!await page.locator('#shiftShop').isDisabled()) throw new Error('Manager shop selector must be fixed');
      } else {
        if (await page.locator('#shiftShop').isDisabled()) throw new Error('Admin cannot choose shop');
        await page.locator('#shiftShop').selectOption('900002');
        if (await page.locator('#shiftShop').inputValue() !== '900002') throw new Error('Admin shop selection failed');
      }
      if (errors.length) throw new Error(errors.join('; '));
      await context.close();
    }
    console.log('PASS: Manager auto-select/lock and Admin explicit shop choice survive modal initialization; no JS errors.');
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode=1; });
