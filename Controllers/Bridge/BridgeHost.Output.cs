using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
namespace Cronopus;

public sealed partial class BridgeHost
{
    private readonly BridgeOutput _output=new();
    private readonly ConcurrentDictionary<string,byte> _outputStreams=new();
    private void ObserveOutput(string json,string task)
    {
        try{using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
            if(root.TryGetProperty("line",out var line))_output.Append(task,root.TryGetProperty("run",out var run)?run.GetString()??"":"",Encoding.UTF8.GetBytes(line.GetString()??""));
        }catch(JsonException){}
    }
    private async Task<bool> OutputApi(HttpContext ctx,string key,CancellationToken ct)
    {
        var parts=ctx.Request.Path.Value!.Split('/',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length<5||parts[0]!="bridge"||parts[1]!="v1"||parts[2]!="tasks"||parts[4]!="output"||ctx.Request.Method!="GET")return false;
        var id=parts[3];TaskerService.ValidateId(id);var run=ctx.Request.Query["run"].FirstOrDefault();var cursor=ctx.Request.Query["cursor"].FirstOrDefault();
        if(parts.Length==5){
            var page=_output.Read(id,run,cursor,200,ctx.Request.Query["direction"]=="before");
            if(cursor==null&&string.IsNullOrEmpty(run)&&page.GetProperty("items").GetArrayLength()==0){
                var saved=tasker.SavedOutputTail(id);if(saved.Length>0){_output.Append(id,"",Encoding.UTF8.GetBytes(saved));page=_output.Read(id,run,null,200);}
            }
            await ctx.Response.WriteAsJsonAsync(page,ct);return true;
        }
        if(parts.Length!=6||parts[5]!="stream"){ctx.Response.StatusCode=404;return true;}
        if(!_outputStreams.TryAdd(key,0)){ctx.Response.StatusCode=429;return true;}
        try{
            ctx.Response.ContentType="text/event-stream";
            while(!ct.IsCancellationRequested){var page=_output.Read(id,run,cursor,200,false,64000);cursor=page.GetProperty("next_cursor").GetString();
                await ctx.Response.WriteAsync("event: page\ndata: "+page.GetRawText()+"\n\n",ct);await ctx.Response.Body.FlushAsync(ct);await Task.Delay(1000,ct);}
        }finally{_outputStreams.TryRemove(key,out _);}
        return true;
    }
}
