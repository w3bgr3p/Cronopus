using System.Text;
using System.Text.Json;
namespace Cronopus;

public sealed class BridgeFiles
{
    public object Roots() => DriveInfo.GetDrives().Select(d => new { path = d.RootDirectory.FullName, name = d.Name, kind = "directory" }).ToArray();
    public Task<JsonElement> ListAsync(string path, string? cursor, int limit, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Absolute server path required");
        path = Path.GetFullPath(path); limit = Math.Clamp(limit, 1, 500);
        var after = cursor == null ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var names = new SortedSet<string>(comparer);
        foreach (var item in Directory.EnumerateFileSystemEntries(path))
        {
            ct.ThrowIfCancellationRequested(); var name = Path.GetFileName(item);
            if (comparer.Compare(name, after) <= 0) continue;
            names.Add(name); if (names.Count > limit + 1) names.Remove(names.Max!);
        }
        var more = names.Count > limit; if (more) names.Remove(names.Max!);
        var rows = new List<object>();
        foreach (var name in names)
        {
            var full = Path.Combine(path, name);
            try { var attributes = File.GetAttributes(full); var dir = attributes.HasFlag(FileAttributes.Directory);
                rows.Add(new { name, path = full, kind = dir ? "directory" : "file", link = attributes.HasFlag(FileAttributes.ReparsePoint), size = dir ? 0 : new FileInfo(full).Length }); }
            catch (IOException) { }
        }
        return Task.FromResult(JsonSerializer.SerializeToElement(new { path, parent = Directory.GetParent(path)?.FullName, items = rows,
            next_cursor = more && names.Count > 0 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(names.Max!)) : null }));
    }
}
