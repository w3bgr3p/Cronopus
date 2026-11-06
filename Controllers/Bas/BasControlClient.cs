using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cronopus;

// Runs saved local BAS XML with the standard unprotected engine. No publication or Premium API.
public sealed partial class BasControlClient(string root, string? preparedEngine = null) : IBasControlEngine
{
    private string? _executable;
    private BasProject? _project;
    private string? _engineVersion;
    private readonly CancellationTokenSource _lifetime = new();
    public event Action<string, JsonElement>? Message;
    private void Log(string type, string text) => Message?.Invoke(type, JsonSerializer.SerializeToElement(new { text }));
    public async Task StartAsync(BasProject project, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("BAS requires Windows");
        var prepared = BasXmlProject.Prepare(project.ProjectPath, project.Parameters);
        if (preparedEngine != null) _executable = preparedEngine;
        else
        {
            var shared = Path.Combine(Path.GetDirectoryName(root)!, "_engines");
            var engine = await new BasEnginePackage(shared).PrepareAsync(prepared.EngineVersion, text => Log("engine", text), ct);
            Log("engine", "Preparing isolated runtime (shared engine binaries)");
            _executable = await Task.Run(() => BasEngineFiles.Prepare(Path.GetDirectoryName(engine)!, Path.Combine(root, "engine", prepared.EngineVersion), ct), ct);
        }
        _project = project;
        _engineVersion = prepared.EngineVersion;
        Log("engine", "Local XML engine ready: " + prepared.EngineVersion);
    }
    public async Task<JsonElement> RunAsync(JsonElement parameters, int threadId, CancellationToken ct)
    {
        var project = _project ?? throw new IOException("BAS engine is not prepared");
        var executable = _executable!;
        var prepared = BasXmlProject.Prepare(project.ProjectPath, parameters.GetRawText());
        if (prepared.EngineVersion != _engineVersion) throw new BasConflictException("XML engine version changed. Release runtime before running this version.");
        var directory = Path.GetDirectoryName(executable)!;
        // Keep source XML and its resources untouched; only the owned engine receives a copy.
        BasRunStorage.Prune(Path.Combine(root, "runs"));
        var logs = Path.Combine(root, "runs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(logs);
        BasProjectContext.Apply(prepared.Document, project.ProjectPath);
        prepared.Document.Save(Path.Combine(directory, "project.xml"));
        // Fresh defaults prevent a previous Actual.xml from overriding this launch's inputs.
        foreach (var name in new[] { "Actual.xml", "Actual.xml.default.xml" })
            if (File.Exists(Path.Combine(directory, name))) File.Delete(Path.Combine(directory, name));
        await File.WriteAllTextAsync(Path.Combine(directory, "settings.ini"),
            "[General]\nDefaultLanguageScript=en\nDefaultLanguageInterface=en\nLogLocation=" + logs.Replace('\\', '/') + "\n", new UTF8Encoding(false), ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        using var ownership = new TaskProcessLifetime();
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add("--silent");
        ownership.Start(process);
        Log("process", "BAS PID " + process.Id + " started · " + project.ProjectPath);
        var offsets = new Dictionary<string, long>(); var nextBudgetCheck = DateTime.UtcNow;
        try
        {
            while (!process.HasExited)
            {
                linked.Token.ThrowIfCancellationRequested(); ReadLogs(logs, offsets);
                if (DateTime.UtcNow >= nextBudgetCheck) { BasRunStorage.CheckActive(logs); nextBudgetCheck = DateTime.UtcNow.AddSeconds(5); }
                await Task.Delay(250, linked.Token);
            }
            await process.WaitForExitAsync(linked.Token); BasRunStorage.CheckActive(logs); ReadLogs(logs, offsets);
            var report = Directory.Exists(Path.Combine(logs, "bas_reports"))
                ? Directory.EnumerateFiles(Path.Combine(logs, "bas_reports"), "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
            if (report == null) throw new BasFunctionException("BAS exited with code " + process.ExitCode + " without an execution report. XML execution is unconfirmed.");
            if (new FileInfo(report).Length > 262144) throw new BasFunctionException("BAS execution report exceeds 256 KiB; inspect its native output files.");
            var text = await File.ReadAllTextAsync(report, linked.Token);
            Log("report", text);
            var successes = Number(text, "Success"); var failures = Number(text, "Fails");
            if (process.ExitCode != 0 || failures > 0 || !Regex.IsMatch(text, @"Final Message\s*:\s*Success"))
                throw new BasFunctionException("BAS execution failed. " + text);
            return JsonSerializer.SerializeToElement(new { processId = process.Id, exitCode = process.ExitCode, successes, failures, report = text, logsDirectory = logs });
        }
        finally
        {
            ownership.Dispose();
            if (!process.HasExited) { try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { } }
        }
    }
    private static int Number(string text, string key)
    { var match = Regex.Match(text, Regex.Escape(key) + @"\((\d+)\)"); return match.Success ? int.Parse(match.Groups[1].Value) : 0; }
    public ValueTask DisposeAsync() { _lifetime.Cancel(); return ValueTask.CompletedTask; }
}
