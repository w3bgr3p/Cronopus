using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cronopus;

public sealed partial class BasControlService : IAsyncDisposable
{
    private sealed class RunState
    {
        public required BasRunView View;
        public CancellationTokenSource Cancel = new();
        public Task? Worker;
    }
    private sealed class Runtime
    {
        public required IBasControlEngine Engine;
        public Task? Start;
        public Task? Unload;
        public CancellationTokenSource Lifetime = new();
        public string State = "idle", Error = "";
        public List<RunState> Runs = new();
        public Queue<BasEvent> Events = new();
        public long Cursor;
    }
    private readonly BridgeStore _store;
    private readonly Func<IBasControlEngine>? _factory;
    private readonly object _gate = new();
    private readonly Dictionary<string, BasProject> _projects;
    private readonly Dictionary<string, Runtime> _runtime = new();
    private readonly HashSet<string> _deleting = new();
    private int _thread;
    private bool _disposed;
    public BasControlService(BridgeStore store, Func<IBasControlEngine>? factory = null)
    {
        _store = store; _factory = factory;
        var saved = File.Exists(Path.Combine(store.Root, "bas-projects.bin"))
            ? store.ReadPrivate<List<BasProject>>("bas-projects.bin") : new();
        _projects = saved.ToDictionary(p => p.Id);
    }
    public IReadOnlyList<BasProjectView> List()
    {
        lock (_gate) return _projects.Values.Select(p =>
        {
            _runtime.TryGetValue(p.Id, out var runtime);
            return new BasProjectView(p.Id, p.Revision, p.Name, p.ProjectPath, p.Parameters, p.MaxThreads,
                runtime?.Runs.Count(r => Active(r.View)) ?? 0,
                _deleting.Contains(p.Id) ? "deleting" : runtime?.State ?? "idle", runtime?.Error ?? "");
        }).ToArray();
    }
    public BasProjectView Save(BasProjectEdit input)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            BasProject? old = null;
            if (input.Id.Length > 0)
            {
                old = Project(input.Id);
                if (input.Revision != old.Revision) throw new BasConflictException("Project changed. Reload before saving.");
                if (_deleting.Contains(input.Id)) throw new BasConflictException("Project is being deleted.");
                if (_runtime.ContainsKey(input.Id)) throw new BasConflictException("Unload the engine before editing this project.");
            }
            if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.MaxThreads != 1 || input.Parameters.Length > 262144)
                throw new ArgumentException("Name and a single active XML execution per project are required");
            BasXmlProject.Prepare(input.ProjectPath, input.Parameters);
            using var parameters = JsonDocument.Parse(input.Parameters);
            if (parameters.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Parameters must be a JSON object");
            var id = old?.Id ?? Guid.NewGuid().ToString("N");
            var project = new BasProject(id, Guid.NewGuid().ToString("N"), input.Name.Trim(), input.ProjectPath,
                input.Parameters, 1);
            _projects[id] = project;
            try { Persist(); } catch { if (old == null) _projects.Remove(id); else _projects[id] = old; throw; }
            return List().Single(p => p.Id == id);
        }
    }
    private BasProject Project(string id) => _projects.TryGetValue(id, out var project) ? project : throw new KeyNotFoundException("BAS project not found");
    private void Persist() => _store.SavePrivate("bas-projects.bin", _projects.Values.ToList());
    private static bool Active(BasRunView run) => run.Status is "starting" or "running" or "stopping";
}
