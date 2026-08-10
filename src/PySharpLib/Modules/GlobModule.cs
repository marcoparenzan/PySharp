// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Text;
using System.Text.RegularExpressions;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>glob: real glob()/iglob() — a segment-by-segment directory walk (each path component
/// with `*`/`?`/`[...]` wildcards translated to a real regex and matched against real
/// Directory.GetFileSystemEntries() results), including real recursive `**` support (only
/// wildcard-active when `recursive=True`, matching real CPython). Not a stub or a pattern-string
/// generator — every returned path is checked against the real filesystem. Found via a real
/// file-organizer script needing `glob.glob("**/*.py", recursive=True)`. See ROADMAP.md scenario 8
/// (File system API).</summary>
public static class GlobModule
{
    public static PyModule Create()
    {
        var m = new PyModule("glob");
        var d = m.Dict;

        d["glob"] = new PyBuiltinFunction("glob", (interp, a, kwargs) =>
            new PyList(Iglob(PatternArg(a, kwargs), RecursiveArg(a, kwargs)).Select(p => (object)p)));

        d["iglob"] = new PyBuiltinFunction("iglob", (interp, a, kwargs) =>
            new PyIterator(Iglob(PatternArg(a, kwargs), RecursiveArg(a, kwargs)).GetEnumerator()));

        d["escape"] = new PyBuiltinFunction("escape", (_, a, _) =>
        {
            string s = (string)a[0];
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c is '*' or '?' or '[')
                    sb.Append('[').Append(c).Append(']');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        });

        d["has_magic"] = new PyBuiltinFunction("has_magic", (_, a, _) => HasMagic((string)a[0]));

        return m;
    }

    private static string PatternArg(object[] a, System.Collections.Generic.Dictionary<string, object>? kwargs)
        => a.Length > 0 ? (string)a[0]
            : kwargs is not null && kwargs.TryGetValue("pathname", out var p) ? (string)p
            : throw PyErr.TypeError("glob() missing required argument: 'pathname'");

    private static bool RecursiveArg(object[] a, System.Collections.Generic.Dictionary<string, object>? kwargs)
        => a.Length > 1 ? a[1] is true
            : kwargs is not null && kwargs.TryGetValue("recursive", out var r) && r is true;

    private static bool HasMagic(string s) => s.IndexOfAny(new[] { '*', '?', '[' }) >= 0;

    /// <summary>Real (not stubbed) glob resolution: splits the pattern into path segments, and
    /// walks the real filesystem segment by segment, expanding a literal segment by simple
    /// existence, a wildcard segment (`*`/`?`/`[...]`) by listing the real directory and regex-
    /// matching entries, and a bare `**` segment (only when `recursive` is true — matching real
    /// CPython, where `**` is ordinary-wildcard-shaped otherwise) by expanding to "this directory
    /// and every real descendant directory, at any depth, including itself".</summary>
    internal static IEnumerable<string> Iglob(string pattern, bool recursive)
    {
        if (pattern.Length == 0)
            yield break;

        char sep = Path.DirectorySeparatorChar;
        string normalized = pattern.Replace('/', sep).Replace('\\', sep);
        bool isAbsolute = Path.IsPathRooted(normalized);
        string root = isAbsolute ? Path.GetPathRoot(normalized) ?? "" : "";
        string rest = isAbsolute ? normalized[root.Length..] : normalized;
        var segments = rest.Split(sep, StringSplitOptions.RemoveEmptyEntries);

        var current = new List<string> { isAbsolute ? root.TrimEnd(sep) : "." };
        bool currentIsRootPlaceholder = !isAbsolute;

        for (int i = 0; i < segments.Length; i++)
        {
            string seg = segments[i];
            bool isLast = i == segments.Length - 1;
            var next = new List<string>();

            if (seg == "**" && recursive)
            {
                foreach (var basePath in current)
                    foreach (var dir in AllDirsIncludingSelf(basePath))
                        next.Add(dir);
            }
            else if (HasMagic(seg))
            {
                var regex = TranslateSegment(seg);
                foreach (var basePath in current)
                {
                    string listDir = currentIsRootPlaceholder && basePath == "." ? "." : basePath;
                    if (!Directory.Exists(listDir))
                        continue;
                    foreach (var entry in Directory.EnumerateFileSystemEntries(listDir))
                    {
                        string name = Path.GetFileName(entry);
                        if (name.StartsWith('.') && !seg.StartsWith('.'))
                            continue;
                        if (regex.IsMatch(name))
                        {
                            if (!isLast && !Directory.Exists(entry))
                                continue;
                            next.Add(JoinKeepDot(basePath, name, currentIsRootPlaceholder));
                        }
                    }
                }
            }
            else
            {
                foreach (var basePath in current)
                {
                    string candidate = JoinKeepDot(basePath, seg, currentIsRootPlaceholder);
                    if (isLast ? PathExists(candidate) : Directory.Exists(currentIsRootPlaceholder ? Path.Combine(basePath, seg) : Path.Combine(basePath, seg)))
                        next.Add(candidate);
                }
            }

            current = next;
            currentIsRootPlaceholder = false;
        }

        foreach (var p in current)
            yield return p;
    }

    private static string JoinKeepDot(string basePath, string name, bool baseIsDotPlaceholder)
        => baseIsDotPlaceholder && basePath == "." ? name : Path.Combine(basePath, name);

    private static bool PathExists(string p) => File.Exists(p) || Directory.Exists(p);

    private static IEnumerable<string> AllDirsIncludingSelf(string basePath)
    {
        string start = basePath == "." ? "." : basePath;
        if (!Directory.Exists(start))
            yield break;
        yield return basePath;
        foreach (var dir in Directory.EnumerateDirectories(start, "*", SearchOption.AllDirectories))
        {
            string name = basePath == "."
                ? dir.StartsWith("." + Path.DirectorySeparatorChar) ? dir[2..] : dir
                : dir;
            yield return name;
        }
    }

    private static Regex TranslateSegment(string pattern)
    {
        var sb = new StringBuilder("^");
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            i++;
            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    break;
                case '?':
                    sb.Append('.');
                    break;
                case '[':
                    int j = i;
                    if (j < pattern.Length && (pattern[j] == '!' || pattern[j] == '^'))
                        j++;
                    if (j < pattern.Length && pattern[j] == ']')
                        j++;
                    while (j < pattern.Length && pattern[j] != ']')
                        j++;
                    if (j >= pattern.Length)
                    {
                        sb.Append("\\[");
                    }
                    else
                    {
                        string charClass = pattern[i..j].Replace("\\", "\\\\");
                        if (charClass.StartsWith('!'))
                            charClass = "^" + charClass[1..];
                        sb.Append('[').Append(charClass).Append(']');
                        i = j + 1;
                    }
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Singleline);
    }
}
