using System.Text.Json;
using Cronopus;

static class LiveCases
{
    public static async Task Run(string root)
    {
        var engine = Environment.GetEnvironmentVariable("CRONOPUS_BAS_TEST_ENGINE");
        if (string.IsNullOrEmpty(engine))
        {
            var download = Path.Combine(root, "download");
            var archive = Environment.GetEnvironmentVariable("CRONOPUS_BAS_TEST_ARCHIVE");
            if (!string.IsNullOrEmpty(archive))
            {
                var cache = Path.Combine(download, "cache", "31.1.0"); Directory.CreateDirectory(cache);
                File.Copy(archive, Path.Combine(cache, "engine64.zip"));
            }
            engine = await new BasEnginePackage(download).PrepareAsync("31.1.0", Console.WriteLine, CancellationToken.None);
        }
        var source = Path.Combine(root, "source.xml");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "smoke.xml"), source);
        var original = File.ReadAllBytes(source);
        var storage = new BridgeStore(Path.Combine(root, "state"));
        await using var service = new BasControlService(storage, () => new BasControlClient(Path.Combine(root, "execution"), engine));
        var project = service.Save(new BasProjectEdit { Name = "Local XML live", ProjectPath = source });
        service.Run(project.Id, 1);
        long cursor = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        while (true)
        {
            foreach (var line in service.Events(project.Id, cursor)) { Console.WriteLine(line.Type + ": " + line.Text); cursor = line.Cursor; }
            var run = service.Runs(project.Id).Single();
            if (run.Status == "succeeded")
            {
                if (run.Result?.GetProperty("successes").GetInt32() != 1 || !original.SequenceEqual(File.ReadAllBytes(source)))
                    throw new Exception("Live XML result or source preservation failed");
                Console.WriteLine("PASS real BAS 31.1.0 local XML execution without compilation/publication; native Success(1) report, source unchanged");
                var xml = System.Xml.Linq.XDocument.Load(source);
                xml.Root!.SetElementValue("EngineVersion", "31.0.0"); xml.Save(source);
                service.Run(project.Id, 1);
                while (service.Runs(project.Id)[0].Status is "starting" or "running") await Task.Delay(20, timeout.Token);
                if (!service.Runs(project.Id)[0].Error.Contains("engine version changed")) throw new Exception("engine-version drift was not rejected");
                Console.WriteLine("PASS source engine-version drift requires runtime release");
                await SelectLiveCases.Run(root, engine);
                await ContextLiveCases.Run(root, engine);
                await BrowserLiveCases.Run(root, engine);
                await NoReport(root, engine);
                return;
            }
            if (run.Status == "failed") throw new Exception(run.Error);
            await Task.Delay(250, timeout.Token);
        }
    }
    private static async Task NoReport(string root, string engine)
    {
        var source = Path.Combine(root, "invalid-runtime.xml");
        File.WriteAllText(source, "<BrowserAutomationStudioProject><Script><![CDATA[invalid native project]]></Script><EngineVersion>31.1.0</EngineVersion><ModelList/></BrowserAutomationStudioProject>");
        await using var client = new BasControlClient(Path.Combine(root, "invalid"), engine);
        await client.StartAsync(new BasProject("probe", "1", "Invalid", source, "{}", 1), CancellationToken.None);
        try
        {
            await client.RunAsync(JsonSerializer.SerializeToElement(new { }), 1, CancellationToken.None);
            throw new Exception("An exited engine without a report was marked successful");
        }
        catch (BasFunctionException ex) when (ex.Message.Contains("without an execution report"))
        { Console.WriteLine("PASS real BAS exit 0 without native report remains unconfirmed"); }
    }
}
