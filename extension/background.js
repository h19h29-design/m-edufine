'use strict';
let port=null, linkedTab=null, currentLink=0;
const HOST='com.medufine.bridge';
const official = url => {
  try { const u=new URL(url);return u.protocol==='https:' && !u.username && !u.password &&
    (u.hostname.endsWith('.go.kr')||u.hostname.endsWith('.eduptl.kr')); } catch {return false;}
};
const send = message => { try {port?.postMessage(message);}catch{/* Native process disconnected. */} };
const badge = text => {chrome.action.setBadgeText({text});chrome.action.setBadgeBackgroundColor({color:'#0B776F'});};
async function usableTab() {
  if(linkedTab===null)throw new Error('연결한 탭이 없습니다. 브라우저 확장 버튼에서 이 탭 연결을 누르세요.');
  const tab=await chrome.tabs.get(linkedTab);
  if(!official(tab.url))throw new Error('기관 HTTPS 업무 화면만 연결할 수 있습니다.');
  return tab;
}
async function executeIn(frameId, request) {
  const target={tabId:linkedTab,frameIds:[frameId]};
  await chrome.scripting.executeScript({target,files:['bridge.js'],world:'ISOLATED'});
  const out=await chrome.scripting.executeScript({target,world:'ISOLATED',
    func: request=>globalThis.__mEdufineBridge(request),args:[request]});
  if(!out.length || !out[0].result)throw new Error('현재 화면에 접근할 수 없습니다.');
  return out[0].result;
}
async function handle(request) {
  const generation=currentLink;
  const tab=await usableTab();
  if(request.link!==undefined && request.link!==generation)throw new Error('연결 탭이 변경되었습니다. 다시 읽어 주세요.');
  if(request.action==='showWeb') {
    await chrome.tabs.update(tab.id,{active:true});await chrome.windows.update(tab.windowId,{focused:true});
    return {ok:true,state:'web-shown'};
  }
  if(request.action==='scan') {
    const frames=await chrome.webNavigation.getAllFrames({tabId:tab.id})||[];
    const pages=[];let inaccessible=0;
    for(const f of frames.slice(0,24)) {
      if(!official(f.url)){inaccessible++;continue;}
      if(!await chrome.permissions.contains({origins:[new URL(f.url).origin+'/*']})){inaccessible++;continue;}
      try {
        const r=await executeIn(f.frameId,{action:'scan'});
        if(r.ok)pages.push({...r,frameId:f.frameId});else inaccessible++;
      }catch{inaccessible++;}
    }
    if(generation!==currentLink)throw new Error('읽는 중 페이지가 변경되었습니다. 다시 읽어 주세요.');
    const reply={ok:true,link:generation,host:new URL(tab.url).hostname,title:String(tab.title||'').slice(0,160),
      scannedAt:new Date().toISOString(),pages,inaccessible,truncated:frames.length>24};
    while(new TextEncoder().encode(JSON.stringify(reply)).length>700000) {
      const p=pages.findLast(p=>p.tables.length||p.controls.length);
      if(!p)throw new Error('화면 정보가 너무 큽니다. 원본 웹을 사용하세요.');
      if(p.tables.length)p.tables.pop();else p.controls.splice(-30);
      reply.truncated=true;
    }
    return reply;
  }
  if(!['click','set','focus'].includes(request.action))throw new Error('지원하지 않는 명령입니다.');
  if(!Number.isInteger(request.frameId)||typeof request.docId!=='string'||typeof request.revision!=='string')throw new Error('다시 읽은 화면에서 항목을 선택하세요.');
  const frame=await chrome.webNavigation.getFrame({tabId:tab.id,frameId:request.frameId});
  if(!frame||!official(frame.url)||!await chrome.permissions.contains({origins:[new URL(frame.url).origin+'/*']}))throw new Error('허용되지 않은 프레임입니다.');
  return await executeIn(request.frameId,request);
}
function connect(tabId) {
  if(port)port.disconnect();
  linkedTab=tabId;currentLink++;
  const own=chrome.runtime.connectNative(HOST);port=own;badge('ON');
  own.onMessage.addListener(async req=>{
    if(!req || typeof req.id!=='string')return;
    try{const r=await handle(req);if(port===own)send({id:req.id,...r});}
    catch(e){if(port===own)send({id:req.id,ok:false,error:String(e.message||'연결 오류').slice(0,220)});}
  });
  own.onDisconnect.addListener(()=>{
    void chrome.runtime.lastError;
    if(port===own){port=null;linkedTab=null;badge('');}
  });
  send({event:'connected',link:currentLink});
}
chrome.runtime.onMessage.addListener((req,sender,respond)=>{
  if(sender.id!==chrome.runtime.id||sender.tab){respond({ok:false});return;}
  (async()=>{
    if(req.action==='status')return {ok:true,linked:linkedTab!==null,tabId:linkedTab};
    if(req.action==='connect') {
      const tab=await chrome.tabs.get(req.tabId);
      if(!official(tab.url))throw new Error('현재 탭이 기관의 HTTPS 업무 주소가 아닙니다.');
      const pattern=new URL(tab.url).origin+'/*';
      if(!await chrome.permissions.contains({origins:[pattern]}))throw new Error('먼저 이 기관 주소 접근을 허용해 주세요.');
      connect(tab.id);return {ok:true};
    }
    if(req.action==='disconnect'){port?.disconnect();port=null;linkedTab=null;badge('');return {ok:true};}
    throw new Error('지원하지 않는 요청입니다.');
  })().then(respond,e=>respond({ok:false,error:String(e.message)}));return true;
});
chrome.webNavigation.onCommitted.addListener(d=>{
  if(d.tabId===linkedTab){currentLink++;send({event:'invalidated',link:currentLink});}
});
chrome.tabs.onRemoved.addListener(id=>{if(id===linkedTab){send({event:'invalidated'});port?.disconnect();port=null;linkedTab=null;badge('');}});
