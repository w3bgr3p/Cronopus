using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Cronopus;

public static class BasEngineFiles
{
    private const string LayoutVersion = "2";
    private static readonly HashSet<string> LinkExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll", ".pak", ".dat", ".bin", ".node", ".blob" };
    public static string Prepare(string source, string destination, CancellationToken ct)
    {
        var marker = Path.Combine(destination, ".cronopus-ready");
        if (File.Exists(marker))
        {
            if (File.ReadAllText(marker) != LayoutVersion)
            {
                // v1 copied files only. Missing empty service directories make native
                // browser JavaScript return an empty string despite successful navigation.
                PrepareDirectories(source, destination, ct);
                File.WriteAllText(marker, LayoutVersion);
            }
            return Path.Combine(destination, "FastExecuteScript.exe");
        }
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(staging);
            PrepareDirectories(source, staging, ct);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (LinkExtensions.Contains(Path.GetExtension(file)) && OperatingSystem.IsWindows() && CreateHardLink(target, file, IntPtr.Zero)) continue;
                File.Copy(file, target);
            }
            File.WriteAllText(Path.Combine(staging, ".cronopus-ready"), LayoutVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            return Path.Combine(destination, "FastExecuteScript.exe");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    private static void PrepareDirectories(string source, string destination, CancellationToken ct)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string path, string existing, IntPtr security);
}
