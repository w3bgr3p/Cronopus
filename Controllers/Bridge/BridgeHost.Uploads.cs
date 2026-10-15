using System.Text.Json;
using Microsoft.AspNetCore.Http;
namespace z3nDash;

public sealed partial class BridgeHost
{
    private readonly BridgeUploads _uploads = new(store);
    private async Task<bool> UploadApi(HttpContext ctx,string key,CancellationToken ct)
    {
        var path=ctx.Request.Path.Value!;
        if(!path.StartsWith("/bridge/v1/uploads",StringComparison.Ordinal))return false;
        var parts=path.Split('/',StringSplitOptions.RemoveEmptyEntries);object? result=null;
        if(parts.Length==3&&ctx.Request.Method=="POST")result=await _uploads.Create(key,await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body,cancellationToken:ct),ct);
        else if(parts.Length==4&&ctx.Request.Method=="GET")result=_uploads.Status(key,parts[3]);
        else if(parts.Length==4&&ctx.Request.Method=="DELETE"){await _uploads.Cancel(key,parts[3],ct);result=new{ok=true};}
        else if(parts.Length==6&&parts[4]=="files"&&ctx.Request.Method=="PUT"){
            if(!long.TryParse(ctx.Request.Query["offset"],out var offset))throw new ArgumentException("Offset required");
            result=await _uploads.Put(key,parts[3],parts[5],offset,ctx.Request.Body,ct);
        }
        else if(parts.Length==5&&parts[4]=="commit"&&ctx.Request.Method=="POST"){
            var body=await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body,cancellationToken:ct);
            var replace=body.TryGetProperty("replace",out var values)?values.EnumerateArray().Select(x=>x.GetString()!).ToArray():Array.Empty<string>();
            result=await _uploads.Commit(key,parts[3],replace,ct);
        }
        else{ctx.Response.StatusCode=404;return true;}
        await ctx.Response.WriteAsJsonAsync(result,ct);return true;
    }
}
