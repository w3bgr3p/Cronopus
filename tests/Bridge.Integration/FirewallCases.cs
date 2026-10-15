using System.Diagnostics;
using System.Text;
using z3nDash;

static class FirewallCases
{
    public static async Task Run()
    {
        var firewall=new BridgeFirewall(Guid.NewGuid().ToString("N"));
        foreach(var action in new[]{"open; exit 9","*","remove-all"}){
            try{firewall.Script(action,39291);throw new Exception("Invalid firewall action accepted");}catch(ArgumentException){}
        }
        try{firewall.Script("open",0);throw new Exception("Invalid port accepted");}catch(ArgumentException){}
        var status=firewall.Status(39291);
        if(OperatingSystem.IsWindows()){
            if(!status.Supported||status.Error!=null||status.Exists)throw new Exception("Firewall status failed: "+status.Error);
            var script=firewall.Script("open",39291).Replace(" -ErrorAction Stop | Out-Null"," -WhatIf -ErrorAction Stop | Out-Null").Replace("catch { exit 1 }","catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }");
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell/v1.0/powershell.exe")){
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-EncodedCommand");start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
            await stdout;var error=await stderr;if(process.ExitCode!=0)throw new Exception("Firewall dry-run failed: "+error);
            if(firewall.Status(39291).Exists)throw new Exception("Firewall dry-run changed system rules");
        }
        Console.WriteLine("PASS firewall-validation-status-dry-run");
    }
}
