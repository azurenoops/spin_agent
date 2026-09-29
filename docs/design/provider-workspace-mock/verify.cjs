// Standalone design verification; uses the repository's installed Playwright.
const path=require('path');
const fs=require('fs');
const {pathToFileURL}=require('url');
const root=path.resolve(__dirname,'../../..');
const {chromium}=require(path.join(root,'src/Ato.Copilot.Dashboard/node_modules/playwright'));
const assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({headless:true});
 const page=await browser.newPage({viewport:{width:1440,height:1000},deviceScaleFactor:1});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 const url=pathToFileURL(path.join(__dirname,'index.html')).href;
 await page.goto(url);await page.waitForFunction(()=>window.mockPages?.length>0);
 const pages=await page.evaluate(()=>window.mockPages);
 fs.mkdirSync(path.join(__dirname,'screens'),{recursive:true});
 const outcomes=[];
 for(const p of pages){
   await page.goto(url+'#'+p.id);await page.waitForFunction(id=>document.querySelector('#screen').value===id,p.id);
   assert.equal(await page.locator('h1').innerText(),p.title);
   assert.equal(await page.locator('#content').innerText().then(t=>t.length>80),true);
   const badLinks=await page.evaluate(()=>[...document.querySelectorAll('[data-go]')].map(e=>e.dataset.go).filter(id=>!window.mockPages.some(p=>p.id===id)));
   assert.deepEqual(badLinks,[],'Unknown prototype destination on '+p.id);
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'Desktop overflow '+p.id);
   await page.evaluate(()=>document.querySelector('#toast').style.display='none');await page.screenshot({path:path.join(__dirname,'screens',p.id+'.png'),fullPage:true});
   await page.locator('#actions button').click();
   const responded=await page.evaluate(id=>location.hash.slice(1)!==id||document.querySelector('#dialog').open||document.querySelector('#toast').style.display==='block',p.id);
   assert.equal(responded,true,'Unresponsive primary action '+p.id);
   if(await page.locator('#dialog').evaluate(d=>d.open))await page.locator('#close').click();
   await page.setViewportSize({width:390,height:844});await page.goto(url+'#'+p.id);
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'Mobile overflow '+p.id);
   await page.evaluate(()=>document.querySelector('#toast').style.display='none');if(['overview','release','system-documents','system-hosting'].includes(p.id))await page.screenshot({path:path.join(__dirname,'screens',p.id+'-mobile.png'),fullPage:true});
   await page.setViewportSize({width:1440,height:1000});outcomes.push({screen:p.id,desktop:'pass',mobile:'pass',primaryAction:'pass'});
 }
 // Verify explicit prerequisites and that adoption creates duties, not completion.
 await page.goto(url+'#system-capabilities');await page.locator('#adopt-confirm').check();await page.locator('#actions button').click();
 assert.equal(await page.locator('#dialogTitle').innerText(),'Complete the handoff prerequisites');await page.locator('#close').click();
 await page.evaluate(()=>go('release'));await page.locator('#approve-release').check();await page.locator('#actions button').click();await page.locator('[data-action="commit-publish"]').click();
 await page.waitForFunction(()=>document.querySelector('h1').textContent==='Applied capabilities');
 await page.evaluate(()=>go('system-hosting'));await page.locator('#associate-confirm').check();await page.locator('#actions button').click();
 await page.locator('#adopt-confirm').check();await page.locator('#actions button').click();
 await page.waitForFunction(()=>document.querySelector('h1').textContent==='Responsibilities');assert.match(await page.locator('#content').innerText(),/2 customer duties need attention/);
 await page.evaluate(()=>go('source-review'));await page.locator('#source-confirm').check();await page.locator('#actions button').click();await page.waitForFunction(()=>document.querySelector('h1').textContent==='Centralized audit collection');
 await page.evaluate(()=>go('rule'));await page.locator('[data-action="evaluate"]').click();assert.match(await page.locator('#dialogBody').innerText(),/Triggered/);await page.locator('#close').click();
 await page.evaluate(()=>go('offerings'));await page.locator('[data-filter]').fill('Microsoft 365');assert.equal(await page.locator('tbody tr:visible').count(),1);
 const stateResults=[];
 for(const state of ['empty','error','restricted']){
   await page.goto(url+'#sources');await page.locator('#state').selectOption(state);assert.equal(await page.locator('.empty').count(),1);
   assert.equal(await page.locator('#actions').isVisible(),false);await page.screenshot({path:path.join(__dirname,'screens','state-'+state+'.png'),fullPage:true});stateResults.push(state);
 }
 await page.locator('#state').selectOption('error');await page.locator('[data-action="retry"]').click();assert.equal(await page.locator('#state').inputValue(),'ready');
 assert.deepEqual(errors,[],'Browser script errors');
 const groups=[['Provider operations',pages.filter(p=>!p.id.startsWith('system-'))],['Systems handoff',pages.filter(p=>p.id.startsWith('system-'))]];
 const html=`<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SPIN — Provider & Systems mockup gallery</title><link rel="stylesheet" href="style.css"></head><body><div class="designbar"><span>DESIGN PREVIEW · SYNTHETIC RECORDS · NO LIVE CONNECTIONS</span><a href="index.html">Open interactive prototype →</a></div><main class="gallery"><header><div class="eyebrow">PROVIDER → MISSION SYSTEM → ATO PACKAGE → ONGOING MONITORING</div><h1>One connected service-to-system workflow</h1><p>${pages.length} designed screens, aligned with the Systems redesign. Open any screen to explore its task, document contribution, and Mission Owner handoff.</p><p><a href="../system-overview-mock/gallery.html">View the original 30 Systems screens ↗</a></p></header>${groups.map(([title,items])=>'<h2>'+title+'</h2><div class="gallerygrid">'+items.map(p=>`<a class="gallerycard" href="index.html#${p.id}"><img loading="lazy" src="screens/${p.id}.png" alt="${p.title} screen"><div><span class="eyebrow">${p.group}</span><h3>${p.title}</h3><p>${p.desc}</p></div></a>`).join('')+'</div>').join('')}<h2>Designed for real-world states</h2><div class="gallerygrid">${['empty','error','restricted'].map(s=>`<a class="gallerycard" href="index.html#sources"><img loading="lazy" src="screens/state-${s}.png" alt="${s} state"><div><h3>${s==='error'?'Unavailable':s==='restricted'?'Restricted access':'Empty state'}</h3><p>Use the state selector in the interactive prototype to inspect this view.</p></div></a>`).join('')}</div></main></body></html>`;
 fs.writeFileSync(path.join(__dirname,'gallery.html'),html);
 fs.writeFileSync(path.join(__dirname,'screens/index.json'),JSON.stringify(pages,null,2));
 fs.writeFileSync(path.join(__dirname,'verification.json'),JSON.stringify({scope:'Standalone UI prototype only',screens:outcomes,states:stateResults,flows:['Publication confirmation','Association prerequisite','Adoption prerequisite','Customer duties remain open','Source review','Rule evaluation','Offering filter','Unavailable retry'],browserErrors:errors},null,2));
 await page.goto(pathToFileURL(path.join(__dirname,'gallery.html')).href);await page.screenshot({path:path.join(__dirname,'gallery-preview.png')});
 console.log(JSON.stringify({screens:pages.length,desktop:'pass',mobile:'pass',states:stateResults,flows:'pass',browserErrors:errors}));
 await browser.close();
})().catch(error=>{console.error(error);process.exit(1);});
