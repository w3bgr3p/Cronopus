using System.Security.Cryptography;
using System.Text.Json;
namespace Cronopus;

public sealed partial class BridgeUploads(BridgeStore store)
{
    private readonly SemaphoreSlim _gate = new(1,1);
    private readonly SemaphoreSlim _blocks = new(4,4);
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private string Metadata(string id)
    { if (!Guid.TryParseExact(id,"N",out _)) throw new ArgumentException("Invalid upload ID"); return Path.Combine(store.Root,"uploads",id+".json"); }
    private void Save(BridgeUploadSession session)
    {
        Directory.CreateDirectory(Path.Combine(store.Root,"uploads")); var file=Metadata(session.Id);
        File.WriteAllBytes(file+".tmp",JsonSerializer.SerializeToUtf8Bytes(session,_json));File.Move(file+".tmp",file,true);
    }
    private BridgeUploadSession Load(string key,string id)
    {
        var session=JsonSerializer.Deserialize<BridgeUploadSession>(File.ReadAllBytes(Metadata(id)),_json)!;
        if(session.KeyId!=key)throw new UnauthorizedAccessException("Upload belongs to another key");
        if(session.Cancelled||session.Created<DateTimeOffset.UtcNow.AddHours(-24))throw new ArgumentException("Upload session expired or cancelled");
        store.RevocationToken(key).ThrowIfCancellationRequested(); return session;
    }
    public static string HashFile(string file)
    { using var input=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(input)); }
    public static async Task<string> HashFileAsync(string file,CancellationToken ct)
    {await using var input=File.OpenRead(file);return Convert.ToHexString(await SHA256.HashDataAsync(input,ct));}
    private static object Public(BridgeUploadSession s)=>new {upload_id=s.Id,expires_at=s.Created.AddHours(24),files=s.Files.Select(f=>new{f.Id,f.RelativePath,f.Size,f.Offset,f.State,f.Error}),conflicts=s.Files.Where(f=>f.OriginalHash!=null).Select(f=>new{f.RelativePath,sha256=f.OriginalHash})};
    public object Status(string key,string id)=>Public(Load(key,id));
}
