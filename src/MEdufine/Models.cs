using System.ComponentModel;
using System.IO;
using System.Text.Json;
namespace MEdufine;
public sealed record Choice(string Value,string Label,bool Disabled)
{public override string ToString()=>Label;}
public sealed record ControlRow(int Number,int FrameId,string DocId,string Revision,string TargetId,string Kind,
    string Label,string Value,string EditValue,bool Editable,bool Disabled,bool WebOnly,string Context,List<Choice> Options)
{
    public string Display => Value.Length>0?Value:Kind;
}
public sealed record TableRow(string Name,List<string> Columns,List<List<string>> Rows)
{public override string ToString()=>Name;}

/// <summary>One node of the business menu tree read from the current page.</summary>
public sealed class MenuNode : INotifyPropertyChanged
{
    public required string Key{get;init;}
    public required string Label{get;init;}
    public required string Path{get;init;}
    public required int FrameId{get;init;}
    public required int Level{get;init;}
    public string TargetId{get;set;}="";
    public string DocId{get;set;}="";
    public string Revision{get;set;}="";
    public string Region{get;set;}="";
    public bool Active{get;set;}
    public bool WebOnly{get;set;}
    public List<MenuNode> Children{get;}=[];
    public MenuNode? Parent{get;set;}
    public int Depth{get;set;}
    public bool HasChildren=>Children.Count>0;
    bool expanded;
    public bool IsExpanded{get=>expanded;set{if(expanded==value)return;expanded=value;PropertyChanged?.Invoke(this,new(nameof(IsExpanded)));}}
    bool isSelected;
    public bool IsSelected{get=>isSelected;set{if(isSelected==value)return;isSelected=value;PropertyChanged?.Invoke(this,new(nameof(IsSelected)));}}
    public string Badge=>Active?"● 현재":HasChildren?$"{Children.Count}":WebOnly?"웹":"";
    public event PropertyChangedEventHandler? PropertyChanged;
    public override string ToString()=>Label;
    public IEnumerable<MenuNode> Walk(){yield return this;foreach(var c in Children)foreach(var n in c.Walk())yield return n;}
}

/// <summary>A row in the right-hand list: either a submenu or a live web control.</summary>
public sealed class ItemRow
{
    public required string Kind{get;init;}
    public required string Label{get;init;}
    public string Value{get;init;}="";
    public MenuNode? Menu{get;init;}
    public ControlRow? Control{get;init;}
    public string Detail=>Menu is not null
        ? (Menu.HasChildren?$"폴더 · 하위 {Menu.Children.Count}개":Menu.WebOnly?"원본 웹에서 처리":"실제 메뉴 · Enter로 실행")
        : Control is not null
            ? (Control.Disabled?"비활성":Control.WebOnly?"원본 웹에서 처리":Control.Editable?"입력란 · F2로 편집":"실제 웹 항목")
            : "";
    public override string ToString()=>Label;
}

internal sealed class Preferences
{
    public string StartUrl{get;set;}="https://klef.sen.go.kr/";
    public double FontSize{get;set;}=18;
    public static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"M-Edufine");
    public static bool ValidUrl(string value)
    {
        return Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.Scheme=="https"&&string.IsNullOrEmpty(u.UserInfo)&&
            (u.Host.EndsWith(".go.kr",StringComparison.OrdinalIgnoreCase)||u.Host.EndsWith(".eduptl.kr",StringComparison.OrdinalIgnoreCase))&&
            string.IsNullOrEmpty(u.Query)&&string.IsNullOrEmpty(u.Fragment)&&u.IsDefaultPort;
    }
    public static Preferences Load()
    {
        try {var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Folder,"settings.json")))??new();
            if(!ValidUrl(p.StartUrl))p.StartUrl="https://klef.sen.go.kr/";p.FontSize=Math.Clamp(p.FontSize,16,28);return p;}
        catch{return new();}
    }
    public void Save(){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"settings.json"),JsonSerializer.Serialize(this));}
}
