using System.Security.Cryptography;
using System.Text.Json;

namespace Cronopus;

/// <summary>Read-only, bounded inspection of reference-relative paths, including binary files.</summary>
public static class BridgeFileHashes
{
    public static async Task<object> Inspect(string root, string[] paths, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(root) || paths.Length > 64 ||
            paths.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 4096) ||
            paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("Absolute root and at most 64 relative paths required");
        root = Path.GetFullPath(root);
        // Validate the whole batch before touching files; Resolve also rejects directory links.
        var resolved = paths.Select(p => BridgeUploadPaths.Resolve(root, p)).ToArray();
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists) throw new ArgumentException("Server reference folder is unavailable: " + root);
        for (var d = rootInfo; d != null; d = d.Parent)
            if (d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("Reference folder must not traverse links");
        var items = new List<object>();
        for (var i = 0; i < paths.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var relative = paths[i].Replace('\\', '/');
            try
            {
                var before = new FileInfo(resolved[i]);
                await using var stream = new FileStream(resolved[i], FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var size = before.Length;
                var modified = before.LastWriteTimeUtc;
                var created = before.CreationTimeUtc;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                before.Refresh();
                if (before.Length != size || before.LastWriteTimeUtc != modified || before.CreationTimeUtc != created)
                    throw new IOException("File changed during inspection; retry");
                items.Add(new { path = relative, status = "ok", size, modified_utc = modified, sha256 = hash });
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            { items.Add(new { path = relative, status = "missing" }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { items.Add(new { path = relative, status = "error", error = ex.Message }); }
        }
        return new { root, items };
    }

    public static Task<object> Inspect(JsonElement body, CancellationToken ct) =>
        Inspect(body.GetProperty("root").GetString() ?? "", body.GetProperty("paths").Deserialize<string[]>()
            ?? throw new ArgumentException("paths required"), ct);
}
