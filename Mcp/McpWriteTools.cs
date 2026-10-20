using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace z3nDash.Mcp;

[McpServerToolType]
public sealed class McpWriteTools(McpConnection connection)
{
    [McpServerTool(Name = "run_task", Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Queue real executions now; may bypass deferral. args affects this burst only. Queued does not mean completed. Never blindly retry.")]
    public Task<CallToolResult> Run([Description("Task id")] string id, CancellationToken ct,
        [Description("Number of executions, 1..10000")] int count = 1,
        [Description("Optional nonempty argument override for this burst only")] string? args = null,
        [Description("local or saved connection id")] string node = "local") => McpResults.Execute(async () =>
    {
        McpReadTools.Range(count, 1, 10000);
        if (args != null && string.IsNullOrWhiteSpace(args)) throw new ArgumentException("args must be nonempty when supplied");
        return await Post("run", id, new { id, count, args }, node, ct);
    });

    [McpServerTool(Name = "pause_task", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Pause automatic launches indefinitely; current runs continue. Persists across restarts.")]
    public Task<CallToolResult> Pause([Description("Task id")] string id, CancellationToken ct,
        [Description("local or saved connection id")] string node = "local") => Action("pause", id, node, ct);

    [McpServerTool(Name = "resume_task", Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Clear pause and deferral. Preserve enabled and schedule mode; no forced launch.")]
    public Task<CallToolResult> Resume([Description("Task id")] string id, CancellationToken ct,
        [Description("local or saved connection id")] string node = "local") => Action("resume", id, node, ct);

    [McpServerTool(Name = "defer_task", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Defer automatic/queued launches; current runs continue. Supply delay_seconds XOR until. Cannot shorten an existing deadline.")]
    public Task<CallToolResult> Defer([Description("Task id")] string id, CancellationToken ct,
        [Description("Positive finite number of seconds from now")] double? delay_seconds = null,
        [Description("Future ISO-8601 timestamp with Z or UTC offset")] string? until = null,
        [Description("Optional explanation, at most 2000 characters")] string reason = "",
        [Description("local or saved connection id")] string node = "local") => McpResults.Execute(async () =>
    {
        if ((delay_seconds == null) == (until == null)) throw new ArgumentException("Supply exactly one of delay_seconds or until");
        if (delay_seconds is { } seconds && (!double.IsFinite(seconds) || seconds <= 0)) throw new ArgumentException("delay_seconds must be positive and finite");
        if (reason.Length > 2000) throw new ArgumentException("reason exceeds 2000 characters");
        var body = delay_seconds != null ? (object)new { delay_seconds, reason } : new { until, reason };
        return await Post("defer?id=" + id, id, body, node, ct);
    });

    [McpServerTool(Name = "stop_task", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Kill run_id, or all task instances if omitted. Schedule remains active.")]
    public Task<CallToolResult> Stop([Description("Task id")] string id, CancellationToken ct,
        [Description("Exact runId from list_task_runtime; omit to stop all instances")] string? run_id = null,
        [Description("local or saved connection id")] string node = "local") => McpResults.Execute(async () =>
    {
        if (run_id != null && string.IsNullOrWhiteSpace(run_id)) throw new ArgumentException("run_id must be nonempty");
        return await Post(run_id == null ? "stop" : "kill-instance", id, new { id, runId = run_id }, node, ct);
    });

    [McpServerTool(Name = "clear_task_queue", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete pending queue entries; preserve running instances and schedule.")]
    public Task<CallToolResult> ClearQueue([Description("Task id")] string id, CancellationToken ct,
        [Description("local or saved connection id")] string node = "local") => Action("clear-queue", id, node, ct);

    [McpServerTool(Name = "delete_task", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Delete task and kill its instances; preserve script files. Requires latest _revision from get_task.")]
    public Task<CallToolResult> Delete([Description("Task id")] string id,
        [Description("Latest _revision from get_task")] string revision, CancellationToken ct,
        [Description("local or saved connection id")] string node = "local") => McpResults.Execute(async () =>
    {
        Required(revision, "revision");
        return await Post("delete", id, new { id, _revision = revision }, node, ct);
    });

    private Task<CallToolResult> Action(string route, string id, string node, CancellationToken ct) =>
        McpResults.Execute(async () => await Post(route, id, new { id }, node, ct));
    private Task<JsonElement> Post(string route, string id, object body, string node, CancellationToken ct)
    { TaskerService.ValidateId(id); return connection.Request("POST", McpConnection.TaskPath(route, node), body, ct); }
    internal static void Required(string value, string name)
    { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required"); }

    [McpServerTool(Name = "create_task", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create disabled, schedule-off by default. Requires name/executor/script_path. Enabling a due schedule can run immediately.")]
    public Task<CallToolResult> Create([Description("Typed task settings; JSON fields are objects/arrays")] TaskSettings settings,
        CancellationToken ct, [Description("local or saved connection id")] string node = "local") => McpResults.Execute(async () =>
    {
        Required(settings.name ?? "", "name"); Required(settings.executor ?? "", "executor"); Required(settings.script_path ?? "", "script_path");
        var body = settings.ToWire();
        body.TryAdd("enabled", "false"); body.TryAdd("schedule_mode", "off"); body.TryAdd("max_threads", "1");
        return await connection.Request("POST", McpConnection.TaskPath("save", node), body, ct);
    });

    [McpServerTool(Name = "update_task", Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Patch supplied settings with latest _revision. Reload on conflict. Saving an enabled due schedule can run immediately.")]
    public Task<CallToolResult> Update([Description("Task id")] string id, [Description("Latest _revision from get_task")] string revision,
        [Description("Only fields to change")] TaskSettings settings, CancellationToken ct,
        [Description("local or saved connection id")] string node = "local") => McpResults.Execute(async () =>
    {
        Required(revision, "revision");
        var body = settings.ToWire();
        if (body.Count == 0) throw new ArgumentException("At least one setting is required");
        body["id"] = id; body["_revision"] = revision;
        return await Post("save", id, body, node, ct);
    });
}
