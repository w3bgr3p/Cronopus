using System.Text.Json;
using System.Xml.Linq;
using Cronopus;

static class SelectLiveCases
{
    public static async Task Run(string root, string engine)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var document = XDocument.Load(Path.Combine(fixture, "smoke.xml"), LoadOptions.PreserveWhitespace);
        document.Root!.Element("ModelList")?.Remove();
        document.Root.Add(new XElement(XDocument.Load(Path.Combine(fixture, "uber-inputs.xml")).Root!.Element("ModelList")!));
        var source = Path.Combine(root, "native-inputs.xml"); document.Save(source);
        var original = File.ReadAllBytes(source);
        await using var client = new BasControlClient(Path.Combine(root, "native-inputs"), engine);
        await client.StartAsync(new BasProject("native", "1", "Native inputs", source, "{}", 1), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await client.RunAsync(JsonSerializer.SerializeToElement(new { DiagLog = "true", UberCountry = "de", Thread = 3 }), 1, timeout.Token);
        var actual = XDocument.Load(Path.Combine(Path.GetDirectoryName(engine)!, "Actual.xml"));
        var models = actual.Root!.Element("ModelList")!.Elements("Model").ToDictionary(model => model.Element("Name")!.Value);
        if (result.GetProperty("successes").GetInt32() != 1 || models["DiagLog"].Element("Selected")!.Value != "1"
            || models["UberCountry"].Element("Value")!.Value != "de" || models["Thread"].Element("Value")!.Value != "3")
            throw new Exception("Native BAS did not persist selected input overrides to Actual.xml");
        if (!original.SequenceEqual(File.ReadAllBytes(source))) throw new Exception("Live source changed");
        Console.WriteLine("PASS actual BAS accepts native input configuration and persists Select index=1, country=de, Thread=3 in Actual.xml; source unchanged");
    }
}
