using System.Runtime.InteropServices;

namespace z3nDash;

public sealed record MachineResources(double CpuPct, long MemoryMB, long TotalMemoryMB);

internal sealed class MachineResourceSampler
{
    private (ulong Idle, ulong Total)? _previous;
    public MachineResources Sample()
    {
        double cpu = -1; long used = 0, totalMemory = 0;
        try
        {
            var current = CpuTimes();
            if (_previous is { } previous && current.Total > previous.Total && current.Idle >= previous.Idle)
                cpu = Math.Round(Math.Clamp(100.0 * (1 - (double)(current.Idle - previous.Idle) / (current.Total - previous.Total)), 0, 100), 1);
            _previous = current;
        }
        catch { _previous = null; }
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (GlobalMemoryStatusEx(ref memory))
                {
                    totalMemory = (long)(memory.TotalPhysical / 1024 / 1024);
                    used = (long)((memory.TotalPhysical - memory.AvailablePhysical) / 1024 / 1024);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                var values = File.ReadLines("/proc/meminfo").Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .ToDictionary(parts => parts[0].TrimEnd(':'), parts => long.Parse(parts[1]));
                totalMemory = values["MemTotal"] / 1024;
                used = (values["MemTotal"] - values.GetValueOrDefault("MemAvailable", values["MemFree"])) / 1024;
            }
        }
        catch { used = totalMemory = 0; }
        return new MachineResources(cpu, used, totalMemory);
    }
    private static (ulong Idle, ulong Total) CpuTimes()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) throw new IOException("CPU counters unavailable");
            return (idle, kernel + user);
        }
        if (OperatingSystem.IsLinux())
        {
            // Guest counters are already included in user/nice; count only the first eight fields.
            var times = File.ReadLines("/proc/stat").First(line => line.StartsWith("cpu "))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(ulong.Parse).ToArray();
            return (times[3] + times[4], times.Aggregate(0UL, (sum, time) => sum + time));
        }
        throw new PlatformNotSupportedException();
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
}
