using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace z3nDash;

public static class BridgeUploadPaths
{
    public static string Resolve(string root, string relative)
    {
        if (!Path.IsPathFullyQualified(root) || Path.IsPathRooted(relative)) throw new ArgumentException("Invalid destination path");
        var segments = relative.Replace('\\', '/').Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.EndsWith('.') || s.EndsWith(' ') || s.Contains(':') || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            System.Text.RegularExpressions.Regex.IsMatch(s, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))) throw new ArgumentException("Invalid relative file path");
        var result = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!result.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new ArgumentException("Path escapes destination");
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(result)!); directory != null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException("Upload through a directory link is not allowed");
        if (File.Exists(result) && File.GetAttributes(result).HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException("Cannot replace a link");
        return result;
    }
    public static IDisposable LockParents(string file) => new DirectoryLease(Path.GetDirectoryName(file)!);
    public static void EnsureDirectory(string root,string relative)
    {
        var current="";
        foreach(var part in relative.Replace('\\','/').Split('/')){
            current=current.Length==0?part:current+"/"+part;
            var path=Resolve(root,current);
            using var lease=LockParents(path);
            if(!Directory.Exists(path))Directory.CreateDirectory(path);
            Resolve(root,current+"/.check");
        }
    }
    private sealed class DirectoryLease : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = new();
        public DirectoryLease(string path)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Safe file publication requires Windows directory handles on this build");
            try {
                var chain = new Stack<string>(); for (var d = new DirectoryInfo(path); d != null; d = d.Parent) chain.Push(d.FullName);
                while (chain.Count > 0) {
                    var directory = chain.Pop(); var handle = CreateFileW(directory, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                    if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
                    _handles.Add(handle);
                    if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException("Directory became a link");
                }
            } catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (var handle in _handles) handle.Dispose(); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
