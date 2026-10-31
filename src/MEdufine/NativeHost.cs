using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json.Nodes;
namespace MEdufine;
internal static class NativeHost
{
    public static async Task RunAsync()
    {
        // STDOUT is reserved exclusively for length-prefixed native messages.
        using var pipe=new NamedPipeClientStream(".",BridgeServer.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        try {await pipe.ConnectAsync(1200);}
        catch(TimeoutException)
        {
            // In isolated self-tests (MEDUFINE_PIPE_NAME set) never spawn a real app window.
            if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEDUFINE_PIPE_NAME")))
                throw new TimeoutException("Test pipe unavailable");
            var exe=Environment.ProcessPath??throw new IOException("Executable path unavailable");
            Process.Start(new ProcessStartInfo(exe){UseShellExecute=false});
            await pipe.ConnectAsync(15000);
        }
        using var stdin=Console.OpenStandardInput();using var stdout=Console.OpenStandardOutput();
        using var cancel=new CancellationTokenSource();
        async Task Relay(Stream source,Stream destination)
        {
            while(!cancel.IsCancellationRequested)
            {
                var message=await Wire.Read(source,cancel.Token);if(message is null)break;
                await Wire.Write(destination,message,cancel.Token);
            }
        }
        var inbound=Relay(stdin,pipe);var outbound=Relay(pipe,stdout);
        await Task.WhenAny(inbound,outbound);cancel.Cancel();
        // Native host process ends with its browser port; no unattended service is installed.
    }
}
