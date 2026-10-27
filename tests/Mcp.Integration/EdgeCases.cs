using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Cronopus.Mcp;

internal static class EdgeCases
{
    public static async Task Run()
    {
        var port = 39475;
        using (var probe = new TcpListener(IPAddress.Loopback, port))
        {
            try { probe.Start(); }
            catch (SocketException)
            { using var fallback = new TcpListener(IPAddress.Loopback, 0); fallback.Start(); port = ((IPEndPoint)fallback.LocalEndpoint).Port; }
        }
        var url = $"http://localhost:{port}";
        using var listener = new HttpListener(); listener.Prefixes.Add(url + "/"); listener.Start();
        var writes = 0;
        var server = Task.Run(async () =>
        {
            var status = await listener.GetContextAsync();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { application = "Cronopus", contract_version = 1, instance_id = "fixture", database_connected = false });
            status.Response.ContentType = "application/json";
            await status.Response.OutputStream.WriteAsync(bytes); status.Response.Close();
            var request = await listener.GetContextAsync();
            Interlocked.Increment(ref writes);
            if (request.Request.Headers["X-Cronopus-Instance"] != "fixture") throw new Exception("identity guard missing");
            await Task.Delay(1600);
            request.Response.Close();
        });
        using var connection = new McpConnection(new McpOptions(url, "", false, 1));
        try
        {
            await connection.Request("POST", "/tasker/run", new { id = "fixture" }, CancellationToken.None);
            throw new Exception("mutation timeout was ignored");
        }
        catch (McpBackendException ex)
        {
            if (ex.Error.code != "timeout" || !ex.Error.outcome_unknown) throw new Exception("timeout lost outcome uncertainty");
        }
        await server;
        if (writes != 1) throw new Exception("mutation was retried");
        foreach (var bad in new[] { "http://example.com", "http://localhost:33333/path", "http://user@localhost:33333", "https://localhost:33333" })
        {
            try { McpOptions.ValidateUrl(bad); throw new Exception("unsafe API origin accepted"); }
            catch (ArgumentException) { }
        }
        Console.WriteLine("PASS mutation timeout uncertainty, no retries, identity header and loopback validation");
    }
}
