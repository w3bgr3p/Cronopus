using System.Security.Cryptography;
using System.Text.Json;
using z3nDash;

static partial class BridgeCases
{
    static async Task UploadFailures(string root,HttpClient http,Uri uri)
    {
        var destination=Path.Combine(root,"conflicts");Directory.CreateDirectory(destination);
        var original=new byte[]{9,9};var replacement=new byte[]{1,2};File.WriteAllBytes(Path.Combine(destination,"existing.bin"),original);
        var manifest=new{destination,files=new[]{new{id="existing",relativePath="existing.bin",size=2,sha256=Convert.ToHexString(SHA256.HashData(replacement))}}};
        var upload=await Json(http,uri,"POST","/bridge/v1/uploads",manifest);var id=upload.GetProperty("upload_id").GetString();
        using var bad=await http.PutAsync(new Uri(uri,$"/bridge/v1/uploads/{id}/files/existing?offset=1"),new ByteArrayContent(replacement));
        Check((int)bad.StatusCode==400,"wrong offset accepted");
        using var block=await http.PutAsync(new Uri(uri,$"/bridge/v1/uploads/{id}/files/existing?offset=0"),new ByteArrayContent(replacement));Check(block.IsSuccessStatusCode,"replacement block rejected");
        await Json(http,uri,"POST",$"/bridge/v1/uploads/{id}/commit",new{replace=Array.Empty<string>()});
        Check(File.ReadAllBytes(Path.Combine(destination,"existing.bin")).SequenceEqual(original),"unconfirmed overwrite occurred");
        var traversal=new{destination,files=new[]{new{id="escape",relativePath="../escape.bin",size=2,sha256=Convert.ToHexString(SHA256.HashData(replacement))}}};
        using var rejected=await http.PostAsync(new Uri(uri,"/bridge/v1/uploads"),new StringContent(JsonSerializer.Serialize(traversal),System.Text.Encoding.UTF8,"application/json"));
        Check((int)rejected.StatusCode==400&&!File.Exists(Path.Combine(root,"escape.bin")),"traversal wrote outside destination");
        Console.WriteLine("PASS upload-offset-conflict-traversal");
    }
    static async Task NativeTransfer(string root,BridgeStore store,BridgeConnection connection)
    {
        var source=Path.Combine(root,"source");var destination=Path.Combine(root,"native-destination");Directory.CreateDirectory(source);Directory.CreateDirectory(destination);
        Directory.CreateDirectory(Path.Combine(source,"empty"));await File.WriteAllTextAsync(Path.Combine(source,"hello.py"),"print('fixture')");
        var transfers=new BridgeTransfers(store,new BridgeClient(store));var job=transfers.Select(source);
        transfers.Start(job.Id,connection.Id,destination);
        var deadline=DateTime.UtcNow.AddSeconds(20);
        while(job.State is not "completed" and not "failed"&&DateTime.UtcNow<deadline)await Task.Delay(100);
        Check(job.State=="completed","native transfer failed: "+job.State+" "+job.Error);
        Check(File.ReadAllText(Path.Combine(destination,"hello.py"))=="print('fixture')"&&Directory.Exists(Path.Combine(destination,"empty")),"folder or empty directory lost");
        Console.WriteLine("PASS native-folder-transfer");
    }
}
