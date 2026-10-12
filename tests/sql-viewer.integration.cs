using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Npgsql;
using z3nDash;

var connectionString = Environment.GetEnvironmentVariable("SQL_VIEWER_TEST_CONNECTION")
    ?? throw new Exception("Set SQL_VIEWER_TEST_CONNECTION to a disposable PostgreSQL database.");
await using (var setup = new NpgsqlConnection(connectionString))
{
    await setup.OpenAsync();
    await using var cmd = new NpgsqlCommand("""
        CREATE SCHEMA IF NOT EXISTS "viewer.test";
        DROP TABLE IF EXISTS "viewer.test"."odd""table";
        CREATE TABLE "viewer.test"."odd""table" (id int, label text, details jsonb, tags text[]);
        INSERT INTO "viewer.test"."odd""table" VALUES (1, NULL, '{"ok":true}', ARRAY['a','b']), (2, 'two', NULL, NULL);
        """, setup);
    await cmd.ExecuteNonQueryAsync();
}

// Stable test port, with OS-selected fallback when another run occupies it.
int port = 39176;
using var listener = new HttpListener();
try { listener.Prefixes.Add($"http://localhost:{port}/"); listener.Start(); }
catch (HttpListenerException)
{
    listener.Prefixes.Clear();
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
    listener.Prefixes.Add($"http://localhost:{port}/"); listener.Start();
}
var sqlite = new SqliteViewerHandler();
var pgType = typeof(SqliteViewerHandler).Assembly.GetType("z3nDash.PostgresViewerHandler");
var pg = pgType == null ? null : Activator.CreateInstance(pgType);
var url = $"http://localhost:{port}/";
Console.WriteLine("SQL viewer test URL: " + url);
var server = Task.Run(async () =>
{
    while (listener.IsListening)
    {
        HttpListenerContext ctx;
        try { ctx = await listener.GetContextAsync(); } catch { break; }
        _ = Task.Run(async () =>
        {
            try
            {
                var path = ctx.Request.Url!.AbsolutePath;
                if (path.StartsWith("/postgres-viewer") && pg != null)
                    await (Task)pgType!.GetMethod("Handle")!.Invoke(pg, [ctx])!;
                else if (sqlite.Matches(path)) await sqlite.Handle(ctx);
                else
                {
                    var relative = path == "/" ? "sqlite.html" : path.TrimStart('/');
                    var file = Path.GetFullPath(Path.Combine("wwwroot", relative));
                    if (!file.StartsWith(Path.GetFullPath("wwwroot") + Path.DirectorySeparatorChar) || !File.Exists(file))
                        ctx.Response.StatusCode = 404;
                    else
                    {
                        ctx.Response.ContentType = Path.GetExtension(file) switch
                        { ".js" => "text/javascript", ".css" => "text/css", _ => "text/html" };
                        await ctx.Response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(file));
                    }
                }
            }
            catch { ctx.Response.StatusCode = 500; }
            finally { ctx.Response.Close(); }
        });
    }
});

using var client = new HttpClient { BaseAddress = new Uri(url) };
async Task<JsonElement> Post(string endpoint, string sql = "", bool ok = true, string? cs = null)
{
    using var response = await client.PostAsync(endpoint, new StringContent(
        JsonSerializer.Serialize(new { connectionString = cs ?? connectionString, sql }), System.Text.Encoding.UTF8, "application/json"));
    var body = await response.Content.ReadAsStringAsync();
    if (response.IsSuccessStatusCode != ok) throw new Exception($"{endpoint}: {(int)response.StatusCode}: {body}");
    return JsonDocument.Parse(body).RootElement.Clone();
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var tables = await Post("postgres-viewer/tables");
Check(tables.GetProperty("tables").EnumerateArray().Any(t =>
    t.GetProperty("schema").GetString() == "viewer.test" && t.GetProperty("name").GetString() == "odd\"table"), "Schema/quoted table missing");
Console.WriteLine("PASS: PostgreSQL schema and quoted table discovery");
var data = await Post("postgres-viewer/query", "SELECT * FROM \"viewer.test\".\"odd\"\"table\" ORDER BY id;");
Check(data.GetProperty("rows").GetArrayLength() == 2, "Wrong row count");
Check(data.GetProperty("rows")[0][1].ValueKind == JsonValueKind.Null, "NULL lost");
Check(data.GetProperty("rows")[0][3].GetString() == "{a,b}", "Array display lost");
Check(data.GetProperty("rowids").GetArrayLength() == 0, "PostgreSQL must not offer SQLite writes");
await Post("postgres-viewer/query", "WITH q AS (SELECT ';' AS val) SELECT * FROM q");
await Post("postgres-viewer/query", "SELECT $$;$$ AS literal -- comment\n");
Console.WriteLine("PASS: PostgreSQL values, NULL, arrays, SELECT/WITH and semicolons in literals");
var capped = await Post("postgres-viewer/query", "SELECT generate_series(1, 12000) AS id");
Check(capped.GetProperty("rows").GetArrayLength() == 10000 && capped.GetProperty("truncated").GetBoolean(), "Result limit missing");
foreach (var sql in new[] {
    "DELETE FROM \"viewer.test\".\"odd\"\"table\"",
    "WITH d AS (DELETE FROM \"viewer.test\".\"odd\"\"table\" RETURNING *) SELECT * FROM d",
    "SELECT 1; COMMIT; DELETE FROM \"viewer.test\".\"odd\"\"table\"",
    "SELECT 1) q; COMMIT; DELETE FROM \"viewer.test\".\"odd\"\"table\"; SELECT 1 --"
}) await Post("postgres-viewer/query", sql, ok: false);
data = await Post("postgres-viewer/query", "SELECT count(*) FROM \"viewer.test\".\"odd\"\"table\"");
Check(data.GetProperty("rows")[0][0].GetString() == "2", "Read-only query changed data");
Console.WriteLine("PASS: bounded results and rejection of writes, writable CTE and statement batches");
var malformed = await Post("postgres-viewer/tables", ok: false, cs: "Password=do-not-expose;bad-key=secret");
Check(!malformed.GetRawText().Contains("do-not-expose") && !malformed.GetRawText().Contains("secret"), "Error leaked credentials");
await Post("postgres-viewer/tables", ok: false, cs: "");
await Post("postgres-viewer/query", ok: false);
Console.WriteLine("PASS: invalid connection and missing inputs");
var dbFile = Path.Combine(Path.GetTempPath(), "sql_viewer_" + Guid.NewGuid().ToString("N") + ".db");
try
{
    using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbFile};Pooling=False"))
    {
        db.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE sample (id INTEGER, name TEXT); INSERT INTO sample VALUES (1, 'SQLite still works');";
        cmd.ExecuteNonQuery();
    }
    using var response = await client.PostAsync("sqlite-viewer/query?sql=" + Uri.EscapeDataString("SELECT * FROM sample"),
        new ByteArrayContent(await File.ReadAllBytesAsync(dbFile)));
    var sqliteData = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    Check(response.IsSuccessStatusCode && sqliteData.GetProperty("rows")[0][1].GetString() == "SQLite still works", "SQLite regression");
    Console.WriteLine("PASS: SQLite file query remains functional");
}
finally { File.Delete(dbFile); }
if (args.Contains("--serve")) { Console.WriteLine("READY " + url); await Task.Delay(Timeout.Infinite); }
listener.Stop();
await server;
