// Read-only inspection of the dedicated Edge QA tab. No Meta inference.
import {mkdir,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const ui='http://127.0.0.1:4300',api='http://127.0.0.1:5391';
const output=fileURLToPath(new URL('../.private/edge-inspection/',import.meta.url));
async function json(url) {
  const r=await fetch(url,{signal:AbortSignal.timeout(10000)});
  if(!r.ok)throw Error('GET '+new URL(url).pathname+': '+r.status);
  return r.json();
}
const before=await json(api+'/api/meta/readiness');
const tabs=await json('http://127.0.0.1:9237/json/list');
const target=tabs.find(t=>t.type==='page'&&new URL(t.url).origin===ui);
if(!target)throw Error('Open ProofLab in the dedicated Edge QA instance on port 9237.');
const ws=new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.onerror=reject;});
let sequence=0,intercepting=false;
const pending=new Map(),blocked=[],errors=[];
function send(method,params={}) {
  return new Promise((resolve,reject)=>{
    const id=++sequence;
    const timer=setTimeout(()=>{pending.delete(id);reject(Error('CDP timeout: '+method));},15000);
    pending.set(id,{resolve,reject,timer});
    ws.send(JSON.stringify({id,method,params}));
  });
}
ws.onmessage=event=>{
  const m=JSON.parse(event.data);
  if(m.id) {
    const p=pending.get(m.id);if(!p)return;
    clearTimeout(p.timer);pending.delete(m.id);
    if(m.error)p.reject(Error(m.error.message));else p.resolve(m.result);
  }
  if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.text);
  if(m.method==='Fetch.requestPaused') {
    const r=m.params.request,u=new URL(r.url);
    const allowed=[ui,api].includes(u.origin)&&['GET','HEAD','OPTIONS'].includes(r.method);
    if(!allowed)blocked.push({method:r.method,path:u.pathname});
    send(allowed?'Fetch.continueRequest':'Fetch.failRequest',{
      requestId:m.params.requestId,...(allowed?{}:{errorReason:'BlockedByClient'})
    }).catch(()=>errors.push('Could not resolve an intercepted request.'));
  }
};
ws.onclose=()=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Edge disconnected'));}pending.clear();};
try {
  await send('Runtime.enable');await send('Page.enable');
  await send('Fetch.enable',{patterns:[{urlPattern:'*',requestStage:'Request'}]});intercepting=true;
  const observed=await send('Runtime.evaluate',{
    expression:"({title:document.title,width:innerWidth,height:innerHeight,noOverflow:document.documentElement.scrollWidth<=innerWidth,viewCount:document.querySelectorAll('.view-panel').length,videos:Array.from(document.querySelectorAll('video')).map(v=>({readyState:v.readyState,currentTime:v.currentTime,paused:v.paused,hasMediaError:!!v.error}))})",
    returnByValue:true
  });
  if(observed.exceptionDetails)throw Error('DOM inspection failed.');
  const screenshot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
  const after=await json(api+'/api/meta/readiness');
  const stable=Number.isInteger(before.usedRequests)&&before.usedRequests===after.usedRequests;
  const result={checkedAtUtc:new Date().toISOString(),...observed.result.value,
    usedRequestsBefore:before.usedRequests,usedRequestsAfter:after.usedRequests,
    counterUnchanged:stable,blocked,errors,
    scope:'Current page inspection only; no playback, physics or new Meta inference validated.'};
  await mkdir(output,{recursive:true});
  const stamp=new Date().toISOString().replaceAll(':','-').replaceAll('.','-');
  await writeFile(output+stamp+'.png',Buffer.from(screenshot.data,'base64'));
  await writeFile(output+stamp+'.json',JSON.stringify(result,null,2));
  console.log(JSON.stringify(result));
  if(!stable||blocked.length||errors.length)process.exitCode=1;
} finally {
  try {if(intercepting&&ws.readyState===WebSocket.OPEN)await send('Fetch.disable');}
  finally {ws.close();}
}
