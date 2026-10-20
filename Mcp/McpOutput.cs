using System.Text;
using System.Text.Json;

namespace z3nDash.Mcp;

internal static class McpOutput
{
    private sealed record Cursor(string Node, string Task, string? Run, string Bridge, int Offset);

    public static async Task<object> Remote(McpConnection connection, string node, string id, string? run,
        string? cursor, int limit, CancellationToken ct)
    {
        Cursor? position = null;
        if (cursor != null)
        {
            try { position = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(cursor)); }
            catch (Exception ex) when (ex is FormatException or JsonException) { throw new ArgumentException("Invalid output cursor"); }
            if (position == null || position.Node != node || position.Task != id || position.Run != run ||
                position.Offset < 0 || string.IsNullOrEmpty(position.Bridge)) throw new ArgumentException("Output cursor belongs to another request");
        }
        var route = McpReadTools.Remote(node, $"tasks/{id}/output") + "?direction=after";
        if (run != null) route += "&run=" + Uri.EscapeDataString(run);
        if (position != null) route += "&cursor=" + Uri.EscapeDataString(position.Bridge);
        var page = await connection.Request("GET", route, null, ct);
        var text = string.Concat(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("line").GetString()));
        var gap = page.GetProperty("gap").GetBoolean();
        var offset = gap ? 0 : position?.Offset ?? 0;
        var end = PageEnd(text, offset, limit);
        var output = text[Math.Min(offset, text.Length)..end];
        var bridge = page.GetProperty("next_cursor").GetString()!;
        var nextOffset = 0;
        if (end < text.Length)
        {
            // Anchor at the first returned entry so continuation reads the same text.
            var first = page.GetProperty("previous_cursor").GetString()!;
            var split = first.LastIndexOf(':');
            bridge = first[..(split + 1)] + (long.Parse(first[(split + 1)..]) - 1);
            nextOffset = end;
        }
        var next = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(node, id, run, bridge, nextOffset)));
        return new { output, next_cursor = next, gap };
    }

    internal static int PageEnd(string text, int offset, int limit)
    {
        if (offset >= text.Length) return text.Length;
        if (offset > 0 && char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1]))
            throw new ArgumentException("offset splits a Unicode character; use the returned continuation");
        var end = (int)Math.Min((long)offset + limit, text.Length);
        if (end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
        if (end == offset) throw new ArgumentException("limit must be at least 2 for this Unicode character");
        return end;
    }
}
