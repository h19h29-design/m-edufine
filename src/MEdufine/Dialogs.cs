using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace MEdufine;
internal static class Dialogs
{
    static Window Shell(Window owner,string title,StackPanel content,double width=740,double height=520)
    {
        var window=new Window{Owner=owner,Title=title,Width=width,Height=height,MinWidth=580,MinHeight=360,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(7,27,81)),
            Foreground=Brushes.White,FontFamily=new FontFamily("Malgun Gothic"),FontSize=18,
            Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        content.Margin=new Thickness(24);return window;
    }
    static TextBlock Text(string text,int size=18)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)};
    static Button Button(string label)=>new(){Content=label,Padding=new Thickness(15,9,15,9),Margin=new Thickness(0,10,10,0),FontSize=17};
    public static string? Edit(Window owner,ControlRow row,string host)
    {
        var panel=new StackPanel();var win=Shell(owner,"실제 웹 입력 · "+row.Label,panel);
        panel.Children.Add(Text(host+"\n"+row.Label,22));
        panel.Children.Add(Text("이 값은 실제 웹 입력란에 전달됩니다. 화면에 따라 변경 즉시 자동 저장될 수 있습니다. 최종 결과는 원본 웹에서 확인하세요.",16));
        TextBox? input=null;ComboBox? select=null;
        if(row.Kind=="선택"){
            select=new ComboBox{ItemsSource=row.Options.Where(o=>!o.Disabled).ToList(),MinHeight=40,FontSize=18};
            select.SelectedItem=row.Options.FirstOrDefault(o=>o.Value==row.EditValue&&!o.Disabled);panel.Children.Add(select);
        }else{
            input=new TextBox{Text=row.EditValue,FontSize=19,MinHeight=100,MaxLength=4000,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};panel.Children.Add(input);
        }
        string? result=null;var buttons=new WrapPanel();var apply=Button("실제 입력란에 적용 (Alt+A)");apply.Content="실제 입력란에 적용 (_A)";
        var cancel=Button("취소 (Esc)");cancel.IsCancel=true;
        apply.Click+=(_,_)=>{if(select is not null&&select.SelectedItem is not Choice)return;result=select is not null?((Choice)select.SelectedItem).Value:input!.Text;win.DialogResult=true;};
        buttons.Children.Add(apply);buttons.Children.Add(cancel);panel.Children.Add(buttons);
        win.Loaded+=(_,_)=>{if(input is not null){input.Focus();input.SelectAll();}else select?.Focus();};
        return win.ShowDialog()==true?result:null;
    }
    public static void Help(Window owner)
    {
        var p=new StackPanel();var w=Shell(owner,"처음 연결하기 · M-에듀파인",p,850,720);
        p.Children.Add(Text("한 번만 설정하면, 이후에는 로그인 → 연결",25));
        p.Children.Add(Text("① 아래 버튼으로 확장 폴더를 엽니다.\n② Edge 주소창에 edge://extensions 를 입력합니다. 개발자 모드를 켜고 ‘압축 풀린 확장 로드’로 이 폴더를 선택합니다.\n③ 실제 K-에듀파인 탭에서 로그인한 뒤 확장 ‘M-에듀파인 연결’ → ‘이 탭 연결’을 누릅니다.\n④ 이 프로그램으로 돌아와 F5를 누릅니다.\n\n기관에서 확장 설치를 제한한 PC에서는 관리자 승인·배포가 필요합니다. 정책을 우회하지 마세요.",18));
        var open=Button("확장 폴더 열기");open.Click+=(_,_)=>{
            var folder=Path.Combine(AppContext.BaseDirectory,"extension");
            if(Directory.Exists(folder))Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});
            else MessageBox.Show(w,"설치파일을 이용하거나 portable 패키지 전체를 압축 해제해 주세요.");};p.Children.Add(open);
        p.Children.Add(Text("\n키보드\nF2 입력 · F3 찾기 · F4 연결한 원본 웹 · F5 현재 DOM 읽기\nF6 표 · F7 읽기 전용 전환 · F8 저장 버튼 찾기\nF9 로그인 페이지 · F10 종료 · F11 최대화 · F12 설정\n방향키 이동 · Enter 실행 · Tab 영역 이동 · Esc 검색 해제",17));
        p.Children.Add(Text("반드시 알아둘 점\n• F5는 서버 재조회가 아닙니다. 웹의 조회 버튼으로 최신 자료를 불러오세요.\n• 로그인 상태나 저장 성공을 추측하지 않습니다. 원본 웹 결과를 확인하세요.\n• 팝업·새 탭은 그 창에서 ‘이 탭 연결’을 다시 누릅니다.\n• 특수 그리드·캔버스·보안 모듈은 F4로 원본 웹에서 처리합니다.\n• 입력·업무 자료를 디스크나 외부 AI로 전송하지 않습니다.\n• 실제 K-에듀파인 계정의 호환성은 현장검증 전입니다.",16));
        var close=Button("닫기");close.IsCancel=true;p.Children.Add(close);w.ShowDialog();
    }
    public static bool Settings(Window owner,Preferences preferences)
    {
        var p=new StackPanel();var w=Shell(owner,"설정",p,760,530);
        p.Children.Add(Text("시작 주소",22));var url=new TextBox{Text=preferences.StartUrl,Padding=new Thickness(8),FontSize=17};p.Children.Add(url);
        p.Children.Add(Text("기관의 HTTPS 주소만 사용합니다. 토큰이 포함될 수 있는 쿼리·해시 주소는 저장하지 않습니다.",15));
        p.Children.Add(Text("글자 크기 (16~28)",18));var size=new Slider{Minimum=16,Maximum=28,Value=preferences.FontSize,TickFrequency=2,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,0,25)};p.Children.Add(size);
        p.Children.Add(Text("기본 주소: https://klef.sen.go.kr/\n업무포털: https://sen.eduptl.kr/\n설정은 사용자 LocalAppData 폴더에 저장됩니다. 업무 데이터나 인증서는 저장하지 않습니다.",16));
        var save=Button("설정 저장");save.IsDefault=true;save.Click+=(_,_)=>{
            if(!Preferences.ValidUrl(url.Text.Trim())){MessageBox.Show(w,".go.kr 또는 .eduptl.kr의 HTTPS 주소를 입력하세요. 쿼리, 해시, 사용자정보, 별도 포트는 사용할 수 없습니다.");return;}
            preferences.StartUrl=url.Text.Trim();preferences.FontSize=size.Value;
            try{preferences.Save();w.DialogResult=true;MessageBox.Show(owner,"설정을 저장했습니다.","저장 완료");}catch{MessageBox.Show(w,"설정을 저장하지 못했습니다. 사용자 폴더 권한을 확인하세요.");}};
        var cancel=Button("취소");cancel.IsCancel=true;var buttons=new WrapPanel();buttons.Children.Add(save);buttons.Children.Add(cancel);p.Children.Add(buttons);
        return w.ShowDialog()==true;
    }
}
