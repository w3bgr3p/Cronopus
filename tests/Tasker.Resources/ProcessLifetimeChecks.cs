using System.Diagnostics;

internal static class ProcessLifetimeChecks
{
    internal static async Task Parent()
    {
        using var lifetime = new Cronopus.TaskProcessLifetime();
        using var child = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!, "--root")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true } };
        lifetime.Start(child);
        var leaf = await child.StandardOutput.ReadLineAsync();
        Console.WriteLine($"{child.Id} {leaf}");
        var command = await Console.In.ReadLineAsync();
        if (command == "exit") Environment.Exit(0);
        lifetime.Dispose();
        using var blocked = new Process { StartInfo = child.StartInfo };
        try { lifetime.Start(blocked); throw new Exception("Started a task after disposal"); }
        catch (ObjectDisposedException) { }
    }

    internal static async Task Run()
    {
        using var unrelated = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--leaf")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        await unrelated.StandardOutput.ReadLineAsync();
        try
        {
            foreach (var mode in new[] { "kill", "exit", "dispose" })
                await Check(mode, unrelated);
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(entireProcessTree: true); }
    }

    private static async Task Check(string mode, Process unrelated)
    {
        using var parent = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--lifetime-parent")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardInput = true })!;
        Process? root = null, leaf = null;
        try
        {
            var ready = await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var ids = ready!.Split(' ').Select(int.Parse).ToArray();
            root = Process.GetProcessById(ids[0]);
            leaf = Process.GetProcessById(ids[1]);
            if (mode == "kill") parent.Kill();
            else await parent.StandardInput.WriteLineAsync(mode);
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                await Task.WhenAll(root.WaitForExitAsync(), leaf.WaitForExitAsync())
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException) { throw new Exception($"FAIL {mode}: task root or grandchild survived scheduler exit"); }
            if (unrelated.HasExited) throw new Exception("FAIL unrelated process was terminated");
            Console.WriteLine($"PASS {mode}: root and grandchild terminated; unrelated process alive");
        }
        finally
        {
            if (!parent.HasExited) parent.Kill();
            if (root is { HasExited: false }) root.Kill(entireProcessTree: true);
            if (leaf is { HasExited: false }) leaf.Kill(entireProcessTree: true);
            root?.Dispose(); leaf?.Dispose();
        }
    }
}
