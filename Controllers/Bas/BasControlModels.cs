using System.Text.Json;

namespace Cronopus;

public sealed class BasProjectEdit
{
    public string Id { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string Parameters { get; set; } = "{}";
    public int MaxThreads { get; set; } = 1;
}

public sealed record BasProject(string Id, string Revision, string Name, string ProjectPath,
    string Parameters, int MaxThreads);
public sealed record BasProjectView(string Id, string Revision, string Name, string ProjectPath,
    string Parameters, int MaxThreads, int Active, string State, string Error);
public sealed record BasRunView(string Id, int ThreadId, string Status, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, JsonElement? Result, string Error);
public sealed record BasEvent(long Cursor, DateTimeOffset Time, string Type, string Text);
public sealed class BasConflictException(string message) : Exception(message);
public sealed class BasFunctionException(string message) : Exception(message);

public interface IBasControlEngine : IAsyncDisposable
{
    event Action<string, JsonElement>? Message;
    Task StartAsync(BasProject project, CancellationToken ct);
    Task<JsonElement> RunAsync(JsonElement parameters, int threadId, CancellationToken ct);
}
