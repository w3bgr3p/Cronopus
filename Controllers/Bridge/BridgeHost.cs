using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace z3nDash;

public sealed partial class BridgeHost(BridgeStore store, TaskerService tasker, SchedulerService scheduler)
{
    private WebApplication? _app;
    private System.Security.Cryptography.X509Certificates.X509Certificate2? _certificate;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _limits = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _transferLimits = new();
    private System.Threading.Timer? _cleanup;
    public Task CleanupUploads(string? revokedKey=null)=>_uploads.Cleanup(revokedKey);
    private readonly BridgeAudit _audit=new(store.Root);
    public Uri? Address { get; private set; }
    public string Error { get; private set; } = "";
    public async Task<Uri> StartAsync(CancellationToken ct)
    {
        if (_app != null) return Address!;
        var port = store.State.Settings.Port;
        try { return await Bind(port, ct); }
        catch (IOException ex) when (port != 0) { Error = ex.Message; return await Bind(0, ct); }
    }
    private async Task<Uri> Bind(int port, CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        if (Environment.GetEnvironmentVariable("BRIDGE_TRACE_TLS") == "1") builder.Logging.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        var certificate = store.Certificate();
        builder.WebHost.ConfigureKestrel(options => {
            options.Limits.MaxRequestBodySize = 8 * 1024 * 1024;
            options.Listen(IPAddress.Parse(store.State.Settings.BindAddress), port, listen => listen.UseHttps(certificate));
        });
        var app = builder.Build(); app.Run(Handle);
        try { await app.StartAsync(ct); }
        catch { await app.DisposeAsync(); certificate.Dispose(); throw; }
        _app = app;
        SseHub.OutputObserved+=ObserveOutput;
        _cleanup=new System.Threading.Timer(state=>{_=_uploads.Cleanup();},null,TimeSpan.Zero,TimeSpan.FromHours(1));
        _certificate = certificate;
        var actual = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        Address = new UriBuilder(actual) { Host = store.State.Settings.AdvertisedAddress }.Uri;
        Console.WriteLine("Remote Tasker: " + Address);
        return Address;
    }
    public async Task StopAsync(CancellationToken ct)
    { if (_app != null) { _cleanup?.Dispose();SseHub.OutputObserved-=ObserveOutput; await _app.StopAsync(ct); await _app.DisposeAsync(); _app = null; Address = null; _certificate?.Dispose(); _certificate = null; } }
    private async Task Handle(HttpContext ctx)
    {
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        ctx.Response.Headers.CacheControl = "no-store";
        var authorization = ctx.Request.Headers.Authorization.ToString();
        var key = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? store.Authorize(authorization[7..]) : null;
        if (key == null) { ctx.Response.StatusCode = 401; await ctx.Response.WriteAsJsonAsync(new { code = "unauthorized", error = "Invalid or revoked key" }); return; }
        var transfer=ctx.Request.Path.StartsWithSegments("/bridge/v1/uploads")||ctx.Request.Path.Value!.EndsWith("/output/stream");
        var gate = transfer?_transferLimits.GetOrAdd(key,_=>new(2)):_limits.GetOrAdd(key, _ => new(8));
        if (!await gate.WaitAsync(0)) { ctx.Response.StatusCode = 429; ctx.Response.Headers.RetryAfter = "1"; return; }
        using var revoked = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, store.RevocationToken(key));
        try { await Dispatch(ctx, key, revoked.Token); }
        catch (OperationCanceledException) { if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 503; }
        catch (Exception ex)
        {
            if (ctx.Response.HasStarted) { ctx.Abort(); return; }
            ctx.Response.StatusCode = ex is ArgumentException or JsonException ? 400 : ex is KeyNotFoundException ? 404 : ex is UnauthorizedAccessException ? 403 : 500;
            await ctx.Response.WriteAsJsonAsync(new { code = "bridge_error", error = ex is ArgumentException or KeyNotFoundException or UnauthorizedAccessException ? ex.Message : "Server operation failed" });
        }
        finally { gate.Release();_audit.Write(key,ctx.Request.Method,ctx.Request.Path,ctx.Response.StatusCode,System.Diagnostics.Stopwatch.GetElapsedTime(started)); }
    }
    private async Task Dispatch(HttpContext ctx, string key, CancellationToken ct)
    {
        var path = ctx.Request.Path.Value!;
        if (path == "/bridge/v1/info" && ctx.Request.Method == "GET")
        {
            await ctx.Response.WriteAsJsonAsync(new { node_id = store.NodeId, api_version = 1,
                version = typeof(BridgeHost).Assembly.GetName().Version!.ToString(), database_epoch = tasker.DatabaseEpoch,
                time = DateTimeOffset.UtcNow, timezone = TimeZoneInfo.Utc.Id,
                capabilities = new[] { "tasks", "payload", "schedules", "files", "output", "changes", "long-operations" }.Concat(OperatingSystem.IsWindows()?new[]{"uploads"}:Array.Empty<string>()),
                limits = new { page = 500, json_bytes = 8388608, log_bytes = 262144, upload_bytes_per_second = store.State.Settings.TransferBytesPerSecond } }, ct); return;
        }
        await DispatchApi(ctx, key, ct);
    }
}
