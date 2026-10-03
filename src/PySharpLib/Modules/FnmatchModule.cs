// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Text;
using System.Text.RegularExpressions;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>fnmatch: Unix shell-style wildcards (<c>*</c>, <c>?</c>, <c>[seq]</c>, <c>[!seq]</c>) — <c>fnmatch</c>, <c>fnmatchcase</c>,
/// <c>filter</c>, <c>translate</c>. <c>fnmatch</c> normalizes case on Windows only, like the real module.</summary>
public static class FnmatchModule
{
    private static bool CaseInsensitive => OperatingSystem.IsWindows();

    public static PyModule Create()
    {
        var m = new PyModule("fnmatch");
        m.Dict["fnmatch"] = new PyBuiltinFunction("fnmatch", (_, a, _) => Match((string)a[0], (string)a[1], CaseInsensitive));
        m.Dict["fnmatchcase"] = new PyBuiltinFunction("fnmatchcase", (_, a, _) => Match((string)a[0], (string)a[1], false));
        m.Dict["filter"] = new PyBuiltinFunction("filter", (interp, a, _) =>
        {
            var re = ToRegex((string)a[1], CaseInsensitive);
            return new PyList(PyOps.Iterate(interp, a[0]).Where(n => re.IsMatch((string)n)));
        });
        m.Dict["translate"] = new PyBuiltinFunction("translate", (_, a, _) => "(?s:" + ToPattern((string)a[0]) + ")\\Z");
        return m;
    }

    private static bool Match(string name, string pattern, bool ignoreCase) => ToRegex(pattern, ignoreCase).IsMatch(name);

    private static Regex ToRegex(string pattern, bool ignoreCase)
        => new("^" + ToPattern(pattern) + "$", RegexOptions.Singleline | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));

    private static string ToPattern(string pat)
    {
        var sb = new StringBuilder();
        int i = 0, n = pat.Length;
        while (i < n)
        {
            char c = pat[i++];
            if (c == '*') { sb.Append(".*"); continue; }
            if (c == '?') { sb.Append('.'); continue; }
            if (c == '[')
            {
                int j = i;
                if (j < n && pat[j] == '!') j++;
                if (j < n && pat[j] == ']') j++;
                while (j < n && pat[j] != ']') j++;
                if (j >= n) { sb.Append("\\["); continue; }
                string stuff = pat[i..j];
                i = j + 1;
                if (stuff.StartsWith('!')) stuff = "^" + stuff[1..];
                else if (stuff.StartsWith('^')) stuff = "\\" + stuff;
                sb.Append('[').Append(stuff.Replace("\\", "\\\\")).Append(']');
                continue;
            }
            sb.Append(Regex.Escape(c.ToString()));
        }
        return sb.ToString();
    }
}
