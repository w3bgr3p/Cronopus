using System.IO.Compression;
using System.Security.Cryptography;

namespace z3nDash;

internal static class UpdatePackage
{
    public static bool IsApplicationFile(string relative)
    {
        var path = relative.Replace('\\', '/');
        var first = path.Split('/')[0];
        if (first.Equals(".runtime", StringComparison.OrdinalIgnoreCase) || new[] { "logs", "reports", "temp", "bridge", "data", "Tasks" }
            .Contains(first, StringComparer.OrdinalIgnoreCase)) return false;
        var name = Path.GetFileName(path);
        return !name.Equals("appsettings.secrets.json", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("ui-state.json", StringComparison.OrdinalIgnoreCase)
            && !new[] { ".sqlite", ".db", ".dat", ".key", ".log", ".jsonl", ".bak" }
                .Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }

    public static async Task StageAsync(string archivePath, string destination, string? digest)
    {
        if (string.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            || digest.Length != 71)
            throw new InvalidDataException("Release has no SHA-256 digest. Update was not installed.");
        using (var stream = File.OpenRead(archivePath))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update SHA-256 mismatch. Update was not installed.");
        }
        using var archive = ZipFile.OpenRead(archivePath);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        // Validate the entire archive before writing any files.
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            var target = Path.GetFullPath(Path.Combine(root, path));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || path.Contains(':')
                || path.Split('/').Any(p => p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
                throw new InvalidDataException("Unsafe path in update archive.");
            if (entry.Name.Length == 0) continue;
            if (!files.Add(path)) throw new InvalidDataException("Duplicate file in update archive.");
            total = checked(total + entry.Length);
            if (total > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Update archive is too large.");
        }
        if (!files.Contains("z3nDash.exe") || !files.Contains("z3nDash.dll")
            || !files.Contains("wwwroot/config.html"))
            throw new InvalidDataException("Incomplete portable update package.");
        foreach (var entry in archive.Entries.Where(e => e.Name.Length != 0 && IsApplicationFile(e.FullName)))
        {
            var target = Path.Combine(root, entry.FullName.Replace('\\', '/'));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            await input.CopyToAsync(output);
        }
    }
}
