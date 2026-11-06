using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cronopus;

public sealed class BasEnginePackage(string root, HttpClient? httpClient = null)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly HttpClient _http = httpClient ?? Http;
    public async Task<string> PrepareAsync(string version, Action<string> progress, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(root), _ => new(1, 1));
        await gate.WaitAsync(ct);
        try { return await PrepareCoreAsync(version, progress, ct); }
        finally { gate.Release(); }
    }
    private async Task<string> PrepareCoreAsync(string version, Action<string> progress, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("BAS engines require Windows");
        if (!Version.TryParse(version, out var parsed) || parsed < new Version(22, 4, 2))
            throw new ArgumentException("Unsupported BAS engine version");
        var executableRoot = Path.Combine(root, "engine", version);
        var executable = Path.Combine(executableRoot, "FastExecuteScript.exe");
        if (File.Exists(executable)) return executable;
        var arch = Environment.Is64BitOperatingSystem ? "64" : "32";
        using var metadata = await ReadJson($"https://bablosoft.com/distr/FastExecuteScript{arch}/{version}/FastExecuteScript.x{arch}.zip.meta.json", ct);
        var url = new Uri(metadata.RootElement.GetProperty("Url").GetString()!);
        // Publisher metadata still advertises HTTP; the official endpoint also serves HTTPS.
        if (url.Scheme == "http" && url.IsDefaultPort && url.UserInfo.Length == 0
            && (url.Host == "downloads.bablosoft.com" || url.Host == "bablosoft.com"))
            url = new UriBuilder(url) { Scheme = "https", Port = -1 }.Uri;
        if (url.Scheme != "https") throw new IOException("BAS download must use HTTPS");
        var checksum = metadata.RootElement.GetProperty("Checksum").GetString()!;
        var cache = Path.Combine(root, "cache", version); Directory.CreateDirectory(cache);
        var archive = Path.Combine(cache, $"engine{arch}.zip");
        if (!File.Exists(archive) || !await Matches(archive, checksum, ct))
        {
            progress("Downloading BAS engine " + version);
            var temp = archive + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using (var output = File.Create(temp)) await response.Content.CopyToAsync(output, ct);
                if (!await Matches(temp, checksum, ct)) throw new IOException("BAS engine checksum mismatch");
                File.Move(temp, archive, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        progress("Extracting BAS engine " + version);
        var staging = executableRoot + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            ZipFile.ExtractToDirectory(archive, staging);
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(Path.Combine(staging, "FastExecuteScript.exe"))) throw new IOException("BAS archive has no executable");
            Directory.CreateDirectory(Path.GetDirectoryName(executableRoot)!);
            Directory.Move(staging, executableRoot);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        return executable;
    }
    private async Task<JsonDocument> ReadJson(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct); response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    private static async Task<bool> Matches(string file, string checksum, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA1.HashDataAsync(stream, ct)).Equals(checksum, StringComparison.OrdinalIgnoreCase);
    }
}
