using System.Text.Json;
using System.Xml.Linq;
using Cronopus;

static class ContextCases
{
    public static void Run(string root)
    {
        var source = Path.Combine(root, "quoted ' folder", "source.xml");
        const string code = "var a=project_path(); var b=project_path ( );\n"
            + "var s=\"project_path()\"; var q='project_path()';\n"
            + "// project_path()\n/* project_path() */\nobj.project_path(); not_project_path();\n"
            + "obj. project_path(); obj./*gap*/project_path();\n"
            + "var regex=/project_path()/; if(x) /project_path()/.test(x);\n"
            + "if(x) foo(); else /project_path()/.test(x); do /project_path()/.test(x); while(x);\n"
            + "var template=`project_path()`; function project_path(){}\n";
        var document = new XDocument(new XElement("BrowserAutomationStudioProject", new XElement("Script", new XCData(code))));
        BasProjectContext.Apply(document, source);
        var changed = document.Root!.Element("Script")!.Value;
        var path = JsonSerializer.Serialize(source.Replace('\\', '/'));
        if (!changed.StartsWith("var a=" + path + "; var b=" + path + ";\n")
            || !changed.EndsWith(code[code.IndexOf("var s=")..])) throw new Exception("Project context changed noncode references");
        if (document.Root.Element("Script")!.FirstNode is not XCData) throw new Exception("Project context lost native CDATA");
        var division = new XDocument(new XElement("BrowserAutomationStudioProject", new XElement("Script", "var ratio=4 / project_path().length;")));
        BasProjectContext.Apply(division, source);
        if (!division.Root!.Element("Script")!.Value.Contains("4 / " + path + ".length")) throw new Exception("Division mistaken for regex");
        Console.WriteLine("PASS original project path rewriting preserves literals, comments, member calls and CDATA");
    }
}
