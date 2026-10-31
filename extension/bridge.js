/* Runs in the extension's ISOLATED world, not in the website's JavaScript world.
   No server API calls, cookie access, network traffic, logging or local persistence. */
(() => {
  'use strict';
  if (globalThis.__mEdufineBridge) return;
  const docId = crypto.randomUUID();
  let revision = '', elements = new Map();
  const sensitive = /password|passwd|token|secret|비밀번호|인증서|주민등록|계좌번호|보안카드|인증번호/i;
  const highRisk = /결재|승인|반려|송금|지급|삭제|폐기|불용결정|관리전환확정|계약체결/;
  const clean = (v,n=180) => String(v ?? '').replace(/\s+/g,' ').trim().slice(0,n);
  const mask = v => clean(v).replace(/\b\d{6}[- ]?[1-8]\d{6}\b/g,'[민감정보 숨김]');
  const visible = e => {
    if (!e?.isConnected || !e.getClientRects().length || e.closest('[hidden],[inert],[aria-hidden="true"]')) return false;
    const s=getComputedStyle(e);return s.visibility!=='hidden' && s.display!=='none' && s.opacity!=='0';
  };
  const menuLike = e => {
    // Enterprise portals often attach delegated click handlers to LI/SPAN/DIV rather than anchors.
    // Discover only visible, short labels in navigation-like regions or with pointer/keyboard semantics.
    if (!/^(LI|SPAN|DIV|TD|P|STRONG|B)$/.test(e.tagName)) return false;
    if (e.closest('table,[role="grid"],[role="table"],[contenteditable="true"]')) return false;
    const txt = clean(e.innerText,160);
    if (!txt || txt.length > 64 || txt.includes('\n') || e.children.length > 6) return false;
    const box=e.getBoundingClientRect();
    if (box.width<8 || box.height<8 || box.height>90 || box.width>1600) return false;
    const className=typeof e.className==='string'?e.className:'';
    const hints=[className,e.id,e.parentElement?.className,e.parentElement?.id].map(x=>typeof x==='string'?x:'').join(' ');
    const contextual=!!e.closest('nav,aside,[role="navigation"],[role="menu"],[role="menubar"],[class*="menu"],[class*="Menu"],[id*="menu"],[id*="Menu"],[class*="gnb"],[class*="Gnb"],[class*="lnb"],[class*="Lnb"],[class*="tab"],[class*="Tab"]');
    const explicit=e.hasAttribute('onclick')||e.hasAttribute('role')||e.tabIndex>=0;
    const pointer=getComputedStyle(e).cursor==='pointer';
    return (contextual && (pointer || explicit || /menu|tab|gnb|lnb|nav|item|depth|node|tree/i.test(hints))) || (pointer && /menu|tab|gnb|lnb|nav/i.test(hints));
  };
  const label = e => {
    const named=e.getAttribute('aria-labelledby');
    const linked=named ? named.split(/\s+/).map(id=>document.getElementById(id)?.textContent||'').join(' ') : '';
    return clean(e.getAttribute('aria-label') || linked || (e.labels ? [...e.labels].map(l=>l.textContent).join(' ') : '') ||
      e.getAttribute('title') || e.getAttribute('placeholder') ||
      ((e.tagName==='INPUT' && /button|submit|reset/.test(e.type)) ? e.value : e.innerText) ||
      e.getAttribute('name') || e.id || e.tagName);
  };
  const secret = e => e.type==='password' || e.type==='hidden' || e.type==='file' ||
    sensitive.test([e.getAttribute('name'),e.id,e.autocomplete,label(e)].join(' '));
  const editable = e => /^(INPUT|TEXTAREA|SELECT)$/.test(e.tagName) && !/^(button|submit|reset|image|hidden|password|file)$/.test(e.type||'');
  const value = e => secret(e)?'[보호됨]':e.type==='checkbox'||e.type==='radio' ? (e.checked?'선택':'해제') :
    e.tagName==='SELECT' ? clean([...e.selectedOptions].map(o=>o.label).join(', ')) : editable(e)?clean(e.value,1000):'';
  const signature = e => [e.tagName,e.type||'',label(e),e.getAttribute('href')||'',editable(e)?String(e.value):value(e),String(!!e.checked)].join('\u001f');
  const kind = e => e.tagName==='SELECT'?'선택':e.type==='checkbox' || e.type==='radio'?'체크':editable(e)?'입력':
    e.tagName==='A' || menuLike(e) || ['menuitem','tab','treeitem','link','option'].includes(e.getAttribute('role'))?'메뉴':'버튼';
  const supportedEdit = e => e.tagName==='TEXTAREA' || e.tagName==='SELECT' && !e.multiple ||
    e.tagName==='INPUT' && ['text','search','email','url','tel','number','date','month','week','time','datetime-local'].includes(e.type);
  const collectRoots = () => {
    const roots=[document];
    // Open shadow roots only. Closed roots, canvas and inaccessible frames are never bypassed.
    for(let i=0;i<roots.length && i<40;i++) for(const e of roots[i].querySelectorAll('*')) if(e.shadowRoot) roots.push(e.shadowRoot);
    return roots;
  };
  function scan() {
    revision=crypto.randomUUID(); elements=new Map();
    const controls=[], tables=[];let truncated=false, hiddenProtected=0;
    const selector='a,button,input,textarea,select,[role="button"],[role="tab"],[role="treeitem"],[role="menuitem"],[role="link"],[role="option"],[onclick],nav li,nav span,nav div,aside li,aside span,[class*="menu"] li,[class*="menu"] span,[class*="Menu"] li,[class*="Menu"] span,[class*="tab"] li,[class*="tab"] span,[class*="Tab"] li,[class*="Tab"] span,[class*="gnb"] li,[class*="lnb"] li,[id*="menu"] li,[id*="tab"] li,li,span,div';
    const seen=new Set();
    for(const root of collectRoots()) {
      for(const e of root.querySelectorAll(selector)) {
        if(seen.has(e)||!visible(e)) continue;seen.add(e);
        const nativeAction=/^(A|BUTTON|INPUT|TEXTAREA|SELECT)$/.test(e.tagName);
        if(!nativeAction && !e.hasAttribute('onclick') && !['button','tab','treeitem','menuitem','link','option'].includes(e.getAttribute('role')) && !menuLike(e)) continue;
        if(secret(e)){hiddenProtected++;continue;}
        // A nested label and its outer menu item must not both appear, but keep parent tabs
        // whose children only contain inert text.
        if(!nativeAction && [...e.querySelectorAll('a,button,[role="button"],[role="menuitem"],[onclick]')].some(visible)) continue;
        const name=mask(label(e));if(!name || (!nativeAction && name.length>64))continue;
        if(controls.length>=400){truncated=true;break;}
        const id=String(controls.length+1);
        const raw=String(e.value??'');
        const mayEdit=supportedEdit(e) && raw.length<=4000 && !/\b\d{6}[- ]?[1-8]\d{6}\b/.test(raw);
        const item={id,kind:kind(e),label:name,value:mask(value(e)),editValue:mayEdit?raw:'',editable:mayEdit,
          disabled:!!e.disabled||e.getAttribute('aria-disabled')==='true'||!!e.readOnly,
          webOnly:highRisk.test(label(e))||e.type==='file',
          options:e.tagName==='SELECT'?[...e.options].slice(0,200).map(o=>({value:o.value,label:mask(o.label),disabled:o.disabled})):[]};
        elements.set(id,{e,sig:signature(e)});controls.push(item);
      }
      for(const table of root.querySelectorAll('table,[role="grid"],[role="table"]')) {
        if(!visible(table)||tables.length>=6)continue;
        const rows=[...table.querySelectorAll('tr,[role="row"]')].filter(visible);
        if(!rows.length)continue;
        const heads=[...rows[0].querySelectorAll('th,[role="columnheader"]')];
        const protectedCols=heads.map(h=>sensitive.test(h.innerText));
        const body=rows.slice(0,120).map(row=>[...row.querySelectorAll('th,td,[role="cell"],[role="gridcell"],[role="columnheader"]')]
          .slice(0,30).map((cell,i)=>protectedCols[i]||secret(cell)?'[보호됨]':mask(cell.innerText)));
        if(body.some(r=>r.length))tables.push({name:mask(table.getAttribute('aria-label')||table.caption?.innerText||`표 ${tables.length+1}`),rows:body});
        if(rows.length>120)truncated=true;
      }
    }
    return {ok:true,docId,revision,host:location.hostname,title:mask(document.title),controls,tables,
      protectedCount:hiddenProtected,truncated,
      notice:'현재 DOM에 표시된 자료만 읽었습니다. 서버 재조회·전체 자료 동기화가 아닙니다.'};
  }
  function act(req) {
    if(req.docId!==docId || req.revision!==revision)return {ok:false,error:'화면이 변경되었습니다. F5로 다시 읽어 주세요.'};
    const entry=elements.get(String(req.targetId));
    if(!entry || !visible(entry.e) || signature(entry.e)!==entry.sig)return {ok:false,error:'대상 또는 값이 변경되었습니다. 다시 읽은 뒤 실행해 주세요.'};
    const e=entry.e;
    if(secret(e))return {ok:false,error:'인증·보호 입력은 원본 웹에서 직접 처리해 주세요.'};
    if(e.disabled||e.readOnly||e.getAttribute('aria-disabled')==='true')return {ok:false,error:'사용할 수 없는 항목입니다.'};
    if(!/^(A|BUTTON|INPUT|TEXTAREA|SELECT)$/.test(e.tagName)&&!menuLike(e)&&!e.hasAttribute('onclick')&&!['button','tab','treeitem','menuitem','link','option'].includes(e.getAttribute('role')))return {ok:false,error:'메뉴가 변경되었습니다. 원본 웹에서 다시 확인해 주세요.'};
    if(req.action==='focus'){e.scrollIntoView({block:'center'});e.focus();return {ok:true,state:'focused'};}
    if(!req.confirmed)return {ok:false,error:'실행 확인이 필요합니다.'};
    if(highRisk.test(label(e)))return {ok:false,error:'결재·지급·삭제 등은 F4 원본 웹에서 최종 처리해 주세요.'};
    if(req.action==='set') {
      if(!supportedEdit(e))return {ok:false,error:'이 입력 형식은 원본 웹에서 처리해 주세요.'};
      const next=String(req.value??'');
      if(next.length>4000)return {ok:false,error:'입력값이 너무 깁니다.'};
      if(e.maxLength>=0 && next.length>e.maxLength)return {ok:false,error:'웹 입력란의 최대 글자수를 초과했습니다.'};
      if(e.tagName==='SELECT' && ![...e.options].some(o=>o.value===next&&!o.disabled))return {ok:false,error:'선택 가능한 옵션이 아닙니다.'};
      e.focus();
      const proto=e.tagName==='SELECT'?HTMLSelectElement.prototype:e.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;
      Object.getOwnPropertyDescriptor(proto,'value').set.call(e,next);
      e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));
      revision='';
      return {ok:true,state:'input-applied',notice:'웹 입력란에 전달했습니다. 자동 저장 여부와 서버 반영은 원본 화면에서 확인하세요.'};
    }
    if(req.action==='click') {
      if(editable(e)&&e.type!=='checkbox'&&e.type!=='radio')return {ok:false,error:'입력 항목은 편집 기능을 사용해 주세요.'};
      // Never execute javascript: URLs or custom protocols through the bridge.
      if(e.tagName==='A') {
        const raw=e.getAttribute('href')||'';
        if(!raw.startsWith('#')) {
          let u;try{u=new URL(raw,location.href);}catch{return {ok:false,error:'유효하지 않은 링크입니다.'};}
          if(u.protocol!=='https:')return {ok:false,error:'스크립트·외부 앱 링크는 원본 웹에서 실행하세요.'};
        }
      }
      revision='';e.focus();e.click();
      return {ok:true,state:'click-dispatched',notice:'웹 클릭을 전달했습니다. 저장 성공을 뜻하지 않습니다. 웹 결과를 확인하세요.'};
    }
    return {ok:false,error:'지원하지 않는 작업입니다.'};
  }
  Object.defineProperty(globalThis,'__mEdufineBridge',{value:req=>{
    try {return req?.action==='scan'?scan():act(req||{});}
    catch {return {ok:false,error:'이 화면을 읽거나 제어하지 못했습니다. F4로 원본 웹을 사용하세요.'};}
  },configurable:false});
})();
