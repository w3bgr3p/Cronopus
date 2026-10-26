using System.IO.Compression;
using System.Security.Cryptography;
using z3nDash;

var root = Path.Combine(Path.GetTempPath(), "z3ndash-update-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var zip = Path.Combine(root, "package.zip");
    void Package(params (string Path, string Content)[] files)
    {
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (path, content) in files)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(content);
        }
    }
    string Digest() => "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
    async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Unsafe package was accepted");
    }
    Package(("z3nDash.exe", "new exe"), ("z3nDash.dll", "new dll"),
        ("wwwroot/config.html", "new UI"), ("appsettings.secrets.json", "must not ship"),
        ("ui-state.json", "must not overwrite"), ("logs/current.jsonl", "must not overwrite"),
        (".playwright/node/win32_x64/node.exe", "required browser driver"));
    var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
    var staged = Path.Combine(root, "staged");
    await UpdatePackage.StageAsync(zip, staged, hash);
    if (File.ReadAllText(Path.Combine(staged, "wwwroot/config.html")) != "new UI")
        throw new Exception("UI not extracted");
    if (File.Exists(Path.Combine(staged, "appsettings.secrets.json"))
        || File.Exists(Path.Combine(staged, "ui-state.json")) || Directory.Exists(Path.Combine(staged, "logs")))
        throw new Exception("Mutable application state included in update payload");
    if (!File.Exists(Path.Combine(staged, ".playwright/node/win32_x64/node.exe")))
        throw new Exception("Browser runtime omitted from update");
    Console.WriteLine("PASS verified package staged without touching running installation");
    await Reject(() => UpdatePackage.StageAsync(zip, Path.Combine(root, "bad-hash"), "sha256:" + new string('0', 64)));
    Console.WriteLine("PASS corrupt download rejected before extraction");
    File.Delete(zip);
    Package(("z3nDash.exe", "new exe"), ("../escape.txt", "escape"));
    await Reject(() => UpdatePackage.StageAsync(zip, Path.Combine(root, "traversal"), Digest()));
    if (File.Exists(Path.Combine(root, "escape.txt"))) throw new Exception("ZIP escaped destination");
    Console.WriteLine("PASS archive traversal rejected");
    foreach (var name in new[] { "appsettings.secrets.json", "ui-state.json", "bridge/state.json", "logs/data.jsonl", "data.sqlite", ".runtime/mcp.json" })
        if (UpdatePackage.IsApplicationFile(name)) throw new Exception("Mutable file accepted: " + name);
    Console.WriteLine("PASS configuration, runtime state, logs and databases excluded");
    File.Delete(zip);
    Package(("wwwroot/config.html", "incomplete"));
    await Reject(() => UpdatePackage.StageAsync(zip, Path.Combine(root, "incomplete"), Digest()));
    Console.WriteLine("PASS incomplete release rejected");
}
finally { Directory.Delete(root, true); }
