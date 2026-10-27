using System.Text.Json;
namespace Cronopus;

public sealed partial class BridgeUploads
{
    public async Task Cleanup(string? revokedKey=null)
    {
        if(revokedKey!=null)await _gate.WaitAsync();else if(!await _gate.WaitAsync(0))return;
        try{
            var folder=Path.Combine(store.Root,"uploads");if(!Directory.Exists(folder))return;
            foreach(var file in Directory.EnumerateFiles(folder,"*.json")){
                try{
                    var session=JsonSerializer.Deserialize<BridgeUploadSession>(File.ReadAllBytes(file),_json)!;
                    if(session.Created>DateTimeOffset.UtcNow.AddHours(-24)&&!session.Cancelled&&(revokedKey==null||session.KeyId!=revokedKey))continue;
                    CleanupStage(session);File.Delete(file);
                }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException){}
            }
        }finally{_gate.Release();}
    }
}
