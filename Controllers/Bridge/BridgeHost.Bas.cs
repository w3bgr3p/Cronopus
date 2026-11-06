using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Cronopus;

public sealed partial class BridgeHost
{
    public BasControlService Bas { get; init; } = new(store);
    public Task<TaskerReply> InvokeBasAsync(string key, string requestId, string method, string path, JsonElement? body, long after = 0, string? projectPath = null)
        => method == "GET" ? Bas.InvokeAsync(method, path, body, after, projectPath)
            : _operations.ExecuteAsync(key, requestId, method + path + body?.GetRawText(), () => Bas.InvokeAsync(method, path, body, after));
    public object? BasOperation(string key, string id) => _operations.Get(key, id);
    private async Task BasApi(HttpContext ctx, string key, CancellationToken ct)
    {
        var path = ctx.Request.Path.Value!["/bridge/v1/bas/".Length..];
        var method = ctx.Request.Method;
        JsonElement? body = method == "GET" ? null : await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, cancellationToken: ct);
        var reply = await InvokeBasAsync(key, ctx.Request.Headers["X-Request-ID"].ToString(), method, path, body,
            long.TryParse(ctx.Request.Query["after"], out var after) ? after : 0, ctx.Request.Query["projectPath"]);
        ctx.Response.StatusCode = reply.StatusCode; ctx.Response.ContentType = reply.ContentType;
        await ctx.Response.WriteAsync(reply.Text, ct);
    }
}
