using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Cronopus.Mcp;

public sealed record McpError(string code, string message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? http_status = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool outcome_unknown = false);
public sealed class McpBackendException(McpError error) : Exception(error.message)
{
    public McpError Error { get; } = error;
}
public static class McpResults
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    // The SDK binds arguments before Execute runs, so a missing one would surface as its generic error text.
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> RequireArguments(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) =>
        {
            if (request.MatchedPrimitive is McpServerTool tool &&
                tool.ProtocolTool.InputSchema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            {
                var missing = required.EnumerateArray().Select(r => r.GetString()!)
                    .Where(name => request.Params?.Arguments?.ContainsKey(name) != true).ToList();
                if (missing.Count > 0)
                    return await Execute(() => throw new ArgumentException(string.Join(", ", missing) + (missing.Count == 1 ? " is required" : " are required")));
            }
            return await next(request, ct);
        };

    public static async Task<CallToolResult> Execute(Func<Task<object?>> action)
    {
        object? result;
        var failed = false;
        object Failure(McpError error) { failed = true; return new { error }; }
        try { result = await action(); }
        catch (McpBackendException ex) { result = Failure(ex.Error); }
        catch (ArgumentException ex) { result = Failure(new("invalid_argument", ex.Message)); }
        catch (FileNotFoundException) { result = Failure(new("not_found", "Document or runtime file not found. Start the updated dashboard first.")); }
        catch (McpException ex) { result = Failure(new("not_found", ex.Message)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { result = Failure(new("internal_error", "MCP operation failed. Inspect dashboard logs.")); }
        return new CallToolResult
        {
            IsError = failed ? true : null,
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(result, Json) }]
        };
    }
}
