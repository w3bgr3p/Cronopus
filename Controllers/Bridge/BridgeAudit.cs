using System.Text.Json;
namespace z3nDash;

public sealed class BridgeAudit(string root)
{
    private readonly object _gate=new();
    public void Write(string key,string method,string path,int status,TimeSpan duration)
    {
        lock(_gate){
            try{
                var file=Path.Combine(root,"audit.log");
                if(File.Exists(file)&&new FileInfo(file).Length>=10*1024*1024){
                    var last=file+".4";if(File.Exists(last))File.Delete(last);
                    for(var i=3;i>=1;i--)if(File.Exists(file+"."+i))File.Move(file+"."+i,file+"."+(i+1),true);
                    File.Move(file,file+".1",true);
                }
                File.AppendAllText(file,JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,key_id=key,method,path,status,duration_ms=duration.TotalMilliseconds})+Environment.NewLine);
            }catch(IOException){}
        }
    }
}
