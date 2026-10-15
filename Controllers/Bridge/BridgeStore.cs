using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;

namespace z3nDash;

public sealed class BridgeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _revocations = new();
    public string Root { get; }
    public BridgeState State { get; }
    public string NodeId => State.NodeId;
    public BridgeStore(string? root = null)
    {
        Root = root ?? Environment.GetEnvironmentVariable("Z3NDASH_BRIDGE_STATE_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "z3nDash", "bridge");
        if (!Path.IsPathFullyQualified(Root)) throw new ArgumentException("Bridge state path must be absolute");
        Directory.CreateDirectory(Root);
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(Root).SetAccessControl(security);
        }
        else File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var file = Path.Combine(Root, "state.bin");
        State = File.Exists(file) ? JsonSerializer.Deserialize<BridgeState>(Unprotect(File.ReadAllBytes(file)))! : new();
        Save();
    }
    private static byte[] Protect(byte[] data) => OperatingSystem.IsWindows()
        ? ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser) : data;
    private static byte[] Unprotect(byte[] data) => OperatingSystem.IsWindows()
        ? ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser) : data;
    public void Save()
    {
        lock (_gate)
        {
            var file = Path.Combine(Root, "state.bin"); var temp = file + ".tmp";
            File.WriteAllBytes(temp, Protect(JsonSerializer.SerializeToUtf8Bytes(State)));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, file, true);
        }
    }
    public BridgeIssuedKey CreateKey(string name)
    {
        lock (_gate)
        {
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var id = Guid.NewGuid().ToString("N");
            State.Keys.Add(new(id, name, Hash(secret), false)); Save();
            return new(id, secret);
        }
    }
    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
    public string? Authorize(string secret)
    {
        var hash = Convert.FromHexString(Hash(secret));
        lock (_gate) return State.Keys.FirstOrDefault(k => !k.Revoked &&
            CryptographicOperations.FixedTimeEquals(Convert.FromHexString(k.Hash), hash))?.Id;
    }
    public CancellationToken RevocationToken(string key)
    {
        lock (_gate)
        {
            if (!_revocations.TryGetValue(key, out var cts)) _revocations[key] = cts = new();
            if (State.Keys.All(k => k.Id != key || k.Revoked)) cts.Cancel();
            return cts.Token;
        }
    }
    public void RevokeKey(string id)
    {
        lock (_gate)
        {
            var index = State.Keys.FindIndex(k => k.Id == id);
            if (index < 0) throw new KeyNotFoundException("Key not found");
            State.Keys[index] = State.Keys[index] with { Revoked = true };
            if (_revocations.TryGetValue(id, out var cts)) cts.Cancel();
            Save();
        }
    }
    public BridgeConnection Connection(string id)
    { lock (_gate) return State.Connections.FirstOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException("Connection not found"); }
    public X509Certificate2 Certificate(bool regenerate = false)
    {
        lock (_gate)
        {
            if (regenerate || State.Certificate.Length == 0)
            {
                using var rsa = RSA.Create(3072);
                var request = new CertificateRequest("CN=z3nDash " + NodeId, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var san = new SubjectAlternativeNameBuilder();
                san.AddIpAddress(IPAddress.Parse(State.Settings.AdvertisedAddress));
                request.CertificateExtensions.Add(san.Build());
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
                State.Certificate = Convert.ToBase64String(cert.Export(X509ContentType.Pfx)); Save();
            }
            return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(State.Certificate), null, X509KeyStorageFlags.EphemeralKeySet);
        }
    }
    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexString(SHA256.HashData(cert.RawData));
}
