using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Cronopus;

public sealed record TaskerReply(int StatusCode, string Text, string ContentType);

public sealed class TaskerExchange
{
    public HttpListenerContext? Native { get; }
    public TaskerInput Request { get; }
    public TaskerOutput Response { get; }
    public CancellationToken Cancellation { get; set; }
    public TaskerExchange(HttpListenerContext context)
    { Native = context; Request = new(context.Request); Response = new(context.Response); }
    public TaskerExchange(string method, string path, JsonElement? body)
    { Request = new(method, path, body); Response = new(); }
}

public sealed class TaskerInput
{
    public Uri Url { get; }
    public string HttpMethod { get; }
    public Stream InputStream { get; }
    public NameValueCollection QueryString { get; } = new();
    public TaskerInput(HttpListenerRequest request)
    { Url = request.Url!; HttpMethod = request.HttpMethod; InputStream = request.InputStream; QueryString = request.QueryString; }
    public TaskerInput(string method, string path, JsonElement? body)
    {
        Url = new Uri("http://localhost" + path); HttpMethod = method;
        InputStream = new MemoryStream(Encoding.UTF8.GetBytes(body?.GetRawText() ?? "{}"));
        foreach (var pair in Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        { var p = pair.Split('=', 2); QueryString.Add(WebUtility.UrlDecode(p[0]), p.Length > 1 ? WebUtility.UrlDecode(p[1]) : ""); }
    }
}

public sealed class TaskerOutput
{
    private readonly HttpListenerResponse? _native;
    private int _status = 200;
    private string _type = "application/json";
    public int StatusCode { get => _native?.StatusCode ?? _status; set { _status = value; if (_native != null) _native.StatusCode = value; } }
    public string ContentType { get => _native?.ContentType ?? _type; set { _type = value; if (_native != null) _native.ContentType = value; else if(OutputStream is TaskerBuffer buffer)buffer.Tail=value.StartsWith("text/event-stream"); } }
    public long ContentLength64 { set { if (_native != null) _native.ContentLength64 = value; } }
    public bool SendChunked { set { if (_native != null) _native.SendChunked = value; } }
    public WebHeaderCollection Headers { get; }
    public Stream OutputStream { get; }
    public Action<string>? Progress { set {if(OutputStream is TaskerBuffer buffer)buffer.Progress=value;} }
    public TaskerOutput(HttpListenerResponse? native = null)
    { _native = native; Headers = native?.Headers ?? new(); OutputStream = native?.OutputStream ?? new TaskerBuffer(); }
    public void Close() { _native?.Close(); }
    public void AddHeader(string name, string value) => Headers.Add(name, value);
    public async Task WriteJson(object data)
    {
        ContentType = "application/json";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data); ContentLength64 = bytes.Length;
        await OutputStream.WriteAsync(bytes);
    }
    public TaskerReply Reply() => new(StatusCode, Encoding.UTF8.GetString(((MemoryStream)OutputStream).ToArray()), ContentType);
}
