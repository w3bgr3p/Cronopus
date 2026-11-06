using System.Text.Json;
using System.Xml.Linq;
using Cronopus;

static class BasInputCases
{
    public static void Run(string root)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "fixtures", "uber-inputs.xml");
        var original = File.ReadAllBytes(source);
        var metadata = JsonSerializer.SerializeToElement(BasXmlProject.Resources(source));
        var visible = metadata.GetProperty("Resources").EnumerateArray().Where(f => f.GetProperty("Visible").GetBoolean()).ToArray();
        var names = visible.Select(f => f.GetProperty("Name").GetString()).ToArray();
        if (!names.SequenceEqual(new[] { "DiagLog", "UberService", "UberCountry", "UberProxyCountry", "UberProvider", "Thread" }))
            throw new Exception("Native BAS input visibility mismatch");
        if (!visible[0].GetProperty("Editable").GetBoolean() || visible[0].GetProperty("DefaultValue").GetString() != "false"
            || visible[0].GetProperty("Options").GetArrayLength() != 2 || visible[1].GetProperty("Label").GetString() != "Simroute service code")
            throw new Exception("Native BAS select or description mismatch");
        var prepared = BasXmlProject.Prepare(source, "{\"DiagLog\":\"true\",\"Thread\":3,\"UberCountry\":\"de\",\"UberProxyCountry\":\"\"}");
        var models = prepared.Document.Root!.Element("ModelList")!.Elements("Model").ToDictionary(m => m.Element("Name")!.Value);
        if (models["DiagLog"].Element("Selected")!.Value != "1" || models["DiagLog"].Element("Value") != null
            || models["Thread"].Element("Value")!.Value != "3" || models["UberCountry"].Element("Value")!.Value != "de")
            throw new Exception("Resource override did not reach native BAS fields");
        Reject(() => BasXmlProject.Prepare(source, "{\"DiagLog\":\"invalid\"}"));
        if (!original.SequenceEqual(File.ReadAllBytes(source))) throw new Exception("Native source fixture changed");
        Console.WriteLine("PASS native BAS visible inputs, descriptions, editable Select and Selected-index override");
    }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid select option accepted"); }
}
