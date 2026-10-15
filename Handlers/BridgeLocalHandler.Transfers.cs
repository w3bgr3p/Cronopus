using System.Net;
using System.Text.Json;
namespace z3nDash;

public sealed partial class BridgeLocalHandler
{
    private readonly BridgeTransfers _transfers=new(store,new BridgeClient(store));
    private async Task<bool> TransferRoute(HttpListenerContext ctx,string path)
    {
        var method=ctx.Request.HttpMethod;
        if(path=="selection"&&method=="POST"){
            var body=await Body(ctx);var mode=body.GetProperty("mode").GetString();
            if(mode is not "file" and not "folder")throw new ArgumentException("Invalid selection mode");
            var selected=TaskerService.ShowPicker(mode,"","");
            await Write(ctx,string.IsNullOrEmpty(selected)?new{cancelled=true}:_transfers.Public(_transfers.Select(selected)));return true;
        }
        var parts=path.Split('/');
        if(parts.Length==2&&parts[1]=="transfers"&&method=="POST"){
            var body=await Body(ctx);store.Connection(parts[0]);
            await Write(ctx,_transfers.Start(body.GetProperty("selectionId").GetString()!,parts[0],body.GetProperty("destination").GetString()!));return true;
        }
        if(parts.Length>=2&&parts[0]=="transfers"){
            if(method=="GET"&&parts.Length==2){await Write(ctx,_transfers.Public(_transfers.Get(parts[1])));return true;}
            if(method=="DELETE"&&parts.Length==2){await _transfers.Cancel(parts[1]);await Write(ctx,new{ok=true});return true;}
            if(method=="POST"&&parts.Length==3&&parts[2]=="resume"){
                var job=_transfers.Get(parts[1]);await Write(ctx,_transfers.Start(job.Id,job.ConnectionId,job.Destination));return true;}
            if(method=="POST"&&parts.Length==3&&parts[2]=="confirm"){
                var body=await Body(ctx);_transfers.Confirm(parts[1],body.GetProperty("replace").EnumerateArray().Select(x=>x.GetString()!).ToList());await Write(ctx,new{ok=true});return true;}
        }
        return false;
    }
}
