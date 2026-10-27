using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Cronopus;

static class LoadCases
{
    static async Task LoadTransfer(HttpClient client,string root)
    {
        var block=new byte[4*1024*1024];Random.Shared.NextBytes(block);
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(block));
        using var created=await client.PostAsync("/bridge/v1/uploads",new StringContent(JsonSerializer.Serialize(new{destination=root,files=new[]{new{id="load-file",relativePath="load.bin",size=block.Length,sha256=hash}}}),Encoding.UTF8,"application/json"));
        created.EnsureSuccessStatusCode();var json=JsonDocument.Parse(await created.Content.ReadAsStringAsync());var id=json.RootElement.GetProperty("upload_id").GetString();
        using var sent=await client.PutAsync($"/bridge/v1/uploads/{id}/files/load-file?offset=0",new ByteArrayContent(block));sent.EnsureSuccessStatusCode();
        using var commit=await client.PostAsync($"/bridge/v1/uploads/{id}/commit",new StringContent("{\"replace\":[]}",Encoding.UTF8,"application/json"));commit.EnsureSuccessStatusCode();
        if(!File.ReadAllBytes(Path.Combine(root,"load.bin")).SequenceEqual(block))throw new Exception("Load transfer corrupted");
    }
    public static async Task Run(int minutes)
    {
        var root=Path.Combine(Path.GetTempPath(),"cronopus-bridge-load-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var db=new DbConnectionService();db.Connect(new DbConfig{Type="sqlite",SqlitePath=Path.Combine(root,"tasks.db")});SchedulerService.PrepareTables(db.GetDb());
        using var fakeCancellation=new CancellationTokenSource();using var scheduler=new SchedulerService(db);
        using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db.GetDb().Source}.ToString())){
            connection.Open();using var tx=connection.BeginTransaction();using var cmd=connection.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="INSERT INTO schedules(id,name,enabled,executor,schedule_mode,status) VALUES($id,$id,'false','python','off','running')";
            var parameter=cmd.Parameters.Add("$id",SqliteType.Text);for(var i=0;i<1000;i++){parameter.Value="load-"+i;cmd.ExecuteNonQuery();}tx.Commit();
        }
        var running=typeof(SchedulerService).GetField("_running",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scheduler)!;
        var runningType=running.GetType().GenericTypeArguments[1];var add=running.GetType().GetMethod("TryAdd")!;
        for(var i=0;i<1000;i++)add.Invoke(running,new[]{(object)("load-"+i+":run"),Activator.CreateInstance(runningType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object?[]{null,DateTime.UtcNow,fakeCancellation,null},null)!});
        var tasker=new TaskerService(db,scheduler,root);var store=new BridgeStore(Path.Combine(root,"bridge"));store.State.Settings.BindAddress="127.0.0.1";store.State.Settings.Port=39237;
        var key=store.CreateKey("load");var host=new BridgeHost(store,tasker,scheduler);var uri=await host.StartAsync(CancellationToken.None);
        using var cert=store.Certificate();using var client=new HttpClient(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,c,_,_)=>c!=null&&c.RawData.SequenceEqual(cert.RawData)}){BaseAddress=uri};
        client.DefaultRequestHeaders.Authorization=new("Bearer",key.Secret);
        var output=(BridgeOutput)typeof(BridgeHost).GetField("_output",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;
        using var stop=new CancellationTokenSource();var payload=Encoding.UTF8.GetBytes(new string('x',250));long lines=0;
        var producer=Task.Run(async()=>{while(!stop.IsCancellationRequested){for(var i=0;i<1000;i++){output.Append("load-"+i,"run",payload);Interlocked.Increment(ref lines);}await Task.Delay(100);}});
        var transfer=LoadTransfer(client,root);
        var samples=new List<object>();var latencies=new List<double>();var errors=0;var began=DateTimeOffset.UtcNow;var deadline=began.AddMinutes(minutes);var iteration=0;
        while(DateTimeOffset.UtcNow<deadline){
            var watch=Stopwatch.StartNew();using var response=await client.GetAsync("/bridge/v1/tasks?limit=100");await response.Content.ReadAsByteArrayAsync();latencies.Add(watch.Elapsed.TotalMilliseconds);if(!response.IsSuccessStatusCode)errors++;
            using(var command=new HttpRequestMessage(HttpMethod.Post,"/bridge/v1/tasks/load-0/actions/pause")){command.Headers.Add("X-Request-ID",Guid.NewGuid().ToString());command.Content=new StringContent("{}",Encoding.UTF8,"application/json");using var result=await client.SendAsync(command);if(!result.IsSuccessStatusCode)errors++;}
            if(iteration++%10==0){samples.Add(new{at=DateTimeOffset.UtcNow,ram=Process.GetCurrentProcess().WorkingSet64,buffer=output.BufferedBytes,lines=Interlocked.Read(ref lines)});Console.WriteLine($"LOAD {DateTimeOffset.UtcNow-began:mm\\:ss} buffer={output.BufferedBytes} lines={lines}");}
            if(output.BufferedBytes>64L*1024*1024)throw new Exception("Output buffer exceeded cap");await Task.Delay(1000);
        }
        stop.Cancel();await producer;await transfer;
        await host.StopAsync(CancellationToken.None);
        latencies.Sort();var p95=latencies[(int)((latencies.Count-1)*.95)];
        var report=new{minutes,tasks=1000,lines,errors,p95Ms=p95,samples};
        var reportPath=Path.Combine(Path.GetTempPath(),"cronopus-bridge-load-result.json");
        await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("LOAD REPORT "+reportPath+" p95="+p95+" errors="+errors);
        if(errors!=0||p95>1000)throw new Exception("Load acceptance failed");
    }
}
