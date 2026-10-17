using System.Text.Json;
using z3nDash;

static partial class BridgeCases
{
    static async Task TaskUpload(string root, BridgeStore store, BridgeConnection connection, HttpClient http, Uri uri)
    {
        var transfers = new BridgeTransfers(store, new BridgeClient(store));
        var start = typeof(BridgeTransfers).GetMethod("StartTask");
        Check(start != null, "Task upload must transfer the script and create its task");
        var source = Path.Combine(root, "copy-source.py");
        File.WriteAllText(source, "print('uploaded')");
        var destination = Path.Combine(root, "task-copy"); Directory.CreateDirectory(destination);
        var settings = new Dictionary<string,string> {
            ["id"]="original", ["name"]="Uploaded task", ["executor"]="python", ["script_path"]=source,
            ["args"]="--example", ["enabled"]="true", ["schedule_mode"]="cron", ["cron"]="0 0 1 1 *",
            ["on_overlap"]="queue", ["max_threads"]="3", ["timeout_seconds"]="77", ["use_venv"]="true",
            ["payload_schema"]="[{\"name\":\"text\",\"type\":\"string\"}]", ["payload_values"]="{\"text\":\"тест ' payload\"}",
            ["browser_json"]="{\"mode\":\"test\"}", ["runs_total"]="99", ["runs_success"]="88", ["sched_runs"]="44"
        };
        var response = JsonSerializer.SerializeToElement(start!.Invoke(transfers, new object[]{JsonSerializer.SerializeToElement(settings),connection.Id,destination}));
        var job = transfers.Get(response.GetProperty("Id").GetString()!);
        async Task Finished()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while(job.State is not "completed" and not "failed" and not "awaiting_conflicts") await Task.Delay(30,timeout.Token);
        }
        await Finished();
        Check(job.State=="completed", "Task copy failed: "+job.Error);
        Check(File.ReadAllText(Path.Combine(destination,Path.GetFileName(source)))==File.ReadAllText(source),"Task script bytes differ");
        var list = await Json(http,uri,"GET","/bridge/v1/tasks?limit=500");
        var copy = list.GetProperty("items").EnumerateArray().Single(t=>t.GetProperty("name").GetString()==settings["name"]);
        var id = copy.GetProperty("id").GetString();
        Check(id!="original","Source ID reused");
        var detail = await Json(http,uri,"GET","/bridge/v1/tasks/"+id);
        foreach(var key in new[]{"name","executor","args","enabled","schedule_mode","cron","on_overlap","max_threads","timeout_seconds","use_venv","payload_schema","payload_values","browser_json"})
            Check(detail.GetProperty(key).GetString()==settings[key],"Task copy lost "+key);
        Check(detail.GetProperty("script_path").GetString()==Path.Combine(destination,Path.GetFileName(source)),"Remote script path incorrect");
        Check(detail.GetProperty("runs_total").GetString()=="0" && detail.GetProperty("runs_success").GetString()=="0","Execution counters copied");
        // Simulate losing the local confirmation after the server created the task.
        job.RemoteTaskId="";job.State="interrupted";job.Error="Response lost";
        transfers.Start(job.Id,connection.Id,destination);
        await Finished();
        Check(job.State=="completed"&&job.RemoteTaskId==id,"Resume did not recover the confirmed task");
        Check(job.Error=="","Completed task retained an old transfer error");
        list = await Json(http,uri,"GET","/bridge/v1/tasks?limit=500");
        Check(list.GetProperty("items").EnumerateArray().Count(t=>t.GetProperty("name").GetString()==settings["name"])==1,"Resume duplicated task");
        Console.WriteLine("PASS task-upload-file-settings-payload-idempotency");
        settings["name"]="Declined task copy";
        response=JsonSerializer.SerializeToElement(start.Invoke(transfers,new object[]{JsonSerializer.SerializeToElement(settings),connection.Id,destination}));
        job=transfers.Get(response.GetProperty("Id").GetString()!);
        await Finished();Check(job.State=="awaiting_conflicts","Task upload skipped overwrite confirmation");
        transfers.Confirm(job.Id,new());
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            while(job.State is not "failed" and not "completed")await Task.Delay(30,timeout.Token);
        Check(job.State=="failed"&&job.RemoteTaskId=="","Task created after declining file replacement");
        list=await Json(http,uri,"GET","/bridge/v1/tasks?limit=500");
        Check(!list.GetProperty("items").EnumerateArray().Any(t=>t.GetProperty("name").GetString()==settings["name"]),"Declined upload created a task");
        Console.WriteLine("PASS task-upload-declined-overwrite-no-task");
    }
}
