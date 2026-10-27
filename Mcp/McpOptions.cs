namespace Cronopus.Mcp;

public sealed record McpOptions(string? ApiUrl, string RuntimeFile, bool ReadOnly, int TimeoutSeconds)
{
    public static McpOptions Parse(string[] args)
    {
        string? url = null;
        var runtime = Path.Combine(AppContext.BaseDirectory, ".runtime", "mcp.json");
        var readOnly = false;
        var timeout = 30;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value");
            switch (args[i].ToLowerInvariant())
            {
                case "--mcp": break;
                case "--read-only": readOnly = true; break;
                case "--api-url": url = Value(); break;
                case "--runtime-file": runtime = Path.GetFullPath(Value()); break;
                case "--timeout-seconds":
                    if (!int.TryParse(Value(), out timeout) || timeout is < 1 or > 300)
                        throw new ArgumentException("timeout-seconds must be 1..300");
                    break;
                default: throw new ArgumentException("Unknown MCP option: " + args[i]);
            }
        }
        if (url != null) ValidateUrl(url);
        return new(url, runtime, readOnly, timeout);
    }

    public static Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            !(uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "[::1]") ||
            uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new ArgumentException("api-url must be a loopback HTTP origin, e.g. http://localhost:33333");
        return uri;
    }
}
