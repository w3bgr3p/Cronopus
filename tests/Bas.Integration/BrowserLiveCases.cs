using System.Text.Json;
using System.Xml.Linq;
using Cronopus;

static class BrowserLiveCases
{
    public static async Task Run(string root, string engine)
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "context.xml"), LoadOptions.PreserveWhitespace);
        var element = xml.Root!.Element("Script")!;
        var header = element.Value[..element.Value.IndexOf("function basProjectDirectory", StringComparison.Ordinal)];
        element.ReplaceNodes(new XCData(header + """
            _call(function(){
            _set_action_info({name:"Ignore Errors"});
            _on_fail(function(){_break(1,true);});
            CYCLES.Current().RemoveLabel("function");
            _load("data:text/html,%3Cbody%3Eprobe%3C/body%3E","",false)!
            },null)!
            _call(function(){
            _set_action_info({name:"Ignore Errors"});
            _on_fail(function(){_break(1,true);});
            CYCLES.Current().RemoveLabel("function");
            general_timeout_next(2000)
            page().script("JSON.stringify({url:location.href,ready:document.readyState!==\"loading\",text:document.body?String(document.body.textContent||\"\"):\"\"})")!
            VAR_BROWSER_RESPONSE=String(_result());
            },null)!
            var observed=JSON.parse(VAR_BROWSER_RESPONSE);
            if(!observed.ready||observed.text!=="probe")fail_user("BROWSER_SNAPSHOT_INVALID",false);
            log("CRONOPUS_BROWSER_SNAPSHOT="+JSON.stringify(observed))
            section_end()!
            })!
            """));
        var source = Path.Combine(root, "browser-probe.xml"); xml.Save(source);
        var original = File.ReadAllBytes(source);
        var lines = new List<string>();
        var isolated = BasEngineFiles.Prepare(Path.GetDirectoryName(engine)!, Path.Combine(root, "browser-engine"), CancellationToken.None);
        await using var client = new BasControlClient(Path.Combine(root, "browser-run"), isolated);
        client.Message += (_, message) => lines.Add(message.GetProperty("text").GetString() ?? "");
        await client.StartAsync(new BasProject("browser", "1", "Browser probe", source, "{}", 1), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await client.RunAsync(JsonSerializer.SerializeToElement(new { }), 1, timeout.Token);
        if (!lines.Any(line => line.Contains("CRONOPUS_BROWSER_SNAPSHOT="))) throw new Exception("Native browser snapshot was not confirmed");
        if (!original.SequenceEqual(File.ReadAllBytes(source))) throw new Exception("Browser probe changed source XML");
        Console.WriteLine("PASS native browser JavaScript snapshot and JSON parsing through BasControlClient; source unchanged");
    }
}
