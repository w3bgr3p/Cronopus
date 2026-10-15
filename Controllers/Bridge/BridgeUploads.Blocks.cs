namespace z3nDash;

public sealed partial class BridgeUploads
{
    private readonly object _rateGate=new();
    private DateTimeOffset _nextBlock=DateTimeOffset.UtcNow;
    public async Task<object> Put(string key,string id,string fileId,long offset,Stream input,CancellationToken ct)
    {
        await _blocks.WaitAsync(ct);
        try{
            using var bytes=new MemoryStream();var buffer=new byte[65536];int read;
            while((read=await input.ReadAsync(buffer,ct))>0){if(bytes.Length+read>4194304)throw new ArgumentException("Block exceeds 4 MiB");await bytes.WriteAsync(buffer.AsMemory(0,read),ct);}
            TimeSpan delay;lock(_rateGate){var now=DateTimeOffset.UtcNow;var start=_nextBlock>now?_nextBlock:now;delay=start-now;_nextBlock=start.AddSeconds(bytes.Length/(double)store.State.Settings.TransferBytesPerSecond);}
            if(delay>TimeSpan.Zero)await Task.Delay(delay,ct);
            await _gate.WaitAsync(ct);
            try{
                var s=Load(key,id);var file=s.Files.FirstOrDefault(f=>f.Id==fileId)??throw new KeyNotFoundException("File not found");
                if(file.State!="pending"||offset!=file.Offset||offset+bytes.Length>file.Size)throw new ArgumentException("Unexpected upload offset");
                var target=BridgeUploadPaths.Resolve(s.Stage,file.Id+".part");
                using var lease=BridgeUploadPaths.LockParents(target);
                await using(var output=new FileStream(target,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous|FileOptions.WriteThrough)){
                    if(output.Length<offset)throw new IOException("Staged file is incomplete");output.SetLength(offset);output.Position=offset;
                    bytes.Position=0;await bytes.CopyToAsync(output,ct);output.Flush(true);
                }
                file.Offset+=bytes.Length;Save(s);return new{offset=file.Offset};
            }finally{_gate.Release();}
        }finally{_blocks.Release();}
    }
}
