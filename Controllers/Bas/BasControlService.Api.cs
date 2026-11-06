using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cronopus;

public sealed partial class BasControlService
{
    private static readonly JsonSerializerOptions ApiJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static bool Allows(string method, string path)
    {
        if (path == "resources") return method == "GET";
        if (path == "projects") return method is "GET" or "POST";
        if (Regex.IsMatch(path, "^projects/[a-f0-9]{32}$")) return method == "DELETE";
        return Regex.IsMatch(path, "^projects/[a-f0-9]{32}/(runs|events)$") && method == "GET"
            || Regex.IsMatch(path, "^projects/[a-f0-9]{32}/(run|stop|unload)$") && method == "POST";
    }
    public async Task<TaskerReply> InvokeAsync(string method, string path, JsonElement? body = null, long after = 0, string? projectPath = null)
    {
        try
        {
            if (!Allows(method, path)) return Reply(404, new { error = "BAS route not found" });
            if (path == "resources") return Reply(200, BasXmlProject.Resources(projectPath ?? ""));
            if (path == "projects" && method == "GET")
                return Reply(200, new { supported = OperatingSystem.IsWindows() || _factory != null, projects = List() });
            var input = body ?? JsonSerializer.SerializeToElement(new { });
            if (path == "projects") return Reply(200, Save(input.Deserialize<BasProjectEdit>(ApiJson) ?? throw new ArgumentException("Project required")));
            var parts = path.Split('/'); var id = parts[1];
            if (method == "DELETE") { await DeleteAsync(id, input.GetProperty("revision").GetString() ?? ""); return Reply(200, new { ok = true }); }
            if (parts[2] == "runs") return Reply(200, Runs(id));
            if (parts[2] == "events") return Reply(200, Events(id, after));
            if (parts[2] == "run")
                return Reply(200, new { runs = Run(id, input.TryGetProperty("count", out var count) ? count.GetInt32() : 1) });
            if (parts[2] == "stop")
                await StopAsync(id, input.TryGetProperty("runId", out var runId) ? runId.GetString() : null);
            else await UnloadAsync(id);
            return Reply(200, new { ok = true });
        }
        catch (Exception ex)
        {
            var status = ex is BasConflictException ? 409 : ex is KeyNotFoundException ? 404
                : ex is ArgumentException or JsonException or System.Xml.XmlException or InvalidOperationException or FormatException ? 400 : 500;
            return Reply(status, new { error = status == 500 ? "BAS operation failed" : ex.Message });
        }
    }
    private static TaskerReply Reply(int status, object body) => new(status, JsonSerializer.Serialize(body, ApiJson), "application/json");
}
