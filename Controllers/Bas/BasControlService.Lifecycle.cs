using System.Text.Json;

namespace Cronopus;

public sealed partial class BasControlService
{
    public IReadOnlyList<BasRunView> Runs(string id)
    { lock (_gate) { Project(id); return _runtime.TryGetValue(id, out var runtime) ? runtime.Runs.Select(r => r.View).Reverse().ToArray() : Array.Empty<BasRunView>(); } }
    public IReadOnlyList<BasEvent> Events(string id, long after)
    { lock (_gate) { Project(id); return _runtime.TryGetValue(id, out var runtime) ? runtime.Events.Where(e => e.Cursor > after).ToArray() : Array.Empty<BasEvent>(); } }
    private void AddEvent(Runtime runtime, string type, JsonElement data)
    {
        lock (_gate)
        {
            var text = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("text", out var value) ? value.ToString() : data.GetRawText();
            var id = _runtime.FirstOrDefault(pair => ReferenceEquals(pair.Value, runtime)).Key;
            if (id != null && _projects.TryGetValue(id, out var project)) text = Redact(project, text);
            if (text.Length > 16384) text = text[..16384];
            runtime.Events.Enqueue(new(++runtime.Cursor, DateTimeOffset.UtcNow, type, text));
            while (runtime.Events.Count > 500) runtime.Events.Dequeue();
            if (type == "connection_closed" && runtime.State == "ready")
            { runtime.State = "failed"; runtime.Error = text; }
        }
    }
    public async Task StopAsync(string id, string? runId = null)
    {
        Task[] workers;
        lock (_gate)
        {
            Project(id);
            if (!_runtime.TryGetValue(id, out var runtime)) return;
            var targets = runtime.Runs.Where(r => (runId == null || r.View.Id == runId) && Active(r.View)).ToArray();
            if (runId != null && runtime.Runs.All(r => r.View.Id != runId)) throw new KeyNotFoundException("BAS run not found");
            foreach (var run in targets) { run.View = run.View with { Status = "stopping" }; run.Cancel.Cancel(); }
            workers = targets.Select(r => r.Worker!).ToArray();
        }
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10));
    }
    public Task UnloadAsync(string id)
    {
        Runtime? runtime;
        lock (_gate)
        {
            Project(id);
            if (!_runtime.TryGetValue(id, out runtime)) return Task.CompletedTask;
            if (runtime.Unload != null) return runtime.Unload;
            runtime.State = "unloading"; runtime.Lifetime.Cancel();
            runtime.Unload = Task.Run(() => UnloadRuntime(id, runtime));
            return runtime.Unload;
        }
    }
    private async Task UnloadRuntime(string id, Runtime runtime)
    {
        if (runtime.Start != null) { try { await runtime.Start; } catch { } }
        await Task.WhenAll(runtime.Runs.Select(r => r.Worker!));
        await runtime.Engine.DisposeAsync();
        lock (_gate) { _runtime.Remove(id); foreach (var run in runtime.Runs) run.Cancel.Dispose(); runtime.Lifetime.Dispose(); }
    }
    public async Task DeleteAsync(string id, string revision)
    {
        lock (_gate)
        {
            var project = Project(id);
            if (project.Revision != revision) throw new BasConflictException("Project changed. Reload before deleting.");
            if (_deleting.Contains(id)) throw new BasConflictException("Project is being deleted.");
            if (_runtime.TryGetValue(id, out var runtime) && runtime.Runs.Any(run => Active(run.View)))
                throw new BasConflictException("Stop active runs before deleting this project.");
            // Reserve the project until persistence completes, including the gap after
            // unloading removes its runtime. Other clients cannot run or edit it there.
            _deleting.Add(id);
        }
        try
        {
            await UnloadAsync(id);
            lock (_gate)
            {
                var project = Project(id);
                _projects.Remove(id); try { Persist(); } catch { _projects[id] = project; throw; }
            }
        }
        finally { lock (_gate) _deleting.Remove(id); }
    }
    public async ValueTask DisposeAsync()
    {
        string[] ids;
        lock (_gate) { if (_disposed) return; _disposed = true; ids = _runtime.Keys.ToArray(); }
        foreach (var id in ids)
        {
            Task unload;
            lock (_gate) unload = _projects.ContainsKey(id) ? UnloadAsync(id) : Task.CompletedTask;
            await unload;
        }
    }
}
