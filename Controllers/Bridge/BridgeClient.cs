using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
namespace z3nDash;

public sealed class BridgeClient(BridgeStore store)
{
    private readonly Dictionary<string, HttpClient> _clients = new();
    private readonly object _gate = new();
    public async Task<HttpResponseMessage> SendAsync(string connectionId, HttpMethod method, string path, HttpContent? content, CancellationToken ct,string? requestId=null)
    {
        var connection = store.Connection(connectionId);
        if (!path.StartsWith("/bridge/v1/", StringComparison.Ordinal) || path.Contains("..") || path.Contains('\\')) throw new ArgumentException("Invalid API path");
        var uri = new Uri(connection.Address.TrimEnd('/') + path);
        if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("HTTPS address required");
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, errors) => cert != null &&
            (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0 &&
            cert.NotBefore.ToUniversalTime() <= DateTime.UtcNow && cert.NotAfter.ToUniversalTime() > DateTime.UtcNow &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.RawData), Convert.FromHexString(connection.Fingerprint.Replace(":", "").Replace(" ", "")));
        HttpClient client;
        var cacheKey = connection.Id + connection.Address + connection.Secret + connection.Fingerprint;
        lock (_gate)
        {
            if (!_clients.TryGetValue(cacheKey, out client!))
            {
                if (_clients.Count >= 100) { foreach (var old in _clients.Values) old.Dispose(); _clients.Clear(); }
                _clients[cacheKey] = client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            }
            else handler.Dispose();
        }
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Secret);
        if (method != HttpMethod.Get) request.Headers.Add("X-Request-ID", requestId??Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(path.StartsWith("/bridge/v1/uploads")?TimeSpan.FromMinutes(5):TimeSpan.FromSeconds(15));
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }
    public async Task<JsonElement> CheckAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(id, HttpMethod.Get, "/bridge/v1/info", null, ct);
        response.EnsureSuccessStatusCode();
        var info = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (info.GetProperty("node_id").GetString() != store.Connection(id).NodeId) throw new ArgumentException("Server identity changed");
        if (info.GetProperty("api_version").GetInt32() != 1) throw new ArgumentException("Incompatible API version");
        return info;
    }
}
