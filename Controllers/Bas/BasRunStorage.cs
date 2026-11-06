namespace Cronopus;

public static class BasRunStorage
{
    public const int RetainedRuns = 30;
    public const long RetainedBytes = 1024L * 1024 * 1024;
    public const long ActiveBytes = 512L * 1024 * 1024;
    public static void Prune(string root)
    {
        var resolved = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(resolved)) return;
        var directories = Directory.EnumerateDirectories(resolved).Select(p => new DirectoryInfo(p))
            .Where(d => Guid.TryParseExact(d.Name, "N", out _) && (d.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(d => d.CreationTimeUtc).ToArray();
        long retained = 0;
        for (var index = 0; index < directories.Length; index++)
        {
            var directory = directories[index]; retained += Size(directory.FullName);
            if (index < RetainedRuns - 1 && retained < RetainedBytes - ActiveBytes) continue;
            var target = Path.GetFullPath(directory.FullName);
            if (!target.StartsWith(resolved, StringComparison.OrdinalIgnoreCase)) throw new IOException("BAS run directory escaped its storage root");
            Directory.Delete(target, true);
        }
    }
    public static void CheckActive(string root)
    {
        if (Size(root) > ActiveBytes) throw new IOException("BAS output exceeded 512 MiB for this execution; its owned processes were stopped.");
    }
    private static long Size(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(root, "*", options).Sum(file => new FileInfo(file).Length);
    }
}
