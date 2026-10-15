using System.Net;
using System.Text.Json;
namespace z3nDash;

public sealed partial class BridgeLocalHandler
{
    private async Task Dispatch(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url!.AbsolutePath["/bridge-local".Length..].Trim('/');
        var method = ctx.Request.HttpMethod;
        if(path=="server/firewall"){await Firewall(ctx);return;}
        if(path=="server/public-ip"&&method=="GET"){
            try{await Write(ctx,new{address=await BridgePublicAddress.DetectAsync(PublicIpClient,CancellationToken.None)});}
            catch(HttpRequestException ex){ctx.Response.StatusCode=502;await Write(ctx,new{error=ex.Message});}return;
        }
        if(await TransferRoute(ctx,path))return;
        if (path == "connections" && method == "GET") { await Write(ctx, store.State.Connections.Select(c => c.Public())); return; }
        if (path == "connections" && method == "POST" || path.StartsWith("connections/") && method == "PUT")
        {
            var json = await Body(ctx); var input = json.Deserialize<BridgeConnection>(Json)!;
            var existing = method == "PUT" ? store.Connection(path[12..]) : null;
            if (!Uri.TryCreate(input.Address, UriKind.Absolute, out var address) || address.Scheme != "https" || address.AbsolutePath != "/" || address.Query.Length > 0 || address.Fragment.Length > 0 || address.UserInfo.Length > 0)
                throw new ArgumentException("Address must be https://IP:port");
            if (input.Fingerprint.Replace(":", "").Replace(" ", "").Length != 64) throw new ArgumentException("SHA-256 fingerprint required");
            Convert.FromHexString(input.Fingerprint.Replace(":", "").Replace(" ", ""));
            if (string.IsNullOrWhiteSpace(input.NodeId)) throw new ArgumentException("Server node ID required");
            input.Id = existing?.Id ?? Guid.NewGuid().ToString("N");
            input.Secret = string.IsNullOrEmpty(input.Secret) ? existing?.Secret ?? "" : input.Secret;
            if (input.Secret.Length == 0) throw new ArgumentException("Access key required");
            if (existing != null) store.State.Connections.Remove(existing);
            store.State.Connections.Add(input); store.Save(); await Write(ctx, input.Public()); return;
        }
        if (path.StartsWith("connections/") && path.EndsWith("/test") && method == "POST")
        { await Write(ctx, await _client.CheckAsync(path.Split('/')[1], CancellationToken.None)); return; }
        if (path.StartsWith("connections/") && method == "DELETE")
        { store.State.Connections.Remove(store.Connection(path[12..])); store.Save(); await Write(ctx, new { ok = true }); return; }
        if (path == "keys" && method == "POST")
        { var body = await Body(ctx); await Write(ctx, store.CreateKey(body.GetProperty("name").GetString() ?? "desktop")); return; }
        if (path.StartsWith("keys/") && method == "DELETE")
        { store.RevokeKey(path[5..]); await host.CleanupUploads(path[5..]); await Write(ctx, new { ok = true }); return; }
        if (path == "server") { await Server(ctx); return; }
        if (path.Contains("/api/", StringComparison.Ordinal)) { await Proxy(ctx, path); return; }
        ctx.Response.StatusCode = 404;
    }
}
