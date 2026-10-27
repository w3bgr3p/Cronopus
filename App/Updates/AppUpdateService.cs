using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cronopus;

internal sealed class AppUpdateService
{
    internal const string RepositoryUrl = "https://github.com/w3bgr3p/Cronopus";
    private static readonly HttpClient Http = CreateClient();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly string _root;
    private readonly string _pending;
    private string _phase = "idle";
    private string? _error;
    private string? _latest;
    private DateTime? _checkedAt;
    private long _downloaded;
    private long _total;
    private bool _automatic;
    private Release? _release;
    public static string CurrentVersion => typeof(AppUpdateService).Assembly.GetName().Version!.ToString();

    public AppUpdateService()
    {
        _root = UpdateRoot(AppContext.BaseDirectory);
        Directory.CreateDirectory(_root);
        _pending = Path.Combine(_root, "pending.json");
        var settings = Path.Combine(_root, "settings.json");
        try { _automatic = !File.Exists(settings) || JsonSerializer.Deserialize<bool>(File.ReadAllText(settings)); }
        catch { _automatic = true; }
    }

    internal static string UpdateRoot(string installation)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(installation).TrimEnd('\\', '/').ToUpperInvariant())))[..16];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cronopus", "updates", id);
    }

    public object Status()
    {
        PendingUpdate? pending = null;
        try { if (File.Exists(_pending)) pending = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(_pending)); }
        catch (Exception ex) { _error = ex.Message; }
        var errorPath = Path.Combine(_root, "apply-error.txt");
        string? applyError = null;
        try { if (File.Exists(errorPath)) applyError = File.ReadAllText(errorPath); } catch { }
        return new { currentVersion = CurrentVersion, latestVersion = _latest, phase = _phase,
            automatic = _automatic, checkedAt = _checkedAt, error = _error, applyError,
            downloadedBytes = Interlocked.Read(ref _downloaded), totalBytes = Interlocked.Read(ref _total),
            pendingVersion = pending?.Version, restartRequired = pending != null,
            releaseUrl = _release?.Url ?? RepositoryUrl + "/releases/latest", supported = OperatingSystem.IsWindows() };
    }

    public async Task SetAutomaticAsync(bool enabled)
    {
        await _settingsGate.WaitAsync();
        try
        {
            var path = Path.Combine(_root, "settings.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(enabled));
            File.Move(path + ".tmp", path, true);
            _automatic = enabled;
        }
        finally { _settingsGate.Release(); }
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            while (true)
            {
                if (_automatic) await RunAsync(install: true);
                await Task.Delay(TimeSpan.FromHours(6));
            }
        });
    }

    public async Task RunAsync(bool install)
    {
        if (!OperatingSystem.IsWindows() || !await _gate.WaitAsync(0)) return;
        string? work = null;
        try
        {
            _error = null;
            _phase = "checking";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response = await Http.GetAsync("https://api.github.com/repos/w3bgr3p/Cronopus/releases/latest", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            _release = ReadRelease(json.RootElement);
            _latest = _release.Version;
            _checkedAt = DateTime.UtcNow;
            if (Version.Parse(_latest) <= Version.Parse(CurrentVersion)) { _phase = "current"; return; }
            _phase = "available";
            if (!install || File.Exists(_pending)) return;
            work = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            _phase = "downloading";
            Interlocked.Exchange(ref _downloaded, 0);
            Interlocked.Exchange(ref _total, _release.Size);
            var archive = Path.Combine(work, "package.zip");
            using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            using (var download = await Http.GetAsync(_release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, downloadTimeout.Token))
            {
                download.EnsureSuccessStatusCode();
                using var input = await download.Content.ReadAsStreamAsync();
                using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write);
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, downloadTimeout.Token)) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), downloadTimeout.Token);
                    if (Interlocked.Add(ref _downloaded, count) > _release.Size)
                        throw new InvalidDataException("Download exceeds the release asset size.");
                }
            }
            if (_downloaded != _release.Size) throw new InvalidDataException("Incomplete update download.");
            _phase = "verifying";
            var payload = Path.Combine(work, "payload");
            await UpdatePackage.StageAsync(archive, payload, _release.Digest);
            File.Delete(archive);
            await File.WriteAllTextAsync(_pending + ".tmp", JsonSerializer.Serialize(new PendingUpdate(_latest, payload)));
            File.Move(_pending + ".tmp", _pending, true);
            work = null; // Published payload must survive until the next manual start.
            _phase = "ready";
        }
        catch (Exception ex) { _phase = "error"; _error = ex.Message; }
        finally
        {
            if (work != null) { try { Directory.Delete(work, true); } catch { } }
            _gate.Release();
        }
    }

    private static Release ReadRelease(JsonElement json)
    {
        if (json.GetProperty("draft").GetBoolean() || json.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("Only stable releases can be installed.");
        var version = Version.Parse(json.GetProperty("tag_name").GetString()!.TrimStart('v', 'V')).ToString();
        var asset = json.GetProperty("assets").EnumerateArray().Single(a =>
            a.GetProperty("name").GetString() == $"Cronopus_{version}_portable.zip");
        var url = asset.GetProperty("browser_download_url").GetString()!;
        if (!url.StartsWith(RepositoryUrl + "/releases/download/", StringComparison.Ordinal)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidDataException("Unexpected release download URL.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > 1024L * 1024 * 1024) throw new InvalidDataException("Invalid release asset size.");
        return new Release(version, RepositoryUrl + "/releases/tag/" + Uri.EscapeDataString(json.GetProperty("tag_name").GetString()!),
            url, asset.GetProperty("digest").GetString() ?? "", size);
    }

    public static bool TryApplyOnStartup(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Contains("--skip-update-once", StringComparer.OrdinalIgnoreCase)) return false;
        var root = UpdateRoot(AppContext.BaseDirectory);
        var pending = Path.Combine(root, "pending.json");
        if (!File.Exists(pending)) return false;
        try
        {
            var update = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(pending))!;
            if (Version.Parse(update.Version) <= Version.Parse(CurrentVersion))
            {
                File.Delete(pending);
                return false;
            }
            var payload = Path.GetFullPath(update.Directory);
            if (!payload.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(Path.Combine(payload, "Cronopus.exe")))
                throw new InvalidDataException("Pending update payload is missing or invalid.");
            var attempt = Guid.NewGuid().ToString("N");
            var script = Path.Combine(root, "apply-" + attempt + ".ps1");
            using var resource = typeof(AppUpdateService).Assembly.GetManifestResourceStream("Cronopus.apply-update.ps1")!;
            using (var output = File.Create(script)) resource.CopyTo(output);
            var request = Path.Combine(root, "request-" + attempt + ".json");
            File.WriteAllText(request, JsonSerializer.Serialize(new {
                ParentId = Environment.ProcessId, Target = AppContext.BaseDirectory, Payload = payload,
                Root = root, Executable = Environment.ProcessPath,
                Arguments = string.Join(" ", args.Append("--skip-update-once").Select(QuoteArgument)) }));
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Request", request })
                start.ArgumentList.Add(argument);
            using var helper = Process.Start(start) ?? throw new IOException("Cannot start the update helper.");
            return true; // Caller exits before configuration, DB, UI or scheduler startup.
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(root, "apply-error.txt"), ex.Message); } catch { }
            return false;
        }
    }

    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Cronopus-Updater/1.0");
        return client;
    }
    private sealed record Release(string Version, string Url, string DownloadUrl, string Digest, long Size);
    internal sealed record PendingUpdate(string Version, string Directory);
}
