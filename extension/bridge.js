/* Runs in the extension's ISOLATED world, not in the website's JavaScript world.
   No server API calls, cookie access, network traffic, logging or local persistence.

   v0.3.0 adds hierarchical menu extraction for enterprise portals that use
   DIV/SPAN/LI with delegated click handlers instead of semantic buttons. */
(() => {
  'use strict';
  if (globalThis.__mEdufineBridge) return;
  const docId = crypto.randomUUID();
  let revision = '', elements = new Map(), menuIds = new Set();
  const sensitive = /password|passwd|token|secret|비밀번호|인증서|주민등록|계좌번호|보안카드|인증번호/i;
  const highRisk = /결재|승인|반려|송금|지급|삭제|폐기|불용결정|관리전환확정|계약체결/;
  const clean = (v, n = 180) => String(v ?? '').replace(/\s+/g, ' ').trim().slice(0, n);
  const mask = v => clean(v).replace(/\b\d{6}[- ]?[1-8]\d{6}\b/g, '[민감정보 숨김]');
  const visible = e => {
    if (!e?.isConnected || !e.getClientRects().length || e.closest('[hidden],[inert],[aria-hidden="true"]')) return false;
    const s = getComputedStyle(e);
    return s.visibility !== 'hidden' && s.display !== 'none' && s.opacity !== '0';
  };
  const menuLike = e => {
    // Enterprise portals often attach delegated click handlers to LI/SPAN/DIV rather than anchors.
    // Discover only visible, short labels in navigation-like regions or with pointer/keyboard semantics.
    if (!/^(LI|SPAN|DIV|TD|P|STRONG|B)$/.test(e.tagName)) return false;
    if (e.closest('table,[role="grid"],[role="table"],[contenteditable="true"]')) return false;
    const txt = clean(e.innerText, 160);
    if (!txt || txt.length > 64 || txt.includes('\n') || e.children.length > 6) return false;
    const box = e.getBoundingClientRect();
    if (box.width < 8 || box.height < 8 || box.height > 90 || box.width > 1600) return false;
    const className = typeof e.className === 'string' ? e.className : '';
    const hints = [className, e.id, e.parentElement?.className, e.parentElement?.id].map(x => typeof x === 'string' ? x : '').join(' ');
    const contextual = !!e.closest('nav,aside,[role="navigation"],[role="menu"],[role="menubar"],[class*="menu"],[class*="Menu"],[id*="menu"],[id*="Menu"],[class*="gnb"],[class*="Gnb"],[class*="lnb"],[class*="Lnb"],[class*="tab"],[class*="Tab"]');
    const explicit = e.hasAttribute('onclick') || e.hasAttribute('role') || e.tabIndex >= 0;
    const pointer = getComputedStyle(e).cursor === 'pointer';
    return (contextual && (pointer || explicit || /menu|tab|gnb|lnb|nav|item|depth|node|tree/i.test(hints))) || (pointer && /menu|tab|gnb|lnb|nav/i.test(hints));
  };
  const label = e => {
    const named = e.getAttribute('aria-labelledby');
    const linked = named ? named.split(/\s+/).map(id => document.getElementById(id)?.textContent || '').join(' ') : '';
    const imgAlt = /^(A|BUTTON|LI|SPAN|DIV)$/.test(e.tagName)
      ? [...e.querySelectorAll('img[alt]')].map(i => i.alt).join(' ') : '';
    const near = /^(INPUT|SELECT|TEXTAREA)$/.test(e.tagName) && e.previousElementSibling && e.previousElementSibling.children.length === 0
      ? clean(e.previousElementSibling.textContent, 40) : '';
    return clean(e.getAttribute('aria-label') || linked || (e.labels ? [...e.labels].map(l => l.textContent).join(' ') : '') ||
      imgAlt || near || e.getAttribute('title') || e.getAttribute('placeholder') ||
      ((e.tagName === 'INPUT' && /button|submit|reset/.test(e.type)) ? e.value : e.innerText) ||
      e.getAttribute('name') || e.id || e.tagName);
  };
  // Engineering names like "mainframe.MainVFrameSet.TopFrame.form.divTopGrp.f" are not
  // readable labels; present them as a generic input with a short technical tail.
  const friendly = (name, kindName) => {
    if (!/[.]/.test(name) || /[가-힣]/.test(name)) return name;
    const tail = name.split('.').filter(Boolean).pop() || name;
    return `${kindName} (${tail.slice(0, 28)})`;
  };
  const secret = e => e.type === 'password' || e.type === 'hidden' || e.type === 'file' ||
    sensitive.test([e.getAttribute('name'), e.id, e.autocomplete, label(e)].join(' '));
  const editable = e => /^(INPUT|TEXTAREA|SELECT)$/.test(e.tagName) && !/^(button|submit|reset|image|hidden|password|file)$/.test(e.type || '');
  const value = e => secret(e) ? '[보호됨]' : e.type === 'checkbox' || e.type === 'radio' ? (e.checked ? '선택' : '해제') :
    e.tagName === 'SELECT' ? clean([...e.selectedOptions].map(o => o.label).join(', ')) : editable(e) ? clean(e.value, 1000) : '';
  const signature = e => [e.tagName, e.type || '', label(e), e.getAttribute('href') || '', editable(e) ? String(e.value) : value(e), String(!!e.checked)].join('\u001f');
  const kind = e => e.tagName === 'SELECT' ? '선택' : e.type === 'checkbox' || e.type === 'radio' ? '체크' : editable(e) ? '입력' :
    e.tagName === 'A' || menuLike(e) || ['menuitem', 'tab', 'treeitem', 'link', 'option'].includes(e.getAttribute('role')) ? '메뉴' : '버튼';
  const supportedEdit = e => e.tagName === 'TEXTAREA' || e.tagName === 'SELECT' && !e.multiple ||
    e.tagName === 'INPUT' && ['text', 'search', 'email', 'url', 'tel', 'number', 'date', 'month', 'week', 'time', 'datetime-local'].includes(e.type);
  const collectRoots = () => {
    const roots = [document];
    // Open shadow roots only. Closed roots, canvas and inaccessible frames are never bypassed.
    for (let i = 0; i < roots.length && i < 40; i++) for (const e of roots[i].querySelectorAll('*')) if (e.shadowRoot) roots.push(e.shadowRoot);
    return roots;
  };

  /* ---------------------------------------------------------------- menus */
  const NAV_HINT = /(^|[\s_-])(gnb|lnb|menu|nav|tab|tree|sidebar|depth|sitemap|sub|local|top)([\s_-]|$)/i;
  const ACTIVE_CLASS = /(^|[\s_-])(active|on|selected|current|sel|focus)([\s_-]|$)/i;
  const inGrid = e => !!e.closest('table,[role="grid"],[role="table"]');
  const navRegion = e => {
    if (e.tagName === 'NAV') return true;
    if (['navigation', 'menu', 'menubar', 'tablist', 'tree'].includes(e.getAttribute('role'))) return true;
    if (inGrid(e)) return false;
    const hint = [typeof e.className === 'string' ? e.className : '', e.id,
      typeof e.parentElement?.className === 'string' ? e.parentElement.className : '', e.parentElement?.id]
      .map(x => typeof x === 'string' ? x : '').join(' ');
    return NAV_HINT.test(hint);
  };
  const ownText = e => {
    let t = '';
    for (const n of e.childNodes) if (n.nodeType === 3) t += n.nodeValue;
    return clean(t, 80);
  };
  // A branch item's own title is either its direct text or a direct child that
  // contains no further menu candidate (e.g. <li><span>업무관리</span><ul>…).
  const titleChild = (e, all) => {
    for (const c of e.children) {
      if (all.some(o => o !== c && c.contains(o))) continue;
      const t = clean(c.innerText, 48);
      if (t && t.length <= 40 && !t.includes('\n')) return t;
    }
    return '';
  };
  const actionable = e => /^(A|BUTTON)$/.test(e.tagName) || e.hasAttribute('onclick') || e.tabIndex >= 0 ||
    ['menuitem', 'treeitem', 'tab', 'link', 'option', 'button'].includes(e.getAttribute('role')) ||
    getComputedStyle(e).cursor === 'pointer';
  const isActive = e => {
    if (e.getAttribute('aria-selected') === 'true') return true;
    const cur = e.getAttribute('aria-current');
    if (cur && cur !== 'false') return true;
    for (const n of [e, e.parentElement]) {
      if (!n) continue;
      const c = typeof n.className === 'string' ? n.className : '';
      if (ACTIVE_CLASS.test(c)) return true;
      if (n.getAttribute?.('aria-selected') === 'true') return true;
    }
    return false;
  };
  const regionName = (el, region) => {
    const labelledBy = region.getAttribute('aria-labelledby');
    const raw = region.getAttribute('aria-label') || (labelledBy ? document.getElementById(labelledBy)?.textContent || '' : '');
    if (clean(raw)) return clean(raw, 40);
    const heading = region.querySelector('h1,h2,h3,h4,h5,legend,caption,[class*="title"],[class*="Title"]');
    if (heading && heading !== el) {
      const t = clean(heading.textContent, 40);
      if (t && t.length <= 30) return t;
    }
    const c = typeof region.className === 'string' ? region.className : '';
    return clean(c.split(/\s+/)[0] || region.id || '', 40);
  };
  function scanMenus() {
    const regions = [];
    for (const root of collectRoots()) {
      for (const e of root.querySelectorAll('nav,aside,[role="navigation"],[role="menu"],[role="menubar"],[role="tablist"],[role="tree"],[class*="menu"],[class*="Menu"],[class*="gnb"],[class*="lnb"],[class*="tab"],[class*="tree"],[id*="menu"],[id*="Menu"],[id*="gnb"],[id*="lnb"]')) {
        if (!navRegion(e) || !visible(e)) continue;
        if (regions.some(r => r.contains(e))) continue; // keep the outermost region
        regions.push(e);
      }
    }
    const raw = new Set(), regionOf = new Map();
    for (const region of regions) {
      for (const e of region.querySelectorAll('a,button,[role="menuitem"],[role="treeitem"],[role="tab"],[role="link"],[onclick],[tabindex],li,span,div,dt,dd,p,strong,b')) {
        if (raw.has(e) || !visible(e)) continue;
        if (/^(INPUT|SELECT|TEXTAREA)$/.test(e.tagName)) continue;
        const txt = clean(e.innerText, 160);
        // Image-only navigation items expose their name through alt/aria-label.
        const text = txt || (actionable(e) ? clean(label(e), 64) : '');
        if (!text || text.includes('\n') || text.length > 120) continue;
        raw.add(e); regionOf.set(e, region);
      }
    }
    const arr = [...raw];
    const hasDesc = new Map();
    for (const e of arr) hasDesc.set(e, arr.some(o => o !== e && e.contains(o)));
    // Label: leaf -> its whole short text; branch -> its own title only, never the
    // concatenation of all descendants.
    const labelOf = new Map();
    for (const e of arr) {
      if (!hasDesc.get(e)) { labelOf.set(e, clean(label(e), 64)); continue; }
      const t = ownText(e) || titleChild(e, arr);
      labelOf.set(e, t && t.length <= 64 ? t : '');
    }
    const cand = arr.filter(e => {
      const name = labelOf.get(e);
      if (!name) return false;
      if (hasDesc.get(e)) return actionable(e) || name.length > 0;
      return actionable(e) || menuLike(e) || ['menuitem', 'treeitem', 'tab', 'link', 'option'].includes(e.getAttribute('role'));
    });
    // 1) Drop a wrapper that repeats its only nested item.
    // 2) Drop a title span whose parent branch already uses the same label.
    const drop = new Set();
    for (const e of cand) {
      const inner = cand.filter(o => o !== e && e.contains(o));
      if (inner.length === 1 && clean(e.innerText, 120) === clean(inner[0].innerText, 120)) { drop.add(e); continue; }
      if (inner.length === 0) {
        let p = e.parentElement, guard = 0;
        while (p && guard++ < 6) {
          if (cand.includes(p) && !drop.has(p) && labelOf.get(p) && labelOf.get(p) === labelOf.get(e)) { drop.add(e); break; }
          p = p.parentElement;
        }
      }
    }
    const kept = cand.filter(e => !drop.has(e));
    const parentOf = e => {
      let p = e.parentElement;
      while (p) { if (kept.includes(p) && regionOf.get(p) === regionOf.get(e)) return p; p = p.parentElement; }
      return null;
    };
    const menus = [], seen = new Set();
    for (const e of kept) {
      if (menus.length >= 300) break;
      const chain = []; let p = parentOf(e), guard = 0;
      while (p && guard++ < 6) { chain.unshift(labelOf.get(p) || ''); p = parentOf(p); }
      const name = mask(labelOf.get(e) || '');
      if (!name) continue;
      // A label that is just the element's own class/id is a technical fallback, not a menu.
      const cls = clean(typeof e.className === 'string' ? e.className.split(/\s+/)[0] : '', 64);
      if (name === cls || name === clean(e.id, 64)) continue;
      const branch = kept.some(o => o !== e && e.contains(o));
      const path = [...chain.filter(Boolean), name];
      const key = path.join('\u001f') + '\u001e' + (regionName(e, regionOf.get(e)) || '');
      if (seen.has(key)) continue;
      seen.add(key);
      const id = 'm' + (menus.length + 1);
      elements.set(id, { e, sig: signature(e) });
      menuIds.add(id);
      menus.push({
        id, label: name, level: path.length - 1, path, hasChildren: branch,
        active: isActive(e),
        expanded: e.getAttribute('aria-expanded') !== 'false' && branch,
        region: regionName(e, regionOf.get(e)),
        kind: '메뉴'
      });
    }
    return menus;
  }

  /* ------------------------------------------------------------- controls */
  function scanControls() {
    const controls = [], tables = []; let truncated = false, hiddenProtected = 0;
    const seenControlKeys = new Set();
    const selector = 'a,button,input,textarea,select,[role="button"],[role="tab"],[role="treeitem"],[role="menuitem"],[role="link"],[role="option"],[onclick],nav li,nav span,nav div,aside li,aside span,[class*="menu"] li,[class*="menu"] span,[class*="Menu"] li,[class*="Menu"] span,[class*="tab"] li,[class*="tab"] span,[class*="Tab"] li,[class*="Tab"] span,[class*="gnb"] li,[class*="lnb"] li,[id*="menu"] li,[id*="tab"] li,li,span,div';
    const seen = new Set();
    for (const root of collectRoots()) {
      for (const e of root.querySelectorAll(selector)) {
        if (seen.has(e) || !visible(e)) continue; seen.add(e);
        const nativeAction = /^(A|BUTTON|INPUT|TEXTAREA|SELECT)$/.test(e.tagName);
        if (!nativeAction && !e.hasAttribute('onclick') && !['button', 'tab', 'treeitem', 'menuitem', 'link', 'option'].includes(e.getAttribute('role')) && !menuLike(e)) continue;
        if (secret(e)) { hiddenProtected++; continue; }
        // A nested label and its outer menu item must not both appear, but keep parent tabs
        // whose children only contain inert text.
        if (!nativeAction && [...e.querySelectorAll('a,button,[role="button"],[role="menuitem"],[onclick]')].some(visible)) continue;
        const name = friendly(mask(label(e)), kind(e) === '입력' ? '입력란' : kind(e));
        if (!name || (!nativeAction && name.length > 64)) continue;
        const dedupeKey = `${kind(e)}|${name}|${mask(value(e))}`;
        if (seenControlKeys.has(dedupeKey)) continue;
        seenControlKeys.add(dedupeKey);
        if (controls.length >= 400) { truncated = true; break; }
        const id = String(controls.length + 1);
        const rawValue = String(e.value ?? '');
        const mayEdit = supportedEdit(e) && rawValue.length <= 4000 && !/\b\d{6}[- ]?[1-8]\d{6}\b/.test(rawValue);
        const context = mask(clean(e.closest('nav,section,form,fieldset,article,[role="main"],[role="region"]')?.getAttribute('aria-label') || '', 48));
        const item = {
          id, kind: kind(e), label: name, value: mask(value(e)), editValue: mayEdit ? rawValue : '', editable: mayEdit,
          disabled: !!e.disabled || e.getAttribute('aria-disabled') === 'true' || !!e.readOnly,
          webOnly: highRisk.test(label(e)) || e.type === 'file',
          context,
          options: e.tagName === 'SELECT' ? [...e.options].slice(0, 200).map(o => ({ value: o.value, label: mask(o.label), disabled: o.disabled })) : []
        };
        elements.set(id, { e, sig: signature(e) }); controls.push(item);
      }
      for (const table of root.querySelectorAll('table,[role="grid"],[role="table"]')) {
        if (!visible(table) || tables.length >= 6) continue;
        const rows = [...table.querySelectorAll('tr,[role="row"]')].filter(visible);
        if (!rows.length) continue;
        const heads = [...rows[0].querySelectorAll('th,[role="columnheader"]')];
        const protectedCols = heads.map(h => sensitive.test(h.innerText));
        const columns = heads.slice(0, 30).map((h, i) => protectedCols[i] ? '[보호됨]' : mask(h.innerText));
        const body = rows.slice(0, 120).map(row => [...row.querySelectorAll('th,td,[role="cell"],[role="gridcell"],[role="columnheader"]')]
          .slice(0, 30).map((cell, i) => protectedCols[i] || secret(cell) ? '[보호됨]' : mask(cell.innerText)));
        if (body.some(r => r.length)) tables.push({
          name: mask(table.getAttribute('aria-label') || table.caption?.innerText || `표 ${tables.length + 1}`),
          columns, rows: body
        });
        if (rows.length > 120) truncated = true;
      }
    }
    return { controls, tables, truncated, hiddenProtected };
  }

  function scan() {
    revision = crypto.randomUUID(); elements = new Map(); menuIds = new Set();
    const body = scanControls();
    const menus = scanMenus();
    return {
      ok: true, docId, revision, host: location.hostname, title: mask(document.title),
      controls: body.controls, tables: body.tables, menus,
      activeMenu: menus.find(m => m.active)?.path?.join(' > ') || '',
      protectedCount: body.hiddenProtected, truncated: body.truncated || menus.length >= 300,
      notice: '현재 DOM에 표시된 자료만 읽었습니다. 서버 재조회·전체 자료 동기화가 아닙니다.'
    };
  }

  // Faithful pointer/mouse/click sequence: enterprise portals often listen on
  // mousedown or pointerup through a delegated parent handler.
  function press(e) {
    try { e.scrollIntoView({ block: 'nearest' }); } catch { }
    try { e.focus({ preventScroll: true }); } catch { }
    const box = e.getBoundingClientRect();
    const base = { bubbles: true, cancelable: true, view: window, button: 0, buttons: 1,
      clientX: Math.round(box.left + box.width / 2), clientY: Math.round(box.top + box.height / 2) };
    const fire = (Ctor, type, extra) => { try { e.dispatchEvent(new Ctor(type, { ...base, ...extra })); } catch { } };
    fire(PointerEvent, 'pointerdown');
    fire(MouseEvent, 'mousedown');
    fire(PointerEvent, 'pointerup', { buttons: 0 });
    fire(MouseEvent, 'mouseup', { buttons: 0 });
    e.click();
  }

  function act(req) {
    if (req.docId !== docId || req.revision !== revision) return { ok: false, error: '화면이 변경되었습니다. F5로 다시 읽어 주세요.' };
    const entry = elements.get(String(req.targetId));
    if (!entry || !visible(entry.e) || signature(entry.e) !== entry.sig) return { ok: false, error: '대상 또는 값이 변경되었습니다. 다시 읽은 뒤 실행해 주세요.' };
    const e = entry.e;
    if (secret(e)) return { ok: false, error: '인증·보호 입력은 원본 웹에서 직접 처리해 주세요.' };
    if (e.disabled || e.readOnly || e.getAttribute('aria-disabled') === 'true') return { ok: false, error: '사용할 수 없는 항목입니다.' };
    const allowed = /^(A|BUTTON|INPUT|TEXTAREA|SELECT)$/.test(e.tagName) || menuIds.has(String(req.targetId)) || menuLike(e) ||
      e.hasAttribute('onclick') || ['button', 'tab', 'treeitem', 'menuitem', 'link', 'option'].includes(e.getAttribute('role'));
    if (!allowed) return { ok: false, error: '메뉴가 변경되었습니다. 원본 웹에서 다시 확인해 주세요.' };
    if (req.action === 'focus') { e.scrollIntoView({ block: 'center' }); e.focus(); return { ok: true, state: 'focused' }; }
    if (!req.confirmed) return { ok: false, error: '실행 확인이 필요합니다.' };
    if (highRisk.test(label(e))) return { ok: false, error: '결재·지급·삭제 등은 F4 원본 웹에서 최종 처리해 주세요.' };
    if (req.action === 'set') {
      if (!supportedEdit(e)) return { ok: false, error: '이 입력 형식은 원본 웹에서 처리해 주세요.' };
      const next = String(req.value ?? '');
      if (next.length > 4000) return { ok: false, error: '입력값이 너무 깁니다.' };
      if (e.maxLength >= 0 && next.length > e.maxLength) return { ok: false, error: '웹 입력란의 최대 글자수를 초과했습니다.' };
      if (e.tagName === 'SELECT' && ![...e.options].some(o => o.value === next && !o.disabled)) return { ok: false, error: '선택 가능한 옵션이 아닙니다.' };
      e.focus();
      const proto = e.tagName === 'SELECT' ? HTMLSelectElement.prototype : e.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      Object.getOwnPropertyDescriptor(proto, 'value').set.call(e, next);
      e.dispatchEvent(new Event('input', { bubbles: true })); e.dispatchEvent(new Event('change', { bubbles: true }));
      revision = '';
      return { ok: true, state: 'input-applied', notice: '웹 입력란에 전달했습니다. 자동 저장 여부와 서버 반영은 원본 화면에서 확인하세요.' };
    }
    if (req.action === 'click') {
      if (editable(e) && e.type !== 'checkbox' && e.type !== 'radio') return { ok: false, error: '입력 항목은 편집 기능을 사용해 주세요.' };
      // Block links that would hand off to an external app or a non-HTTPS page.
      // Same-document links (#, javascript:) are the site's own menu handlers and are
      // executed by the site's code, exactly as a real user click would.
      if (e.tagName === 'A') {
        const rawHref = e.getAttribute('href') || '';
        const sameDocument = rawHref === '' || rawHref.startsWith('#') || /^javascript:/i.test(rawHref);
        if (!sameDocument) {
          let u; try { u = new URL(rawHref, location.href); } catch { return { ok: false, error: '유효하지 않은 링크입니다.' }; }
          if (u.protocol !== 'https:') return { ok: false, error: '스크립트·외부 앱 링크는 원본 웹에서 실행하세요.' };
        }
      }
      revision = ''; press(e);
      return { ok: true, state: 'click-dispatched', notice: '웹 클릭을 전달했습니다. 저장 성공을 뜻하지 않습니다. 웹 결과를 확인하세요.' };
    }
    return { ok: false, error: '지원하지 않는 작업입니다.' };
  }
  Object.defineProperty(globalThis, '__mEdufineBridge', {
    value: req => {
      try { return req?.action === 'scan' ? scan() : act(req || {}); }
      catch { return { ok: false, error: '이 화면을 읽거나 제어하지 못했습니다. F4로 원본 웹을 사용하세요.' }; }
    }, configurable: false
  });
})();
