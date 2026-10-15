using System.Text.Json;
using Microsoft.AspNetCore.Http;
namespace z3nDash;

public sealed partial class BridgeHost
{
    private async Task DispatchApi(HttpContext ctx, string key, CancellationToken ct)
    {
        var path = ctx.Request.Path.Value!;
        if (!path.StartsWith("/bridge/v1/tasker/", StringComparison.Ordinal)) { ctx.Response.StatusCode = 404; return; }
        var target = "/tasker/" + path[18..] + ctx.Request.QueryString;
        JsonElement? body = ctx.Request.Method == "GET" ? null : (await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, cancellationToken: ct));
        var reply = await tasker.InvokeAsync(ctx.Request.Method, target, body, ct);
        ctx.Response.StatusCode = reply.StatusCode; ctx.Response.ContentType = reply.ContentType;
        await ctx.Response.WriteAsync(reply.Text, ct);
    }
}
