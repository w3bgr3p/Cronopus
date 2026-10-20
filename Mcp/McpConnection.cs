using System.Net.Http.Json;
using System.Text.Json;

namespace z3nDash.Mcp;

public sealed class McpConnection(McpOptions options) : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
    public async Task<object?> Status(CancellationToken ct)
    {
        var (_, _, status) = await Resolve(ct);
        return new { dashboard = status, read_only = options.ReadOnly, transport = "stdio" };
    }
    private async Task<(Uri Url, string Instance, JsonElement Status)> Resolve(CancellationToken ct)
    {
        string? expected = null;
        var url = options.ApiUrl;
        if (url == null)
        {
            try
            {
                using var file = JsonDocument.Parse(await File.ReadAllTextAsync(options.RuntimeFile, ct));
                url = file.RootElement.GetProperty("api_url").GetString();
                expected = file.RootElement.GetProperty("instance_id").GetString();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
            { throw new McpBackendException(new("discovery_unavailable", "Start the updated dashboard from this installation, or supply --api-url / --runtime-file.")); }
        }
        var origin = McpOptions.ValidateUrl(url ?? "");
        var status = await Send(origin, "GET", "/mcp/status", null, null, ct);
        if (status.ValueKind != JsonValueKind.Object || !status.TryGetProperty("application", out var app) || app.GetString() != "z3nDash" ||
            !status.TryGetProperty("contract_version", out var version) || version.GetInt32() != 1 ||
            !status.TryGetProperty("instance_id", out var instance) || string.IsNullOrEmpty(instance.GetString()))
            throw new McpBackendException(new("incompatible_dashboard", "Endpoint is not a compatible z3nDash. Update the dashboard."));
        if (expected != null && expected != instance.GetString())
            throw new McpBackendException(new("instance_changed", "Runtime file is stale or belongs to another dashboard. Restart the intended dashboard."));
        return (origin, instance.GetString()!, status);
    }
    public async Task<JsonElement> Request(string method, string path, object? body, CancellationToken ct)
    {
        var (url, instance, _) = await Resolve(ct);
        return await Send(url, method, path, body, instance, ct);
    }
    public static string TaskPath(string route, string node)
    {
        if (node == "local") return "/tasker/" + route;
        TaskerService.ValidateId(node);
        return "/bridge-local/" + node + "/api/tasker/" + route;
    }
    private async Task<JsonElement> Send(Uri origin, string method, string path, object? body, string? instance, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var mutation = method != "GET";
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(origin, path));
            request.Headers.Add("X-Bridge-Local", "1");
            request.Headers.Add("X-Request-ID", Guid.NewGuid().ToString("N"));
            if (instance != null) request.Headers.Add("X-Z3nDash-Instance", instance);
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + read > 8 * 1024 * 1024)
                    throw new McpBackendException(new("response_too_large", "Dashboard response exceeds 8 MiB. Use a narrower query.", (int)response.StatusCode, mutation));
                buffer.Write(bytes, 0, read);
            }
            var data = buffer.Length == 0 ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.Deserialize<JsonElement>(buffer.ToArray());
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var message = status >= 500 ? "Dashboard request failed; inspect dashboard logs." :
                    data.ValueKind == JsonValueKind.Object && data.TryGetProperty("error", out var error) ? error.ToString() : "Dashboard rejected the request.";
                var code = status switch { 400 => "invalid_argument", 401 or 403 => "access_denied", 404 => "not_found",
                    409 => "conflict", 428 => "revision_required", 503 => "database_unavailable", _ => "dashboard_error" };
                throw new McpBackendException(new(code, message, status, mutation && status >= 500));
            }
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                throw new McpBackendException(new("operation_failed", data.TryGetProperty("error", out var error) ? error.ToString() : "Dashboard operation failed."));
            return data;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new McpBackendException(new("timeout", "Dashboard timed out. Check state before retrying a mutation.", null, mutation)); }
        catch (HttpRequestException)
        { throw new McpBackendException(new("dashboard_unavailable", "Cannot reach the dashboard. Start z3nDash and check the selected URL.", null, mutation)); }
        catch (JsonException)
        { throw new McpBackendException(new("invalid_response", "Expected JSON from an updated z3nDash dashboard.", null, mutation)); }
    }
    public void Dispose() => _http.Dispose();
}
