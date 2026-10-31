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
if __name__=='__main__':
 suite=unittest.defaultTestLoader.loadTestsFromTestCase(BridgeTests)
 result=unittest.TextTestRunner(verbosity=2).run(suite)
 (ROOT/'artifacts').mkdir(exist_ok=True)
 (ROOT/'artifacts/bridge-tests.json').write_text(json.dumps({'tests':result.testsRun,'failures':len(result.failures),'errors':len(result.errors),'scope':'local synthetic Chromium DOM only'},indent=2))
 raise SystemExit(not result.wasSuccessful())
