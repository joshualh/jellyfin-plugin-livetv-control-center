const {test,before,after}=require('node:test');
const assert=require('node:assert/strict');
const http=require('node:http');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const base=path.join(__dirname,'../../src/Jellyfin.Plugin.LiveTvGroups');
const id='11111111111111111111111111111111';
const scripts='window.LiveTvGroupsTranslations='+fs.readFileSync(path.join(base,'Localization/strings.json'),'utf8')+';\n'
    +fs.readFileSync(path.join(base,'Web/localization.js'),'utf8')+'\n'
    +fs.readFileSync(path.join(base,'Web/client.js'),'utf8');
let server,browser,url;
const html=[
'<!doctype html><html lang="en"><body>',
'<a id="tile" href="#/list?parentId='+id+'&serverId=server">TV Guide</a>',
'<div id="screen"></div><script>',
'window.documentMarker=Math.random().toString();',
'window.ApiClient={accessToken:()=> "token",getCurrentUserId:()=> "user",serverId:()=> "server",getUrl:(p,q)=>"/"+p+(q?"?"+new URLSearchParams(q):""),getJSON:async u=>(await fetch(u)).json(),getLiveTvChannels:async q=>({Items:[],TotalRecordCount:0})};',
'function mount(){const screen=document.getElementById("screen");if(location.hash.startsWith("#/list")&&!document.getElementById("category-grid"))screen.insertAdjacentHTML("beforeend", "<div id=category-grid>Sports folder</div>");if(location.hash.startsWith("#/livetv")&&!document.querySelector(".tvguide"))screen.insertAdjacentHTML("beforeend","<div class=tvguide><div class=tvGuideHeader>Date</div><div class=programGrid>Programme timeline</div></div>");}',
'mount();window.addEventListener("hashchange",mount);',
'</script><script src="/client.js"></script></body></html>'
].join('\n');
before(async()=>{
 server=http.createServer((req,res)=>{
 const p=new URL(req.url,'http://localhost').pathname;
 if(p==='/client.js'){res.setHeader('Content-Type','application/javascript');res.end(scripts);return;}
 if(p==='/LiveTvGroups/Entry'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({ChannelId:id}));return;}
 if(p==='/LiveTvGroups/Groups'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify([{Id:'22222222-2222-2222-2222-222222222222',Name:'Sports',ChannelCount:2}]));return;}
 res.setHeader('Content-Type','text/html');res.end(html);
 });
 await new Promise(r=>server.listen(0,'127.0.0.1',r));
 url='http://127.0.0.1:'+server.address().port+'/';
 browser=await chromium.launch({headless:true});
});
after(async()=>{await browser?.close();await new Promise(r=>server.close(r));});
test('home tile opens fresh guide without category grid',async()=>{
 const page=await browser.newPage({viewport:{width:393,height:852},isMobile:true});
 try {
 await page.goto(url+'#/home');
 await page.waitForFunction(()=>document.getElementById('tile').href.includes('liveTvGroups=1'));
 const old=await page.evaluate(()=>window.documentMarker);
 await page.locator('#tile').click();
 await page.waitForFunction(previous=>window.documentMarker!==previous&&location.hash.startsWith('#/livetv'),old);
 await page.locator('.jlh-tv-categories select').waitFor();
 assert.equal(await page.locator('#category-grid').count(),0);
 assert.equal(await page.locator('.programGrid').count(),1);
 assert.deepEqual(await page.locator('.jlh-tv-categories option').allTextContents(),['All channels','Sports (2)']);
 }finally{await page.close();}
});
test('direct folder route is replaced with a clean native guide',async()=>{
 const page=await browser.newPage({viewport:{width:393,height:852}});
 try {
 await page.goto(url+'#/list?parentId='+id);
 await page.waitForURL('**/#/livetv?tab=1&serverId=server');
 await page.locator('.jlh-tv-categories select').waitFor();
 assert.equal(await page.locator('#category-grid').count(),0);
 assert.equal(await page.locator('.programGrid').count(),1);
 }finally{await page.close();}
});