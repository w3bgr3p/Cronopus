using System.Text.Json;
namespace Cronopus;

public sealed partial class BridgeUploads
{
    public async Task<object> Create(string key,JsonElement manifest,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try {
            var session=manifest.Deserialize<BridgeUploadSession>(_json)!;
            session.Id=Guid.NewGuid().ToString("N");session.KeyId=key;session.Created=DateTimeOffset.UtcNow;session.Cancelled=false;
            if(!Directory.Exists(session.Destination)||!Path.IsPathFullyQualified(session.Destination))throw new ArgumentException("Choose an existing server folder");
            if(session.Files.Count>10000||session.Directories.Count>10000)throw new ArgumentException("Too many entries");
            var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
            var paths=new HashSet<string>(comparer);var ids=new HashSet<string>();long total=0;
            foreach(var file in session.Files){
                TaskerService.ValidateId(file.Id);
                if(!ids.Add(file.Id)||!paths.Add(file.RelativePath.Replace('\\','/')))throw new ArgumentException("Duplicate file path or ID");
                var target=BridgeUploadPaths.Resolve(session.Destination,file.RelativePath);
                if(file.Size<0||file.Size>20L*1024*1024*1024||file.Sha256.Length!=64)throw new ArgumentException("Invalid file size or SHA-256");
                Convert.FromHexString(file.Sha256);total=checked(total+file.Size);
                file.Offset=0;file.State="pending";file.Error="";file.OriginalHash=File.Exists(target)?await HashFileAsync(target,ct):null;
            }
            foreach(var directory in session.Directories)BridgeUploadPaths.Resolve(session.Destination,directory);
            var metaRoot=Path.Combine(store.Root,"uploads");Directory.CreateDirectory(metaRoot);
            long reserved=0;var count=0;
            foreach(var path in Directory.EnumerateFiles(metaRoot,"*.json")){
                var existing=JsonSerializer.Deserialize<BridgeUploadSession>(File.ReadAllBytes(path),_json)!;
                if(existing.Created<DateTimeOffset.UtcNow.AddHours(-24)||existing.Cancelled)continue;
                count++;reserved=checked(reserved+existing.Files.Where(f=>f.State=="pending").Sum(f=>f.Size));
            }
            if(count>=1000||total+reserved>store.State.Settings.UploadQuotaBytes)throw new ArgumentException("Upload quota exceeded");
            var drive=new DriveInfo(Path.GetPathRoot(session.Destination)!);
            if(total>drive.AvailableFreeSpace)throw new IOException("Not enough free disk space");
            session.Stage=BridgeUploadPaths.Resolve(session.Destination,".cronopus-upload-"+session.Id);
            using(var lease=BridgeUploadPaths.LockParents(session.Stage))Directory.CreateDirectory(session.Stage);
            Save(session);return Public(session);
        }finally{_gate.Release();}
    }
}
