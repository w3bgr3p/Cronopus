using System.Net;
using System.Text.Json;
namespace z3nDash;

public sealed partial class BridgeLocalHandler
{
    private readonly BridgeFirewall _firewall=new(store.NodeId);
    private int FirewallPort=>host.Address?.Port??store.State.Settings.Port;
    private async Task Firewall(HttpListenerContext ctx)
    {
        if(ctx.Request.HttpMethod=="GET"){await Write(ctx,_firewall.Status(FirewallPort));return;}
        if(ctx.Request.HttpMethod!="POST"){ctx.Response.StatusCode=405;return;}
        var body=await Body(ctx);var action=body.GetProperty("action").GetString()??"";
        if(action=="open"&&host.Address==null)throw new ArgumentException("Enable and apply the HTTPS server settings before opening its port.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await Write(ctx,await _firewall.Change(action,FirewallPort,timeout.Token));
    }
}
