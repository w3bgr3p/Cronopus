using System.Diagnostics;
using Cronopus;

static Process Start(string mode) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!, mode)
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;

if (args.Contains("--lifetime-check"))
{
    await ProcessLifetimeChecks.Run();
    return;
}
if (args.Contains("--cleanup-check"))
{
    await CleanupChecks.Run();
    return;
}
if (args.Contains("--cleanup-parent"))
{
    using var worker = CleanupChecks.Worker(args[1]);
    Console.WriteLine(await worker.StandardOutput.ReadLineAsync());
    return;
}
if (args.Contains("--lifetime-parent"))
{
    await ProcessLifetimeChecks.Parent();
    return;
}

if (args.Contains("--benchmark"))
{
    using var self = Process.GetCurrentProcess();
    using var root = Start("--root");
    await root.StandardOutput.ReadLineAsync();
    try
    {
        foreach (var active in new[] { false, true })
        {
            var wall = Stopwatch.StartNew();
            var cpu = self.TotalProcessorTime;
            for (var i = 0; i < 5; i++)
            {
                var probe = new SchedulerService();
                if (active) probe.Track("bench:one", root.Id);
                var result = probe.GetResources();
                if (active) Check(result.Processes >= 2, "benchmark includes child");
            }
            var sampleMs = wall.Elapsed.TotalMilliseconds / 5;
            Console.WriteLine($"BENCH active={active}: wall={sampleMs:F1} ms/sample, self CPU={(self.TotalProcessorTime - cpu).TotalMilliseconds / 5:F1} ms/sample");
            if (!active) Check(sampleMs < 50, "idle resource sampling stays below 50 ms/sample");
        }
    }
    finally { if (!root.HasExited) root.Kill(entireProcessTree: true); }
    return;
}

if (args.Contains("--cpu"))
{
    Console.WriteLine("CPU worker ready");
    var duration = Stopwatch.StartNew();
    Parallel.For(0, Math.Min(4, Environment.ProcessorCount), _ => { while (duration.Elapsed.TotalSeconds < 10) Thread.SpinWait(10000); });
    return;
}
if (args.Length > 0)
{
    Process? child = args[0] == "--root" ? Start("--leaf") : null;
    if (child != null) await child.StandardOutput.ReadLineAsync();
    var allocation = new byte[(child == null ? 96 : 24) * 1024 * 1024];
    for (int i = 0; i < allocation.Length; i += 4096) allocation[i] = 1;
    Console.WriteLine(child?.Id ?? Environment.ProcessId);
    await Task.Delay(TimeSpan.FromMinutes(2));
    GC.KeepAlive(allocation);
    return;
}

using var first = Start("--root");
using var second = Start("--root");
try
{
    var leaf1 = int.Parse((await first.StandardOutput.ReadLineAsync())!);
    var leaf2 = int.Parse((await second.StandardOutput.ReadLineAsync())!);
    var scheduler = new SchedulerService();
    scheduler.Track("task:one", first.Id);
    scheduler.Track("task:two", second.Id);
    scheduler.Track("xml:in-process", -1);
    var snapshot = scheduler.GetResources();
    var runs = snapshot.InstanceDetails["task"];
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(snapshot));
    Console.WriteLine($"Roots {first.Id}/{second.Id}; leaves {leaf1}/{leaf2}");
    Check(runs.Count == 2, "two instances");
    Check(runs.All(r => r.Processes >= 2), "each root includes its child (and Windows console hosts)");
    Check(runs.All(r => r.MemoryMB > 120), "child allocation is included");
    Check(snapshot.Tasks["task"].MemoryMB == runs.Sum(r => r.MemoryMB), "task equals sum of instances");
    Check(snapshot.MemoryMB == runs.Sum(r => r.MemoryMB), "global equals sum of tasks");
    Check(snapshot.Processes == runs.Sum(r => r.Processes)
        && snapshot.Tasks["task"].Processes == snapshot.Processes, "process counts agree");
    Check(snapshot.InstanceDetails["xml"][0] is { InProcess: true, MemoryMB: 0, Processes: 0 },
        "in-process memory is not duplicated");
    foreach (var run in runs)
        Console.WriteLine($"PASS {run.RunId}: {run.MemoryMB} MB, {run.Processes} processes");
    Console.WriteLine($"PASS total: {snapshot.MemoryMB} MB, {snapshot.Processes} processes; XML shared");
    Check(snapshot.Machine is { TotalMemoryMB: > 0, MemoryMB: > 0, CpuPct: -1 }, "machine RAM and CPU warmup");
    using var cpuWorker = Start("--cpu");
    try
    {
        await cpuWorker.StandardOutput.ReadLineAsync();
        await Task.Delay(3300);
        var sampled = scheduler.GetResources();
        Check(sampled.Machine is { CpuPct: >= 0 and <= 100 }, "machine CPU sample");
        Check(sampled.Machine!.CpuPct > sampled.CpuPct, "machine CPU includes load outside Tasker roots");
        var idle = new SchedulerService().GetResources();
        Check(idle.Instances == 0 && idle.Machine is { TotalMemoryMB: > 0, MemoryMB: > 0 }, "machine RAM available with no tasks");
        Console.WriteLine($"PASS machine CPU {sampled.Machine.CpuPct}% vs tasks {sampled.CpuPct}%; RAM {sampled.Machine.MemoryMB}/{sampled.Machine.TotalMemoryMB} MB");
    }
    finally { if (!cpuWorker.HasExited) cpuWorker.Kill(entireProcessTree: true); }
}
finally
{
    if (!first.HasExited) first.Kill(entireProcessTree: true);
    if (!second.HasExited) second.Kill(entireProcessTree: true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL " + message);
}

namespace Cronopus
{
    // Use the production sampler with controlled live roots, without starting the scheduler or DB.
    public sealed partial class SchedulerService
    {
        private readonly Dictionary<string, TestRun> _running = new();
        public void Track(string key, int pid) => _running[key] = new(pid);
        private sealed record TestRun(int Pid)
        {
            public bool HasExited => false;
            public long UptimeSec => 1;
        }
    }
}
