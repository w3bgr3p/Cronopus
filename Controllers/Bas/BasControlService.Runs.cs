using System.Text.Json;

namespace Cronopus;

public sealed partial class BasControlService
{
    public IReadOnlyList<BasRunView> Run(string id, int count)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var project = Project(id);
            if (_deleting.Contains(id)) throw new BasConflictException("Project is being deleted.");
            if (count < 1 || count > project.MaxThreads) throw new ArgumentException("Requested threads exceed the project limit");
            if (!_runtime.TryGetValue(id, out var runtime))
            {
                runtime = new Runtime { Engine = _factory?.Invoke() ?? new BasControlClient(Path.Combine(_store.Root, "bas", id)) };
                var capture = runtime;
                runtime.Engine.Message += (type, data) => AddEvent(capture, type, data);
                _runtime[id] = runtime;
            }
            if (runtime.Unload != null || runtime.State == "failed") throw new BasConflictException("Unload the engine before running again");
            if (runtime.Runs.Count(r => Active(r.View)) + count > project.MaxThreads) throw new BasConflictException("All BAS threads are busy");
            var added = new List<BasRunView>();
            for (var n = 0; n < count; n++)
            {
                var run = new RunState { View = new(Guid.NewGuid().ToString("N"), Interlocked.Increment(ref _thread), "starting", DateTimeOffset.UtcNow, null, null, "") };
                runtime.Runs.Add(run); added.Add(run.View);
                run.Worker = Task.Run(() => Execute(project, runtime, run));
            }
            while (runtime.Runs.Count > 250 && runtime.Runs.Any(r => !Active(r.View)))
            { var removed = runtime.Runs.First(r => !Active(r.View)); runtime.Runs.Remove(removed); removed.Cancel.Dispose(); }
            return added;
        }
    }
    private async Task Execute(BasProject project, Runtime runtime, RunState run)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(run.Cancel.Token, runtime.Lifetime.Token);
        var ct = linked.Token;
        try
        {
            Task start;
            lock (_gate) { runtime.Start ??= StartEngine(project, runtime); start = runtime.Start; }
            await start.WaitAsync(ct); ct.ThrowIfCancellationRequested();
            lock (_gate) run.View = run.View with { Status = "running" };
            using var parameters = JsonDocument.Parse(project.Parameters);
            var result = await runtime.Engine.RunAsync(parameters.RootElement.Clone(), run.View.ThreadId, ct);
            ct.ThrowIfCancellationRequested();
            lock (_gate) run.View = run.View with { Status = "succeeded", Result = result.Clone(), FinishedAt = DateTimeOffset.UtcNow };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { lock (_gate) run.View = run.View with { Status = "stopped", FinishedAt = DateTimeOffset.UtcNow }; }
        catch (Exception ex)
        {
            var error = Redact(project, ex.Message);
            lock (_gate) run.View = run.View with { Status = "failed", Error = error, FinishedAt = DateTimeOffset.UtcNow };
            AddEvent(runtime, "error", JsonSerializer.SerializeToElement(new { text = error }));
        }
    }
    private async Task StartEngine(BasProject project, Runtime runtime)
    {
        lock (_gate) { if (runtime.Unload == null) runtime.State = "starting"; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(runtime.Lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try { await runtime.Engine.StartAsync(project, timeout.Token); lock (_gate) { if (runtime.Unload == null) runtime.State = "ready"; } }
        catch (Exception ex)
        {
            lock (_gate) { if (runtime.Unload == null) runtime.State = "failed"; runtime.Error = Redact(project, ex.Message); }
            throw;
        }
    }
    private static string Redact(BasProject project, string text)
    {
        using var values = JsonDocument.Parse(project.Parameters);
        foreach (var value in values.RootElement.EnumerateObject())
            if (value.Name.Contains("password", StringComparison.OrdinalIgnoreCase) || value.Name.Contains("token", StringComparison.OrdinalIgnoreCase) || value.Name.Contains("secret", StringComparison.OrdinalIgnoreCase))
                if (value.Value.ToString().Length > 0) text = text.Replace(value.Value.ToString(), "[redacted]", StringComparison.Ordinal);
        return text.Length <= 16384 ? text : text[..16384];
    }
}
