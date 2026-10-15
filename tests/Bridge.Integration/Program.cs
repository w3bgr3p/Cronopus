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
    var storeType = typeof(SchedulerService).Assembly.GetType("z3nDash.BridgeStore");
    Check(storeType != null, "BridgeStore is missing");
    dynamic store = Activator.CreateInstance(storeType!, Path.Combine(root, "bridge"))!;
    dynamic key = store.CreateKey("desktop");
    Check(store.Authorize((string)key.Secret) == (string)key.Id, "key rejected");
    store.RevokeKey((string)key.Id);
    Check(store.Authorize((string)key.Secret) == null, "revoked key accepted");
    dynamic reopened = Activator.CreateInstance(storeType!, Path.Combine(root, "bridge"))!;
    Check((string)reopened.NodeId == (string)store.NodeId, "node identity changed");
    Console.WriteLine("PASS identity");
    var hostType = typeof(SchedulerService).Assembly.GetType("z3nDash.BridgeHost");
    Check(hostType != null, "BridgeHost is missing");
    dynamic host = Activator.CreateInstance(hostType!, reopened, service, scheduler)!;
    reopened.State.Settings.BindAddress = "127.0.0.1";
    reopened.State.Settings.Port = 0;
    dynamic good = reopened.CreateKey("tls");
    Uri uri = await host.StartAsync(CancellationToken.None);
    using var cert = (System.Security.Cryptography.X509Certificates.X509Certificate2)reopened.Certificate(false);
    using var anonymous = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c != null && c.RawData.SequenceEqual(cert.RawData) });
    Check((int)(await anonymous.GetAsync(new Uri(uri, "/bridge/v1/info"))).StatusCode == 401, "anonymous access");
    anonymous.DefaultRequestHeaders.Authorization = new("Bearer", (string)good.Secret);
    Check((await anonymous.GetAsync(new Uri(uri, "/bridge/v1/info"))).IsSuccessStatusCode, "authorized TLS failed");
    var tasksResponse = await anonymous.GetAsync(new Uri(uri, "/bridge/v1/tasks?limit=100"));
    Check(tasksResponse.IsSuccessStatusCode, "tasks API unavailable");
    using var runRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(uri, "/bridge/v1/tasks/" + id + "/actions/pause"));
    runRequest.Headers.Add("X-Request-ID", "pause-test");
    runRequest.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
    Check((await anonymous.SendAsync(runRequest)).IsSuccessStatusCode, "pause API failed");
    await host.StopAsync(CancellationToken.None);
    Console.WriteLine("PASS tls");
}
finally { Directory.Delete(root, true); }

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
