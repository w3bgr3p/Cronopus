using System.Net;
using System.Text;
using System.Text.Json;
namespace z3nDash;

public sealed partial class BridgeTransfers
{
    private readonly SemaphoreSlim _workers=new(2,2);
    public object Start(string id,string connectionId,string destination)
    {
        var job=Get(id);
        if(job.CancelRequested)throw new ArgumentException("Cancelled transfers require a new selection");
        if(job.ConnectionId.Length>0&&job.ConnectionId!=connectionId)throw new ArgumentException("Transfer belongs to a different connection");
        if(job.Destination.Length>0&&job.Destination!=destination)throw new ArgumentException("Transfer destination cannot change");
        if(_running.ContainsKey(id))return Public(job);
        job.ConnectionId=connectionId;job.NodeName=store.Connection(connectionId).Name;job.Destination=destination;var cts=new CancellationTokenSource();
        if(!_running.TryAdd(id,cts))return Public(job);
        _=Task.Run(async()=>{try{await _workers.WaitAsync(cts.Token);try{await Run(job,cts.Token);}finally{_workers.Release();}}
            catch(OperationCanceledException){job.State=job.CancelRequested?"cancelled":"interrupted";job.Error=job.CancelRequested?"":"Transfer interrupted. Resume to continue.";Save(job);}
            catch(Exception ex){job.State="failed";job.Error=ex is HttpRequestException?"Connection failed. Check the server and resume.":ex.Message;Save(job);}
            finally{_running.TryRemove(id,out _);cts.Dispose();}});
        return Public(job);
    }
    public void Confirm(string id,List<string> replace){var job=Get(id);job.Replace=replace;Save(job);_=Task.Run(async()=>{while(_running.ContainsKey(id))await Task.Delay(20);Start(id,job.ConnectionId,job.Destination);});}
    public async Task Cancel(string id)
    {
        var job=Get(id);job.CancelRequested=true;if(_running.TryGetValue(id,out var cts))cts.Cancel();
        if(job.UploadId.Length>0){try{using var response=await client.SendAsync(job.ConnectionId,HttpMethod.Delete,"/bridge/v1/uploads/"+job.UploadId,null,CancellationToken.None);}catch(HttpRequestException){}}
        job.State="cancelled";Save(job);
    }
    private async Task<JsonElement> JsonRequest(BridgeTransfer job,HttpMethod method,string path,object? body,CancellationToken ct)
    {
        using var content=body==null?null:new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var response=await client.SendAsync(job.ConnectionId,method,path,content,ct);
        var data=await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
        if(!response.IsSuccessStatusCode)throw new IOException(data.TryGetProperty("error",out var error)?error.GetString():"Transfer failed");return data;
    }
    private async Task Run(BridgeTransfer job,CancellationToken ct)
    {
        await client.CheckAsync(job.ConnectionId,ct);job.State="hashing";job.Error="";Save(job);
        foreach(var file in job.Files){ct.ThrowIfCancellationRequested();await using var input=File.OpenRead(file.Source);
            var hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input,ct));
            if(file.Sha256.Length>0&&file.Sha256!=hash)throw new IOException("A selected source file changed. Start a new transfer.");file.Sha256=hash;}
        JsonElement remote;
        if(job.UploadId.Length==0){remote=await JsonRequest(job,HttpMethod.Post,"/bridge/v1/uploads",new{destination=job.Destination,directories=job.Directories,files=job.Files.Select(f=>new{id=f.Id,relativePath=f.RelativePath,size=f.Size,sha256=f.Sha256})},ct);job.UploadId=remote.GetProperty("upload_id").GetString()!;}
        else remote=await JsonRequest(job,HttpMethod.Get,"/bridge/v1/uploads/"+job.UploadId,null,ct);
        job.Conflicts=remote.GetProperty("conflicts").EnumerateArray().Select(f=>f.GetProperty("relativePath").GetString()!).ToList();
        if(job.Conflicts.Count>0&&job.Replace==null){job.State="awaiting_conflicts";Save(job);return;}
        job.State="uploading";job.Transferred=0;Save(job);
        var offsets=remote.GetProperty("files").EnumerateArray().ToDictionary(f=>f.GetProperty("id").GetString()!,f=>f.GetProperty("offset").GetInt64());
        var buffer=new byte[4194304];
        foreach(var file in job.Files){
            var offset=offsets[file.Id];job.Transferred+=offset;
            await using var input=new FileStream(file.Source,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
            input.Position=offset;
            while(offset<file.Size){var count=await input.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,file.Size-offset)),ct);
                if(count==0)throw new IOException("Source file ended unexpectedly");
                using var content=new ByteArrayContent(buffer,0,count);
                using var response=await client.SendAsync(job.ConnectionId,HttpMethod.Put,$"/bridge/v1/uploads/{job.UploadId}/files/{file.Id}?offset={offset}",content,ct);
                if(!response.IsSuccessStatusCode)throw new IOException("Block upload failed. Resume to reconcile its offset.");
                var result=await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
                var next=result.GetProperty("offset").GetInt64();if(next!=offset+count)throw new IOException("Invalid confirmed offset");
                offset=next;job.Transferred+=count;Save(job);
            }
        }
        job.State="committing";Save(job);
        var committed=await JsonRequest(job,HttpMethod.Post,"/bridge/v1/uploads/"+job.UploadId+"/commit",new{replace=job.Replace??new()},ct);
        var errors=committed.GetProperty("files").EnumerateArray().Where(f=>f.GetProperty("error").GetString()?.Length>0).Select(f=>f.GetProperty("relativePath").GetString()+": "+f.GetProperty("error").GetString()).ToArray();
        job.State=errors.Length>0?"failed":"completed";job.Error=string.Join("\n",errors);Save(job);
    }
}
