using System.Net;
using System.Net.Sockets;
using System.Text;
namespace Cronopus;

public static class BridgePublicAddress
{
    public static async Task<string> DetectAsync(HttpClient client,CancellationToken ct)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(TimeSpan.FromSeconds(8));
        foreach(var endpoint in new[]{"https://api4.ipify.org/","https://checkip.amazonaws.com/"}){
            if(budget.IsCancellationRequested)break;
            try{
                using var attempt=CancellationTokenSource.CreateLinkedTokenSource(budget.Token);attempt.CancelAfter(TimeSpan.FromSeconds(4));
                using var response=await client.GetAsync(endpoint,HttpCompletionOption.ResponseHeadersRead,attempt.Token);response.EnsureSuccessStatusCode();
                await using var stream=await response.Content.ReadAsStreamAsync(attempt.Token);var buffer=new byte[65];int count=0,read=0;
                while(count<buffer.Length&&(read=await stream.ReadAsync(buffer.AsMemory(count),attempt.Token))>0)count+=read;
                var text=Encoding.ASCII.GetString(buffer,0,count).Trim();
                if(count<65&&IsPublicIpv4(text))return IPAddress.Parse(text).ToString();
            }catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or IOException){}
        }
        ct.ThrowIfCancellationRequested();throw new HttpRequestException("Could not detect the public IP. Enter the server's public IP manually.");
    }
    public static bool IsPublicIpv4(string value)
    {
        if(!IPAddress.TryParse(value,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork)return false;
        var b=ip.GetAddressBytes();
        return !(b[0] is 0 or 10 or 127 or >=224 || b[0]==169&&b[1]==254 || b[0]==172&&b[1] is >=16 and <=31 || b[0]==192&&b[1]==168 || b[0]==100&&b[1] is >=64 and <=127 || b[0]==198&&b[1] is 18 or 19);
    }
}
