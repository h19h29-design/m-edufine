using System.IO;
using System.Text.Json;
namespace MEdufine;
public sealed record Choice(string Value,string Label,bool Disabled)
{public override string ToString()=>Label;}
public sealed record ControlRow(int Number,int FrameId,string DocId,string Revision,string TargetId,string Kind,
    string Label,string Value,string EditValue,bool Editable,bool Disabled,bool WebOnly,List<Choice> Options);
public sealed record TableRow(string Name,List<List<string>> Rows)
{public override string ToString()=>Name;}
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
