using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using z3nDash;
using z3nDash.Mcp;

var root = Path.Combine(Path.GetTempPath(), "z3ndash-mcp-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var manifest = Path.Combine(root, "mcp.json");
var dbs = new DbConnectionService();
dbs.Connect(new DbConfig { Type = "sqlite", SqlitePath = Path.Combine(root, "tasks.db") });
SchedulerService.PrepareTables(dbs.GetDb());
using var scheduler = new SchedulerService(dbs);
var tasker = new TaskerService(dbs, scheduler, root);
var bridgeStore = new BridgeStore(Path.Combine(root, "bridge"));
var bridgeHost = new BridgeHost(bridgeStore, tasker, scheduler);
var dashboard = new EmbeddedServer(new LogsConfig { DashboardPort = "39471", LogsFolder = root, ReportsFolder = root }, dbs);
using var runtime = new McpRuntime(dashboard.Port, dbs, tasker, scheduler, manifest);
dashboard.RegisterHandler(runtime);
dashboard.RegisterHandler(new SchedulerHandler(tasker));
dashboard.RegisterHandler(new BridgeLocalHandler(bridgeStore, bridgeHost));
dashboard.Start(); runtime.Publish();
var url = "http://localhost:" + dashboard.Port;
var exeIndex = Array.IndexOf(args, "--mcp-exe");
var exe = exeIndex >= 0 ? Path.GetFullPath(args[exeIndex + 1]) : Path.Combine(AppContext.BaseDirectory, "z3nDash.exe");
try
{
    await using var client = await Connect(["--runtime-file", manifest]);
    var metricsIndex = Array.IndexOf(args, "--metrics");
    if (metricsIndex >= 0) await TokenMetrics.Capture(client, args[metricsIndex + 1]);
    await AgentCases.Run(client);
    var tools = await client.ListToolsAsync();
    Check(tools.Count >= 18, "tool catalog incomplete");
    foreach (var tool in tools)
    {
        Check(!string.IsNullOrWhiteSpace(tool.Description), "missing tool description");
        Check(tool.JsonSchema.ValueKind == JsonValueKind.Object, "missing input schema");
        Check(tool.ProtocolTool.OutputSchema == null, "redundant generic output schema");
        Check(tool.ProtocolTool.Annotations != null, "missing operation annotations");
    }
    Check((await Call(client, "get_status")).GetProperty("dashboard").GetProperty("instance_id").GetString() == runtime.InstanceId, "wrong dashboard");
    Check((await Call(client, "read_document")).GetProperty("text").GetString()!.Contains("stdio"), "guide missing");
    var resources = await client.ListResourcesAsync();
    Check(resources.Any(r => r.Uri == "z3ndash://docs/mcp"), "guide resource missing");
    Check((await client.ReadResourceAsync("z3ndash://docs/mcp")).Contents.OfType<TextResourceContents>().Single().Text.Contains("run_task"), "resource content missing");
    Console.WriteLine("PASS MCP handshake, catalog, schemas, tools and resources");

    // Queue must round-trip in the scheduler's storage format, including clones of get_task output.
    foreach (var policy in new[] { "parallel", "queue" })
    {
        var overlapTask = await Call(client, "create_task", new { settings = new {
            name = "mcp overlap " + policy, executor = "cmd", script_path = "echo overlap", on_overlap = policy } });
        var overlapId = overlapTask.GetProperty("id").GetString()!;
        var overlapDetail = await Call(client, "get_task", new { id = overlapId });
        Check(overlapDetail.GetProperty("on_overlap").GetString() == "parallel", "Queue creation did not save scheduler policy");
        foreach (var updatePolicy in new[] { "skip", "parallel", "kill_restart", "queue" })
        {
            await Call(client, "update_task", new { id = overlapId,
                revision = overlapDetail.GetProperty("_revision").GetString(), settings = new { on_overlap = updatePolicy } });
            overlapDetail = await Call(client, "get_task", new { id = overlapId });
            Check(overlapDetail.GetProperty("on_overlap").GetString() == (updatePolicy == "queue" ? "parallel" : updatePolicy),
                "Overlap update did not save scheduler policy: " + updatePolicy);
        }
        await Call(client, "update_task", new { id = overlapId,
            revision = overlapDetail.GetProperty("_revision").GetString(), settings = new { on_overlap = "invalid" } }, "invalid_argument");
        await Call(client, "delete_task", new { id = overlapId, revision = overlapDetail.GetProperty("_revision").GetString() });
    }
    Console.WriteLine("PASS Queue create/update round-trip and alias normalization; invalid overlap rejected");

    var created = await Call(client, "create_task", new { settings = new { name = "mcp smoke тест", executor = "cmd", script_path = "echo MCP-SMOKE-OK" } });
    var id = created.GetProperty("id").GetString()!;
    var detail = (await Call(client, "get_task", new { id }));
    Check(!detail.GetProperty("enabled").GetBoolean(), "new task auto-enabled");
    var revision = detail.GetProperty("_revision").GetString();
    await Call(client, "update_task", new { id, revision, settings = new { args = "тест", max_threads = 1 } });
    await Call(client, "update_task", new { id, revision, settings = new { name = "stale" } }, "conflict");
    var unchanged = (await Call(client, "get_task", new { id }));
    Check(unchanged.GetProperty("name").GetString() == "mcp smoke тест", "stale edit overwrote task");
    await Call(client, "pause_task", new { id });
    await Call(client, "defer_task", new { id, delay_seconds = 3600 });
    await Call(client, "resume_task", new { id });
    await Call(client, "get_task_payload", new { id });
    await Call(client, "get_resources");
    await Call(client, "list_task_runtime", new { id });
    await Call(client, "list_task_runtime", new { id, kind = "queue" });
    Console.WriteLine("PASS task create/read/update, revision conflict, pause/defer/resume and runtime");

    await Call(client, "list_tasks", new { search = "mcp smoke", limit = 1 });
    await Call(client, "run_task", new { id, count = 0 }, "invalid_argument");
    await Call(client, "get_task", new { id = "' OR 1=1" }, "invalid_argument");
    await Call(client, "get_task", new { id = "does-not-exist" }, "not_found");
    await Call(client, "defer_task", new { id, delay_seconds = 1, until = "2030-01-01T00:00:00Z" }, "invalid_argument");
    await Call(client, "run_task", new { id });
    JsonElement output = default;
    for (var i = 0; i < 100; i++)
    {
        output = (await Call(client, "read_task_output", new { id, live = false, limit = 64000 }));
        if (output.GetProperty("output").GetString()!.Contains("MCP-SMOKE-OK")) break;
        await Task.Delay(100);
    }
    Check(output.GetProperty("output").GetString()!.Contains("MCP-SMOKE-OK"), "real task did not run");
    await Call(client, "stop_task", new { id });
    await Call(client, "clear_task_queue", new { id });
    Console.WriteLine("PASS real queued execution and saved output; validation failures");

    // Check bounded output and stable task pagination using only isolated fixture rows.
    dbs.GetDb().Query($"UPDATE schedules SET last_output='{new string('x', 150000)}' WHERE id='{id}'", thrw: true);
    var page = (await Call(client, "read_task_output", new { id, live = false, limit = 1000 }));
    Check(page.GetProperty("output").GetString()!.Length == 1000 && page.GetProperty("next_offset").GetInt32() == 1000, "output is not bounded");
    var heavy = await Call(client, "create_task", new { settings = new { name = "mcp smoke second", executor = "cmd",
        script_path = "echo second", payload_values = new { large = new string('p', 20000) } } });
    var heavyId = heavy.GetProperty("id").GetString();
    var compact = await Call(client, "get_task", new { id = heavyId });
    Check(!compact.TryGetProperty("payload_values", out _) && compact.GetRawText().Length < 2000, "get_task eagerly loaded a large payload");
    var full = await Call(client, "get_task", new { id = heavyId, include_payload = true });
    Check(full.GetProperty("payload_values").GetProperty("large").GetString()!.Length == 20000, "opt-in payload lost data");
    var payloadOnly = await Call(client, "get_task_payload", new { id = heavyId });
    Check(payloadOnly.GetProperty("values").GetProperty("large").GetString()!.Length == 20000, "payload was double-encoded or lost");
    var firstPage = (await Call(client, "list_tasks", new { search = "mcp smoke", limit = 1 }));
    var cursor = firstPage.GetProperty("next_cursor").GetString();
    Check(cursor != null, "missing task cursor");
    Check(!firstPage.GetProperty("items")[0].TryGetProperty("script_path", out _), "list_tasks loaded full settings by default");
    var detailedPage = await Call(client, "list_tasks", new { search = "mcp smoke", include_details = true });
    Check(detailedPage.GetProperty("items")[0].TryGetProperty("script_path", out _), "opt-in list details are unavailable");
    var secondPage = (await Call(client, "list_tasks", new { search = "mcp smoke", limit = 1, cursor }));
    Check(firstPage.GetProperty("items")[0].GetProperty("id").GetString() != secondPage.GetProperty("items")[0].GetProperty("id").GetString(), "pagination repeated task");
    await Call(client, "list_tasks", new { search = "changed search", cursor }, "invalid_argument");
    await Call(client, "list_tasks", new { cursor = "malformed" }, "invalid_argument");
    using (var direct = new HttpClient(new HttpClientHandler { UseProxy = false }))
    {
        direct.DefaultRequestHeaders.Add("X-Z3nDash-Instance", "wrong");
        Check((int)(await direct.GetAsync(url + "/mcp/status")).StatusCode == 409, "instance header was not enforced");
    }
    Console.WriteLine("PASS output bounds, pagination and instance request guard");

    await using (var readOnly = await Connect(["--api-url", url, "--read-only"]))
    {
        var readTools = await readOnly.ListToolsAsync();
        Check(readTools.All(t => !new[] { "run_task", "delete_task", "create_task", "update_task", "pause_task" }.Contains(t.Name)), "write tools exposed in read-only mode");
        await Call(readOnly, "get_status");
        try
        {
            await readOnly.CallToolAsync("run_task", new Dictionary<string, object?> { ["id"] = id });
            throw new Exception("hidden mutation executed");
        }
        catch (McpProtocolException) { }
    }
    var savedManifest = File.ReadAllText(manifest);
    File.WriteAllText(manifest, savedManifest.Replace(runtime.InstanceId, "wrong-instance"));
    await Call(client, "get_status", null, "instance_changed");
    File.WriteAllText(manifest, savedManifest);
    await RemoteCases.Run(client, bridgeStore, bridgeHost, dbs, Call);
    var latest = (await Call(client, "get_task", new { id })).GetProperty("_revision").GetString();
    await Call(client, "delete_task", new { id, revision = latest });
    await Call(client, "get_task", new { id }, "not_found");
    Console.WriteLine("PASS read-only enforcement, stale discovery and delete");
    dashboard.Stop();
    await Call(client, "get_status", null, "dashboard_unavailable");
    await Call(client, "read_document", new { topic = "task-api" });
    Console.WriteLine("PASS unavailable dashboard and offline documentation");
    await EdgeCases.Run();
}
finally
{
    dashboard.Stop();
    scheduler.Dispose();
    await bridgeHost.StopAsync(CancellationToken.None);
    dbs.Disconnect();
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
}

async Task<McpClient> Connect(string[] options) => await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "z3nDash integration", Command = exe, Arguments = ["--mcp", .. options]
}));
static async Task<JsonElement> Call(McpClient client, string name, object? args = null, string? error = null)
{
    var parameters = args == null ? new Dictionary<string, object?>() : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(args))!;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    var reply = await client.CallToolAsync(name, parameters, cancellationToken: timeout.Token);
    var json = JsonSerializer.Deserialize<JsonElement>(reply.Content.OfType<TextContentBlock>().Single().Text);
    Check(reply.StructuredContent == null, "tool result duplicates its JSON payload");
    Check((reply.IsError == true) == (error != null), name + ": " + json.GetRawText());
    if (error != null) Check(json.GetProperty("error").GetProperty("code").GetString() == error, name + ": " + json.GetRawText());
    return json;
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
