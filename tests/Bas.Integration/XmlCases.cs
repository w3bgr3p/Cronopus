using System.Text.Json;
using System.Xml.Linq;
using Cronopus;

static class XmlCases
{
    public static void Run(string root)
    {
        var source = Path.Combine(root, "source.xml");
        var xml = "<BrowserAutomationStudioProject><EngineVersion>31.1.0</EngineVersion><Script><![CDATA[log('original')]]></Script>" +
            "<ModelList><Model><Name>Message</Name><Type>FixedString</Type><Value>old</Value></Model>" +
            "<Model><Name>Count</Name><Type>FixedInteger</Type><Value>1</Value><MinValue>1</MinValue><MaxValue>10</MaxValue></Model>" +
            "<Model><Name>Input</Name><Type>LinesFromFile</Type><Filename>input.txt</Filename></Model>" +
            "<Model><Name>Native</Name><Type>RandomInteger</Type><Value>5</Value></Model></ModelList></BrowserAutomationStudioProject>";
        File.WriteAllText(source, xml);
        var prepared = BasXmlProject.Prepare(source, "{\"Message\":\"a<&😀\",\"Count\":3}");
        if (prepared.EngineVersion != "31.1.0") throw new Exception("engine version lost");
        var model = prepared.Document.Root!.Element("ModelList")!.Elements("Model").ToArray();
        if (model[0].Element("Value")!.Value != "a<&😀" || model[1].Element("Value")!.Value != "3") throw new Exception("resource override damaged");
        if (model[2].Element("Filename")!.Value != Path.Combine(root, "input.txt")) throw new Exception("relative input path not resolved");
        if (File.ReadAllText(source) != xml) throw new Exception("source project was modified");
        var copy = Path.Combine(root, "prepared.xml"); prepared.Document.Save(copy);
        if (!File.ReadAllText(copy).Contains("<![CDATA[log('original')]]>")) throw new Exception("BAS script CDATA was lost");
        var metadata = JsonSerializer.SerializeToElement(BasXmlProject.Resources(source));
        var fields = metadata.GetProperty("Resources");
        if (fields.GetArrayLength() != 4 || fields[0].GetProperty("DefaultValue").GetString() != "old"
            || fields[1].GetProperty("Min").GetInt32() != 1 || fields[1].GetProperty("Max").GetInt32() != 10
            || fields[2].GetProperty("DefaultValue").GetString() != Path.Combine(root, "input.txt")
            || fields[3].GetProperty("Editable").GetBoolean()) throw new Exception("XML resource discovery changed defaults or capability metadata");
        if (File.ReadAllText(source) != xml) throw new Exception("resource discovery modified source");
        Reject(() => BasXmlProject.Prepare(source, "{\"Unknown\":1}"));
        Reject(() => BasXmlProject.Prepare(source, "{\"Count\":11}"));
        var remote = Path.Combine(root, "remote.xml"); File.WriteAllText(remote, "<Remote ScriptName='Private' />");
        Reject(() => BasXmlProject.Prepare(remote, "{}"));
        BasInputCases.Run(root);
        Console.WriteLine("PASS local XML preparation, resources, validation, relative paths and source preservation");
    }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("invalid XML settings accepted"); }
}
