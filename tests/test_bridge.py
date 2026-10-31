"""Real Chromium DOM tests; only a local synthetic fixture, never a live institution."""
import json, pathlib, unittest, os
from playwright.sync_api import sync_playwright
ROOT=pathlib.Path(__file__).resolve().parents[1]
class BridgeTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.p=sync_playwright().start(); cls.browser=cls.p.chromium.launch(headless=True, executable_path=os.environ.get("CHROMIUM_EXECUTABLE_PATH"))
 @classmethod
 def tearDownClass(cls): cls.browser.close(); cls.p.stop()
 def setUp(self):
  self.page=self.browser.new_page()
  # In-memory synthetic page: no navigation, file URL or network access.
  self.page.set_content((ROOT/'tests/fixture.html').read_text(encoding='utf-8'))
  # about:blank lacks secure-context randomUUID. Test harness only; production uses HTTPS.
  self.page.evaluate("if (!crypto.randomUUID) crypto.randomUUID=()=>[...crypto.getRandomValues(new Uint8Array(16))].map(x=>x.toString(16).padStart(2,'0')).join('')")
  self.page.add_script_tag(path=str(ROOT/'extension/bridge.js'))
  self.snap=self.call({'action':'scan'})
 def tearDown(self): self.page.close()
 def call(self,req):return self.page.evaluate('(r)=>globalThis.__mEdufineBridge(r)',req)
 def row(self,name):return next(c for c in self.snap['controls'] if c['label']==name)
 def action(self,name,action='click',**extra):
  row=self.row(name)
  return self.call(dict(action=action,docId=self.snap['docId'],revision=self.snap['revision'],targetId=row['id'],**extra))
 def test_portal_navigation_menu(self):
  names=[c['label'] for c in self.snap['controls'] if c['kind']=='메뉴']
  self.assertIn('문서관리',names)
  self.assertIn('공람대기',names)
  self.assertNotIn('비상호출 안내',names)
  self.assertTrue(self.action('문서관리',confirmed=True)['ok'])
  self.assertEqual(self.page.evaluate('window.menus'),1)
 def test_real_controls(self):self.assertEqual(self.row('물품명')['value'],'업무용 모니터')
 def test_credentials_not_collected(self):
  text=json.dumps(self.snap,ensure_ascii=False)
  for value in ['never-read-this','must-not-appear','confidential-account','private-bank-value','900101-1234567']:
   self.assertNotIn(value,text)
 def test_sensitive_value_not_editable(self):self.assertFalse(self.row('기타 값')['editable'])
 def test_hidden_not_listed(self):self.assertNotIn('숨겨진 버튼',[c['label'] for c in self.snap['controls']])
 def test_click_requires_confirmation(self):
  self.assertFalse(self.action('저장')['ok']);self.assertEqual(self.page.evaluate('saves'),0)
 def test_confirmed_click_once(self):
  result=self.action('저장',confirmed=True);self.assertEqual(result['state'],'click-dispatched')
  self.assertEqual(self.page.evaluate('saves'),1)
  self.assertFalse(self.action('저장',confirmed=True)['ok']);self.assertEqual(self.page.evaluate('saves'),1)
 def test_save_not_claimed_success(self):
  self.assertIn('저장 성공을 뜻하지 않습니다',self.action('저장',confirmed=True)['notice'])
 def test_high_risk_web_only(self):
  self.assertTrue(self.row('지급 승인')['webOnly']);self.assertFalse(self.action('지급 승인',confirmed=True)['ok'])
  self.assertEqual(self.page.evaluate('pays'),0)
 def test_edit_live_input_events(self):
  self.page.evaluate("document.querySelector('#asset').addEventListener('change',()=>window.changed=true)")
  self.assertTrue(self.action('물품명','set',confirmed=True,value='새 모니터')['ok'])
  self.assertEqual(self.page.locator('#asset').input_value(),'새 모니터');self.assertTrue(self.page.evaluate('changed'))
 def test_preserve_multiline(self):
  self.assertEqual(self.row('비고')['editValue'],'여러 줄\n  원문 공백 유지')
 def test_long_edit_not_truncated(self):
  value='가'*600+'\n  tail';self.page.locator('#notes').fill(value);self.snap=self.call({'action':'scan'})
  self.assertEqual(self.row('비고')['editValue'],value)
 def test_too_long_edit_web_only(self):
  self.page.locator('#notes').fill('가'*4001);self.snap=self.call({'action':'scan'})
  self.assertFalse(self.row('비고')['editable']);self.assertEqual(self.row('비고')['editValue'],'')
 def test_stale_value_rejected(self):
  self.page.locator('#asset').fill('다른 사용자가 변경')
  self.assertFalse(self.action('물품명','set',confirmed=True,value='덮어쓰기')['ok'])
  self.assertEqual(self.page.locator('#asset').input_value(),'다른 사용자가 변경')
 def test_whitespace_change_is_stale(self):
  self.page.locator('#notes').fill('여러 줄\n 원문 공백 유지')
  self.assertFalse(self.action('비고','set',confirmed=True,value='overwrite')['ok'])
 def test_old_snapshot_rejected(self):
  self.call({'action':'scan'});self.assertFalse(self.action('저장',confirmed=True)['ok'])
 def test_disabled_rejected(self):self.assertFalse(self.action('사용 불가',confirmed=True)['ok'])
 def test_removed_target_rejected(self):
  self.page.locator('#save').evaluate('(e)=>e.remove()');self.assertFalse(self.action('저장',confirmed=True)['ok'])
 def test_select_real_option(self):
  self.assertTrue(self.action('설치 장소','set',confirmed=True,value='b')['ok'])
  self.assertEqual(self.page.locator('#location').input_value(),'b')
 def test_select_disabled_option_rejected(self):
  self.assertFalse(self.action('설치 장소','set',confirmed=True,value='c')['ok'])
 def test_focus_does_not_pay(self):
  self.assertTrue(self.action('지급 승인','focus')['ok']);self.assertEqual(self.page.evaluate('pays'),0)
 def test_check_live_checkbox(self):
  self.assertTrue(self.action('선택 여부',confirmed=True)['ok']);self.assertTrue(self.page.locator('#checked').is_checked())
 def test_current_table(self):
  self.assertEqual(self.snap['tables'][0]['rows'][1],['책상','3','[보호됨]'])
 def test_shadow_dom(self):
  self.page.evaluate("let h=document.createElement('div'); document.body.append(h); h.attachShadow({mode:'open'}).innerHTML='<button>그림자 버튼</button>'")
  self.snap=self.call({'action':'scan'});self.assertEqual(self.row('그림자 버튼')['kind'],'버튼')
 def test_input_becomes_password(self):
  self.page.locator('#asset').evaluate("e=>e.type='password'")
  self.assertFalse(self.action('물품명','set',confirmed=True,value='leak')['ok'])
class MenuTests(unittest.TestCase):
 """Hierarchical menu extraction for non-semantic enterprise portals. Synthetic page only."""
 @classmethod
 def setUpClass(cls):
  cls.p=sync_playwright().start(); cls.browser=cls.p.chromium.launch(headless=True, executable_path=os.environ.get("CHROMIUM_EXECUTABLE_PATH"))
 @classmethod
 def tearDownClass(cls): cls.browser.close(); cls.p.stop()
 def setUp(self):
  self.page=self.browser.new_page()
  self.page.set_content((ROOT/'tests/menu-fixture.html').read_text(encoding='utf-8'))
  self.page.evaluate("if (!crypto.randomUUID) crypto.randomUUID=()=>[...crypto.getRandomValues(new Uint8Array(16))].map(x=>x.toString(16).padStart(2,'0')).join('')")
  self.page.add_script_tag(path=str(ROOT/'extension/bridge.js'))
  self.snap=self.call({'action':'scan'})
 def tearDown(self): self.page.close()
 def call(self,req):return self.page.evaluate('(r)=>globalThis.__mEdufineBridge(r)',req)
 def menu(self,path):return next(m for m in self.snap['menus'] if m['path']==path)
 def activate(self,path,confirmed=True):
  m=self.menu(path)
  return self.call(dict(action='click',docId=self.snap['docId'],revision=self.snap['revision'],targetId=m['id'],confirmed=confirmed))
 def test_top_level_branch(self):
  m=self.menu(['업무관리']);self.assertEqual(m['level'],0);self.assertTrue(m['hasChildren'])
 def test_nested_branch_and_leaf(self):
  self.assertEqual(self.menu(['업무관리','계약관리'])['level'],1)
  leaf=self.menu(['업무관리','계약관리','계약요청']);self.assertEqual(leaf['level'],2);self.assertFalse(leaf['hasChildren'])
 def test_regions(self):
  self.assertEqual(self.menu(['업무관리'])['region'],'주메뉴')
  self.assertEqual(self.menu(['문서함'])['region'],'문서함')
 def test_active_menu(self):self.assertTrue(self.menu(['문서함'])['active'])
 def test_duplicate_names_distinct_paths(self):
  names=[m for m in self.snap['menus'] if m['label']=='공람대기']
  self.assertEqual(len({tuple(m['path']) for m in names}),2)
  self.assertIn(['업무관리','계약관리','공람대기'],[[*m['path']] for m in names])
  self.assertIn(['문서함','공람대기'],[[*m['path']] for m in names])
 def test_ad_and_hidden_excluded(self):
  labels=[m['label'] for m in self.snap['menus']]
  self.assertNotIn('비상연락 안내',labels);self.assertNotIn('숨김메뉴',labels)
 def test_tabs_extracted(self):
  labels=[m['label'] for m in self.snap['menus'] if m['region']=='업무 탭']
  self.assertIn('조회',labels);self.assertIn('등록',labels)
 def test_menu_click_requires_confirmation(self):
  self.assertFalse(self.activate(['업무관리','계약관리','계약요청'],confirmed=False)['ok'])
  self.assertEqual(self.page.evaluate('delegated'),0)
 def test_delegated_mousedown_activation(self):
  result=self.activate(['업무관리','계약관리','계약요청'])
  self.assertEqual(result['state'],'click-dispatched')
  self.assertEqual(self.page.evaluate('delegated'),1)  # mousedown-only handler fired
 def test_menu_target_stale_after_rescan(self):
  self.call({'action':'scan'})
  self.assertFalse(self.activate(['업무관리','계약관리','계약요청'])['ok'])
 def test_controls_still_collected(self):
  self.assertIn('조회 실행',[c['label'] for c in self.snap['controls']])
 def test_javascript_menu_click_allowed(self):
  row=next(c for c in self.snap['controls'] if c['label']=='스크립트 메뉴')
  r=self.call(dict(action='click',docId=self.snap['docId'],revision=self.snap['revision'],targetId=row['id'],confirmed=True))
  self.assertEqual(r['state'],'click-dispatched')
  self.assertEqual(self.page.evaluate('clicks'),1)
 def test_file_link_blocked(self):
  row=next(c for c in self.snap['controls'] if c['label']=='파일 메뉴')
  r=self.call(dict(action='click',docId=self.snap['docId'],revision=self.snap['revision'],targetId=row['id'],confirmed=True))
  self.assertFalse(r['ok']);self.assertIn('외부 앱',r['error'])
 def test_image_only_menu_uses_alt(self):
  self.assertIn(['이미지 메뉴'],[[*m['path']] for m in self.snap['menus']])
 def test_technical_input_name_is_readable(self):
  row=next(c for c in self.snap['controls'] if c['value']=='기술이름')
  self.assertEqual(row['label'],'입력란 (txtSearch)')
if __name__=='__main__':
 suite=unittest.defaultTestLoader.loadTestsFromTestCase(BridgeTests)
 suite.addTests(unittest.defaultTestLoader.loadTestsFromTestCase(MenuTests))
 result=unittest.TextTestRunner(verbosity=2).run(suite)
 (ROOT/'artifacts').mkdir(exist_ok=True)
 (ROOT/'artifacts/bridge-tests.json').write_text(json.dumps({'tests':result.testsRun,'failures':len(result.failures),'errors':len(result.errors),'scope':'local synthetic Chromium DOM only'},indent=2))
 raise SystemExit(not result.wasSuccessful())
