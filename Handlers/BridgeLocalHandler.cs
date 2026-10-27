using System.Net;
using System.Text;
using System.Text.Json;
namespace Cronopus;

public sealed partial class BridgeLocalHandler(BridgeStore store, BridgeHost host) : IScriptHandler
{
    private readonly BridgeClient _client = new(store);
    private readonly SemaphoreSlim _admin=new(1,1);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public string PathPrefix => "/bridge-local";
    public void Init() { }
    public async Task<bool> HandleRequest(HttpListenerContext ctx)
    {
        ctx.Response.Headers.Remove("Access-Control-Allow-Origin");
        ctx.Response.Headers.Remove("Access-Control-Allow-Headers");
        var origin = ctx.Request.Headers["Origin"];
        if (ctx.Request.Headers["X-Bridge-Local"] != "1" || (origin != null && origin != ctx.Request.Url!.GetLeftPart(UriPartial.Authority)))
        { ctx.Response.StatusCode = 403; return true; }
        ctx.Response.Headers["Cache-Control"] = "no-store";
        var administrative=ctx.Request.Url!.AbsolutePath.StartsWith("/bridge-local/server")||ctx.Request.Url.AbsolutePath.StartsWith("/bridge-local/connections")||ctx.Request.Url.AbsolutePath.StartsWith("/bridge-local/keys");
        if(administrative)await _admin.WaitAsync();
        try { await Dispatch(ctx); }
        catch (Exception ex)
        {
            ctx.Response.StatusCode = ex is ArgumentException or JsonException ? 400 : ex is KeyNotFoundException ? 404 : ex is OperationCanceledException ? 504 : ex is HttpRequestException ? 502 : 500;
            var reason=ex is OperationCanceledException?"Server did not respond in time. Check its HTTPS address, port and firewall.":ex is HttpRequestException request&&request.StatusCode==HttpStatusCode.Unauthorized?"Server rejected the access key. Check that it is correct and has not been revoked.":ex is HttpRequestException?"Cannot connect: check server address, HTTPS port, certificate and key":ex.Message;
            await Write(ctx, new { error = reason });
        }
        finally{if(administrative)_admin.Release();}
        return true;
    }
    private static async Task Write(HttpListenerContext ctx, object? data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json); ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length; await ctx.Response.OutputStream.WriteAsync(bytes);
    }
    private static async Task<JsonElement> Body(HttpListenerContext ctx)
    {
        using var buffer = new MemoryStream(); var bytes = new byte[65536]; int count;
        while ((count = await ctx.Request.InputStream.ReadAsync(bytes)) > 0)
        { if (buffer.Length + count > 8388608) throw new ArgumentException("Body is too large"); await buffer.WriteAsync(bytes.AsMemory(0, count)); }
        return JsonSerializer.Deserialize<JsonElement>(buffer.Length == 0 ? "{}" : Encoding.UTF8.GetString(buffer.ToArray()));
    }
}
