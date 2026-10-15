using System.Text.Json;
using Microsoft.AspNetCore.Http;
namespace z3nDash;

public sealed partial class BridgeHost
{
    private readonly BridgeOperations _operations = new(store.Root);
    private async Task DispatchApi(HttpContext ctx, string key, CancellationToken ct)
    {
        var path = ctx.Request.Path.Value!;
        if (path.StartsWith("/bridge/v1/operations/") && ctx.Request.Method == "GET")
        { var op = _operations.Get(key, path[22..]); ctx.Response.StatusCode = op == null ? 404 : 200; await ctx.Response.WriteAsJsonAsync(op, ct); return; }
        if (path == "/bridge/v1/tasks" && ctx.Request.Method == "GET")
        { await ctx.Response.WriteAsJsonAsync(tasker.Page(int.TryParse(ctx.Request.Query["limit"], out var size) ? size : 100, ctx.Request.Query["cursor"].FirstOrDefault(), ctx.Request.Query["search"].ToString()), ct); return; }
        var method = ctx.Request.Method;
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
            if (target.Length == 0) { ctx.Response.StatusCode = 404; return; }
            if (method is "PUT" or "DELETE") method = "POST";
            body = JsonSerializer.SerializeToElement(values);
        }
        var requestId = ctx.Request.Headers["X-Request-ID"].ToString();
        var reply = method == "GET" ? await tasker.InvokeAsync(method, target, body, ct)
            : await _operations.ExecuteAsync(key, requestId, method + target + body?.GetRawText(), () => tasker.InvokeAsync(method, target, body, ct));
        ctx.Response.StatusCode = reply.StatusCode; ctx.Response.ContentType = reply.ContentType;
        await ctx.Response.WriteAsync(reply.Text, ct);
    }
}
