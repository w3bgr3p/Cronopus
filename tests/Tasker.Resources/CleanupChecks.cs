using System.Diagnostics;

internal static class CleanupChecks
{
    internal static async Task Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cronopus orphan test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "worker with spaces.py");
        var other = Path.Combine(folder, "other.py");
        var code = "import os,sys,time,subprocess\nif '--leaf' in sys.argv:\n print(os.getpid(),flush=True)\nelse:\n p=subprocess.Popen([sys.executable,'-u',__file__,'--leaf'],stdout=subprocess.PIPE,text=True)\n p.stdout.readline()\n print(str(os.getpid())+' '+str(p.pid),flush=True)\ntime.sleep(120)\n";
        File.WriteAllText(script, code); File.WriteAllText(other, code);
        var orphan = await Orphan(script);
        var unrelated = await Orphan(other);
        using var live = Worker(script);
        await live.StandardOutput.ReadLineAsync();
        try
        {
            var preview = await Batch(script);
            Require(preview.Contains("Orphan roots: 1") && preview.Contains(orphan.Root.Id.ToString()), "preview finds only the orphan for the exact script");
            Require(!orphan.Root.HasExited && !orphan.Leaf.HasExited, "preview does not terminate processes");
            var filtered = await Batch(script, parent: Environment.ProcessId);
            Require(filtered.Contains("Orphan roots: 0"), "parent filter excludes other launchers");
            var result = await Batch(script, apply: true, parent: orphan.Parent);
            await Task.WhenAll(orphan.Root.WaitForExitAsync(), orphan.Leaf.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            Require(!live.HasExited && !unrelated.Root.HasExited && !unrelated.Leaf.HasExited, "live-parent and unrelated-script processes survive cleanup");
            Require((await Batch(script)).Contains("Orphan roots: 0"), "repeat scan is empty");
            Console.WriteLine("PASS cleanup: preview, exact path with spaces, parent filter, tree termination, unrelated/live processes and repeat scan");
        }
        finally
        {
            foreach (var p in new[] { orphan.Root, orphan.Leaf, unrelated.Root, unrelated.Leaf, live })
            {
                if (!p.HasExited) p.Kill(entireProcessTree: true);
                p.Dispose();
            }
            // Only these two known temporary test files are removed.
            File.Delete(script); File.Delete(other); Directory.Delete(folder);
        }
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL " + message);
    }

    internal static Process Worker(string path)
    {
        var info = new ProcessStartInfo("python") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-u"); info.ArgumentList.Add(path);
        return Process.Start(info)!;
    }

    private static async Task<(Process Root, Process Leaf, int Parent)> Orphan(string path)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("--cleanup-parent"); info.ArgumentList.Add(path);
        using var parent = Process.Start(info)!;
        var ids = (await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)))!
            .Split(' ').Select(int.Parse).ToArray();
        await parent.WaitForExitAsync();
        return (Process.GetProcessById(ids[0]), Process.GetProcessById(ids[1]), parent.Id);
    }

    private static async Task<string> Batch(string path, bool apply = false, int? parent = null)
    {
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["CLEANUP_TOOL"] = Path.GetFullPath("tools/cleanup-orphan-tasks.bat");
        info.Environment["CLEANUP_TARGET"] = path;
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("& $env:CLEANUP_TOOL --script $env:CLEANUP_TARGET" + (apply ? " --apply" : "") +
            (parent.HasValue ? $" --parent-pid {parent.Value}" : "") + "; exit $LASTEXITCODE");
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var output = await stdout;
        if (process.ExitCode != 0) throw new Exception($"Cleanup exited {process.ExitCode}: {await stderr}\n{output}");
        return output;
    }
}
