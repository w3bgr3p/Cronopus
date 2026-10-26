using System.Net;
using System.Text;
using System.Text.Json;
namespace z3nDash;

public sealed partial class BridgeLocalHandler
{
    private static readonly HttpClient PublicIpClient=new(new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(3)}){Timeout=Timeout.InfiniteTimeSpan};
    private async Task Server(HttpListenerContext ctx)
    {
        if (ctx.Request.HttpMethod == "PUT")
        {
            var body=await Body(ctx);var input = body.Deserialize<BridgeSettings>(Json)!;
            if (!IPAddress.TryParse(input.BindAddress, out _) || !IPAddress.TryParse(input.AdvertisedAddress, out _) || input.Port is < 1 or > 65535 || input.TransferBytesPerSecond < 65536 || input.UploadQuotaBytes < 1048576)
                throw new ArgumentException("Check IP addresses, port and transfer limits");
            var regenerate = input.AdvertisedAddress != store.State.Settings.AdvertisedAddress || body.TryGetProperty("renewCertificate",out var renew)&&renew.ValueKind==JsonValueKind.True;
            await host.StopAsync(CancellationToken.None); store.State.Settings = input;
            using var renewed = store.Certificate(regenerate); store.Save();
            if (input.Enabled) await host.StartAsync(CancellationToken.None);
            if(input.Enabled&&body.TryGetProperty("openFirewall",out var openFirewall)&&openFirewall.ValueKind==JsonValueKind.True){
                using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));await _firewall.Change("open",FirewallPort,timeout.Token);
            }
        }
        using var cert = store.Certificate();
        await Write(ctx, new { settings = store.State.Settings, nodeId = store.NodeId, fingerprint = BridgeStore.Fingerprint(cert),
            expires = cert.NotAfter.ToUniversalTime(), actualUrl = host.Address?.ToString(), error = host.Error,
            keys = store.State.Keys.Select(k => new { k.Id, k.Name, k.Revoked }), storage = store.Root,firewall=_firewall.Status(FirewallPort),
            storageProtection = OperatingSystem.IsWindows() ? "Windows user encryption (DPAPI)" : "Owner-only file permissions" });
    }
    private async Task Proxy(HttpListenerContext ctx, string path)
    {
        var split = path.IndexOf("/api/", StringComparison.Ordinal); var id = path[..split]; var api = path[(split + 5)..];
        var method = ctx.Request.HttpMethod;
        var allowed = api.StartsWith("tasker/") && TaskerService.RemoteRoutes.Contains(method + " /" + api)
            || api == "info" && method == "GET" || api is "tasks" or "changes" && method == "GET"
            || api is "files/roots" or "files/list" && method == "GET"
            || api == "files/text" && method is "GET" or "PUT"
            || api == "files/directories" && method == "POST"
            || System.Text.RegularExpressions.Regex.IsMatch(api,"^tasks/[A-Za-z0-9_-]{1,128}/output(/stream)?$") && method == "GET"
            || System.Text.RegularExpressions.Regex.IsMatch(api,"^tasks/[A-Za-z0-9_-]{1,128}/(instances|queue)$") && method == "GET"
            || System.Text.RegularExpressions.Regex.IsMatch(api,"^tasks/[A-Za-z0-9_-]{1,128}/actions/(install|build|ensure-venv)$") && method == "POST"
            || api.StartsWith("operations/") && method == "GET";
        if (!allowed) { ctx.Response.StatusCode = 404; return; }
        await _client.CheckAsync(id, CancellationToken.None);
        HttpContent? body = method == "GET" ? null : new StringContent((await Body(ctx)).GetRawText(), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(id, new HttpMethod(method), "/bridge/v1/" + api + ctx.Request.Url!.Query, body, CancellationToken.None,ctx.Request.Headers["X-Request-ID"]);
        ctx.Response.StatusCode = (int)response.StatusCode;
        ctx.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        ctx.Response.SendChunked = true;
        using var timeout=new CancellationTokenSource();if(!api.EndsWith("/stream"))timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await response.Content.CopyToAsync(ctx.Response.OutputStream,timeout.Token);
    }
}
