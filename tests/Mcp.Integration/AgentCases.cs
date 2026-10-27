using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

internal static class AgentCases
{
    public static async Task Run(McpClient client)
    {
        var tools = await client.ListToolsAsync();
        var docSchema = tools.Single(t => t.Name == "read_document").JsonSchema.GetProperty("properties");
        if (docSchema.TryGetProperty("language", out _)) throw new Exception("Agent documentation still exposes multiple languages");
        foreach (var tool in tools)
        {
            if (Regex.IsMatch(tool.Description ?? "", "[А-Яа-яЁё]")) throw new Exception("Non-English tool description");
            if (tool.ProtocolTool.OutputSchema != null) throw new Exception("Repeated generic output schema still in tool catalog");
        }
        var reply = await client.CallToolAsync("read_document", new Dictionary<string, object?>());
        if (reply.StructuredContent != null) throw new Exception("Tool result duplicates the JSON payload");
        var doc = JsonSerializer.Deserialize<JsonElement>(reply.Content.OfType<TextContentBlock>().Single().Text);
        var text = doc.GetProperty("text").GetString()!;
        if (text.Length > 4000 || Regex.IsMatch(text, "[А-Яа-яЁё]")) throw new Exception("Default guide must be short and English");
        if (!text.Contains("run_task")) throw new Exception("Agent guide lost core execution semantics");
        var templates = await client.ListResourceTemplatesAsync();
        if (templates.Any(t => t.UriTemplate.Contains("language"))) throw new Exception("Agent resource templates expose translated docs");
        foreach (var topic in new[] { "setup", "task-api", "script-agent", "tasker", "remote-tasker" })
        {
            var resource = await client.ReadResourceAsync("cronopus://docs/" + topic);
            if (Regex.IsMatch(resource.Contents.OfType<TextResourceContents>().Single().Text, "[А-Яа-яЁё]"))
                throw new Exception("Non-English agent documentation: " + topic);
        }
        var setupArgs = new Dictionary<string, object?> { ["topic"] = "setup", ["limit"] = 128 };
        var setup = await client.CallToolAsync("read_document", setupArgs);
        var first = JsonSerializer.Deserialize<JsonElement>(setup.Content.OfType<TextContentBlock>().Single().Text);
        if (first.GetProperty("text").GetString()!.Length != 128 || first.GetProperty("next_offset").GetInt32() != 128)
            throw new Exception("Documentation paging failed");
        if (tools.Single(t => t.Name == "list_tasks").JsonSchema.GetProperty("properties").GetProperty("limit").GetProperty("default").GetInt32() != 20)
            throw new Exception("Default task page is too large");
        if (tools.Single(t => t.Name == "read_task_output").JsonSchema.GetProperty("properties").GetProperty("limit").GetProperty("default").GetInt32() != 2000)
            throw new Exception("Default output page is too large");
        Console.WriteLine("PASS English-only agent contract, compact guide and single-copy results");
    }
}
