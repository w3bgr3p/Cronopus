using System.Text.Json;
using Microsoft.AspNetCore.Http;
namespace z3nDash;

public sealed partial class BridgeHost
{
    private readonly BridgeOperations _operations = new(store.Root);
    private readonly BridgeChanges _changes = new(tasker);
    private async Task DispatchApi(HttpContext ctx, string key, CancellationToken ct)
    {
        var path = ctx.Request.Path.Value!;
        if (await OutputApi(ctx,key,ct)) return;
        if (await UploadApi(ctx,key,ct)) return;
        if (await FileApi(ctx, ct)) return;
        if(path=="/bridge/v1/changes"&&ctx.Request.Method=="GET"){await ctx.Response.WriteAsJsonAsync(await _changes.Read(ctx.Request.Query["cursor"].FirstOrDefault(),500,ct),ct);return;}
        if (path.StartsWith("/bridge/v1/operations/") && ctx.Request.Method == "GET")
        { var op = _operations.Get(key, path[22..]); ctx.Response.StatusCode = op == null ? 404 : 200; await ctx.Response.WriteAsJsonAsync(op, ct); return; }
        if (path == "/bridge/v1/tasks" && ctx.Request.Method == "GET")
        { var page=JsonSerializer.SerializeToElement(tasker.Page(int.TryParse(ctx.Request.Query["limit"], out var size) ? size : 100, ctx.Request.Query["cursor"].FirstOrDefault(), ctx.Request.Query["search"].ToString()));
            await ctx.Response.WriteAsJsonAsync(new{items=page.GetProperty("items"),next_cursor=page.GetProperty("next_cursor"),change_cursor=_changes.Cursor,database_epoch=tasker.DatabaseEpoch},ct);return; }
        var method = ctx.Request.Method;
        if(path=="/bridge/v1/tasker/list"&&method=="GET"){var page=JsonSerializer.SerializeToElement(tasker.Page(500));await ctx.Response.WriteAsJsonAsync(page.GetProperty("items"),ct);return;}
        if(method=="GET"){
            var runtime=path.Split('/',StringSplitOptions.RemoveEmptyEntries);
            if(runtime.Length==5&&runtime[2]=="tasks"&&runtime[4] is "instances" or "queue"){
                await ctx.Response.WriteAsJsonAsync(tasker.RuntimePage(runtime[3],runtime[4],int.TryParse(ctx.Request.Query["cursor"],out var offset)?offset:0,int.TryParse(ctx.Request.Query["limit"],out var count)?count:100),ct);return;
            }
            if(path is "/bridge/v1/tasker/instances" or "/bridge/v1/tasker/queue"){
                var page=JsonSerializer.SerializeToElement(tasker.RuntimePage(ctx.Request.Query["id"].ToString(),path.EndsWith("queue")?"queue":"instances",0,500));
                await ctx.Response.WriteAsJsonAsync(page.GetProperty("items"),ct);return;
            }
        }
        JsonElement? body = method == "GET" || ctx.Request.ContentLength == 0 ? null : await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, cancellationToken: ct);
        string target;
        if (path.StartsWith("/bridge/v1/tasker/", StringComparison.Ordinal)) target = "/tasker/" + path[18..] + ctx.Request.QueryString;
        else if (path == "/bridge/v1/tasks" && method == "POST") target = "/tasker/save";
        else
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || parts[0] != "bridge" || parts[1] != "v1" || parts[2] != "tasks") { ctx.Response.StatusCode = 404; return; }
            var id = parts[3]; TaskerService.ValidateId(id);
            var values = body is { ValueKind: JsonValueKind.Object } b ? JsonSerializer.Deserialize<Dictionary<string, object>>(b.GetRawText())! : new();
            values["id"] = id;
            if (ctx.Request.Headers.IfMatch.Count > 0) values["_revision"] = ctx.Request.Headers.IfMatch.ToString().Trim('"');
            target = method switch { "GET" => "/tasker/detail?id=" + id, "PUT" => "/tasker/save", "DELETE" => "/tasker/delete", _ => "" };
            if (parts.Length == 5 && method == "GET" && parts[4] is "instances" or "queue" or "payload") target = "/tasker/" + parts[4] + "?id=" + id;
            if (parts.Length == 6 && parts[4] == "actions" && method == "POST") target = "/tasker/" + parts[5] + (parts[5] == "defer" ? "?id=" + id : "");
            if(parts.Length==6&&parts[4]=="actions"&&parts[5]=="install"&&method=="POST"){target="/tasker/install/stream?id="+id;method="GET";}
            if(parts.Length==6&&parts[4]=="actions"&&parts[5] is "enable" or "disable" or "reset-successes"&&method=="POST"){
                target="/tasker/save";values[parts[5]=="reset-successes"?"runs_success":"enabled"]=parts[5]=="enable"?"true":parts[5]=="disable"?"false":"0";
            }
            if(parts.Length==5&&parts[4]=="clone"&&method=="POST"){
                var source=await tasker.InvokeAsync("GET","/tasker/detail?id="+id,null,ct);
                if(source.StatusCode!=200){ctx.Response.StatusCode=source.StatusCode;await ctx.Response.WriteAsync(source.Text,ct);return;}
                values=JsonSerializer.Deserialize<Dictionary<string,object>>(source.Text)!;values.Remove("id");values.Remove("_revision");values.Remove("edit_version");
                values["name"]=values["name"].ToString()+" (copy)";values["enabled"]="false";values["runs_total"]="0";values["runs_success"]="0";target="/tasker/save";
            }
            if (target.Length == 0) { ctx.Response.StatusCode = 404; return; }
            if (method is "PUT" or "DELETE") method = "POST";
            body = JsonSerializer.SerializeToElement(values);
        }
        var requestId = ctx.Request.Headers["X-Request-ID"].ToString();
        if(target.Split('?')[0] is "/tasker/save" or "/tasker/delete" or "/tasker/payload" && method!="GET" && body is {} edit && edit.TryGetProperty("id",out var editedId) && !string.IsNullOrEmpty(editedId.GetString()) && (!edit.TryGetProperty("_revision",out var revision)||string.IsNullOrEmpty(revision.GetString())))
        {ctx.Response.StatusCode=428;await ctx.Response.WriteAsJsonAsync(new{error="Task revision required. Reload the task before changing it."},ct);return;}
        var longOperation=target.Split('?')[0] is "/tasker/build" or "/tasker/ensure-venv" or "/tasker/install/stream";
        var reply = longOperation?_operations.Start(key,requestId,method+target+body?.GetRawText(),()=>tasker.InvokeAsync(method,target,body,store.RevocationToken(key),text=>_operations.SetProgress(key,requestId,text))):method == "GET" ? await tasker.InvokeAsync(method, target, body, ct)
            : await _operations.ExecuteAsync(key, requestId, method + target + body?.GetRawText(), () => tasker.InvokeAsync(method, target, body, ct));
        ctx.Response.StatusCode = reply.StatusCode; ctx.Response.ContentType = reply.ContentType;
        await ctx.Response.WriteAsync(reply.Text, ct);
    }
}
