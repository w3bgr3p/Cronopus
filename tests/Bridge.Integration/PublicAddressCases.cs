using System.Net;
using z3nDash;

static class PublicAddressCases
{
    public static async Task Run(bool live=false)
    {
        foreach(var invalid in new[]{"127.0.0.1","10.1.2.3","172.16.1.2","192.168.1.2","100.64.0.1","169.254.1.2","0.0.0.0","224.0.0.1","::1","<html>error</html>"})
            if(BridgePublicAddress.IsPublicIpv4(invalid))throw new Exception("Non-public IP accepted");
        using var fake=new HttpClient(new Responses());
        if(await BridgePublicAddress.DetectAsync(fake,CancellationToken.None)!="93.184.216.34")throw new Exception("Public IP fallback failed");
        Console.WriteLine("PASS public-ip-validation-fallback");
        if(live){using var client=new HttpClient(new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false});
            var address=await BridgePublicAddress.DetectAsync(client,CancellationToken.None);
            if(!BridgePublicAddress.IsPublicIpv4(address))throw new Exception("Live IP invalid");Console.WriteLine("PASS public-ip-live");}
    }
    sealed class Responses:HttpMessageHandler
    {
        int calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
            =>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(++calls==1?"127.0.0.1":"93.184.216.34\n")});
    }
}
