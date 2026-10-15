using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
namespace z3nDash;

public sealed record BridgeFirewallStatus(bool Supported,int Port,string RuleName,bool Exists,bool Open,string? Error);
public sealed class BridgeFirewall(string nodeId)
{
    public string RuleName{get;}="z3nDash.RemoteTasker."+Guid.Parse(nodeId).ToString("N");
    public BridgeFirewallStatus Status(int port)
    {
        if(!OperatingSystem.IsWindows())return new(false,port,RuleName,false,false,"Windows Firewall is available on Windows only.");
        object? policy=null,rule=null,rules=null;
        try{
            policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2",true)!);rules=((dynamic)policy!).Rules;
            try{rule=((dynamic)rules).Item(RuleName);}catch(Exception ex)when(ex.HResult==unchecked((int)0x80070002)){return new(true,port,RuleName,false,false,null);}
            dynamic item=rule;
            var open=(bool)item.Enabled&&(int)item.Direction==1&&(int)item.Action==1&&(int)item.Protocol==6&&(string)item.LocalPorts==port.ToString(CultureInfo.InvariantCulture)
                &&string.Equals((string)item.ApplicationName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase);
            return new(true,port,RuleName,true,open,null);
        }catch(Exception ex){return new(true,port,RuleName,false,false,"Cannot read Windows Firewall: "+ex.Message);}
        finally{Release(rule);Release(rules);Release(policy);}
    }
    private static void Release(object? value){if(OperatingSystem.IsWindows()&&value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
    private static string Quote(string value)=>"'"+value.Replace("'","''")+"'";
    public string Script(string action,int port)
    {
        if(action is not "open" and not "close"||port is <1 or >65535)throw new ArgumentException("Invalid firewall action or port");
        var name=Quote(RuleName);var find="Get-NetFirewallRule -Name "+name+" -ErrorAction SilentlyContinue";
        var script="$ErrorActionPreference='Stop'; try { ";
        if(action=="close")script+=find+" | Remove-NetFirewallRule -ErrorAction Stop; ";
        else{
            var options=" -Direction Inbound -Action Allow -Protocol TCP -LocalPort "+port+" -Profile Any -Enabled True -Program "+Quote(Environment.ProcessPath??throw new InvalidOperationException("Application path unavailable"));
            // INetFwRules.Item uses DisplayName, whereas NetSecurity uses Name.
            // Keep both identities equal, including when updating older rules.
            script+="if ("+find+") { Set-NetFirewallRule -Name "+name+" -NewDisplayName "+name+options+" -ErrorAction Stop | Out-Null } else { New-NetFirewallRule -Name "+name+" -DisplayName "+name+" -Group 'z3nDash'"+options+" -ErrorAction Stop | Out-Null }; ";
        }
        return script+"exit 0 } catch { exit 1 }";
    }
    public async Task<BridgeFirewallStatus> Change(string action,int port,CancellationToken ct)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows Firewall is available on Windows only.");
        var script=Script(action,port);
        var powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
        var start=new ProcessStartInfo(powershell){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,
            Arguments="-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))};
        try{
            using var process=Process.Start(start)??throw new InvalidOperationException("Cannot start Windows Firewall setup");
            await process.WaitForExitAsync(ct);
            if(process.ExitCode!=0)throw new InvalidOperationException("Windows Firewall could not update the rule. Check administrator permissions and system policy.");
        }catch(System.ComponentModel.Win32Exception ex)when(ex.NativeErrorCode==1223){throw new InvalidOperationException("Windows administrator approval was cancelled. The firewall rule was not changed.");}
        catch(OperationCanceledException){throw new InvalidOperationException("Firewall setup did not finish in time. Check its status before repeating the action; the rule may already have changed.");}
        var status=Status(port);
        if(status.Error!=null||action=="open"&&!status.Open||action=="close"&&status.Exists)throw new InvalidOperationException(status.Error??"Windows Firewall did not confirm the requested rule. Refresh its status.");
        return status;
    }
}
