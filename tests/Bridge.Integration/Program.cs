using System.Reflection;
using System.Text.Json;
using Cronopus;

if(args.Contains("--config-startup")){await ConfigStartupCases.Run();return;}
if(args.Contains("--reset-attempts")){await ResetAttemptsCases.Run();return;}
if(args.Contains("--firewall-live")){await FirewallCases.Live();return;}
if(args.Contains("--load")){await LoadCases.Run(args.Contains("--quick")?1:15);return;}
if(args.Contains("--public-ip")){await PublicAddressCases.Run(true);return;}
if(!args.Contains("--file-text")){await PublicAddressCases.Run();await FirewallCases.Run();}
if(args.Length>=2&&args[0]=="--setup-e2e"){PublishedFixture.Setup(args[1]);return;}

var root = Path.Combine(Path.GetTempPath(), "cronopus-bridge-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var type = typeof(SchedulerService).Assembly.GetType("Cronopus.TaskerService");
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
    var storeType = typeof(SchedulerService).Assembly.GetType("Cronopus.BridgeStore");
    Check(storeType != null, "BridgeStore is missing");
    dynamic store = Activator.CreateInstance(storeType!, Path.Combine(root, "bridge"))!;
    dynamic key = store.CreateKey("desktop");
    Check(store.Authorize((string)key.Secret) == (string)key.Id, "key rejected");
    store.RevokeKey((string)key.Id);
    Check(store.Authorize((string)key.Secret) == null, "revoked key accepted");
    dynamic reopened = Activator.CreateInstance(storeType!, Path.Combine(root, "bridge"))!;
    Check((string)reopened.NodeId == (string)store.NodeId, "node identity changed");
    Console.WriteLine("PASS identity");
    var hostType = typeof(SchedulerService).Assembly.GetType("Cronopus.BridgeHost");
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
    var rootsResponse = await anonymous.GetAsync(new Uri(uri, "/bridge/v1/files/roots"));
    Check(rootsResponse.IsSuccessStatusCode, "server disk API unavailable");
    await BridgeCases.FileText(root, anonymous, uri);
    if(args.Contains("--file-text")){if(args.Contains("--serve"))await BrowserFixture.Serve(root,(BridgeStore)reopened,uri,(string)good.Secret);await host.StopAsync(CancellationToken.None);return;}
    var uploadDir = Path.Combine(root, "destination"); Directory.CreateDirectory(uploadDir);
    var bytes = new byte[] { 1, 2, 3, 4 };
    var manifest = JsonSerializer.Serialize(new { destination = uploadDir, directories = new[] { "empty" },
        files = new[] { new { id = "file1", relativePath = "nested/тест.bin", size = 4, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) } } });
    var createUpload = await anonymous.PostAsync(new Uri(uri, "/bridge/v1/uploads"), new StringContent(manifest, System.Text.Encoding.UTF8, "application/json"));
    Check(createUpload.IsSuccessStatusCode, "uploads API unavailable");
    var uploadId = JsonDocument.Parse(await createUpload.Content.ReadAsStringAsync()).RootElement.GetProperty("upload_id").GetString();
    Check((await anonymous.PutAsync(new Uri(uri, $"/bridge/v1/uploads/{uploadId}/files/file1?offset=0"), new ByteArrayContent(bytes[..2]))).IsSuccessStatusCode, "block1 failed");
    Check((await anonymous.PutAsync(new Uri(uri, $"/bridge/v1/uploads/{uploadId}/files/file1?offset=2"), new ByteArrayContent(bytes[2..]))).IsSuccessStatusCode, "block2 failed");
    Check((await anonymous.PostAsync(new Uri(uri, $"/bridge/v1/uploads/{uploadId}/commit"), new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).IsSuccessStatusCode, "commit failed");
    Check(File.ReadAllBytes(Path.Combine(uploadDir,"nested","тест.bin")).SequenceEqual(bytes), "published bytes differ");
    Console.WriteLine("PASS uploads");
    var outputType=typeof(SchedulerService).Assembly.GetType("Cronopus.BridgeOutput");
    Check(outputType!=null,"BridgeOutput is missing");
    dynamic output=Activator.CreateInstance(outputType!,262144,2000)!;
    output.Append("task","run",new ReadOnlyMemory<byte>(new byte[1000000]));
    JsonElement outputPage=output.Read("task","run",null,200);
    Check(System.Text.Encoding.UTF8.GetByteCount(outputPage.GetRawText())<=262144,"unbounded output page");
    Console.WriteLine("PASS bounded-output");
    await BridgeCases.Run(root,(TaskerService)service,(BridgeStore)reopened,uri,(string)good.Secret,anonymous);
    if(args.Contains("--serve"))await BrowserFixture.Serve(root,(BridgeStore)reopened,uri,(string)good.Secret);
    await host.StopAsync(CancellationToken.None);
    Console.WriteLine("PASS tls");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
