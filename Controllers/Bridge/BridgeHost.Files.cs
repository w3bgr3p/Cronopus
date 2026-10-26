using System.Text.Json;
using Microsoft.AspNetCore.Http;
namespace z3nDash;

public sealed partial class BridgeHost
{
    private readonly BridgeFiles _files = new();
    private readonly BridgeFileText _fileText = new();
    private async Task<bool> FileApi(HttpContext ctx, CancellationToken ct)
    {
        var path = ctx.Request.Path.Value!;
        if (path == "/bridge/v1/files/text" && ctx.Request.Method is "GET" or "PUT")
        {
            try
            {
                if (ctx.Request.Method == "GET")
                    await ctx.Response.WriteAsJsonAsync(await _fileText.Open(ctx.Request.Query["path"].ToString(), ct), ct);
                else
                {
                    var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, cancellationToken: ct);
                    var result = await _fileText.Save(body.GetProperty("path").GetString()!, body.GetProperty("revision").GetString()!, body.GetProperty("text").GetString()!, ct);
                    if (result == null) { ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = "File changed on the server. Reopen it before saving; your edits have been kept." }, ct); }
                    else await ctx.Response.WriteAsJsonAsync(result, ct);
                }
            }
            catch (FileNotFoundException) { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { error = "File no longer exists on the server" }, ct); }
            catch (DirectoryNotFoundException) { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { error = "Server folder no longer exists" }, ct); }
            catch (IOException) { ctx.Response.StatusCode = 423; await ctx.Response.WriteAsJsonAsync(new { error = "Cannot access the server file. It may be locked by another process." }, ct); }
            return true;
        }
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
