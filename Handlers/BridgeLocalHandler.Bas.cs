using System.Net;

namespace Cronopus;

public sealed partial class BridgeLocalHandler
{
    private async Task BasRoute(HttpListenerContext ctx, string path)
    {
        const string localKey = "local-bas";
        if (path.StartsWith("operations/", StringComparison.Ordinal) && ctx.Request.HttpMethod == "GET")
        {
            var operation = host.BasOperation(localKey, path[11..]);
            ctx.Response.StatusCode = operation == null ? 404 : 200;
            await Write(ctx, operation); return;
        }
        var reply = await host.InvokeBasAsync(localKey, ctx.Request.Headers["X-Request-ID"] ?? "", ctx.Request.HttpMethod,
            path, ctx.Request.HttpMethod == "GET" ? null : await Body(ctx),
            long.TryParse(ctx.Request.QueryString["after"], out var after) ? after : 0, ctx.Request.QueryString["projectPath"]);
        ctx.Response.StatusCode = reply.StatusCode; ctx.Response.ContentType = reply.ContentType;
        var bytes = System.Text.Encoding.UTF8.GetBytes(reply.Text);
        ctx.Response.ContentLength64 = bytes.Length; await ctx.Response.OutputStream.WriteAsync(bytes);
    }
}
