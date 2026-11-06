using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Cronopus;

public sealed record BasPreparedProject(XDocument Document, string EngineVersion, string SourceDirectory);
public static partial class BasXmlProject
{
    private static string? ResourceField(string? type) => type switch
    { "FixedString" or "RandomString" or "FixedInteger" => "Value", "LinesFromFile" => "Filename", "Select" => "Selected", _ => null };
    public static BasPreparedProject Prepare(string path, string resources)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new ArgumentException("An existing absolute BAS XML path on the execution node is required");
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new ArgumentException("BAS XML exceeds 32 MiB");
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root?.Name.LocalName != "BrowserAutomationStudioProject" || root.Element("Script") == null)
            throw new ArgumentException("Select a local BrowserAutomationStudio XML project");
        var version = root.Element("EngineVersion")?.Value ?? "";
        if (!Version.TryParse(version, out _)) throw new ArgumentException("BAS XML has no valid EngineVersion");
        using var values = JsonDocument.Parse(resources);
        if (values.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Resource overrides must be a JSON object");
        var models = root.Element("ModelList")?.Elements("Model").ToArray() ?? Array.Empty<XElement>();
        foreach (var value in values.RootElement.EnumerateObject())
        {
            var model = models.FirstOrDefault(m => m.Element("Name")?.Value == value.Name)
                ?? throw new ArgumentException("Unknown BAS resource: " + value.Name);
            var type = model.Element("Type")?.Value;
            var text = value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString()! : value.Value.GetRawText();
            var field = ResourceField(type);
            if (field == null) throw new ArgumentException("Resource type requires native BAS settings: " + value.Name + " (" + type + ")");
            if (Flag(model, "NotEmpty") && string.IsNullOrEmpty(text)) throw new ArgumentException("A nonempty value is required for BAS resource: " + value.Name);
            if (type == "Select")
            {
                if (!SingleSelect(model)) throw new ArgumentException("Only native single-choice Select resources can be overridden: " + value.Name);
                var index = Array.IndexOf(SelectOptions(model), text);
                if (index < 0) throw new ArgumentException("Choose a declared BAS option for resource: " + value.Name);
                model.SetElementValue("Selected", index.ToString(CultureInfo.InvariantCulture));
                continue;
            }
            if (type == "FixedInteger")
            {
                if (!int.TryParse(text, CultureInfo.InvariantCulture, out var number)) throw new ArgumentException("Integer resource required: " + value.Name);
                if (int.TryParse(model.Element("MinValue")?.Value, out var min) && number < min ||
                    int.TryParse(model.Element("MaxValue")?.Value, out var max) && number > max)
                    throw new ArgumentException("Resource outside its allowed range: " + value.Name);
            }
            model.SetElementValue(field, text);
        }
        var directory = Path.GetDirectoryName(path)!;
        foreach (var model in models.Where(m => m.Element("Type")?.Value == "LinesFromFile"))
        {
            var file = model.Element("Filename");
            if (file != null && file.Value.Length > 0 && !Path.IsPathFullyQualified(file.Value)) file.Value = Path.GetFullPath(file.Value, directory);
        }
        root.SetElementValue("IsEnginesInAppData", "false");
        root.SetElementValue("SingleInstance", "false");
        return new(document, version, directory);
    }
}
