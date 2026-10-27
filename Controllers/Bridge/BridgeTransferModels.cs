namespace Cronopus;

public sealed class BridgeLocalFile
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string Source { get; set; }="";
    public string RelativePath { get; set; }="";
    public long Size { get; set; }
    public string Sha256 { get; set; }="";
}
public sealed class BridgeTransfer
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string ConnectionId { get; set; }="";
    public string NodeName { get; set; }="";
    public string Name { get; set; }="";
    public string Destination { get; set; }="";
    public string UploadId { get; set; }="";
    public string State { get; set; }="selected";
    public bool CancelRequested { get; set; }
    public string Error { get; set; }="";
    public long Transferred { get; set; }
    public List<BridgeLocalFile> Files { get; set; }=new();
    public List<string> Directories { get; set; }=new();
    public List<string> Skipped { get; set; }=new();
    public List<string> Conflicts { get; set; }=new();
    public List<string>? Replace { get; set; }
    public List<BridgeTransferResult>? Results { get; set; }
    public Dictionary<string,string>? TaskSettings { get; set; }
    public string RemoteTaskId { get; set; }="";
}
public sealed record BridgeTransferResult(string Path,string State,string? Error);
