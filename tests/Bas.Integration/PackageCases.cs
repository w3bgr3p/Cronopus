using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Cronopus;

static class PackageCases
{
    public static async Task Run(string root)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("FastExecuteScript.exe").Open())) await writer.WriteAsync("fixture engine");
        var bytes = zip.ToArray();
        var handler = new MetadataHandler(bytes);
        using var http = new HttpClient(handler);
        var folder = Path.Combine(root, "package");
        var executable = await new BasEnginePackage(folder, http).PrepareAsync("31.1.0", _ => { }, CancellationToken.None);
        if (File.ReadAllText(executable) != "fixture engine" || handler.Requests.Count != 2
            || handler.Requests.Any(url => url.Scheme != "https") || handler.Requests[1].Host != "downloads.bablosoft.com")
            throw new Exception("HTTP publisher URL was not upgraded before download/extraction");
        var bad = new MetadataHandler(bytes) { Checksum = new string('0', 40) };
        using var badHttp = new HttpClient(bad);
        var badRoot = Path.Combine(root, "bad-package");
        try
        {
            await new BasEnginePackage(badRoot, badHttp).PrepareAsync("31.1.0", _ => { }, CancellationToken.None);
            throw new Exception("Corrupted BAS archive accepted");
        }
        catch (IOException ex) when (ex.Message.Contains("checksum mismatch")) { }
        if (Directory.GetFiles(badRoot, "*.tmp", SearchOption.AllDirectories).Length != 0)
            throw new Exception("Failed download left temporary files");
        var untrusted = new MetadataHandler(bytes) { Url = "http://example.invalid/engine.zip" };
        using var untrustedHttp = new HttpClient(untrusted);
        try
        {
            await new BasEnginePackage(Path.Combine(root, "untrusted-package"), untrustedHttp).PrepareAsync("31.1.0", _ => { }, CancellationToken.None);
            throw new Exception("Nonpublisher HTTP endpoint accepted");
        }
        catch (IOException ex) when (ex.Message.Contains("must use HTTPS")) { }
        if (untrusted.Requests.Count != 1) throw new Exception("Insecure download was sent");
        Console.WriteLine("PASS publisher HTTP metadata upgraded to HTTPS, download/extraction and checksum rejection");
    }
    sealed class MetadataHandler(byte[] archive) : HttpMessageHandler
    {
        public string Checksum = Convert.ToHexString(SHA1.HashData(archive));
        public string Url = "http://downloads.bablosoft.com/distr/fixture.zip";
        public List<Uri> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            var content = request.RequestUri!.AbsolutePath.EndsWith(".meta.json")
                ? (HttpContent)new StringContent(JsonSerializer.Serialize(new { Url, Checksum }))
                : new ByteArrayContent(archive);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
