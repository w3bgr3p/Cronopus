using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cronopus;

/// <summary>Owns only external Tasker processes, never the dashboard or updater.</summary>
internal sealed class TaskProcessLifetime : IDisposable
{
    private readonly object _gate = new();
    private SafeFileHandle? _job;
    private bool _disposed;

    public void Start(Process process)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (OperatingSystem.IsWindows()) _job ??= CreateJob();
            process.Start();
            if (_job == null) return;
            if (AssignProcessToJobObject(_job, process.SafeHandle)) return;
            var error = Marshal.GetLastWin32Error();
            // A short command (e.g. echo) may already have completed.
            if (process.HasExited) return;
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new Win32Exception(error, "Cannot protect the Tasker process from surviving dashboard exit");
        }
    }

    private static SafeFileHandle CreateJob()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error);
        }
        var limits = new ExtendedLimits();
        limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) return job;
        var failure = Marshal.GetLastWin32Error();
        job.Dispose();
        throw new Win32Exception(failure);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _job?.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint Priority, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations;
        public ulong ReadBytes, WriteBytes, OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass,
        ref ExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
}
