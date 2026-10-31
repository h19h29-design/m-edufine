using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace MEdufine;

public partial class MainWindow : Window
{
    enum Panel { Tree, Content, Search, Table }
    readonly BridgeServer bridge = new();
    readonly Preferences preferences = Preferences.Load();
    List<MenuNode> roots = [];
    List<ControlRow> controls = [];
    List<TableRow> tables = [];
    bool ready, busy, allowActions, smoke, demo;
    int link = -1;
    string currentHost = "", currentPath = "", selectedKey = "", lastRowLabel = "", baseMeta = "";
    Panel panel = Panel.Tree;

    public MainWindow(bool smoke = false, bool demo = false)
    {
        InitializeComponent();
        this.smoke = smoke; this.demo = demo; ready = true;
        ApplyFont();
        panel = Panel.Tree;
        if (demo)
        {
            StatusText.Text = "합성 UI 시험 모드 · 실제 K-에듀파인/업무 데이터가 아닙니다 · 네트워크 연결 안 함";
            ConnectionText.Text = "● 합성 시험 데이터 (연결 아님)";
            LoadDemo();
        }
        else if (smoke)
        {
            StatusText.Text = "자동 UI 검증 모드 · 실제 업무 데이터 없음 · 네트워크 연결 안 함";
        }
        else
        {
            bridge.ConnectionChanged += connected => Dispatcher.BeginInvoke(new Action(() =>
            {
                ConnectionText.Text = connected ? "● 연결됨 · 인증 미확인" : "○ 브라우저 연결 대기";
                allowActions = false; UpdatePermission();
                if (!connected)
                {
                    ClearData();
                    StatusText.Text = "연결이 끊겼습니다. 이전 업무 데이터 표시는 지웠습니다.";
                }
                else StatusText.Text = "F5로 현재 탭을 읽으세요. 로그인 여부는 원본 웹에서 확인합니다.";
            }));
            bridge.Notice += message => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (message["event"]?.GetValue<string>() == "invalidated")
                {
                    link = message["link"]?.GetValue<int>() ?? -1;
                    ClearData();
                    StatusText.Text = "원본 화면이 전환되었습니다. 이전 항목은 지웠습니다. F5로 다시 읽어 주세요.";
                }
            }));
            bridge.Start();
        }
        Loaded += (_, _) => { panel = Panel.Tree; MenuTree.Focus(); if (MenuTree.Items.Count > 0) SelectFirst(); };
    }

    void LoadDemo() => Dispatcher.BeginInvoke(new Action(() => ApplyScan(DemoData.Scan())), DispatcherPriority.Background);

    void ApplyFont()
    {
        FontSize = preferences.FontSize;
        ContentGrid.FontSize = preferences.FontSize;
        TableGrid.FontSize = preferences.FontSize;
        ContentGrid.RowHeight = Math.Max(44, preferences.FontSize * 2.2);
        TableGrid.RowHeight = ContentGrid.RowHeight;
    }

    void ClearData()
    {
        controls = []; tables = []; roots = []; selectedKey = ""; lastRowLabel = "";
        MenuTree.ItemsSource = null; ContentGrid.ItemsSource = null;
        TablePicker.ItemsSource = null; TableGrid.ItemsSource = null;
        baseMeta = "";
        ContentGrid.Visibility = Visibility.Visible; TablesPanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Visible;
        PageTitle.Text = "업무 화면 연결 대기";
        PageMeta.Text = "화면을 다시 읽으면 현재 실제 자료가 표시됩니다.";
        PathText.Text = "연결 대기 · F5로 현재 화면을 읽습니다.";
        DetailText.Text = "이전 화면의 항목은 재사용하지 않습니다.";
    }

    async Task Run(Func<Task> work)
    {
        if (busy) { StatusText.Text = "이전 요청을 처리 중입니다. 저장·입력 명령은 중복 실행하지 않습니다."; return; }
        busy = true; Cursor = Cursors.Wait;
        try { await work(); }
        catch (TimeoutException) { StatusText.Text = "응답 시간이 초과되었습니다. 자동 재실행하지 않습니다. F4 원본 웹에서 결과부터 확인하세요."; }
        catch (Exception) { StatusText.Text = "요청을 완료하지 못했습니다. 확장 연결·기관 정책·현재 탭을 확인하세요. 저장 작업은 자동 재시도하지 않습니다."; }
        finally { busy = false; Cursor = null; }
    }

    static bool Ok(JsonObject r) => r["ok"]?.GetValue<bool>() == true;
    bool Check(JsonObject r)
    {
        if (Ok(r)) return true;
        StatusText.Text = r["error"]?.GetValue<string>() ?? "원본 웹을 확인해 주세요.";
        return false;
    }

    // ----------------------------------------------------------- reading
    async Task ReadCore()
    {
        if (demo) { ApplyScan(DemoData.Scan()); StatusText.Text = "합성 UI 시험 데이터를 다시 읽었습니다. 실제 서버 조회가 아닙니다."; return; }
        var r = await bridge.Request(new JsonObject { ["action"] = "scan" });
        if (!Check(r)) { ClearData(); return; }
        ApplyScan(r);
    }

    void ApplyScan(JsonObject r)
    {
        link = r["link"]?.GetValue<int>() ?? -1;
        currentHost = r["host"]?.GetValue<string>() ?? "";
        currentPath = r["urlPath"]?.GetValue<string>() ?? "";
        var nextControls = new List<ControlRow>();
        var nextTables = new List<TableRow>();
        var pageMenus = new List<(int FrameId, string Label, string DocId, string Revision, JsonArray Menus)>();
        bool clipped = r["truncated"]?.GetValue<bool>() == true;
        int protectedCount = 0, inaccessible = r["inaccessible"]?.GetValue<int>() ?? 0;

        foreach (var p in r["pages"]?.AsArray() ?? new JsonArray())
        {
            if (p is not JsonObject page) continue;
            int frameId = page["frameId"]!.GetValue<int>();
            string docId = page["docId"]!.GetValue<string>(), revision = page["revision"]!.GetValue<string>();
            clipped |= page["truncated"]?.GetValue<bool>() == true;
            protectedCount += page["protectedCount"]?.GetValue<int>() ?? 0;
            string frameLabel = $"프레임 {frameId}";
            if (page["framePath"] is JsonArray fp && fp.Count > 0 && fp[^1] is JsonObject last)
            {
                var host = last["host"]?.GetValue<string>() ?? "";
                var path = last["path"]?.GetValue<string>() ?? "";
                if (host.Length > 0) frameLabel = host + path;
            }
            foreach (var c in page["controls"]?.AsArray() ?? new JsonArray())
            {
                if (c is not JsonObject co) continue;
                var options = new List<Choice>();
                foreach (var opt in co["options"]?.AsArray() ?? new JsonArray()) if (opt is JsonObject o)
                    options.Add(new(o["value"]?.GetValue<string>() ?? "", o["label"]?.GetValue<string>() ?? "", o["disabled"]?.GetValue<bool>() == true));
                nextControls.Add(new(nextControls.Count + 1, frameId, docId, revision, co["id"]!.GetValue<string>(),
                    co["kind"]?.GetValue<string>() ?? "항목", co["label"]?.GetValue<string>() ?? "", co["value"]?.GetValue<string>() ?? "",
                    co["editValue"]?.GetValue<string>() ?? "", co["editable"]?.GetValue<bool>() == true,
                    co["disabled"]?.GetValue<bool>() == true, co["webOnly"]?.GetValue<bool>() == true,
                    co["context"]?.GetValue<string>() ?? "", options));
            }
            foreach (var t in page["tables"]?.AsArray() ?? new JsonArray()) if (t is JsonObject to)
            {
                var rows = new List<List<string>>();
                foreach (var row in to["rows"]?.AsArray() ?? new JsonArray()) if (row is JsonArray cells)
                    rows.Add(cells.Select(v => v?.GetValue<string>() ?? "").ToList());
                var cols = (to["columns"]?.AsArray() ?? new JsonArray()).Select(v => v?.GetValue<string>() ?? "").ToList();
                nextTables.Add(new($"{to["name"]?.GetValue<string>()} · 프레임 {frameId}", cols, rows));
            }
            if (page["menus"] is JsonArray menus && menus.Count > 0)
                pageMenus.Add((frameId, frameLabel, docId, revision, menus));
        }

        controls = nextControls; tables = nextTables;
        roots = BuildTree(pageMenus);
        var priorPanel = panel;
        bool hadSelection = selectedKey.Length > 0;
        string wanted = selectedKey;
        MenuTree.ItemsSource = roots;
        WelcomePanel.Visibility = roots.Count > 0 || controls.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        PageTitle.Text = r["title"]?.GetValue<string>() ?? "현재 업무 화면";
        if (demo) PageTitle.Text = "[합성] " + PageTitle.Text;

        int menuCount = roots.Sum(n => n.Walk().Count());
        baseMeta = $"{currentHost}  /  읽은 시각 {DateTime.Now:HH:mm:ss}  /  메뉴 {menuCount}개 · 항목 {controls.Count}개 · 표 {tables.Count}개" +
            (demo ? "  /  합성 UI 시험 데이터" : "");
        PageMeta.Text = baseMeta;
        TablePicker.ItemsSource = tables; if (tables.Count > 0) TablePicker.SelectedIndex = 0;

        if (roots.Count > 0)
        {
            var target = (wanted.Length > 0 ? AllNodes().FirstOrDefault(n => n.Key == wanted) : null)
                ?? AllNodes().FirstOrDefault(n => n.Active) ?? roots[0];
            Dbg($"scan roots={roots.Count} wanted='{wanted}' target='{target?.Label}' found={AllNodes().Any(n => n.Key == wanted)}");
            if (target is not null) SelectNode(target, refocus: false);
        }
        else { PopulateRows(); }

        StatusText.Text = $"화면 읽기 완료 · 보호 입력 {protectedCount}개 제외 · 접근 불가 프레임 {inaccessible}개" +
            (clipped ? " · 일부만 표시" : "") + " · 서버 재조회는 원본의 조회 버튼으로 실행";
        if (controls.Count == 0 && menuCount == 0)
            StatusText.Text = "인식 가능한 메뉴·항목이 없습니다. 인증 전이거나 전용 화면일 수 있습니다. F4 원본 웹으로 전환하세요.";
        if (hadSelection && wanted.Length > 0 && AllNodes().All(n => n.Key != wanted))
            StatusText.Text += " · 이전에 선택한 메뉴가 이 화면에 없습니다. 상위 메뉴에서 다시 선택하세요.";
        panel = priorPanel;
        RestoreFocus();
    }

    List<MenuNode> BuildTree(List<(int FrameId, string Label, string DocId, string Revision, JsonArray Menus)> pages)
    {
        var all = new List<MenuNode>();
        bool multi = pages.Count > 1;
        foreach (var page in pages)
        {
            var dict = new Dictionary<string, MenuNode>();
            var pageRoots = new List<MenuNode>();
            foreach (var m in page.Menus)
            {
                if (m is not JsonObject mo) continue;
                var labels = (mo["path"]?.AsArray() ?? new JsonArray()).Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToArray();
                if (labels.Length == 0) continue;
                MenuNode? parent = null;
                for (int i = 0; i < labels.Length; i++)
                {
                    string key = $"{page.FrameId}|{string.Join('\u001f', labels.Take(i + 1))}";
                    bool leaf = i == labels.Length - 1;
                    if (!dict.TryGetValue(key, out var node))
                    {
                        node = new MenuNode
                        {
                            Key = key, Label = labels[i], Path = string.Join(" > ", labels.Take(i + 1)),
                            FrameId = page.FrameId, Level = i, Region = mo["region"]?.GetValue<string>() ?? ""
                        };
                        dict[key] = node;
                        if (parent is null) pageRoots.Add(node); else { parent.Children.Add(node); node.Parent = parent; }
                    }
                    if (leaf)
                    {
                        node.TargetId = mo["id"]?.GetValue<string>() ?? "";
                        node.DocId = page.DocId; node.Revision = page.Revision;
                        node.Active = mo["active"]?.GetValue<bool>() == true;
                        node.WebOnly = mo["webOnly"]?.GetValue<bool>() == true;
                    }
                    parent = node;
                }
            }
            if (multi)
            {
                var wrap = new MenuNode { Key = $"{page.FrameId}|@", Label = page.Label, Path = page.Label, FrameId = page.FrameId, Level = -1 };
                foreach (var n in pageRoots) { n.Parent = wrap; wrap.Children.Add(n); }
                if (wrap.Children.Count > 0) all.Add(wrap);
            }
            else all.AddRange(pageRoots);
        }
        foreach (var n in all) SetDepth(n, 0);
        return all;
    }

    static void SetDepth(MenuNode n, int d) { n.Depth = d; foreach (var c in n.Children) SetDepth(c, d + 1); }
    IEnumerable<MenuNode> AllNodes() => roots.SelectMany(r => r.Walk());

    void SelectNode(MenuNode node, bool refocus = true)
    {
        var chain = new List<MenuNode>();
        for (var n = node; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();
        foreach (var n in chain) n.IsExpanded = true;
        foreach (var n in AllNodes()) if (n != node) n.IsSelected = false;
        node.IsSelected = true;
        selectedKey = node.Key;
        PathText.Text = $"{currentHost}{currentPath}  /  {node.Path}";
        PopulateRows();
        // Containers are created asynchronously; select again once they exist so the
        // TreeView cannot clear the restored selection during realization.
        Dispatcher.BeginInvoke(new Action(() => RealizeSelection(chain, node)), DispatcherPriority.Loaded);
        if (refocus) { panel = Panel.Content; ContentGrid.Focus(); }
    }

    void RealizeSelection(List<MenuNode> chain, MenuNode node)
    {
        ItemsControl parent = MenuTree;
        TreeViewItem? item = null;
        Dbg($"realize chain={string.Join(">", chain.Select(c => c.Label))} items={MenuTree.Items.Count}");
        foreach (var n in chain)
        {
            parent.UpdateLayout();
            var c = parent.ItemContainerGenerator.ContainerFromItem(n) as TreeViewItem;
            Dbg($"  container {n.Label} => {(c is null ? "null" : "ok")}");
            if (c is null) { parent.ApplyTemplate(); parent.UpdateLayout(); c = parent.ItemContainerGenerator.ContainerFromItem(n) as TreeViewItem; }
            if (c is null) return;
            c.IsExpanded = true; c.UpdateLayout(); parent = c; item = c;
        }
        if (item is not null)
        {
            item.IsSelected = true;
            item.BringIntoView();
            Dbg($"  selected {item.Header} isSel={item.IsSelected}");
        }
        node.IsSelected = true;
    }

    internal static void Dbg(string message)
    {
        var path = Environment.GetEnvironmentVariable("MEDUFINE_DEBUG_UI");
        if (string.IsNullOrEmpty(path)) return;
        try { File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n"); } catch { }
    }

    void SelectFirst() { if (roots.Count > 0) SelectNode(roots[0], refocus: false); }

    // ----------------------------------------------------------- right pane
    void PopulateRows()
    {
        if (!ready) return;
        var rows = new List<ItemRow>();
        string q = SearchBox.Text.Trim();
        bool searching = q.Length > 0;
        bool Match(string s) => s.Contains(q, StringComparison.OrdinalIgnoreCase);

        if (searching)
        {
            int m = 0, c = 0;
            foreach (var n in AllNodes())
                if (Match(n.Label) || Match(n.Path)) { rows.Add(new ItemRow { Kind = n.HasChildren ? "폴더" : "메뉴", Label = n.Label, Value = n.Path, Menu = n }); m++; }
            foreach (var ct in controls)
                if (Match(ct.Label) || Match(ct.Value) || Match(ct.Context)) { rows.Add(new ItemRow { Kind = ct.Kind, Label = ct.Label, Value = ct.Value, Control = ct }); c++; }
            PageMeta.Text = $"검색 ‘{q}’ · 메뉴 {m} · 항목 {c} · 현재 읽은 화면 안에서만 찾습니다(서버 전체 검색 아님)";
        }
        else if (MenuTree.SelectedItem is MenuNode node)
        {
            rows.AddRange(node.Children.Select(cn => new ItemRow
            {
                Kind = cn.HasChildren ? "폴더" : "메뉴",
                Label = cn.Label,
                Value = cn.Active ? "● 현재" : cn.HasChildren ? $"하위 {cn.Children.Count}개" : cn.WebOnly ? "원본 웹" : "Enter 실행",
                Menu = cn
            }));
            bool wrapper = node.Key.EndsWith("|@");
            var frameControls = node.FrameId >= 0 && !wrapper ? controls.Where(c => c.FrameId == node.FrameId) : controls;
            rows.AddRange(frameControls.Select(ct => new ItemRow { Kind = ct.Kind, Label = ct.Label, Value = ct.Value, Control = ct }));
            DetailText.Text = node.HasChildren
                ? $"{node.Path}  /  하위 메뉴 {node.Children.Count}개 · 이 화면 항목 {rows.Count - node.Children.Count}개  /  Enter로 폴더 펼치기"
                : $"{node.Path}  /  Enter 실행 · F2 입력 · F4 원본 웹";
        }
        else
        {
            rows.AddRange(controls.Select(ct => new ItemRow { Kind = ct.Kind, Label = ct.Label, Value = ct.Value, Control = ct }));
        }

        ContentGrid.ItemsSource = rows;
        if (!searching && baseMeta.Length > 0) PageMeta.Text = baseMeta;
        int keep = rows.FindIndex(r => r.Label == lastRowLabel && lastRowLabel.Length > 0);
        if (keep >= 0) ContentGrid.SelectedIndex = keep;
        else if (rows.Count > 0) ContentGrid.SelectedIndex = 0;
    }

    // ----------------------------------------------------------- activation
    JsonObject Target(ControlRow row, string action) => new()
    {
        ["action"] = action, ["link"] = link, ["frameId"] = row.FrameId, ["docId"] = row.DocId,
        ["revision"] = row.Revision, ["targetId"] = row.TargetId
    };

    async Task ExecuteControl(ControlRow row, bool editOnly = false)
    {
        if (demo) { StatusText.Text = "합성 UI 시험 모드입니다. 실제 웹에는 아무 것도 보내지 않습니다. (키보드·목록 동작만 확인)"; return; }
        if (row.Disabled) { StatusText.Text = "현재 웹에서 비활성화된 항목입니다."; return; }
        if (row.WebOnly) { await bridge.Request(Target(row, "focus")); await ShowWebCore(); StatusText.Text = "중요 처리 항목은 원본 웹에서 직접 최종 확인하세요."; return; }
        if (!allowActions) { StatusText.Text = "현재 읽기 전용입니다. F7로 이번 연결의 입력·클릭을 허용하세요."; return; }
        if (editOnly && !row.Editable) { StatusText.Text = "이 항목은 직접 편집할 수 없습니다. Enter 실행 또는 F4를 사용하세요."; return; }
        JsonObject request;
        if (row.Editable)
        {
            var value = Dialogs.Edit(this, row, currentHost); if (value is null) return;
            request = Target(row, "set"); request["value"] = value; request["confirmed"] = true;
        }
        else
        {
            if (!Confirm($"[{currentHost}]\n\n실제 웹의 ‘{row.Label}’ 항목을 실행합니다.\n등록·저장 등 실제 업무에 영향을 줄 수 있습니다.\n\n실행하시겠습니까?")) return;
            request = Target(row, "click"); request["confirmed"] = true;
        }
        var result = await bridge.Request(request);
        if (!Check(result)) return;
        var notice = result["notice"]?.GetValue<string>() ?? "웹 작업을 전달했습니다. 원본 결과를 확인하세요.";
        // No write retry and no optimistic 'saved' status. A fresh DOM read does not prove server persistence.
        await Task.Delay(350); await ReadCore(); StatusText.Text = notice;
    }

    async Task ActivateMenu(MenuNode node)
    {
        if (node.HasChildren)
        {
            node.IsExpanded = !node.IsExpanded;
            StatusText.Text = $"‘{node.Label}’ 폴더를 {(node.IsExpanded ? "펼쳤습니다" : "접었습니다")}. 하위 메뉴는 오른쪽 목록에서도 선택할 수 있습니다.";
            return;
        }
        if (node.TargetId.Length == 0)
        {
            StatusText.Text = "이 메뉴의 실행 지점을 현재 화면에서 확인하지 못했습니다. F4 원본 웹에서 선택하세요.";
            return;
        }
        if (demo) { StatusText.Text = "합성 UI 시험 모드입니다. 실제 웹 메뉴 클릭을 보내지 않습니다."; return; }
        if (node.WebOnly) { StatusText.Text = "중요 처리 메뉴입니다. F4 원본 웹에서 직접 확인하세요."; await ShowWebCore(); return; }
        if (!allowActions) { StatusText.Text = "현재 읽기 전용입니다. F7로 이번 연결의 클릭을 허용하세요."; return; }
        if (!Confirm($"[{currentHost}]\n\n실제 웹의 메뉴 ‘{node.Label}’을(를) 실행합니다.\n화면이 이동하거나 서버 조회가 실행될 수 있습니다.\n\n실행하시겠습니까?")) return;
        var req = new JsonObject
        {
            ["action"] = "click", ["link"] = link, ["frameId"] = node.FrameId,
            ["docId"] = node.DocId, ["revision"] = node.Revision, ["targetId"] = node.TargetId, ["confirmed"] = true
        };
        var result = await bridge.Request(req);
        if (!Check(result)) return;
        StatusText.Text = result["notice"]?.GetValue<string>() ?? "메뉴 클릭을 전달했습니다.";
        await Task.Delay(450);
        await ReadCore();
    }

    bool Confirm(string text) => MessageBox.Show(this, text, "실제 웹 실행 확인",
        MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    async Task ShowWebCore() { Check(await bridge.Request(new JsonObject { ["action"] = "showWeb" })); }
    void UpdatePermission()
    {
        PermissionButton.Content = allowActions ? "F7  입력·클릭 허용 중" : "F7  읽기 전용";
        ReadOnlyChip.Text = allowActions ? "조작 허용" : "읽기 전용";
        ReadOnlyChip.Foreground = allowActions
            ? new SolidColorBrush(Color.FromRgb(255, 225, 142))
            : new SolidColorBrush(Color.FromRgb(140, 227, 214));
    }

    void TogglePermission(object? sender, RoutedEventArgs e)
    {
        if (demo) { StatusText.Text = "합성 UI 시험 모드에서는 실제 웹 조작을 허용하지 않습니다."; return; }
        if (!bridge.Connected) { StatusText.Text = "먼저 브라우저를 연결하세요."; return; }
        if (!allowActions && MessageBox.Show(this,
            "이번 연결에서 실제 웹의 입력란과 버튼을 조작하도록 허용합니다.\n입력 변경만으로 자동 저장되는 화면도 있습니다.\n각 실행 전 내용을 확인하고, 중요한 결재·지급·삭제는 원본 웹에서 처리하세요.",
            "실제 업무 조작 허용", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        allowActions = !allowActions; UpdatePermission();
        StatusText.Text = allowActions ? "이번 연결에서 입력·클릭이 허용됩니다. 실제 서버에 영향을 줄 수 있습니다." : "읽기 전용으로 돌아왔습니다.";
    }

    // ----------------------------------------------------------- events
    void SearchChanged(object s, TextChangedEventArgs e)
    {
        if (!ready) return;
        if (SearchBox.Text.Trim().Length > 0) { panel = Panel.Search; }
        PopulateRows();
    }

    void MenuSelected(object s, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!ready) return;
        if (e.NewValue is MenuNode node)
        {
            selectedKey = node.Key;
            PopulateRows();
            if (MenuTree.IsKeyboardFocusWithin || MenuTree.IsFocused) panel = Panel.Tree;
            if (node.HasChildren)
                DetailText.Text = $"{node.Path}  /  하위 메뉴 {node.Children.Count}개  /  Enter로 폴더 펼치기";
        }
    }

    void RowSelected(object s, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (ContentGrid.SelectedItem is ItemRow row)
        {
            lastRowLabel = row.Label;
            DetailText.Text = $"{row.Label}  /  {row.Kind}  /  {row.Detail}" + (row.Control is not null ? $"  /  프레임 {row.Control.FrameId}" : "");
            // Only a real user focus change should move the active panel.
            if (ContentGrid.IsKeyboardFocusWithin || ContentGrid.IsFocused) panel = Panel.Content;
        }
    }

    void TableChanged(object s, SelectionChangedEventArgs e)
    {
        if (TablePicker.SelectedItem is not TableRow t) { TableGrid.ItemsSource = null; return; }
        var data = new DataTable();
        int count = t.Rows.Select(x => x.Count).DefaultIfEmpty(t.Columns.Count).Max();
        if (count == 0) count = t.Columns.Count;
        // Prefer real column headers when the bridge reported them.
        bool useColumns = t.Columns.Count > 0 && t.Rows.Count > 0 && t.Rows[0].Count == t.Columns.Count &&
            t.Rows[0].SequenceEqual(t.Columns);
        for (int i = 0; i < count; i++)
            data.Columns.Add(useColumns ? t.Columns[i] : $"열 {i + 1}", typeof(string));
        var source = useColumns ? t.Rows.Skip(1) : t.Rows;
        foreach (var r in source)
        {
            var values = new object[count];
            for (int i = 0; i < count; i++) values[i] = i < r.Count ? r[i] : "";
            data.Rows.Add(values);
        }
        TableGrid.ItemsSource = data.DefaultView;
    }

    // ----------------------------------------------------------- commands
    async void ExecuteSelected(object s, MouseButtonEventArgs e) { if (ContentGrid.SelectedItem is ItemRow r) await Run(() => ActivateRow(r)); }
    async void ReadClicked(object s, RoutedEventArgs e) => await Run(ReadCore);
    async void EditClicked(object s, RoutedEventArgs e)
    {
        if (ContentGrid.SelectedItem is ItemRow { Control: ControlRow c }) await Run(() => ExecuteControl(c, true));
        else StatusText.Text = "편집할 입력 항목을 오른쪽 목록에서 선택하세요.";
    }
    async void WebClicked(object s, RoutedEventArgs e) => await Run(ShowWebCore);

    void TablesClicked(object s, RoutedEventArgs e)
    {
        if (tables.Count == 0) { StatusText.Text = "현재 읽은 화면에 표가 없습니다. F5로 다시 읽거나 F4 원본 웹을 사용하세요."; return; }
        TablesPanel.Visibility = Visibility.Visible; ContentGrid.Visibility = Visibility.Collapsed;
        TablePicker.SelectedIndex = 0; TablePicker.Focus(); panel = Panel.Table;
        DetailText.Text = "표 보기 · Esc로 목록으로 돌아갑니다. 표는 현재 DOM에 있는 행만 표시합니다.";
    }

    void SaveClicked(object s, RoutedEventArgs e)
    {
        SearchBox.Text = "저장"; SearchBox.Focus();
        StatusText.Text = "현재 화면에서 ‘저장’이 포함된 항목을 찾았습니다. 대상 선택 후 Enter로 실행하세요. 자동 저장하지 않습니다.";
    }

    void LoginClicked(object s, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(preferences.StartUrl) { UseShellExecute = true });
            StatusText.Text = "원본 웹을 열었습니다. 평소처럼 로그인한 뒤 확장 버튼에서 이 탭 연결을 누르세요.";
        }
        catch { StatusText.Text = "브라우저를 열지 못했습니다. Chrome에서 기관 업무 주소를 직접 열어 주세요."; }
    }

    void HelpClicked(object s, RoutedEventArgs e) => Dialogs.Help(this);

    void SettingsClicked(object s, RoutedEventArgs e)
    {
        if (Dialogs.Settings(this, preferences)) { ApplyFont(); StatusText.Text = "설정을 저장했습니다. 업무 데이터는 저장하지 않았습니다."; }
    }

    async void TabsClicked(object s, RoutedEventArgs e) => await Run(SwitchTabCore);

    async Task SwitchTabCore()
    {
        if (demo) { StatusText.Text = "합성 UI 시험 모드에서는 탭 전환을 시험하지 않습니다."; return; }
        var r = await bridge.Request(new JsonObject { ["action"] = "tabs" });
        if (!Check(r)) return;
        var tabs = (r["tabs"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().ToList();
        if (tabs.Count == 0) { StatusText.Text = "연결할 수 있는 기관 HTTPS 업무 탭이 없습니다."; return; }
        if (tabs.Count == 1)
        {
            await Relink(tabs[0]["tabId"]!.GetValue<int>());
            return;
        }
        var win = new Window
        {
            Owner = this, Title = "기관 업무 탭 전환", Width = 760, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(7, 27, 81)), Foreground = Brushes.White,
            FontFamily = new FontFamily("Malgun Gothic"), FontSize = 17
        };
        var list = new ListBox { Margin = new Thickness(18), FontSize = 17 };
        foreach (var t in tabs)
            list.Items.Add($"{(t["linked"]?.GetValue<bool>() == true ? "● " : "")}{t["host"]}{t["path"]}  ·  {t["title"]}");
        list.SelectedIndex = 0;
        var ok = new Button { Content = "이 탭에 연결", IsDefault = true, Margin = new Thickness(8), Padding = new Thickness(14, 8, 14, 8) };
        var cancel = new Button { Content = "취소", IsCancel = true, Margin = new Thickness(8), Padding = new Thickness(14, 8, 14, 8) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 8, 10) };
        bar.Children.Add(ok); bar.Children.Add(cancel);
        var dock = new DockPanel(); DockPanel.SetDock(bar, Dock.Bottom); dock.Children.Add(bar); dock.Children.Add(list);
        win.Content = dock;
        ok.Click += (_, _) => win.DialogResult = true;
        if (win.ShowDialog() == true && list.SelectedIndex >= 0)
            await Relink(tabs[list.SelectedIndex]["tabId"]!.GetValue<int>());
    }

    async Task Relink(int tabId)
    {
        var r = await bridge.Request(new JsonObject { ["action"] = "relink", ["tabId"] = tabId });
        if (!Check(r)) return;
        link = r["link"]?.GetValue<int>() ?? link;
        ClearData(); allowActions = false; UpdatePermission();
        StatusText.Text = "연결 탭을 전환했습니다. F5로 새 탭을 읽으세요.";
    }

    void RestoreFocus()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            switch (panel)
            {
                case Panel.Search: SearchBox.Focus(); break;
                case Panel.Table when TablesPanel.Visibility == Visibility.Visible: TablePicker.Focus(); break;
                case Panel.Content: if (ContentGrid.Items.Count > 0) ContentGrid.Focus(); else MenuTree.Focus(); break;
                default: MenuTree.Focus(); break;
            }
        }), DispatcherPriority.Input);
    }

    // ----------------------------------------------------------- keyboard
    async void OnKeys(object s, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (ctrl && key == Key.K) { e.Handled = true; panel = Panel.Search; SearchBox.Focus(); SearchBox.SelectAll(); return; }
        if (ctrl && key == Key.F4) { e.Handled = true; await Run(SwitchTabCore); return; }
        if (key == Key.Tab)
        {
            e.Handled = true;
            bool back = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (TablesPanel.Visibility == Visibility.Visible) { panel = back ? Panel.Content : Panel.Table; (back ? (Control)ContentGrid : TablePicker).Focus(); return; }
            panel = panel switch
            {
                Panel.Tree => Panel.Content,
                Panel.Content => Panel.Search,
                Panel.Search => Panel.Tree,
                _ => Panel.Tree
            };
            if (back) panel = panel switch { Panel.Tree => Panel.Search, Panel.Content => Panel.Tree, Panel.Search => Panel.Content, _ => Panel.Tree };
            switch (panel)
            {
                case Panel.Tree: MenuTree.Focus(); break;
                case Panel.Content: ContentGrid.Focus(); break;
                case Panel.Search: SearchBox.Focus(); SearchBox.SelectAll(); break;
            }
            return;
        }
        if (key == Key.Escape)
        {
            e.Handled = true;
            if (TablesPanel.Visibility == Visibility.Visible)
            {
                TablesPanel.Visibility = Visibility.Collapsed; ContentGrid.Visibility = Visibility.Visible;
                panel = Panel.Content; ContentGrid.Focus();
            }
            else if (SearchBox.Text.Length > 0) { SearchBox.Clear(); panel = Panel.Tree; MenuTree.Focus(); }
            else { panel = Panel.Tree; MenuTree.Focus(); }
            return;
        }
        if (key == Key.Enter)
        {
            if (panel == Panel.Search || SearchBox.IsKeyboardFocusWithin)
            {
                e.Handled = true; panel = Panel.Tree; MenuTree.Focus();
                if (MenuTree.SelectedItem is null && roots.Count > 0) SelectNode(roots[0]);
                return;
            }
            if (MenuTree.IsKeyboardFocusWithin || panel == Panel.Tree)
            {
                e.Handled = true;
                if (MenuTree.SelectedItem is MenuNode node) await Run(() => ActivateMenu(node));
                else if (roots.Count > 0) SelectNode(roots[0]);
                return;
            }
            if (ContentGrid.IsKeyboardFocusWithin && ContentGrid.SelectedItem is ItemRow row)
            {
                e.Handled = true; await Run(() => ActivateRow(row)); return;
            }
            return;
        }
        if (key == Key.Left && ContentGrid.IsKeyboardFocusWithin) { e.Handled = true; panel = Panel.Tree; MenuTree.Focus(); return; }
        if (key == Key.Right && panel == Panel.Tree && MenuTree.SelectedItem is MenuNode sel && sel.HasChildren)
        {
            e.Handled = true; sel.IsExpanded = true; panel = Panel.Content; ContentGrid.Focus(); return;
        }
        switch (key)
        {
            case Key.F1: e.Handled = true; Dialogs.Help(this); break;
            case Key.F2: e.Handled = true; EditClicked(this, new RoutedEventArgs()); break;
            case Key.F3: e.Handled = true; panel = Panel.Search; SearchBox.Focus(); SearchBox.SelectAll(); break;
            case Key.F4: e.Handled = true; await Run(ShowWebCore); break;
            case Key.F5: e.Handled = true; await Run(ReadCore); break;
            case Key.F6: e.Handled = true; TablesClicked(this, new RoutedEventArgs()); break;
            case Key.F7: e.Handled = true; TogglePermission(this, new RoutedEventArgs()); break;
            case Key.F8: e.Handled = true; SaveClicked(this, new RoutedEventArgs()); break;
            case Key.F9: e.Handled = true; LoginClicked(this, new RoutedEventArgs()); break;
            case Key.F10: e.Handled = true; Close(); break;
            case Key.F11: e.Handled = true; WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break;
            case Key.F12: e.Handled = true; SettingsClicked(this, new RoutedEventArgs()); break;
        }
    }

    async Task ActivateRow(ItemRow row)
    {
        if (row.Menu is MenuNode node) await ActivateMenu(node);
        else if (row.Control is ControlRow c) await ExecuteControl(c);
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (busy && !smoke && !demo && MessageBox.Show(this,
            "요청을 처리 중입니다. 종료해도 이미 전송한 작업은 취소되지 않습니다.\n원본 웹에서 결과를 확인해야 합니다. 종료하시겠습니까?",
            "종료 확인", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) { e.Cancel = true; return; }
        bridge.Dispose(); controls.Clear(); tables.Clear();
    }
}
