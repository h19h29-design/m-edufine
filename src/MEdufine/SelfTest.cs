using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace MEdufine;
internal static class SelfTest
{
    static async Task NativeRoundtrip()
    {
        // Isolate from a real running instance: same-machine users can have the app open,
        // and a shared pipe would let the test child attach to live institutional data.
        Environment.SetEnvironmentVariable("MEDUFINE_PIPE_NAME","M-Edufine.self-test."+Environment.ProcessId+"."+Guid.NewGuid().ToString("N")[..8]);
        using var server=new BridgeServer();
        var connected=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ConnectionChanged+=c=>{if(c)connected.TrySetResult();};server.Start();
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("chrome-extension://"+BuildInfo.ExtensionId+"/");
        using var child=Process.Start(start)??throw new Exception("Host failed to start");
        try
        {
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var responseTask=server.Request(new JsonObject{["action"]="scan"});
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var outbound=await Wire.Read(child.StandardOutput.BaseStream,timeout.Token)??throw new Exception("Missing request");
            await Wire.Write(child.StandardInput.BaseStream,new JsonObject{["id"]=outbound["id"]!.GetValue<string>(),["ok"]=true,["message"]="한글 왕복"},timeout.Token);
            var response=await responseTask;
            if(response["message"]?.GetValue<string>()!="한글 왕복")throw new Exception("Mismatched response");
        }
        finally {if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}}
    }
    public static int Run()
    {
        var results=new List<object>();int failures=0;
        void Test(string name,Action action){try{action();results.Add(new{name,pass=true});}catch(Exception e){failures++;results.Add(new{name,pass=false,error=e.GetType().Name});}}
        void Assert(bool b){if(!b)throw new InvalidOperationException("Assertion failed");}
        Test("HTTPS official address",()=>Assert(Preferences.ValidUrl("https://klef.sen.go.kr/")));
        Test("HTTP rejected",()=>Assert(!Preferences.ValidUrl("http://klef.sen.go.kr/")));
        Test("lookalike rejected",()=>Assert(!Preferences.ValidUrl("https://klef.sen.go.kr.evil.example/")));
        Test("userinfo rejected",()=>Assert(!Preferences.ValidUrl("https://user@klef.sen.go.kr/")));
        Test("token query rejected",()=>Assert(!Preferences.ValidUrl("https://klef.sen.go.kr/?token=abc")));
        Test("fragment rejected",()=>Assert(!Preferences.ValidUrl("https://klef.sen.go.kr/#secret")));
        Test("port rejected",()=>Assert(!Preferences.ValidUrl("https://klef.sen.go.kr:8000/")));
        Test("file rejected",()=>Assert(!Preferences.ValidUrl("file:///C:/Windows")));
        Test("extension ID format",()=>Assert(BuildInfo.ExtensionId.Length==32&&BuildInfo.ExtensionId.All(c=>c>='a'&&c<='p')));
        Test("native UTF8 roundtrip",()=>{using var s=new MemoryStream();Wire.Write(s,new JsonObject{["value"]="한글 물품"}).GetAwaiter().GetResult();s.Position=0;Assert(Wire.Read(s).GetAwaiter().GetResult()?["value"]?.GetValue<string>()=="한글 물품");});
        Test("oversized frame rejected",()=>{using var s=new MemoryStream(BitConverter.GetBytes(Wire.MaxSize+1));try{Wire.Read(s).GetAwaiter().GetResult();throw new Exception();}catch(InvalidDataException){}});
        Test("truncated frame rejected",()=>{using var s=new MemoryStream(new byte[]{10,0,0,0,1});try{Wire.Read(s).GetAwaiter().GetResult();throw new Exception();}catch(EndOfStreamException){}});
        Test("non-object frame rejected",()=>{using var s=new MemoryStream(new byte[]{2,0,0,0,91,93});try{Wire.Read(s).GetAwaiter().GetResult();throw new Exception();}catch(InvalidDataException){}});
        Test("native host and current-user pipe roundtrip",()=>NativeRoundtrip().GetAwaiter().GetResult());
        Test("foreign extension origin rejected",()=>{
            using var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/"}})!;
            if(!child.WaitForExit(10000)){child.Kill();throw new Exception("Child timeout");}Assert(child.ExitCode==2);
        });
        Test("EOF clean",()=>{using var s=new MemoryStream();Assert(Wire.Read(s).GetAwaiter().GetResult() is null);});
        Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/self-test.json",JsonSerializer.Serialize(new{total=results.Count,failures,results},new JsonSerializerOptions{WriteIndented=true}));
        return failures==0?0:1;
    }
}
