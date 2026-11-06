using System.Text.Json;
using System.Xml.Linq;
using Cronopus;

static class ContextLiveCases
{
    public static async Task Run(string root, string engine)
    {
        var folder = Path.Combine(root, "source-tree");
        Directory.CreateDirectory(Path.Combine(folder, "Essentials")); Directory.CreateDirectory(Path.Combine(folder, "data"));
        var sourceFolder = Path.Combine(folder, "running", "uber"); Directory.CreateDirectory(sourceFolder);
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "context.xml"), LoadOptions.PreserveWhitespace);
        var source = Path.Combine(sourceFolder, "probe.xml"); document.Save(source);
        var original = File.ReadAllBytes(source);
        var lines = new List<string>();
        await using var client = new BasControlClient(Path.Combine(root, "context-run"), engine);
        client.Message += (_, message) => lines.Add(message.GetProperty("text").GetString() ?? "");
        await client.StartAsync(new BasProject("context", "1", "Context probe", source, "{}", 1), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await client.RunAsync(JsonSerializer.SerializeToElement(new { }), 1, timeout.Token);
        if (!lines.Any(line => line.Contains("CRONOPUS_PROJECT_PATH=" + source.Replace('\\', '/'))))
            throw new Exception("Native BAS project_path() lost source location: " + string.Join("\n", lines));
        if (!lines.Any(line => line.Contains("CRONOPUS_SHARED_ROOT=" + folder.Replace('\\', '/'))))
            throw new Exception("Native BAS did not find source-tree Essentials/data");
        if (!original.SequenceEqual(File.ReadAllBytes(source))) throw new Exception("Context injection changed source XML");
        Console.WriteLine("PASS native BAS project_path() reports original source XML from isolated engine; source unchanged");
    }
}
