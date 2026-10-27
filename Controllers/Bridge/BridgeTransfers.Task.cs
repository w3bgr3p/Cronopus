using System.Text;
using System.Text.Json;
namespace Cronopus;

public sealed partial class BridgeTransfers
{
    public object StartTask(JsonElement task,string connectionId,string destination)
    {
        store.Connection(connectionId);
        if(string.IsNullOrWhiteSpace(destination))throw new ArgumentException("Choose a destination folder");
        var settings=new Dictionary<string,string>();
        foreach(var key in new[]{"name","executor","script_path","args","enabled","cron","interval_minutes","fixed_time",
            "on_overlap","max_threads","payload_schema","payload_values","schedule_tag","use_venv","schedule_mode","schedule_json","browser_json","timeout_seconds"})
            if(task.TryGetProperty(key,out var value))settings[key]=value.GetString()??"";
        var source=settings.GetValueOrDefault("script_path","");
        if(string.IsNullOrWhiteSpace(source)||(!File.Exists(source)&&!Directory.Exists(source)))throw new ArgumentException("Task file does not exist locally");
        settings["script_path"]=Directory.Exists(source)?destination:destination.TrimEnd('\\','/')+"\\"+Path.GetFileName(source);
        settings["runs_total"]="0";settings["runs_success"]="0";settings["sched_runs"]="0";
        settings["sched_started_at"]="";
        var job=Select(source);job.TaskSettings=settings;job.Name=settings.GetValueOrDefault("name",job.Name);Save(job);
        return Start(job.Id,connectionId,destination);
    }

    private async Task CreateTask(BridgeTransfer job,CancellationToken ct)
    {
        if(job.RemoteTaskId.Length>0){job.State="completed";Save(job);return;}
        job.State="creating_task";job.Error="";Save(job);
        // The stable request ID lets the server replay a confirmed result after a lost response.
        using var content=new StringContent(JsonSerializer.Serialize(job.TaskSettings),Encoding.UTF8,"application/json");
        using var response=await client.SendAsync(job.ConnectionId,HttpMethod.Post,"/bridge/v1/tasks",content,ct,"task-upload-"+job.Id);
        var result=await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
        if(!response.IsSuccessStatusCode)throw new IOException("File uploaded, but task creation failed: "+(result.TryGetProperty("error",out var error)?error.GetString():response.StatusCode.ToString()));
        if(response.StatusCode==System.Net.HttpStatusCode.Accepted){
            for(var n=0;n<120;n++){
                await Task.Delay(1000,ct);
                var operation=await JsonRequest(job,HttpMethod.Get,"/bridge/v1/operations/task-upload-"+job.Id,null,ct);
                var state=operation.GetProperty("state").GetString();
                if(state=="outcome_unknown")throw new IOException("File uploaded; task creation outcome is unknown. Check server tasks before starting another upload.");
                if(state is not "succeeded" and not "failed")continue;
                if(operation.GetProperty("status").GetInt32()>=400)throw new IOException("File uploaded, but task creation failed: "+operation.GetProperty("result").GetString());
                result=JsonSerializer.Deserialize<JsonElement>(operation.GetProperty("result").GetString()!);break;
            }
        }
        if(!result.TryGetProperty("ok",out var ok)||!ok.GetBoolean()||!result.TryGetProperty("id",out var id))
            throw new IOException("File uploaded; task creation is unconfirmed. Resume to check the same operation.");
        job.RemoteTaskId=id.GetString()!;job.State="completed";Save(job);
    }
}
