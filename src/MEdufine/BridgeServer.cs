using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace MEdufine;
internal static class Wire
{
    public const int MaxSize=900_000;
    public static async Task<JsonObject?> Read(Stream stream,CancellationToken token=default)
    {
        var header=new byte[4];int first=await stream.ReadAsync(header.AsMemory(0,4),token);
        if(first==0)return null;
        if(first<4)await stream.ReadExactlyAsync(header.AsMemory(first,4-first),token);
        int size=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(size<2||size>MaxSize)throw new InvalidDataException("Invalid frame size");
        var bytes=new byte[size];await stream.ReadExactlyAsync(bytes,token);
        return JsonNode.Parse(bytes,new JsonNodeOptions(),new JsonDocumentOptions{MaxDepth=40}) as JsonObject
            ?? throw new InvalidDataException("Invalid message");
    }
    public static async Task Write(Stream stream,JsonObject payload,CancellationToken token=default)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(payload);
        if(bytes.Length>MaxSize)throw new InvalidDataException("Message too large");
        var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);
        await stream.WriteAsync(header,token);await stream.WriteAsync(bytes,token);await stream.FlushAsync(token);
    }
}
internal sealed class BridgeServer : IDisposable
{
    public static string UserKey => WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    public static string PipeName => "M-Edufine."+UserKey;
    readonly CancellationTokenSource stop=new();
    readonly SemaphoreSlim writer=new(1,1);
    readonly ConcurrentDictionary<string,TaskCompletionSource<JsonObject>> pending=new();
    NamedPipeServerStream? pipe;
    public bool Connected => pipe?.IsConnected==true;
    public event Action<bool>? ConnectionChanged;
    public event Action<JsonObject>? Notice;
    public void Start()=>_ = Listen();
    async Task Listen()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                using var current=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                pipe=current;await current.WaitForConnectionAsync(stop.Token);ConnectionChanged?.Invoke(true);
                while(!stop.IsCancellationRequested)
                {
                    var message=await Wire.Read(current,stop.Token);if(message is null)break;
                    var id=message["id"]?.GetValue<string>();
                    if(id is not null && pending.TryRemove(id,out var tcs))tcs.TrySetResult(message);
                    else if(message.ContainsKey("event"))Notice?.Invoke(message);
                }
            }
            catch(OperationCanceledException){break;}
            catch(ObjectDisposedException) when(stop.IsCancellationRequested){break;}
            catch(IOException){ }
            catch(InvalidDataException){ }
            finally
            {
                pipe=null;foreach(var kv in pending)if(pending.TryRemove(kv.Key,out var tcs))tcs.TrySetException(new IOException("브라우저 연결이 끊겼습니다."));
                ConnectionChanged?.Invoke(false);
            }
        }
    }
    public async Task<JsonObject> Request(JsonObject request)
    {
        var current=pipe;if(current?.IsConnected!=true)throw new IOException("먼저 브라우저 확장 버튼에서 이 탭 연결을 누르세요.");
        var id=Guid.NewGuid().ToString("N");request["id"]=id;
        var tcs=new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=tcs;
        try
        {
            await writer.WaitAsync(stop.Token);
            try{await Wire.Write(current,request,stop.Token);}finally{writer.Release();}
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(20),stop.Token);
        }
        finally{pending.TryRemove(id,out _);}
    }
    public void Dispose(){stop.Cancel();pipe?.Dispose();}
}
