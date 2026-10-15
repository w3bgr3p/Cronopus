using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
namespace z3nDash;

public sealed partial class BridgeTransfers(BridgeStore store,BridgeClient client)
{
    private readonly ConcurrentDictionary<string,BridgeTransfer> _jobs=new();
    private readonly ConcurrentDictionary<string,CancellationTokenSource> _running=new();
    public object Public(BridgeTransfer t)=>new {t.Id,t.Name,t.NodeName,t.ConnectionId,t.Destination,state=t.State is "hashing" or "uploading" or "committing"&&!_running.ContainsKey(t.Id)?"interrupted":t.State,t.Error,t.Transferred,total=t.Files.Sum(f=>f.Size),files=t.Files.Count,t.Skipped,t.Conflicts};
    private void Save(BridgeTransfer t)=>store.SavePrivate("transfer-"+t.Id+".bin",t);
    public BridgeTransfer Get(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("Invalid transfer ID");
        return _jobs.GetOrAdd(id,_=>store.ReadPrivate<BridgeTransfer>("transfer-"+id+".bin"));
    }
    public BridgeTransfer Select(string source)
    {
        if(_jobs.Count>=100){var finished=_jobs.FirstOrDefault(j=>j.Value.State is "completed" or "cancelled");if(finished.Key!=null)_jobs.TryRemove(finished.Key,out _);else throw new ArgumentException("Finish or cancel an existing transfer first");}
        var job=new BridgeTransfer{Name=Path.GetFileName(source)};
        if(File.Exists(source))AddFile(job,source,Path.GetFileName(source));
        else if(Directory.Exists(source)){
            var pending=new Stack<string>();pending.Push(source);
            while(pending.Count>0){var dir=pending.Pop();foreach(var entry in Directory.EnumerateFileSystemEntries(dir)){
                var relative=Path.GetRelativePath(source,entry);var attributes=File.GetAttributes(entry);
                if(attributes.HasFlag(FileAttributes.ReparsePoint)){job.Skipped.Add(relative);continue;}
                if(attributes.HasFlag(FileAttributes.Directory)){job.Directories.Add(relative);pending.Push(entry);}else AddFile(job,entry,relative);
                if(job.Files.Count+job.Directories.Count>10000)throw new ArgumentException("Selection exceeds 10000 entries");
            }}
        }else throw new ArgumentException("Selection does not exist");
        _jobs[job.Id]=job;Save(job);return job;
    }
    private static void AddFile(BridgeTransfer job,string source,string relative)
    {var file=new FileInfo(source);if(file.Length>20L*1024*1024*1024)throw new ArgumentException("File exceeds 20 GiB");job.Files.Add(new(){Source=source,RelativePath=relative,Size=file.Length});}
}
