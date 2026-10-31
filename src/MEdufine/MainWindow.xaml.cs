using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace MEdufine;
public partial class MainWindow : Window
{
    readonly BridgeServer bridge=new();
    readonly Preferences preferences=Preferences.Load();
    List<ControlRow> controls=[];List<TableRow> tables=[];
    bool ready,busy,allowActions,smoke;int link=-1;string currentHost="";
    public MainWindow(bool smoke=false)
    {
        InitializeComponent();this.smoke=smoke;ApplyFont();ready=true;
        bridge.ConnectionChanged+=connected=>Dispatcher.BeginInvoke(new Action(()=>{
            ConnectionText.Text=connected?"● 브라우저 연결됨 · 인증 여부 미확인":"○ 브라우저 연결 대기";
            allowActions=false;UpdatePermission();ClearData();
            StatusText.Text=connected?"F5로 현재 탭을 읽으세요. 로그인 여부는 원본 웹에서 확인합니다.":"연결이 끊겼습니다. 이전 업무 데이터 표시는 지웠습니다.";
        }));
        bridge.Notice+=message=>Dispatcher.BeginInvoke(new Action(()=>{
            if(message["event"]?.GetValue<string>()=="invalidated"){
                link=message["link"]?.GetValue<int>()??-1;ClearData();StatusText.Text="원본 화면이 전환되었습니다. F5로 다시 읽어 주세요.";
            }
        }));
        if(!smoke)bridge.Start();else StatusText.Text="자동 UI 검증 모드 · 실제 업무 데이터 없음 · 네트워크 연결 안 함";
    }
    void ApplyFont(){FontSize=preferences.FontSize;ControlGrid.FontSize=preferences.FontSize;TableGrid.FontSize=preferences.FontSize;ControlGrid.RowHeight=Math.Max(42,preferences.FontSize*2.1);TableGrid.RowHeight=ControlGrid.RowHeight;}
    void ClearData(){controls=[];tables=[];ControlGrid.ItemsSource=null;TablePicker.ItemsSource=null;TableGrid.ItemsSource=null;
        WelcomePanel.Visibility=Visibility.Visible;PageTitle.Text="업무 화면 연결 대기";PageMeta.Text="화면을 다시 읽으면 현재 실제 자료가 표시됩니다.";DetailText.Text="이전 화면의 항목은 재사용하지 않습니다.";}
    async Task Run(Func<Task> work)
    {
        if(busy){StatusText.Text="이전 요청을 처리 중입니다. 저장·입력 명령은 중복 실행하지 않습니다.";return;}
        busy=true;Cursor=Cursors.Wait;
        try{await work();}
        catch(TimeoutException){StatusText.Text="응답 시간이 초과되었습니다. 자동 재실행하지 않습니다. F4 원본 웹에서 결과부터 확인하세요.";}
        catch(Exception){StatusText.Text="요청을 완료하지 못했습니다. 확장 연결·기관 정책·현재 탭을 확인하세요. 저장 작업은 자동 재시도하지 않습니다.";}
        finally{busy=false;Cursor=null;}
    }
    static bool Ok(JsonObject r)=>r["ok"]?.GetValue<bool>()==true;
    bool Check(JsonObject r){if(Ok(r))return true;StatusText.Text=r["error"]?.GetValue<string>()??"원본 웹을 확인해 주세요.";return false;}
    async Task ReadCore()
    {
        var r=await bridge.Request(new(){["action"]="scan"});if(!Check(r)){ClearData();return;}
        link=r["link"]?.GetValue<int>()??-1;currentHost=r["host"]?.GetValue<string>()??"";
        var next=new List<ControlRow>();var nextTables=new List<TableRow>();bool clipped=r["truncated"]?.GetValue<bool>()==true;
        int protectedCount=0;
        foreach(var p in r["pages"]?.AsArray()??new JsonArray())
        {
            if(p is null)continue;
            int frame=p["frameId"]!.GetValue<int>();string doc=p["docId"]!.GetValue<string>(),revision=p["revision"]!.GetValue<string>();
            clipped|=p["truncated"]?.GetValue<bool>()==true;protectedCount+=p["protectedCount"]?.GetValue<int>()??0;
            foreach(var c in p["controls"]?.AsArray()??new JsonArray())
            {
                if(c is null)continue;
                var options=new List<Choice>();foreach(var opt in c["options"]?.AsArray()??new JsonArray())if(opt is not null)
                    options.Add(new(opt["value"]?.GetValue<string>()??"",opt["label"]?.GetValue<string>()??"",opt["disabled"]?.GetValue<bool>()==true));
                next.Add(new(next.Count+1,frame,doc,revision,c["id"]!.GetValue<string>(),c["kind"]?.GetValue<string>()??"항목",
                    c["label"]?.GetValue<string>()??"",c["value"]?.GetValue<string>()??"",c["editValue"]?.GetValue<string>()??"",c["editable"]?.GetValue<bool>()==true,
                    c["disabled"]?.GetValue<bool>()==true,c["webOnly"]?.GetValue<bool>()==true,options));
            }
            foreach(var t in p["tables"]?.AsArray()??new JsonArray())if(t is not null){
                var rows=new List<List<string>>();foreach(var row in t["rows"]?.AsArray()??new JsonArray())if(row is JsonArray cells)
                    rows.Add(cells.Select(v=>v?.GetValue<string>()??"").ToList());
                nextTables.Add(new($"{t["name"]?.GetValue<string>()} · 프레임 {frame}",rows));
            }
        }
        controls=next;tables=nextTables;WelcomePanel.Visibility=Visibility.Collapsed;
        PageTitle.Text=r["title"]?.GetValue<string>()??"현재 업무 화면";
        PageMeta.Text=$"{currentHost}  /  읽은 시각 {DateTime.Now:HH:mm:ss}  /  항목 {controls.Count}개 · 표 {tables.Count}개";
        TablePicker.ItemsSource=tables;if(tables.Count>0)TablePicker.SelectedIndex=0;
        ApplyFilter();StatusText.Text=$"화면 읽기 완료 · 보호 입력 {protectedCount}개 제외 · 접근 불가 프레임 {r["inaccessible"]?.GetValue<int>()??0}개"+
            (clipped?" · 일부만 표시":"")+" · 서버 재조회는 원본의 조회 버튼으로 실행";
        if(controls.Count==0&&tables.Count==0)StatusText.Text="인식 가능한 항목이 없습니다. 인증 전이거나 전용 화면일 수 있습니다. F4 원본 웹으로 전환하세요.";
        ControlGrid.Focus();if(ControlGrid.Items.Count>0)ControlGrid.SelectedIndex=0;
    }
    void ApplyFilter()
    {
        if(!ready)return;
        int index=CategoryList.SelectedIndex;string q=SearchBox.Text.Trim();
        var rows=controls.Where(c=>(index switch{1=>c.Kind=="메뉴",2=>c.Kind=="버튼",3=>c.Kind is "입력" or "선택" or "체크",_=>true})&&
            (q.Length==0||c.Label.Contains(q,StringComparison.OrdinalIgnoreCase)||c.Value.Contains(q,StringComparison.OrdinalIgnoreCase))).ToList();
        ControlGrid.ItemsSource=rows;
        TablesPanel.Visibility=index==4?Visibility.Visible:Visibility.Collapsed;ControlGrid.Visibility=index==4?Visibility.Collapsed:Visibility.Visible;
        if(rows.Count>0)ControlGrid.SelectedIndex=0;
    }
    JsonObject Target(ControlRow row,string action)=>new(){["action"]=action,["link"]=link,["frameId"]=row.FrameId,["docId"]=row.DocId,
        ["revision"]=row.Revision,["targetId"]=row.TargetId};
    async Task ExecuteCore(bool editOnly=false)
    {
        if(ControlGrid.SelectedItem is not ControlRow row){StatusText.Text="먼저 항목을 선택하세요.";return;}
        if(row.Disabled){StatusText.Text="현재 웹에서 비활성화된 항목입니다.";return;}
        if(row.WebOnly){await bridge.Request(Target(row,"focus"));await ShowWebCore();StatusText.Text="중요 처리 항목은 원본 웹에서 직접 최종 확인하세요.";return;}
        if(!allowActions){StatusText.Text="현재 읽기 전용입니다. F7로 이번 연결의 입력·클릭을 허용하세요.";return;}
        if(editOnly&&!row.Editable){StatusText.Text="이 항목은 직접 편집할 수 없습니다. Enter 실행 또는 F4를 사용하세요.";return;}
        JsonObject request;
        if(row.Editable)
        {
            var value=Dialogs.Edit(this,row,currentHost);if(value is null)return;
            request=Target(row,"set");request["value"]=value;request["confirmed"]=true;
        }
        else
        {
            if(MessageBox.Show(this,$"[{currentHost}]\n\n실제 웹의 ‘{row.Label}’ 항목을 실행합니다.\n등록·저장 등 실제 업무에 영향을 줄 수 있습니다.\n\n실행하시겠습니까?","실제 웹 실행 확인",MessageBoxButton.OKCancel,MessageBoxImage.Warning,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
            request=Target(row,"click");request["confirmed"]=true;
        }
        var result=await bridge.Request(request);if(!Check(result))return;
        var notice=result["notice"]?.GetValue<string>()??"웹 작업을 전달했습니다. 원본 결과를 확인하세요.";
        // No write retry and no optimistic 'saved' status. A fresh DOM read does not prove server persistence.
        await Task.Delay(350);await ReadCore();StatusText.Text=notice;
    }
    async Task ShowWebCore(){Check(await bridge.Request(new(){["action"]="showWeb"}));}
    void UpdatePermission()=>ReadOnlyButton.Content=allowActions?"F7  입력·클릭 허용 중":"F7  읽기 전용";
    void TogglePermission(object? sender,RoutedEventArgs e)
    {
        if(!bridge.Connected){StatusText.Text="먼저 브라우저를 연결하세요.";return;}
        if(!allowActions&&MessageBox.Show(this,"이번 연결에서 실제 웹의 입력란과 버튼을 조작하도록 허용합니다.\n입력 변경만으로 자동 저장되는 화면도 있습니다.\n각 실행 전 내용을 확인하고, 중요한 결재·지급·삭제는 원본 웹에서 처리하세요.","실제 업무 조작 허용",MessageBoxButton.OKCancel,MessageBoxImage.Warning,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
        allowActions=!allowActions;UpdatePermission();
    }
    void SearchChanged(object s,TextChangedEventArgs e)=>ApplyFilter();
    void CategoryChanged(object s,SelectionChangedEventArgs e){if(!ready)return;if(CategoryList.SelectedIndex==5){ShowHelp();CategoryList.SelectedIndex=0;}else ApplyFilter();}
    void SelectionChanged(object s,SelectionChangedEventArgs e){if(ControlGrid.SelectedItem is ControlRow c)DetailText.Text=$"{c.Label}  /  {c.Kind}  /  프레임 {c.FrameId}"+(c.Disabled?"  /  비활성":c.WebOnly?"  /  원본 웹에서 처리":c.Editable?"  /  Enter 또는 F2 입력":"  /  Enter 실행");}
    void TableChanged(object s,SelectionChangedEventArgs e)
    {
        if(TablePicker.SelectedItem is not TableRow t){TableGrid.ItemsSource=null;return;}
        var data=new DataTable();int count=t.Rows.Select(x=>x.Count).DefaultIfEmpty().Max();
        for(int i=0;i<count;i++)data.Columns.Add($"열 {i+1}",typeof(string));
        foreach(var r in t.Rows){var values=new object[count];for(int i=0;i<count;i++)values[i]=i<r.Count?r[i]:"";data.Rows.Add(values);}
        TableGrid.ItemsSource=data.DefaultView;
    }
    async void ExecuteSelected(object s,MouseButtonEventArgs e)=>await Run(()=>ExecuteCore());
    async void ReadClicked(object s,RoutedEventArgs e)=>await Run(ReadCore);
    async void EditClicked(object s,RoutedEventArgs e)=>await Run(()=>ExecuteCore(true));
    async void WebClicked(object s,RoutedEventArgs e)=>await Run(ShowWebCore);
    void TablesClicked(object s,RoutedEventArgs e){CategoryList.SelectedIndex=4;TablePicker.Focus();}
    void SaveClicked(object s,RoutedEventArgs e){CategoryList.SelectedIndex=0;SearchBox.Text="저장";ControlGrid.Focus();StatusText.Text="실제 화면에서 ‘저장’이 포함된 항목을 찾았습니다. 대상 선택 후 Enter로 실행하세요. 자동 저장하지 않습니다.";}
    void LoginClicked(object s,RoutedEventArgs e)
    {
        try{Process.Start(new ProcessStartInfo(preferences.StartUrl){UseShellExecute=true});StatusText.Text="원본 웹을 열었습니다. 평소처럼 로그인한 뒤 확장 버튼에서 이 탭 연결을 누르세요.";}
        catch{StatusText.Text="브라우저를 열지 못했습니다. Edge에서 기관 업무 주소를 직접 열어 주세요.";}
    }
    void HelpClicked(object s,RoutedEventArgs e)=>ShowHelp();
    void ShowHelp()=>Dialogs.Help(this);
    void SettingsClicked(object s,RoutedEventArgs e)
    {
        if(Dialogs.Settings(this,preferences)){ApplyFont();StatusText.Text="설정을 저장했습니다. 업무 데이터는 저장하지 않았습니다.";}
    }
    async void OnKeys(object s,KeyEventArgs e)
    {
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        bool ctrl=Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if(ctrl&&key==Key.K){e.Handled=true;SearchBox.Focus();SearchBox.SelectAll();return;}
        if(key==Key.Enter&&ControlGrid.IsKeyboardFocusWithin){e.Handled=true;await Run(()=>ExecuteCore());return;}
        switch(key)
        {
            case Key.F1:e.Handled=true;ShowHelp();break;
            case Key.F2:e.Handled=true;await Run(()=>ExecuteCore(true));break;
            case Key.F3:e.Handled=true;SearchBox.Focus();SearchBox.SelectAll();break;
            case Key.F4:e.Handled=true;await Run(ShowWebCore);break;
            case Key.F5:e.Handled=true;await Run(ReadCore);break;
            case Key.F6:e.Handled=true;TablesClicked(this,new());break;
            case Key.F7:e.Handled=true;TogglePermission(this,new());break;
            case Key.F8:e.Handled=true;SaveClicked(this,new());break;
            case Key.F9:e.Handled=true;LoginClicked(this,new());break;
            case Key.F10:e.Handled=true;Close();break;
            case Key.F11:e.Handled=true;WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;break;
            case Key.F12:e.Handled=true;SettingsClicked(this,new());break;
            case Key.Escape:e.Handled=true;SearchBox.Clear();ControlGrid.Focus();break;
        }
    }
    void OnClosing(object? sender,CancelEventArgs e)
    {
        if(busy&&!smoke&&MessageBox.Show(this,"요청을 처리 중입니다. 종료해도 이미 전송한 작업은 취소되지 않습니다.\n원본 웹에서 결과를 확인해야 합니다. 종료하시겠습니까?","종료 확인",MessageBoxButton.OKCancel,MessageBoxImage.Warning,MessageBoxResult.Cancel)!=MessageBoxResult.OK){e.Cancel=true;return;}
        bridge.Dispose();controls.Clear();tables.Clear();
    }
}
