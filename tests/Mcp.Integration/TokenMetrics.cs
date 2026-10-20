using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

internal static class TokenMetrics
{
    public static async Task Capture(McpClient client, string file)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var tools = await client.ListToolsAsync();
        var guide = await client.CallToolAsync("read_document", new Dictionary<string, object?>());
        var samples = new Dictionary<string, string>
        {
            ["tools_list"] = JsonSerializer.Serialize(new { tools = tools.Select(t => t.ProtocolTool) }, json),
            ["default_document"] = JsonSerializer.Serialize(guide, json)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(samples));
        foreach (var pair in samples) Console.WriteLine($"METRIC {pair.Key}: {pair.Value.Length} JSON characters");
    }
}
