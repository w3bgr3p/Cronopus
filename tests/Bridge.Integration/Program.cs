using System.Reflection;
using System.Text.Json;
using z3nDash;

var root = Path.Combine(Path.GetTempPath(), "z3ndash-bridge-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var type = typeof(SchedulerService).Assembly.GetType("z3nDash.TaskerService");
    Check(type != null, "TaskerService is missing");
    var dbs = new DbConnectionService();
    dbs.Connect(new DbConfig { Type = "sqlite", SqlitePath = Path.Combine(root, "tasks.db") });
    SchedulerService.PrepareTables(dbs.GetDb());
    using var scheduler = new SchedulerService(dbs);
    dynamic service = Activator.CreateInstance(type!, dbs, scheduler, root)!;
    var body = JsonSerializer.SerializeToElement(new {
        name = "bridge-test", executor = "python", script_path = Path.Combine(root, "test.py"),
        enabled = "false", schedule_mode = "off", payload_schema = "[]", payload_values = "{\"count\":2}" });
    dynamic saved = await service.InvokeAsync("POST", "/tasker/save", body, CancellationToken.None);
    Check(saved.StatusCode == 200, "save failed: " + saved.Text);
    var id = JsonDocument.Parse((string)saved.Text).RootElement.GetProperty("id").GetString()!;
    dynamic payload = await service.InvokeAsync("GET", "/tasker/payload?id=" + id, (JsonElement?)null, CancellationToken.None);
    Check(JsonDocument.Parse((string)payload.Text).RootElement.GetProperty("values").GetString() == "{\"count\":2}", "payload lost");
    Console.WriteLine("PASS tasker-save");
    dynamic detail = await service.InvokeAsync("GET", "/tasker/detail?id=" + id, (JsonElement?)null, CancellationToken.None);
    Check(detail.StatusCode == 200, "Task detail/revision unavailable");
    var revision = JsonDocument.Parse((string)detail.Text).RootElement.GetProperty("_revision").GetString();
    var edit = JsonSerializer.SerializeToElement(new { id, name = "first", _revision = revision });
    dynamic updated = await service.InvokeAsync("POST", "/tasker/save", edit, CancellationToken.None);
    Check(updated.StatusCode == 200, "edit failed");
    dynamic conflict = await service.InvokeAsync("POST", "/tasker/save", edit, CancellationToken.None);
    Check(conflict.StatusCode == 409, "stale revision overwrote task");
    Console.WriteLine("PASS revision-conflict");
}
finally { Directory.Delete(root, true); }

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
