// Reproduces compiled old Razor HTML + newly served static JavaScript.
// All API responses are mocked; no real attendance or kiosk writes are made.
const { chromium } = require('../artifacts/ble-ui/node_modules/playwright');
const fs = require('node:fs');
const path = require('node:path');
const root=path.resolve(__dirname,'..');
const base='http://localhost:5295';
const withoutScripts=html=>html.replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'');
(async()=>{
  const current=await (await fetch(base)).text();
  const stale=fs.readFileSync(path.join(root,'artifacts/ble-ui/stale-dashboard.html'),'utf8');
  if(stale.includes('id="bleSwitch"')) throw new Error('Stale fixture must be actual older markup');
  if(!current.includes('data-ble-ui-version="2"')) throw new Error('Running backend still serves old markup');
  const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  try {
    for(const [name,html] of [['stale',stale],['current',current]]) {
      const page=await browser.newPage(); const errors=[];
      page.on('pageerror',error=>errors.push(error.message));
      let apiCalls=0;
      await page.route('**/*',async route=>{
        const url=new URL(route.request().url());
        if(url.pathname==='/') return route.fulfill({contentType:'text/html',body:withoutScripts(html)});
        if(url.pathname==='/api/attendance/ble/scanners') {
          apiCalls++;
          return route.fulfill({json:{packageReady:true,installerReady:true,scanners:[{
            scannerId:'VERSION-TEST',storeCode:'TEST',isActive:true,online:true,
            lastSeenAt:new Date().toISOString(),scannerState:'running',agentVersion:'2.0'
          }]}});
        }
        if(url.pathname.endsWith('/overview')) return route.fulfill({json:{employees:[],events:[]}});
        return route.abort();
      });
      await page.goto(base,{waitUntil:'domcontentloaded'});
      await page.addScriptTag({path:path.join(root,'wwwroot/js/ble-kiosk.js')});
      await page.evaluate(()=>document.dispatchEvent(new Event('retail365:owner-ready')));
      if(name==='stale') {
        const warning=await page.locator('#bleFeedback').textContent();
        if(!warning.includes('chưa đồng bộ')) throw new Error('Missing actionable version warning');
        if(apiCalls!==0) throw new Error('Old markup must not initialize incompatible handlers');
        if(await page.locator('#bleRefresh').textContent()!=='Tải lại trang') throw new Error('Missing reload action');
      } else {
        await page.waitForFunction(()=>document.getElementById('bleHealth').textContent==='Đang quét');
        if(await page.locator('#bleSwitch').isDisabled()) throw new Error('Current switch never initialized');
        if(!(await page.locator('#bleSwitch').isChecked())) throw new Error('Current switch did not reflect API state');
      }
      if(errors.length) throw new Error(errors.join('; '));
      console.log(`PASS: ${name} Razor HTML with current JS; no null-element exceptions.`);
      await page.close();
    }
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
