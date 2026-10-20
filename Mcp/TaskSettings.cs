using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace z3nDash.Mcp;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TaskSettings
{
    [Description("Task display name")] public string? name { get; set; }
    [Description("python, node, ts-node, npm, cmd, bat, bash, ps1, exe, csx, csx-internal, xml, internal")] public string? executor { get; set; }
    [Description("Script/project path on the selected node; use absolute paths for files. cmd/internal may use a command/task name.")] public string? script_path { get; set; }
    [Description("Saved command-line arguments")] public string? args { get; set; }
    [Description("Enable automatic scheduling; creation defaults to false")] public bool? enabled { get; set; }
    [Description("Maximum parallel instances, 1..10000")] public int? max_threads { get; set; }
    [Description("Execution timeout in seconds; 0 disables timeout")] public int? timeout_seconds { get; set; }
    [Description("Use the task's Python virtual environment")] public bool? use_venv { get; set; }
    [Description("Schedule mode: off, cron or zp. Read tasker documentation for zp schema.")] public string? schedule_mode { get; set; }
    [Description("Cron expression for cron mode")] public string? cron { get; set; }
    [Description("Schedule specification as a JSON object, not an encoded JSON string")] public JsonElement? schedule_json { get; set; }
    [Description("Task grouping tag")] public string? schedule_tag { get; set; }
    [Description("Overlap policy: skip, queue or kill_restart (kills existing instances)")] public string? on_overlap { get; set; }
    [Description("Browser configuration as a JSON object; see tasker documentation")] public JsonElement? browser_json { get; set; }
    [Description("Payload field schema as a JSON array")] public JsonElement? payload_schema { get; set; }
    [Description("Payload values as a JSON object")] public JsonElement? payload_values { get; set; }

    public Dictionary<string, string> ToWire()
    {
        if (max_threads is < 1 or > 10000) throw new ArgumentException("max_threads must be 1..10000");
        if (timeout_seconds < 0) throw new ArgumentException("timeout_seconds must be nonnegative");
        if (schedule_mode != null && schedule_mode is not ("off" or "cron" or "zp")) throw new ArgumentException("schedule_mode must be off, cron or zp");
        if (on_overlap != null && on_overlap is not ("skip" or "queue" or "kill_restart")) throw new ArgumentException("on_overlap must be skip, queue or kill_restart");
        if (executor != null && executor is not ("python" or "node" or "ts-node" or "npm" or "cmd" or "bat" or "bash" or "ps1" or "exe" or "csx" or "csx-internal" or "xml" or "internal"))
            throw new ArgumentException("Unsupported executor; read tasker documentation");
        if (schedule_json is { ValueKind: not JsonValueKind.Object }) throw new ArgumentException("schedule_json must be an object");
        if (browser_json is { ValueKind: not JsonValueKind.Object }) throw new ArgumentException("browser_json must be an object");
        if (payload_schema is { ValueKind: not JsonValueKind.Array }) throw new ArgumentException("payload_schema must be an array");
        if (payload_values is { ValueKind: not JsonValueKind.Object }) throw new ArgumentException("payload_values must be an object");
        return JsonSerializer.SerializeToElement(this).EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText());
    }
}
