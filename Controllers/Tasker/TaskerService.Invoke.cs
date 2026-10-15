using System.Text.Json;
namespace z3nDash;

public sealed partial class TaskerService
{
    public static readonly HashSet<string> RemoteRoutes = new(StringComparer.Ordinal) {
        "GET /tasker/list", "POST /tasker/save", "POST /tasker/delete", "POST /tasker/run",
        "POST /tasker/stop", "POST /tasker/pause", "POST /tasker/resume", "POST /tasker/defer",
        "GET /tasker/payload", "POST /tasker/payload", "GET /tasker/instances", "GET /tasker/queue",
        "GET /tasker/process-stats", "GET /tasker/resources", "POST /tasker/kill-instance",
        "POST /tasker/clear-queue", "GET /tasker/scan-folder", "GET /tasker/package-scripts",
        "GET /tasker/config-file", "POST /tasker/config-file", "GET /tasker/script-params",
        "POST /tasker/args-preview", "POST /tasker/schedule-preview", "POST /tasker/build",
        "POST /tasker/ensure-venv", "GET /tasker/install/stream"
    };
    public async Task<TaskerReply> InvokeAsync(string method, string path, JsonElement? body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var clean = path.Split('?')[0];
        if (!RemoteRoutes.Contains(method + " " + clean)) return new(404, "{\"error\":\"Unsupported operation\"}", "application/json");
        var context = new TaskerExchange(method, path, body);
        await HandleRequest(context);
        return context.Response.Reply();
    }
    private static Task WriteJson(TaskerOutput output, object data) => output.WriteJson(data);
    private static Task<(DateTimeOffset Until, string Reason)> ReadDefer(TaskerInput request)
        => TaskControlHandler.ReadDefer(request.InputStream);
}
