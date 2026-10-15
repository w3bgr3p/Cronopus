using System.Text.Json;
using System.Text;
using Microsoft.Data.Sqlite;
using z3nDash;

static partial class BridgeCases
{
    static async Task ApiCases(string root,HttpClient http,Uri uri)
    {
        var created=await Json(http,uri,"POST","/bridge/v1/tasks",new{name="API CASE",enabled="false",schedule_mode="off",executor="python"});
        var id=created.GetProperty("id").GetString();
        using(var request=new HttpRequestMessage(HttpMethod.Put,new Uri(uri,"/bridge/v1/tasks/"+id))){
            request.Headers.Add("X-Request-ID",Guid.NewGuid().ToString());request.Content=new StringContent("{\"name\":\"missing revision\"}",Encoding.UTF8,"application/json");
            using var response=await http.SendAsync(request);Check((int)response.StatusCode==428,"remote edit without revision accepted");
        }
        var detail=await Json(http,uri,"GET","/bridge/v1/tasks/"+id);
        await Json(http,uri,"PUT","/bridge/v1/tasks/"+id,new{name="API EDIT",_revision=detail.GetProperty("_revision").GetString()});
        var clone=await Json(http,uri,"POST","/bridge/v1/tasks/"+id+"/clone",new{});
        Check(clone.GetProperty("id").GetString()!=id,"clone reused identity");
        var runtime=await Json(http,uri,"GET","/bridge/v1/tasks/"+id+"/queue?limit=1");Check(runtime.GetProperty("items").GetArrayLength()==0,"unexpected queue");
        Check(runtime.GetProperty("total_pending").GetInt64()==0,"queue total missing");
        var changes=await Json(http,uri,"GET","/bridge/v1/changes");Check(changes.TryGetProperty("next_cursor",out _),"changes cursor missing");
        Console.WriteLine("PASS remote-revision-clone-pages-changes");
        var ops=new BridgeOperations(Path.Combine(root,"journal-expiry"));
        using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(root,"journal-expiry","operations.db")}.ToString())){
            c.Open();using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO operations VALUES('old','old','h','succeeded',200,'{}','application/json',datetime('now','-8 days'))";cmd.ExecuteNonQuery();
        }
        await ops.ExecuteAsync("new","new","input",()=>Task.FromResult(new TaskerReply(200,"{}","application/json")));
        Check(ops.Get("old","old")==null,"expired journal row retained at admission");Console.WriteLine("PASS operation-expiry");
    }
}
