using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Cronopus.Mcp;

[McpServerResourceType]
public sealed class McpDocuments
{
    public string Read(string topic)
    {
        var filename = topic switch
        {
            "mcp" => "Mcp/AGENT.md",
            "setup" => "Mcp/README.md",
            "task-api" => "docs-vault-en/Navigation/Pages/Task API.md",
            "script-agent" => "docs-vault-en/Navigation/Pages/Script agent instructions.md",
            "tasker" => "docs-vault-en/Navigation/Pages/Tasker.md",
            "remote-tasker" => "docs-vault-en/Navigation/Pages/Remote Tasker.md",
            _ => throw new ArgumentException("Unknown topic. Use mcp, setup, task-api, script-agent, tasker or remote-tasker.")
        };
        var path = Path.Combine(AppContext.BaseDirectory, filename);
        if (!File.Exists(path)) throw new McpException("Documentation is missing from this installation. Rebuild or reinstall Cronopus.");
        return File.ReadAllText(path);
    }

    [McpServerResource(UriTemplate = "cronopus://docs/mcp", Name = "MCP guide", MimeType = "text/markdown")]
    [Description("Compact English agent contract. Read only if needed.")]
    public string Guide() => Read("mcp");

    [McpServerResource(UriTemplate = "cronopus://docs/{topic}", Name = "Cronopus documentation", MimeType = "text/markdown")]
    [Description("English topics: mcp, setup, task-api, script-agent, tasker, remote-tasker.")]
    public string Document(string topic) => Read(topic);
}
