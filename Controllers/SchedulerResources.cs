using System.Diagnostics;
#if WINDOWS
using System.Management;
#endif

namespace Cronopus;

/// <summary>
/// Суммарное потребление памяти задачами, запущенными оркестратором:
/// по каждой задаче и по всем сразу. Считается по дереву процессов каждого
/// инстанса (python и его дочерние — браузеры, прокси-хелперы).
/// </summary>
public sealed partial class SchedulerService
{
    /// <summary>CpuPct — доля всей машины (как в диспетчере задач); -1, пока нет второго снимка.</summary>
    public sealed record TaskResources(int Instances, int Processes, long MemoryMB, double CpuPct);
    public sealed record InstanceResources(string RunId, int Pid, long UptimeSec,
        int Processes, long MemoryMB, double CpuPct, bool InProcess);

    public sealed record ResourceSnapshot(
        Dictionary<string, TaskResources> Tasks,
        int  Instances,
        int  Processes,
        long MemoryMB,
        double CpuPct,
        int  InProcessRuns,
        long MasterMB)
    {
        public Dictionary<string, List<InstanceResources>> InstanceDetails { get; init; } = new();
        public MachineResources? Machine { get; init; }
    }

    private readonly object _resLock = new();
    private ResourceSnapshot? _resCache;
    private DateTime _resCachedAt;
    private Dictionary<int, ulong>? _prevCpu;
    private DateTime _prevCpuAt;
    private readonly MachineResourceSampler _machineResources = new();

    /// <summary>
    /// Снимок берётся не чаще раза в 3 секунды: WMI-запрос по всем процессам
    /// дорог, а UI опрашивает его из нескольких вкладок.
    /// </summary>
    public ResourceSnapshot GetResources()
    {
        lock (_resLock)
        {
            if (_resCache != null && (DateTime.UtcNow - _resCachedAt).TotalSeconds < 3)
                return _resCache;
            _resCache   = BuildResources();
            _resCachedAt = DateTime.UtcNow;
            return _resCache;
        }
    }

    private ResourceSnapshot BuildResources()
    {
        // instanceKey = "<taskId>:<runId>"; остальной код сервиса режет по
        // id + ":", поэтому id задачи двоеточия не содержит.
        var live = _running
            .Where(kv => !kv.Value.HasExited)
            .Select(kv =>
            {
                var cut = kv.Key.IndexOf(':');
                return (TaskId: cut > 0 ? kv.Key[..cut] : kv.Key,
                    RunId: cut > 0 ? kv.Key[(cut + 1)..] : "",
                    Pid: kv.Value.Pid, UptimeSec: kv.Value.UptimeSec);
            })
            .ToList();

        // No external roots means there is no process tree to measure. Keep
        // sampling machine counters below, but avoid a machine-wide WMI query.
        var table = live.Any(run => run.Pid > 0)
            ? SnapshotProcessTable()
            : new ProcessTable(new(), new(), new());
        var now   = DateTime.UtcNow;

        // Дельта процессорного времени между двумя снимками. Процесс, которого
        // не было в прошлом снимке, начался внутри окна — его время целиком.
        var prev    = _prevCpu;
        var elapsed = (now - _prevCpuAt).TotalSeconds;
        bool cpuOk  = prev != null && elapsed > 0.5 && table.Cpu.Count > 0;
        double Cpu(int pid)
        {
            if (!cpuOk || !table.Cpu.TryGetValue(pid, out var cur)) return 0;
            prev!.TryGetValue(pid, out var old);
            return cur >= old ? (cur - old) / 1e7 : 0;   // 100-нс тики → секунды
        }

        var tasks = new Dictionary<string, (int inst, int procs, long memoryMB, double cpuSec)>();
        var counted  = new HashSet<int>();   // процесс не должен попасть в сумму дважды
        var details = new Dictionary<string, List<InstanceResources>>();
        int inProc   = 0;

        double Pct(double cpuSec) => cpuOk
            ? Math.Round(cpuSec / elapsed / Environment.ProcessorCount * 100, 1)
            : -1;

        foreach (var (taskId, runId, pid, uptimeSec) in live)
        {
            tasks.TryGetValue(taskId, out var acc);
            acc.inst++;

            // Прогон без внешнего процесса (xml-шаблон) идёт внутри Master:
            // его память не отделить от памяти самого приложения.
            if (pid <= 0) inProc++;
            long instanceBytes = 0;
            int instanceProcesses = 0;
            double instanceCpu = 0;

            foreach (var p in pid > 0 ? WalkTree(pid, table) : Enumerable.Empty<int>())
            {
                if (!counted.Add(p)) continue;
                if (!table.Memory.TryGetValue(p, out var bytes)) continue;
                instanceProcesses++;
                instanceBytes += bytes;
                instanceCpu += Cpu(p);
            }
            if (!details.TryGetValue(taskId, out var instances))
                details[taskId] = instances = new();
            var instanceMB = instanceBytes / 1024 / 1024;
            instances.Add(new InstanceResources(runId, pid, uptimeSec, instanceProcesses,
                instanceMB, Pct(instanceCpu), pid <= 0));
            acc.procs += instanceProcesses;
            // Итоги складываются из тех же MB, которые показываются у инстансов.
            acc.memoryMB += instanceMB;
            acc.cpuSec += instanceCpu;
            tasks[taskId] = acc;
        }

        var perTask = tasks.ToDictionary(
            kv => kv.Key,
            kv => new TaskResources(kv.Value.inst, kv.Value.procs, kv.Value.memoryMB, Pct(kv.Value.cpuSec)));

        _prevCpu   = table.Cpu;
        _prevCpuAt = now;

        long master = 0;
        try
        {
            using var me = Process.GetCurrentProcess();
            master = me.WorkingSet64 / 1024 / 1024;
        }
        catch { }

        return new ResourceSnapshot(
            perTask,
            live.Count,
            tasks.Values.Sum(t => t.procs),
            tasks.Values.Sum(t => t.memoryMB),
            Pct(tasks.Values.Sum(t => t.cpuSec)),
            inProc,
            master) { InstanceDetails = details, Machine = _machineResources.Sample() };
    }

    private sealed record ProcessTable(
        Dictionary<int, List<int>> Children,
        Dictionary<int, long>      Memory,
        Dictionary<int, ulong>     Cpu);

    private static ProcessTable SnapshotProcessTable()
    {
        var children = new Dictionary<int, List<int>>();
        var memory   = new Dictionary<int, long>();
        var cpu      = new Dictionary<int, ulong>();

#if WINDOWS
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, WorkingSetSize, UserModeTime, KernelModeTime FROM Win32_Process");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var id     = Convert.ToInt32((uint)item["ProcessId"]);
                    var parent = Convert.ToInt32((uint)item["ParentProcessId"]);
                    memory[id] = item["WorkingSetSize"] is ulong ws ? (long)ws : 0;
                    cpu[id]    = (item["UserModeTime"] is ulong um ? um : 0)
                               + (item["KernelModeTime"] is ulong km ? km : 0);
                    if (!children.TryGetValue(parent, out var list))
                        children[parent] = list = new List<int>();
                    list.Add(id);
                }
            }
            return new ProcessTable(children, memory, cpu);
        }
        catch { }
#endif

        // Без WMI дерево недоступно: считаем только корневые процессы инстансов.
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try { memory[p.Id] = p.WorkingSet64; } catch { }
                }
            }
        }
        catch { }
        return new ProcessTable(children, memory, cpu);
    }

    private static IEnumerable<int> WalkTree(int root, ProcessTable table)
    {
        var seen    = new HashSet<int>();
        var pending = new Queue<int>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!seen.Add(id)) continue;
            yield return id;
            if (table.Children.TryGetValue(id, out var kids))
                foreach (var k in kids) pending.Enqueue(k);
        }
    }
}
