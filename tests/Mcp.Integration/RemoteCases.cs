using System.Text.Json;
using ModelContextProtocol.Client;
using Cronopus;

internal static class RemoteCases
{
    public static async Task Run(McpClient client, BridgeStore store, BridgeHost host, DbConnectionService database,
        Func<McpClient, string, object?, string?, Task<JsonElement>> invoke)
    {
        Task<JsonElement> Call(string name, object? args = null, string? error = null) => invoke(client, name, args, error);
        store.State.Settings.BindAddress = "127.0.0.1";
        store.State.Settings.AdvertisedAddress = "127.0.0.1";
        store.State.Settings.Port = 39473;
        Uri address;
        try { address = await host.StartAsync(CancellationToken.None); }
        catch (IOException) { store.State.Settings.Port = 0; address = await host.StartAsync(CancellationToken.None); }
        var key = store.CreateKey("MCP integration only");
        using var cert = store.Certificate();
        var remote = new BridgeConnection { Name = "MCP fixture", Address = address.ToString().TrimEnd('/'),
            NodeId = store.NodeId, Fingerprint = BridgeStore.Fingerprint(cert), Secret = key.Secret };
        store.State.Connections.Add(remote); store.Save();
        var nodes = await Call("list_nodes");
        if (nodes.GetRawText().Contains(key.Secret)) throw new Exception("Remote credential leaked");
        var node = remote.Id;
        var created = (await Call("create_task", new { node, settings = new { name = "mcp remote fixture", executor = "cmd", script_path = "echo remote" } }));
        var id = created.GetProperty("id").GetString();
        var detail = (await Call("get_task", new { id, node }));
        var revision = detail.GetProperty("_revision").GetString();
        await Call("update_task", new { id, node, revision, settings = new { args = "--remote" } });
        await Call("pause_task", new { id, node });
        await Call("defer_task", new { id, node, delay_seconds = 60 });
        await Call("resume_task", new { id, node });
        await Call("list_tasks", new { node, search = "mcp remote", limit = 1 });
        await Call("list_task_runtime", new { id, node, kind = "queue" });
        await Call("list_task_runtime", new { id, node, kind = "instances" });
        var expected = string.Concat(Enumerable.Range(0, 3000).Select(i => $"{i:D4}|\U0001F642"));
        database.GetDb().Query($"UPDATE schedules SET last_output='{expected}' WHERE id='{id}'", thrw: true);
        string? cursor = null;
        var collected = new System.Text.StringBuilder();
        for (var i = 0; i < 20; i++)
        {
            var page = await Call("read_task_output", new { id, node, cursor });
            var chunk = page.GetProperty("output").GetString()!;
            if (chunk.Length > 2000) throw new Exception("Remote output exceeded the default character limit");
            cursor = page.GetProperty("next_cursor").GetString();
            if (chunk.Length == 0) break;
            collected.Append(chunk);
        }
        if (collected.ToString() != expected) throw new Exception("Remote cursor lost or repeated text");
        await Call("read_task_output", new { id, node, cursor, run_id = "wrong-run" }, "invalid_argument");
        revision = (await Call("get_task", new { id, node })).GetProperty("_revision").GetString();
        await Call("delete_task", new { id, node, revision });
        await host.StopAsync(CancellationToken.None);
        Console.WriteLine("PASS remote MCP tools over real certificate-pinned HTTPS bridge; credentials hidden");
    }
}
