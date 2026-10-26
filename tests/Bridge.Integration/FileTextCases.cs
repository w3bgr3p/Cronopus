using System.Net;
using System.Text;
using System.Text.Json;

static partial class BridgeCases
{
    public static async Task FileText(string root, HttpClient http, Uri uri)
    {
        var file = Path.Combine(root, "editable.txt");
        var route = "/bridge/v1/files/text?path=" + Uri.EscapeDataString(file);
        await File.WriteAllTextAsync(file, "hello\r\nмир\r\n", new UTF8Encoding(true));
        var original = await File.ReadAllBytesAsync(file);
        var opened = await Json(http, uri, "GET", route);
        Check(opened.GetProperty("text").GetString() == "hello\r\nмир\r\n", "text read changed content");
        var revision = opened.GetProperty("revision").GetString();
        var saved = await Json(http, uri, "PUT", "/bridge/v1/files/text", new { path = file, revision, text = "edited\r\nмир\r\n" });
        Check(await File.ReadAllTextAsync(file) == "edited\r\nмир\r\n", "text save failed");
        Check((await File.ReadAllBytesAsync(file)).Take(3).SequenceEqual(original.Take(3)), "UTF-8 BOM lost");
        using var stale = await http.PutAsync(new Uri(uri, "/bridge/v1/files/text"), new StringContent(JsonSerializer.Serialize(new { path = file, revision, text = "stale" }), Encoding.UTF8, "application/json"));
        Check(stale.StatusCode == HttpStatusCode.Conflict, "stale text overwrote server file");
        Check(await File.ReadAllTextAsync(file) == "edited\r\nмир\r\n", "conflict modified file");
        await File.WriteAllTextAsync(file, "external change");
        using var external = await http.PutAsync(new Uri(uri, "/bridge/v1/files/text"), new StringContent(JsonSerializer.Serialize(new { path = file, revision = saved.GetProperty("revision").GetString(), text = "overwrite" }), Encoding.UTF8, "application/json"));
        Check(external.StatusCode == HttpStatusCode.Conflict, "external changes not protected");
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) })
        {
            await File.WriteAllTextAsync(file, "old\nтекст", encoding);
            opened = await Json(http, uri, "GET", route);
            await Json(http, uri, "PUT", "/bridge/v1/files/text", new { path = file, revision = opened.GetProperty("revision").GetString(), text = "new\nтекст" });
            Check((await File.ReadAllBytesAsync(file)).SequenceEqual(encoding.GetPreamble().Concat(encoding.GetBytes("new\nтекст"))), "encoding changed");
        }
        await File.WriteAllBytesAsync(file, [0, 1, 2, 255]);
        using var binary = await http.GetAsync(new Uri(uri, route));
        Check(binary.StatusCode == HttpStatusCode.BadRequest, "binary accepted as text");
        await File.WriteAllBytesAsync(file, new byte[1024 * 1024 + 1]);
        using var large = await http.GetAsync(new Uri(uri, route));
        Check(large.StatusCode == HttpStatusCode.BadRequest, "oversized text accepted");
        File.Delete(file);
        using var missing = await http.PutAsync(new Uri(uri, "/bridge/v1/files/text"), new StringContent(JsonSerializer.Serialize(new { path = file, revision, text = "new file" }), Encoding.UTF8, "application/json"));
        Check(missing.StatusCode == HttpStatusCode.NotFound && !File.Exists(file), "save recreated deleted file");
        Console.WriteLine("PASS remote-text: read/save, BOM/UTF-16, conflicts, binary/size rejection, deleted file");
    }
}
