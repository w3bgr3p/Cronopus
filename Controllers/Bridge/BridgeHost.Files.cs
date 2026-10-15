using System.Text.Json;
using Microsoft.AspNetCore.Http;
namespace z3nDash;

public sealed partial class BridgeHost
{
    private readonly BridgeFiles _files = new();
    private async Task<bool> FileApi(HttpContext ctx, CancellationToken ct)
    {
        var path = ctx.Request.Path.Value!;
        if (path == "/bridge/v1/files/roots" && ctx.Request.Method == "GET")
        { await ctx.Response.WriteAsJsonAsync(_files.Roots(), ct); return true; }
        if (path == "/bridge/v1/files/list" && ctx.Request.Method == "GET")
        { await ctx.Response.WriteAsJsonAsync(await _files.ListAsync(ctx.Request.Query["path"].ToString(), ctx.Request.Query["cursor"].FirstOrDefault(), int.TryParse(ctx.Request.Query["limit"], out var size) ? size : 100, ct), ct); return true; }
        if (path == "/bridge/v1/files/directories" && ctx.Request.Method == "POST")
        { var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, cancellationToken: ct); var parent = body.GetProperty("parent").GetString()!; var name = body.GetProperty("name").GetString()!;
            var target = BridgeUploadPaths.Resolve(parent, name); Directory.CreateDirectory(target); await ctx.Response.WriteAsJsonAsync(new { path = target }, ct); return true; }
        return false;
    }
}
