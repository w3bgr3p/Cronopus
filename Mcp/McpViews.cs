using System.Text.Json;

namespace z3nDash.Mcp;

internal static class McpViews
{
    private static readonly HashSet<string> Summary = ["id", "name", "status", "enabled"];

    public static object Nodes(JsonElement rows) => new
    {
        local = "local",
        connections = rows.EnumerateArray().Select(row => Fields(row, p => p.Name is "id" or "name" or "address")).ToArray()
    };
    private static readonly HashSet<string> Payload = ["payload_schema", "payload_values", "browser_json"];

    public static object TaskPage(JsonElement page) => new
    {
        items = page.GetProperty("items").EnumerateArray().Select(row => Fields(row, p => Summary.Contains(p.Name))).ToArray(),
        next_cursor = page.GetProperty("next_cursor")
    };

    public static object Task(JsonElement row, bool includePayload) =>
        Fields(row, p => (includePayload || !Payload.Contains(p.Name)) && p.Value.ToString().Length > 0);

    public static object TaskPayload(JsonElement row) => Fields(row, _ => true);

    public static object Resources(JsonElement row, bool includeDetails) =>
        Fields(row, p => includeDetails || !(p.Name.Equals("tasks", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("instanceDetails", StringComparison.OrdinalIgnoreCase)));

    private static Dictionary<string, object?> Fields(JsonElement row, Func<JsonProperty, bool> include) =>
        row.EnumerateObject().Where(include).ToDictionary(p => p.Name, p => Value(p.Name, p.Value));

    private static object? Value(string name, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return value;
        var text = value.GetString()!;
        if (name is "enabled" or "use_venv" or "schedule_paused" && bool.TryParse(text, out var flag)) return flag;
        if (name is "max_threads" or "timeout_seconds" && int.TryParse(text, out var number)) return number;
        if (name.EndsWith("_json") || name is "payload_schema" or "payload_values" or "schema" or "values")
        {
            try { return JsonSerializer.Deserialize<JsonElement>(text); }
            catch (JsonException) { }
        }
        return text;
    }
}
