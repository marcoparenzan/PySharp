// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Linq;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// textwrap: just `dedent`, ported faithfully from CPython's own algorithm (treats whitespace-only
/// lines as blank, finds the longest common leading-whitespace prefix across the remaining lines,
/// strips it) — the only real usage observed so far. Found via anyio's real `from textwrap import
/// dedent` (_core/_exceptions.py), itself a real dependency of starlette. `wrap`/`fill`/`indent`/
/// `shorten` not attempted since nothing in the real dependency chain has needed them yet. See
/// FASTAPI_PLAN.md.
/// </summary>
public static class TextwrapModule
{
    public static PyModule Create()
    {
        var m = new PyModule("textwrap");
        m.Dict["dedent"] = new PyBuiltinFunction("dedent", (_, a, _) => Dedent((string)a[0]));
        return m;
    }

    private static string Dedent(string text)
    {
        var lines = text.Split('\n')
            .Select(line => line.Length > 0 && line.All(c => c is ' ' or '\t') ? "" : line)
            .ToArray();

        string? margin = null;
        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
                i++;
            if (i == line.Length)
                continue;
            string indent = line[..i];
            if (margin is null)
                margin = indent;
            else if (indent.StartsWith(margin, StringComparison.Ordinal))
                { /* current margin already a prefix of this line's indent */ }
            else if (margin.StartsWith(indent, StringComparison.Ordinal))
                margin = indent;
            else
            {
                int common = 0;
                while (common < margin.Length && common < indent.Length && margin[common] == indent[common])
                    common++;
                margin = margin[..common];
            }
        }

        if (string.IsNullOrEmpty(margin))
            return string.Join("\n", lines);

        return string.Join("\n", lines.Select(line =>
            line.StartsWith(margin, StringComparison.Ordinal) ? line[margin.Length..] : line));
    }
}
