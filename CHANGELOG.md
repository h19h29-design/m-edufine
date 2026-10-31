# Changelog

## 0.3.0 Preview — 2026-10-31

### Mdir형 메뉴 중심 UI
- Left pane is now a real **business menu tree** read from the page, replacing the fixed category list.
- Right pane shows the selected node's child menus, live controls and table view (F6) with real column headers.
- Breadcrumb shows organization host / page path / selected menu path.
- Selection, search text and active panel survive F5 re-reads.
- Accessibility: tree items expose their Korean menu name (not the CLR type name); focus cycling Tab/Shift+Tab is explicit.

### Menu extraction
- Hierarchical paths from nested `div/span/li` delegated-click menus; frames and regions disambiguate duplicate names.
- Image-only navigation items use `img alt` / `aria-label`.
- Technical class/id fallbacks are not shown as menus.
- Clicks dispatch a faithful pointer/mouse/click sequence (mousedown-only handlers now work).
- Same-document `javascript:`/`#` links are allowed (site's own handler); external schemes stay blocked.

### Reliability fixes
- `--self-test` no longer hangs or attaches to a running user's pipe: tests use an isolated pipe name.
- Programmatic row selection no longer steals focus from the tree (Enter acted on the wrong item).
- Stale search text no longer remains in the page meta after clearing the search.
- DRM suffixes such as `_WebDRM[…]` are stripped from the window title.
- Control list deduplicated; technical input names rendered as `입력란 (field)`.
- Ctrl+F4 lists official HTTPS work tabs and relinks (handles new windows/popups).
- `--ui-demo` renders deterministic synthetic data for GUI/keyboard tests; it never connects to a browser or server.

### Verified
- Real authenticated portal and K-에듀파인 main screen read **read-only** (25 and 11 menus).
- Windows keyboard navigation, folder expand/collapse, search, table view and read-only guard exercised through UI Automation.
- Upstream/Downstream: see `docs/QA-0.3.0.md` for what is still unverified.

## 0.2.0 Preview — 2026-10-31

- Fix missing navigation in older enterprise websites that use DIV, SPAN, LI and delegated click handlers instead of semantic buttons.
- Keyboard navigation: Enter/Right from category to results; Left from results back to category.
- Chrome-first extension setup instructions and updated smoke/release packaging.
- Retain F7 read-only default, per-action confirmation and no claims of server persistence.
- **Not authenticated K-Edufine end-to-end verified; menu heuristics may miss unsupported frames.**

## 0.1.0 Preview — 2026-10-31

- Native Windows WPF Mdir-style two-panel keyboard UI.
- Edge/Chrome extension with explicit active-tab pairing and Native Messaging.
- Current-user pipe bridge; no listening network ports, cloud AI or separate business database.
- Live DOM controls, input editing, select/checkbox support, current table reading, allowed frames and open shadow roots.
- Read-only default, per-action confirmation, web-only high-impact actions, stale-target checks and no write retries.
- Korean documentation, field-test checklist, Codex handoff, Windows installer/portable build and regression tests.
- Actual authenticated K-Edufine compatibility remains unverified.
