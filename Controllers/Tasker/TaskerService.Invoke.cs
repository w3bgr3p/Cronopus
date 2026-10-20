using System.Text.Json;
namespace z3nDash;

public sealed partial class TaskerService
{
    public static readonly HashSet<string> RemoteRoutes = new(StringComparer.Ordinal) {
        "GET /tasker/list", "GET /tasker/detail", "POST /tasker/save", "POST /tasker/delete", "POST /tasker/run",
        "POST /tasker/stop", "POST /tasker/pause", "POST /tasker/resume", "POST /tasker/defer",
        "POST /tasker/reset-attempts",
        "GET /tasker/payload", "POST /tasker/payload", "GET /tasker/instances", "GET /tasker/queue",
        "GET /tasker/process-stats", "GET /tasker/resources", "POST /tasker/kill-instance",
        "POST /tasker/clear-queue", "GET /tasker/scan-folder", "GET /tasker/package-scripts",
        "GET /tasker/config-file", "POST /tasker/config-file", "GET /tasker/script-params",
        "POST /tasker/args-preview", "POST /tasker/schedule-preview", "POST /tasker/build",
        "POST /tasker/ensure-venv", "GET /tasker/install/stream"
    };
    public async Task<TaskerReply> InvokeAsync(string method, string path, JsonElement? body, CancellationToken ct,Action<string>? progress=null)
    {
        ct.ThrowIfCancellationRequested();
        var clean = path.Split('?')[0];
        if (!RemoteRoutes.Contains(method + " " + clean)) return new(404, "{\"error\":\"Unsupported operation\"}", "application/json");
        var context = new TaskerExchange(method, path, body);
        context.Cancellation=ct;context.Response.Progress=progress;
        var id = context.Request.QueryString["id"];
        if (!string.IsNullOrEmpty(id)) ValidateId(id);
        if (body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("id", out var bid) && !string.IsNullOrEmpty(bid.GetString())) ValidateId(bid.GetString()!);
        await HandleRequest(context);
        return context.Response.Reply();
    }
    private static Task WriteJson(TaskerOutput output, object data) => output.WriteJson(data);
    private static Task<(DateTimeOffset Until, string Reason)> ReadDefer(TaskerInput request)
        => TaskControlHandler.ReadDefer(request.InputStream);
}
