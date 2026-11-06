using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cronopus;

static class ApiCases
{
    public static async Task Run(string root, bool serve)
    {
        var folder = Path.Combine(root, "api"); Directory.CreateDirectory(folder);
        var db = new DbConnectionService(); db.Connect(new DbConfig { Type = "sqlite", SqlitePath = Path.Combine(folder, "tasks.db") });
        SchedulerService.PrepareTables(db.GetDb());
        using var scheduler = new SchedulerService(db);
        var tasker = new TaskerService(db, scheduler, folder);
        var store = new BridgeStore(Path.Combine(folder, "bridge"));
        await using var bas = new BasControlService(store, () => new FixtureEngine());
        var host = new BridgeHost(store, tasker, scheduler) { Bas = bas };
        store.State.Settings.BindAddress = "127.0.0.1"; store.State.Settings.Port = 0;
        var key = store.CreateKey("BAS fixture"); var uri = await host.StartAsync(CancellationToken.None);
        using var cert = store.Certificate();
        using var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c != null && BridgeStore.Fingerprint(c) == BridgeStore.Fingerprint(cert) });
        try
        {
            using var anonymous = await http.GetAsync(new Uri(uri, "/bridge/v1/bas/projects"));
            Check((int)anonymous.StatusCode == 401, "anonymous BAS request accepted");
            http.DefaultRequestHeaders.Authorization = new("Bearer", key.Secret);
            var sourcePath = TestProject.Create(root);
            using (var resourceReply = await http.GetAsync(new Uri(uri, "/bridge/v1/bas/resources?projectPath=" + Uri.EscapeDataString(sourcePath))))
            {
                Check(resourceReply.IsSuccessStatusCode, "XML resource discovery failed over HTTPS");
                var metadata = await resourceReply.Content.ReadFromJsonAsync<JsonElement>();
                Check(metadata.GetProperty("resources").GetArrayLength() == 2, "XML resources missing");
            }
            var saved = await Send(http, uri, "POST", "projects", new { name = "Fixture", projectPath = TestProject.Create(root), parameters = "{\"X\":2,\"Y\":3}", maxThreads = 1 }, "save-once");
            var id = saved.GetProperty("id").GetString()!;
            await Send(http, uri, "POST", "projects/" + id + "/run", new { count = 1 }, "run-once");
            await Send(http, uri, "POST", "projects/" + id + "/run", new { count = 1 }, "run-once");
            Check(bas.Runs(id).Count == 1, "duplicate request launched two BAS runs");
            var list = await http.GetStringAsync(new Uri(uri, "/bridge/v1/bas/projects"));
            Check(!list.Contains("hidden-"), "HTTPS response leaked credentials");
            var connection = new BridgeConnection { Name = "BAS fixture node", Address = uri.ToString(), NodeId = store.NodeId, Fingerprint = BridgeStore.Fingerprint(cert), Secret = key.Secret };
            store.State.Connections.Add(connection); store.Save();
            var client = new BridgeClient(store);
            using var pinned = await client.SendAsync(connection.Id, HttpMethod.Get, "/bridge/v1/bas/projects", null, CancellationToken.None);
            Check(pinned.IsSuccessStatusCode, "pinned BAS request failed");
            Console.WriteLine("PASS authenticated HTTPS BAS routes, mutation deduplication, redaction and certificate-pinned client");
            if (serve) await Serve(folder, db, store, host);
            store.RevokeKey(key.Id);
            using var revoked = await http.GetAsync(new Uri(uri, "/bridge/v1/bas/projects"));
            Check((int)revoked.StatusCode == 401, "revoked BAS key accepted");
        }
        finally { await host.StopAsync(CancellationToken.None); db.Disconnect(); }
    }
    private static async Task<JsonElement> Send(HttpClient http, Uri uri, string method, string path, object body, string requestId)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(uri, "/bridge/v1/bas/" + path));
        request.Headers.Add("X-Request-ID", requestId); request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        Check(response.IsSuccessStatusCode, path + ": " + text); return JsonDocument.Parse(text).RootElement.Clone();
    }
    private static async Task Serve(string root, DbConnectionService db, BridgeStore store, BridgeHost host)
    {
        var dashboard = new EmbeddedServer(new LogsConfig { DashboardPort = "39761", LogsFolder = root, ReportsFolder = root }, db);
        dashboard.RegisterHandler(new BridgeLocalHandler(store, host)); dashboard.Start();
        Console.WriteLine("BAS_BROWSER_URL=http://localhost:" + dashboard.Port + "/bas.html");
        try { await Task.Delay(TimeSpan.FromMinutes(15)); } finally { dashboard.Stop(); }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}

sealed class FixtureEngine : IBasControlEngine
{
    public event Action<string, JsonElement>? Message;
    public Task StartAsync(BasProject project, CancellationToken ct) => Task.CompletedTask;
    public async Task<JsonElement> RunAsync(JsonElement parameters, int threadId, CancellationToken ct)
    {
        Message?.Invoke("log", JsonSerializer.SerializeToElement(new { text = "<img src=x onerror=alert(1)> fixture log ёж😀" }));
        await Task.Delay(1500, ct); return JsonSerializer.SerializeToElement(parameters.GetProperty("X").GetInt32() + parameters.GetProperty("Y").GetInt32());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
