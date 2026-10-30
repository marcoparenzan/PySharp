using System.Text.RegularExpressions;
using PySharpLib.Runtime;
namespace PySharpLib.Modules;
public static partial class InspectModule
{
    private static string ReadSource(object target)
    {
        target = UnwrapBoundMethod(target);
        var module = target switch
        {
            PyModule m => m,
            PyFunction f => f.Module,
            _ => throw PyErr.TypeError("source inspection supports Python modules, functions and methods")
        };
        string source;
        try { source = File.ReadAllText(module.FileName).Replace("\r\n", "\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { throw PyErr.OSError("could not get source code"); }
        if (target is PyModule) return source;
        var fn = (PyFunction)target;
        if (fn.LambdaBody is not null) throw PyErr.OSError("lambda source extraction is not supported");
        var lines = source.Split('\n');
        int bodyLine = fn.Body is { Count: > 0 } ? fn.Body[0].Line : lines.Length;
        var declaration = new Regex(@"^(\s*)(?:async\s+)?def\s+" + Regex.Escape(fn.Name) + @"\s*\(");
        int start = -1;
        for (int i = Math.Min(bodyLine - 1, lines.Length - 1); i >= 0; i--)
            if (declaration.IsMatch(lines[i])) { start = i; break; }
        if (start < 0) throw PyErr.OSError("could not locate function source");
        int indent = lines[start].TakeWhile(char.IsWhiteSpace).Count();
        int definition = start;
        while (start > 0 && lines[start - 1].TrimStart().StartsWith('@') && lines[start - 1].TakeWhile(char.IsWhiteSpace).Count() == indent) start--;
        int end = definition + 1;
        // File-backed indentation blocks; multiline string/continuation edge cases remain outside this first extraction path.
        while (end < lines.Length)
        {
            var line = lines[end];
            if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#') && line.TakeWhile(char.IsWhiteSpace).Count() <= indent) break;
            end++;
        }
        while (end > definition + 1 && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
        return string.Join("\n", lines[start..end]) + "\n";
    }
}