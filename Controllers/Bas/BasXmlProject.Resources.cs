using System.Globalization;
using System.Xml.Linq;

namespace Cronopus;

public sealed record BasResource(string Name, string Type, string DefaultValue, bool Editable, int? Min, int? Max,
    string Label, string Hint, string Section, bool Visible, bool Enabled, bool Advanced,
    string[] Options, string ConditionResource, string ConditionValue, bool Multiline, bool Required);

public static partial class BasXmlProject
{
    private static bool Flag(XElement model, string name, bool fallback = false)
        => model.Element(name) is { } field ? field.Value is "1" or "true" : fallback;
    private static bool SingleSelect(XElement model) => (model.Element("SelectType")?.Value ?? "0") == "0";
    private static string[] SelectOptions(XElement model)
        => model.Element("Values") is { Value.Length: > 0 } values ? values.Value.Replace("\r\n", "\n").Split('\n') : [];
    private static string Caption(XElement model, string name)
    {
        var element = model.Element(name);
        return element?.Attribute("en")?.Value ?? element?.Attribute("ru")?.Value ?? element?.Value ?? "";
    }
    public static object Resources(string path)
    {
        var project = Prepare(path, "{}");
        var models = project.Document.Root!.Element("ModelList")?.Elements("Model") ?? [];
        var resources = models.Select(model =>
        {
            var name = model.Element("Name")?.Value ?? "";
            var type = model.Element("Type")?.Value ?? "";
            var field = ResourceField(type);
            int? Bound(string key) => int.TryParse(model.Element(key)?.Value, CultureInfo.InvariantCulture, out var value) ? value : null;
            var options = type == "Select" ? SelectOptions(model) : [];
            var index = Bound("Selected") ?? -1;
            var value = type == "Select" ? (index >= 0 && index < options.Length ? options[index] : "")
                : model.Element(field ?? "Value")?.Value ?? "";
            var enabled = Flag(model, "Enabled", true);
            var label = Caption(model, "Description");
            return new BasResource(name, type, value,
                field != null && enabled && (type != "Select" || SingleSelect(model)), Bound("MinValue"), Bound("MaxValue"),
                label.Length > 0 ? label : name, Flag(model, "HintVisible", true) ? Caption(model, "Hint") : "",
                Caption(model, "SectionName"), Flag(model, "Visible", true), enabled, Flag(model, "IsAdvanced"), options,
                model.Element("VisibilityConditionVariable")?.Value ?? "", model.Element("VisibilityConditionValue")?.Value ?? "",
                Flag(model, "MultiLine"), Flag(model, "NotEmpty"));
        }).Where(resource => resource.Name.Length > 0).ToArray();
        return new { project.EngineVersion, Resources = resources };
    }
}
