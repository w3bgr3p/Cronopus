using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using z3nDash;

static partial class BridgeCases
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static async Task<JsonElement> Json(HttpClient client,Uri uri,string method,string path,object? body=null,string? requestId=null)
    {
        using var request=new HttpRequestMessage(new HttpMethod(method),new Uri(uri,path));
        if(body!=null)request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        if(method!="GET")request.Headers.Add("X-Request-ID",requestId??Guid.NewGuid().ToString("N"));
        using var response=await client.SendAsync(request);var text=await response.Content.ReadAsStringAsync();
        Check(response.IsSuccessStatusCode,path+" failed "+response.StatusCode+": "+text);return JsonDocument.Parse(text).RootElement.Clone();
    }
    public static async Task Run(string root,TaskerService service,BridgeStore store,Uri uri,string secret,HttpClient http)
    {
        var connection=new BridgeConnection{Name="Fixture",Address=uri.ToString(),NodeId=store.NodeId,Fingerprint=BridgeStore.Fingerprint(store.Certificate()),Secret=secret};
        store.State.Connections.Add(connection);store.Save();
        var client=new BridgeClient(store);
        Check((await client.CheckAsync(connection.Id,CancellationToken.None)).GetProperty("api_version").GetInt32()==1,"pinned client failed");
        connection.Fingerprint=new string('0',64);
        try{await client.CheckAsync(connection.Id,CancellationToken.None);throw new Exception("wrong pin accepted");}catch(HttpRequestException){}
        connection.Fingerprint=BridgeStore.Fingerprint(store.Certificate());
        Console.WriteLine("PASS pinned-client");
        var count=0;var ops=new BridgeOperations(store.Root);
        await ops.ExecuteAsync("test","once","same",()=>Task.FromResult(new TaskerReply(200,(++count).ToString(),"application/json")));
        await ops.ExecuteAsync("test","once","same",()=>Task.FromResult(new TaskerReply(200,(++count).ToString(),"application/json")));
        Check(count==1,"duplicate operation executed");
        Check((await ops.ExecuteAsync("test","once","different",()=>throw new Exception("must not execute"))).StatusCode==409,"request ID input mismatch accepted");
        Console.WriteLine("PASS operation-idempotency");
        Directory.CreateDirectory(Path.Combine(root,"journal-expiry"));await ApiCases(root,http,uri);
        await UploadFailures(root,http,uri);
        await NativeTransfer(root,store,connection);
        await TaskUpload(root,store,connection,http,uri);
        var output=new BridgeOutput();var encoded=Encoding.UTF8.GetBytes("😀");output.Append("utf","run",encoded.AsMemory(0,2));output.Append("utf","run",encoded.AsMemory(2));
        var page=output.Read("utf","run",null,200);Check(page.GetProperty("items")[0].GetProperty("line").GetString()=="😀","split UTF-8 damaged");
        Console.WriteLine("PASS split-utf8");
        var streamKey=store.CreateKey("stream-revoke");
        using var streamClient=new HttpClient(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert!=null&&BridgeStore.Fingerprint(cert)==connection.Fingerprint});
        streamClient.DefaultRequestHeaders.Authorization=new("Bearer",streamKey.Secret);
        using var streamResponse=await streamClient.GetAsync(new Uri(uri,"/bridge/v1/tasks/roundtrip/output/stream"),HttpCompletionOption.ResponseHeadersRead);
        Check(streamResponse.IsSuccessStatusCode,"stream failed");var stream=await streamResponse.Content.ReadAsStreamAsync();var buffer=new byte[4096];Check(await stream.ReadAsync(buffer)>0,"stream did not produce first frame");
        store.RevokeKey(streamKey.Id);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try{while(await stream.ReadAsync(buffer,timeout.Token)>0){}}catch(HttpRequestException){}catch(IOException){}
        using var revoked=await streamClient.GetAsync(new Uri(uri,"/bridge/v1/info"));Check((int)revoked.StatusCode==401,"revoked stream key accepted");Console.WriteLine("PASS active-stream-revocation");
    }
}
