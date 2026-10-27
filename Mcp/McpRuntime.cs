using System.Net;
using System.Text.Json;

namespace Cronopus.Mcp;

public sealed class McpRuntime(int port, DbConnectionService database, TaskerService tasker, SchedulerService scheduler, string? runtimeFile = null) : IScriptHandler, IDisposable
{
    public string PathPrefix => "/mcp/";
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    public string RuntimeFile { get; } = runtimeFile ?? Path.Combine(AppContext.BaseDirectory, ".runtime", "mcp.json");
    public static string? ActiveInstanceId { get; private set; }
    public void Init() => ActiveInstanceId = InstanceId;
    private object Status() => new { application = "Cronopus", contract_version = 1, instance_id = InstanceId,
        api_url = $"http://localhost:{port}", version = typeof(McpRuntime).Assembly.GetName().Version!.ToString(),
        process_id = Environment.ProcessId, database_connected = database.IsConnected };

    public void Publish()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RuntimeFile)!);
            var temp = RuntimeFile + "." + InstanceId + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Status()));
            File.Move(temp, RuntimeFile, true);
        }
        catch (IOException ex) { Console.Error.WriteLine("MCP discovery unavailable; use --api-url: " + ex.Message); }
        catch (UnauthorizedAccessException) { Console.Error.WriteLine("MCP discovery is not writable; use --api-url."); }
    }

    public async Task<bool> HandleRequest(HttpListenerContext context)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        object result;
        try
        {
            if (context.Request.HttpMethod != "GET") { context.Response.StatusCode = 405; return true; }
            var query = context.Request.QueryString;
            int Number(string key, int fallback, int max) => query[key] == null ? fallback :
                int.TryParse(query[key], out var n) && n >= 0 && n <= max ? n : throw new ArgumentException("Invalid " + key);
            if (context.Request.Url!.AbsolutePath == "/mcp/status") result = Status();
            else if (!database.IsConnected) { context.Response.StatusCode = 503; result = new { error = "DB not connected" }; }
            else if (context.Request.Url.AbsolutePath == "/mcp/tasks")
                result = tasker.Page(Number("limit", 100, 500), query["cursor"], query["search"] ?? "");
            else if (context.Request.Url.AbsolutePath == "/mcp/runtime")
            {
                var id = query["id"] ?? ""; TaskerService.ValidateId(id);
                var kind = query["kind"] ?? "instances";
                if (kind is not ("instances" or "queue")) throw new ArgumentException("Invalid runtime kind");
                result = tasker.RuntimePage(id, kind, Number("offset", 0, int.MaxValue), Number("limit", 100, 500));
            }
            else if (context.Request.Url.AbsolutePath == "/mcp/output")
            {
                var id = query["id"] ?? ""; TaskerService.ValidateId(id);
                var db = database.GetDb();
                if (string.IsNullOrEmpty(db.Get("id", DbSchema.Schedules.Name, where: $"\"id\" = '{id}'")))
                { context.Response.StatusCode = 404; result = new { error = "Task not found" }; }
                else
                {
                    var live = query["live"] != "false";
                    var text = live ? scheduler.GetLiveOutput(id, query["run"]) : db.Get("last_output", DbSchema.Schedules.Name, where: $"\"id\" = '{id}'") ?? "";
                    var offset = Number("offset", 0, int.MaxValue); var limit = Math.Max(1, Number("limit", 16000, 64000));
                    var end = McpOutput.PageEnd(text, offset, limit);
                    var slice = text[Math.Min(offset, text.Length)..end];
                    result = new { id, live, is_running = scheduler.IsRunning(id), output = slice, offset,
                        next_offset = offset + slice.Length < text.Length ? (int?)(offset + slice.Length) : null, total_chars = text.Length };
                }
            }
            else { context.Response.StatusCode = 404; result = new { error = "Unknown MCP support endpoint" }; }
        }
        catch (ArgumentException ex) { context.Response.StatusCode = 400; result = new { error = ex.Message }; }
        catch (JsonException) { context.Response.StatusCode = 400; result = new { error = "Invalid page cursor" }; }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result);
        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(bytes);
        return true;
    }

    public void Dispose()
    {
        try
        {
            if (!File.Exists(RuntimeFile)) return;
            using var manifest = JsonDocument.Parse(File.ReadAllText(RuntimeFile));
            if (manifest.RootElement.ValueKind == JsonValueKind.Object && manifest.RootElement.TryGetProperty("instance_id", out var instance) && instance.GetString() == InstanceId)
                File.Delete(RuntimeFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }
}
