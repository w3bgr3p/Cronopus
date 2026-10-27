namespace Cronopus;

public sealed class BridgeUploadFile
{
    public string Id { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string? OriginalHash { get; set; }
    public long Offset { get; set; }
    public string State { get; set; } = "pending";
    public string Error { get; set; } = "";
}
public sealed class BridgeUploadSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string KeyId { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Stage { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public List<BridgeUploadFile> Files { get; set; } = new();
    public List<string> Directories { get; set; } = new();
    public bool Cancelled { get; set; }
}
