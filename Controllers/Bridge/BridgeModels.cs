namespace z3nDash;

public sealed class BridgeSettings
{
    public bool Enabled { get; set; }
    public string BindAddress { get; set; } = "0.0.0.0";
    public string AdvertisedAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 38473;
    public long UploadQuotaBytes { get; set; } = 40L * 1024 * 1024 * 1024;
    public long TransferBytesPerSecond { get; set; } = 8 * 1024 * 1024;
}
public sealed class BridgeConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Server";
    public string Address { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Secret { get; set; } = "";
    public string LastFolder { get; set; } = "";
    public object Public() => new { Id, Name, Address, NodeId, Fingerprint, LastFolder, HasKey = Secret.Length > 0 };
}
public sealed record BridgeIssuedKey(string Id, string Secret);
public sealed record BridgeKey(string Id, string Name, string Hash, bool Revoked);
public sealed class BridgeState
{
    public string NodeId { get; set; } = Guid.NewGuid().ToString("N");
    public BridgeSettings Settings { get; set; } = new();
    public string Certificate { get; set; } = "";
    public List<BridgeKey> Keys { get; set; } = new();
    public List<BridgeConnection> Connections { get; set; } = new();
}
