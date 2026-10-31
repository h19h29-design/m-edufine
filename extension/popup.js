'use strict';
let current=null;const status=document.getElementById('status');
chrome.tabs.query({active:true,currentWindow:true}).then(tabs=>{
  current=tabs[0];try{document.getElementById('host').textContent=new URL(current.url).hostname;}catch{document.getElementById('host').textContent='기관 업무 탭에서 실행하세요.';}
});
document.getElementById('connect').onclick=async()=>{
  try {
    if(!current?.id)throw new Error('먼저 연결할 업무 탭을 열어 주세요.');
    const u=new URL(current.url);
    if(u.protocol!=='https:'||!(u.hostname.endsWith('.go.kr')||u.hostname.endsWith('.eduptl.kr')))throw new Error('기관 HTTPS 업무 화면만 연결할 수 있습니다.');
    if(!await chrome.permissions.request({origins:[u.origin+'/*']}))throw new Error('해당 기관 주소 접근이 허용되지 않았습니다.');
    const r=await chrome.runtime.sendMessage({action:'connect',tabId:current.id});
    status.textContent=r.ok?'연결 요청을 보냈습니다. Windows 앱에서 F5를 누르세요. 연결 안 됨이면 설치·기관 정책을 확인하세요.':r.error;
  }catch(e){status.textContent=e.message;}
};
document.getElementById('disconnect').onclick=async()=>{await chrome.runtime.sendMessage({action:'disconnect'});status.textContent='연결을 해제했습니다.';};
