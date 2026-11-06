using System.Text.Json;
using System.Text;
using System.Xml.Linq;

namespace Cronopus;

/// <summary>Keep BAS project_path() bound to the operator-selected XML, despite the execution copy.</summary>
public static class BasProjectContext
{
    public static void Apply(XDocument document, string sourcePath)
    {
        var script = document.Root?.Element("Script") ?? throw new ArgumentException("BAS Script is required");
        var path = JsonSerializer.Serialize(Path.GetFullPath(sourcePath).Replace('\\', '/'));
        // BAS creates thread contexts after the top-level script runs, discarding a global override.
        // Rewrite direct native calls in executable code; leave strings, comments and canvas metadata intact.
        script.ReplaceNodes(new XCData(Rewrite(script.Value, path)));
    }
    private static bool Identifier(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' || c > 127;
    private static string Rewrite(string code, string path)
    {
        var result = new StringBuilder(code.Length);
        var expression = true;
        var previous = "";
        var parens = new Stack<bool>();
        for (var i = 0; i < code.Length;)
        {
            var start = i; var c = code[i];
            if (char.IsWhiteSpace(c)) { result.Append(c); i++; continue; }
            if (c == '/' && i + 1 < code.Length && code[i + 1] is '/' or '*')
            {
                i = CommentEnd(code, i); result.Append(code, start, i - start); continue;
            }
            if (c is '\'' or '"' or '`')
            {
                i = QuotedEnd(code, i, c); result.Append(code, start, i - start); expression = false; previous = "literal"; continue;
            }
            if (c == '/' && expression)
            {
                i++; var characterClass = false;
                while (i < code.Length)
                {
                    var current = code[i++];
                    if (current == '\\') { if (i < code.Length) i++; continue; }
                    if (current == '[') characterClass = true;
                    if (current == ']') characterClass = false;
                    if (current == '/' && !characterClass) break;
                }
                while (i < code.Length && Identifier(code[i])) i++;
                result.Append(code, start, i - start); expression = false; previous = "literal"; continue;
            }
            if (Identifier(c))
            {
                while (i < code.Length && Identifier(code[i])) i++;
                var token = code[start..i];
                if (token == "project_path" && previous is not "." and not "?." and not "function" and not "new")
                {
                    var open = TriviaEnd(code, i);
                    var close = open < code.Length && code[open] == '(' ? TriviaEnd(code, open + 1) : -1;
                    if (close >= 0 && close < code.Length && code[close] == ')')
                    { result.Append(path); i = close + 1; previous = "literal"; expression = false; continue; }
                }
                result.Append(code, start, i - start);
                expression = token is "return" or "throw" or "case" or "delete" or "typeof" or "void" or "new" or "in" or "instanceof" or "yield" or "else" or "do";
                previous = token; continue;
            }
            if (c == '(') { parens.Push(previous is "if" or "while" or "for" or "with" or "switch" or "catch"); expression = true; }
            else if (c == ')') expression = parens.Count > 0 && parens.Pop();
            else if (c == ']') expression = false;
            else if (c == '.') expression = false;
            else expression = true;
            previous = c.ToString(); result.Append(c); i++;
        }
        return result.ToString();
    }
    private static int QuotedEnd(string code, int start, char quote)
    {
        var i = start + 1;
        while (i < code.Length)
        {
            if (code[i] == '\\') { i = Math.Min(i + 2, code.Length); continue; }
            if (code[i++] == quote) break;
        }
        return i;
    }
    private static int CommentEnd(string code, int start)
    {
        if (code[start + 1] == '/')
        {
            var end = code.IndexOf('\n', start + 2); return end < 0 ? code.Length : end;
        }
        var close = code.IndexOf("*/", start + 2, StringComparison.Ordinal);
        return close < 0 ? code.Length : close + 2;
    }
    private static int TriviaEnd(string code, int start)
    {
        while (start < code.Length)
        {
            if (char.IsWhiteSpace(code[start])) { start++; continue; }
            if (code[start] == '/' && start + 1 < code.Length && code[start + 1] is '/' or '*') { start = CommentEnd(code, start); continue; }
            break;
        }
        return start;
    }
}
