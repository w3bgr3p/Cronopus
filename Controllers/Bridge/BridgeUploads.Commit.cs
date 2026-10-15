namespace z3nDash;

public sealed partial class BridgeUploads
{
    public async Task<object> Commit(string key,string id,IReadOnlyCollection<string> replace,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try{
            var s=Load(key,id);
            foreach(var directory in s.Directories)BridgeUploadPaths.EnsureDirectory(s.Destination,directory);
            foreach(var file in s.Files){
                ct.ThrowIfCancellationRequested();if(file.State=="published"||file.State=="skipped")continue;
                file.Error="";
                try{
                    var target=BridgeUploadPaths.Resolve(s.Destination,file.RelativePath);
                    var staged=BridgeUploadPaths.Resolve(s.Stage,file.Id+".part");
                    if(file.Offset!=file.Size)throw new IOException("File transfer is incomplete");
                    if(!File.Exists(staged)&&File.Exists(target)&&await HashFileAsync(target,ct)==file.Sha256.ToUpperInvariant()){file.State="published";Save(s);continue;}
                    if(file.Size==0&&!File.Exists(staged))File.WriteAllBytes(staged,Array.Empty<byte>());
                    if(await HashFileAsync(staged,ct)!=file.Sha256.ToUpperInvariant())throw new IOException("SHA-256 mismatch");
                    if(file.OriginalHash!=null&&!replace.Contains(file.RelativePath)){file.State="skipped";Save(s);continue;}
                    var parent=Path.GetDirectoryName(file.RelativePath.Replace('/',Path.DirectorySeparatorChar));
                    if(!string.IsNullOrEmpty(parent))BridgeUploadPaths.EnsureDirectory(s.Destination,parent);
                    using var targetLease=BridgeUploadPaths.LockParents(target);using var stageLease=BridgeUploadPaths.LockParents(staged);
                    target=BridgeUploadPaths.Resolve(s.Destination,file.RelativePath);
                    var currentHash=File.Exists(target)?await HashFileAsync(target,ct):null;
                    if(currentHash!=file.OriginalHash)throw new IOException("Destination changed since confirmation. Start a new transfer.");
                    ct.ThrowIfCancellationRequested();File.Move(staged,target,currentHash!=null);file.State="published";
                }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){file.Error=ex.Message;}
                Save(s);
            }
            if(s.Files.All(f=>f.State is "published" or "skipped"))CleanupStage(s);
            return Public(s);
        }finally{_gate.Release();}
    }
    private static void CleanupStage(BridgeUploadSession session)
    {
        var expected=BridgeUploadPaths.Resolve(session.Destination,".z3ndash-upload-"+session.Id);
        if(session.Stage!=expected)throw new UnauthorizedAccessException("Invalid staging directory");
        if(Directory.Exists(expected)){using var lease=BridgeUploadPaths.LockParents(expected);Directory.Delete(expected,true);}
    }
    public async Task Cancel(string key,string id,CancellationToken ct)
    {await _gate.WaitAsync(ct);try{var s=Load(key,id);s.Cancelled=true;Save(s);CleanupStage(s);}finally{_gate.Release();}}
}
