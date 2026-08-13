// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// re: backed by System.Text.RegularExpressions — a real backtracking engine, not a hand-rolled
/// subset (Python and .NET regex syntax are close enough for common patterns that only the named-
/// group syntax needs translating: `(?P&lt;name&gt;...)` -&gt; `(?&lt;name&gt;...)`,
/// `(?P=name)` -&gt; `\k&lt;name&gt;`). v1 scope: compile/match/search/fullmatch/findall/finditer/
/// sub/subn/split, Match (group/groups/groupdict/start/end/span), Pattern, the common flags. See
/// FASTAPI_PLAN.md Phase 1.9 (originally flagged as item 1.4 at the very start of this plan).
/// Real `bytes` pattern/subject support (real CPython's `re` matches against `bytes` as well as
/// `str`): a `bytes` pattern/subject is decoded via Latin-1 (a lossless, byte-for-byte 1:1 mapping
/// of 0-255 to the identically-valued codepoint, since .NET's Regex only operates on `string`) into
/// the working string actually matched against, and every result (match text, group values,
/// `.string`, `sub`/`split` output) is re-encoded back to `bytes` via the same mapping. A pattern's
/// bytes-vs-str mode is fixed at compile time and enforced against every subject given to it after,
/// matching real CPython's own "cannot use a bytes pattern on a string-like object" (and the
/// reverse) errors. Found via real h11's own `_readers.py`/`_events.py`/`_headers.py`
/// (`re.compile(rb"[0-9]+")` etc.) — a real httpx transitive dependency (its low-level HTTP/1.1
/// transport).
/// </summary>
public static class ReModule
{
    private const string PatternKey = "__pattern__";
    private const string RegexKey = "__regex__";
    private const string MatchKey = "__match__";
    private const string StringKey = "__string__";
    private const string BytesModeKey = "__bytes_mode__";

    public static readonly PyClass PatternClass = BuildPatternClass();
    public static readonly PyClass MatchClass = BuildMatchClass();
    public static readonly PyClass ErrorClass = new("error", new List<PyClass> { PyErr.Exception });

    public static PyModule Create()
    {
        var m = new PyModule("re");
        var d = m.Dict;

        d["error"] = ErrorClass;
        d["Pattern"] = PatternClass;
        d["Match"] = MatchClass;

        d["IGNORECASE"] = new BigInteger(2);
        d["I"] = d["IGNORECASE"];
        d["MULTILINE"] = new BigInteger(8);
        d["M"] = d["MULTILINE"];
        d["DOTALL"] = new BigInteger(16);
        d["S"] = d["DOTALL"];
        d["VERBOSE"] = new BigInteger(64);
        d["X"] = d["VERBOSE"];
        d["ASCII"] = new BigInteger(256);
        d["A"] = d["ASCII"];
        d["UNICODE"] = new BigInteger(32);
        d["U"] = d["UNICODE"];

        d["compile"] = new PyBuiltinFunction("compile", (_, a, kwargs) =>
            MakeCompiled(a[0], FlagsOf(a, kwargs, 1)));

        d["match"] = new PyBuiltinFunction("match", (_, a, kwargs) =>
        {
            var p = CompileFor(a, kwargs);
            var (s, isBytes) = ResolveSubject(p, a[1]);
            return MatchAt(Rx(p), s, isBytes, anchor: true, full: false);
        });
        d["fullmatch"] = new PyBuiltinFunction("fullmatch", (_, a, kwargs) =>
        {
            var p = CompileFor(a, kwargs);
            var (s, isBytes) = ResolveSubject(p, a[1]);
            return MatchAt(Rx(p), s, isBytes, anchor: true, full: true);
        });
        d["search"] = new PyBuiltinFunction("search", (_, a, kwargs) =>
        {
            var p = CompileFor(a, kwargs);
            var (s, isBytes) = ResolveSubject(p, a[1]);
            return SearchIn(Rx(p), s, isBytes);
        });
        d["findall"] = new PyBuiltinFunction("findall", (_, a, kwargs) =>
        {
            var p = CompileFor(a, kwargs);
            var (s, isBytes) = ResolveSubject(p, a[1]);
            return FindAll(Rx(p), s, isBytes);
        });
        d["finditer"] = new PyBuiltinFunction("finditer", (_, a, kwargs) =>
        {
            var p = CompileFor(a, kwargs);
            var (s, isBytes) = ResolveSubject(p, a[1]);
            return FindIter(Rx(p), s, isBytes);
        });
        d["sub"] = new PyBuiltinFunction("sub", (interp, a, kwargs) =>
        {
            var p = CompileForAt(a, kwargs, 0);
            var (s, isBytes) = ResolveSubject(p, a[2]);
            return Sub(interp, Rx(p), a[1], s, isBytes, CountOf(a, kwargs, 3));
        });
        d["subn"] = new PyBuiltinFunction("subn", (interp, a, kwargs) =>
        {
            var p = CompileForAt(a, kwargs, 0);
            var (s, isBytes) = ResolveSubject(p, a[2]);
            return SubN(interp, Rx(p), a[1], s, isBytes, CountOf(a, kwargs, 3));
        });
        d["split"] = new PyBuiltinFunction("split", (_, a, kwargs) =>
        {
            var p = CompileForAt(a, kwargs, 0);
            var (s, isBytes) = ResolveSubject(p, a[1]);
            return Split(Rx(p), s, isBytes, CountOf(a, kwargs, 2));
        });
        d["escape"] = new PyBuiltinFunction("escape", (_, a, _) =>
        {
            string s = ToWorkingString(a[0], out bool isBytes);
            return WrapResult(Regex.Escape(s), isBytes);
        });

        return m;
    }

    // ------------------------------------------------------------------ bytes/str duality

    /// <summary>Decodes a `str` or `bytes` argument into the working .NET string actually matched
    /// against (bytes via Latin-1 — a lossless 1:1 byte-to-codepoint mapping).</summary>
    private static string ToWorkingString(object o, out bool isBytes)
    {
        switch (o)
        {
            case string s:
                isBytes = false;
                return s;
            case PyBytes b:
                isBytes = true;
                return Encoding.Latin1.GetString(b.Data);
            // A real `class Foo(str): ...` subclass instance (see PyInstance.StrValue's own doc
            // comment) is a real string everywhere else already — `re` must accept one too. Found
            // via real sqlalchemy's own `sql/elements.py` `class quoted_name(..., str): ...`
            // identifiers flowing into real regex matching (e.g. `_requires_quotes`'s
            // `legal_characters.match(str(value))`/other real `re` usage on identifier names).
            case PyInstance inst when inst.StrValue is not null:
                isBytes = false;
                return inst.StrValue;
            default:
                throw PyErr.TypeError($"expected string or bytes-like object, got '{PyOps.TypeName(o)}'");
        }
    }

    private static object WrapResult(string value, bool isBytes) =>
        isBytes ? new PyBytes(Encoding.Latin1.GetBytes(value)) : value;

    /// <summary>Resolves a match subject against an already-compiled Pattern instance, enforcing
    /// real CPython's own bytes-vs-str consistency rule between a pattern and what it's matched
    /// against.</summary>
    private static (string Working, bool IsBytes) ResolveSubject(PyInstance patternInst, object subject)
    {
        bool patternIsBytes = (bool)patternInst.Dict[BytesModeKey];
        string working = ToWorkingString(subject, out bool subjectIsBytes);
        if (subjectIsBytes != patternIsBytes)
            throw PyErr.TypeError(patternIsBytes
                ? "cannot use a bytes pattern on a string-like object"
                : "cannot use a string pattern on a bytes-like object");
        return (working, patternIsBytes);
    }

    private static Regex Rx(PyInstance patternInst) => (Regex)patternInst.Dict[RegexKey];

    // ------------------------------------------------------------------ pattern compilation

    private static RegexOptions FlagsOf(object[] a, Dictionary<string, object>? kwargs, int argIndex)
    {
        long flags = a.Length > argIndex ? (long)PyOps.AsBigInt(a[argIndex], "flags")
            : kwargs is not null && kwargs.TryGetValue("flags", out var f) ? (long)PyOps.AsBigInt(f, "flags")
            : 0;
        var opts = RegexOptions.None;
        if ((flags & 2) != 0) opts |= RegexOptions.IgnoreCase;
        if ((flags & 8) != 0) opts |= RegexOptions.Multiline;
        if ((flags & 16) != 0) opts |= RegexOptions.Singleline;
        if ((flags & 64) != 0) opts |= RegexOptions.IgnorePatternWhitespace;
        return opts;
    }

    private static string TranslatePattern(string pyPattern) =>
        pyPattern.Replace("(?P<", "(?<").Replace("(?P=", @"\k<").Replace(@"\k<", " TEMP ")
            .Replace(" TEMP ", @"\k<") is var s && s.Contains(@"\k<")
            ? FixBackref(s)
            : s;

    private static string FixBackref(string s)
    {
        // (?P=name) -> \k<name> needs the trailing ) turned into >
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            int idx = s.IndexOf(@"\k<", i, StringComparison.Ordinal);
            if (idx < 0) { sb.Append(s, i, s.Length - i); break; }
            sb.Append(s, i, idx - i).Append(@"\k<");
            int close = s.IndexOf(')', idx);
            if (close < 0) { sb.Append(s, idx + 3, s.Length - idx - 3); break; }
            sb.Append(s, idx + 3, close - idx - 3).Append('>');
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>Real per-codepoint character-class handling for astral (&gt;U+FFFF) Unicode
    /// codepoints — .NET's regex engine matches UTF-16 *code units*, so a literal astral character
    /// (already decoded to a surrogate pair by the time a Python string literal like `\U00010000`
    /// reaches here) inside a `[...]` class range gets misparsed as two independent BMP-range
    /// endpoints, producing a bogus "range in reverse order" error for completely valid Python `re`
    /// syntax. Found via real rfc3986's own `abnf_regexp.py` (RFC 3987 IUNRESERVED/IPRIVATE ranges —
    /// an httpx transitive dependency, e.g. `"\U00010000-\U0001FFFD\U00020000-\U0002FFFD..."`).
    /// Rewrites any non-negated class containing an astral member/range into an alternation: any
    /// BMP-only members stay a plain `[...]`, and each astral range is decomposed into the standard
    /// UTF-16 surrogate-pair sub-range fragments (the same technique other UTF-16-based regex
    /// engines — e.g. JavaScript's own `u`-flag polyfills — use for Unicode-aware character
    /// classes). Negated classes (`[^...]`) and a class where an escape sequence forms a range
    /// endpoint are left unchanged, a documented, safe fallback rather than a silently wrong
    /// rewrite — nothing reachable needs either.</summary>
    private static string RewriteAstralCharClasses(string pattern)
    {
        if (!ContainsSurrogatePair(pattern))
            return pattern;

        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                sb.Append(c).Append(pattern[i + 1]);
                i += 2;
                continue;
            }
            if (c == '[')
            {
                int closeIdx = FindClassEnd(pattern, i);
                if (closeIdx < 0)
                {
                    sb.Append(pattern, i, pattern.Length - i);
                    break;
                }
                sb.Append(RewriteOneClass(pattern, i, closeIdx));
                i = closeIdx + 1;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static bool ContainsSurrogatePair(string s)
    {
        for (int i = 0; i + 1 < s.Length; i++)
            if (char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
                return true;
        return false;
    }

    /// <summary>Index of the ']' closing the class starting at pattern[start] == '[' (respecting a
    /// leading ']'/'^]' as a literal member and '\]' escapes inside the class), or -1 if
    /// unterminated.</summary>
    private static int FindClassEnd(string pattern, int start)
    {
        int i = start + 1;
        if (i < pattern.Length && pattern[i] == '^')
            i++;
        if (i < pattern.Length && pattern[i] == ']')
            i++;
        while (i < pattern.Length)
        {
            if (pattern[i] == '\\' && i + 1 < pattern.Length) { i += 2; continue; }
            if (pattern[i] == ']')
                return i;
            i++;
        }
        return -1;
    }

    private static string RewriteOneClass(string pattern, int openIdx, int closeIdx)
    {
        string whole = pattern.Substring(openIdx, closeIdx - openIdx + 1);
        int contentStart = openIdx + 1;
        if (contentStart < closeIdx && pattern[contentStart] == '^')
            return whole; // negated: documented, safe fallback

        string content = pattern.Substring(contentStart, closeIdx - contentStart);
        if (!ContainsSurrogatePair(content))
            return whole; // no astral content: nothing to rewrite

        var bmp = new System.Text.StringBuilder();
        var astralRanges = new List<(int Start, int End)>();

        int i = 0;
        while (i < content.Length)
        {
            if (content[i] == '\\' && i + 1 < content.Length)
            {
                int after = i + 2;
                if (after < content.Length && content[after] == '-' && after + 1 < content.Length)
                    return whole; // escape as a range endpoint: safe fallback
                bmp.Append(content, i, 2);
                i += 2;
                continue;
            }

            int cp1Len = IsAstralAt(content, i) ? 2 : 1;
            int cp1 = cp1Len == 2 ? char.ConvertToUtf32(content[i], content[i + 1]) : content[i];
            int afterFirst = i + cp1Len;

            if (afterFirst < content.Length && content[afterFirst] == '-' && afterFirst + 1 < content.Length)
            {
                int secondIdx = afterFirst + 1;
                if (content[secondIdx] == '\\')
                    return whole; // escape as a range endpoint: safe fallback
                int cp2Len = IsAstralAt(content, secondIdx) ? 2 : 1;
                int cp2 = cp2Len == 2 ? char.ConvertToUtf32(content[secondIdx], content[secondIdx + 1]) : content[secondIdx];

                if (cp1 >= 0x10000 || cp2 >= 0x10000)
                    astralRanges.Add((cp1, cp2));
                else
                    bmp.Append(content, i, secondIdx + cp2Len - i);

                i = secondIdx + cp2Len;
                continue;
            }

            if (cp1 >= 0x10000)
                astralRanges.Add((cp1, cp1));
            else
                bmp.Append(content, i, cp1Len);
            i = afterFirst;
        }

        var alts = new List<string>();
        if (bmp.Length > 0)
            alts.Add($"[{bmp}]");
        foreach (var (rs, re) in astralRanges)
            alts.AddRange(SurrogatePairFragments(rs, re));

        return alts.Count == 0 ? whole : $"(?:{string.Join("|", alts)})";
    }

    private static bool IsAstralAt(string s, int i)
        => char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);

    /// <summary>Standard UTF-16 surrogate-pair range decomposition: splits a codepoint range
    /// [start, end] (start &gt;= U+10000) into the minimal set of (high-surrogate-range,
    /// low-surrogate-range) two-code-unit fragments whose union covers exactly that range.</summary>
    private static IEnumerable<string> SurrogatePairFragments(int start, int end)
    {
        static (int High, int Low) HiLo(int cp)
        {
            int v = cp - 0x10000;
            return (0xD800 + (v >> 10), 0xDC00 + (v & 0x3FF));
        }
        static string Frag(int h1, int h2, int l1, int l2) =>
            $"[\\u{h1:X4}-\\u{h2:X4}][\\u{l1:X4}-\\u{l2:X4}]";

        var (startHigh, startLow) = HiLo(start);
        var (endHigh, endLow) = HiLo(end);

        if (startHigh == endHigh)
        {
            yield return Frag(startHigh, startHigh, startLow, endLow);
            yield break;
        }

        if (startLow != 0xDC00)
        {
            yield return Frag(startHigh, startHigh, startLow, 0xDFFF);
            startHigh++;
        }
        if (endLow != 0xDFFF)
        {
            yield return Frag(endHigh, endHigh, 0xDC00, endLow);
            endHigh--;
        }
        if (startHigh <= endHigh)
            yield return Frag(startHigh, endHigh, 0xDC00, 0xDFFF);
    }

    private static PyInstance MakeCompiled(object patternArg, RegexOptions opts)
    {
        // Real CPython: `re.compile(x)` is idempotent — if `x` is already a compiled Pattern, it's
        // returned as-is (flags must be 0 when re-passing one; not enforced here, nothing reachable
        // needs that edge). Found via real sqlalchemy's own `sql/compiler.py` bind-name-escaping
        // logic, which stores a pre-compiled `_bind_translate_re` and can pass it back through
        // `re.compile`-shaped helpers.
        if (patternArg is PyInstance already && already.Class == PatternClass)
            return already;
        string workingPattern = ToWorkingString(patternArg, out bool isBytes);
        Regex regex;
        try
        {
            regex = new Regex(RewriteAstralCharClasses(TranslatePattern(workingPattern)), opts);
        }
        catch (ArgumentException ex)
        {
            throw new PyRaise(PyErr.MakeInstance(ErrorClass, ex.Message));
        }
        var inst = new PyInstance(PatternClass);
        inst.Dict[PatternKey] = patternArg;
        inst.Dict[RegexKey] = regex;
        inst.Dict[BytesModeKey] = isBytes;
        return inst;
    }

    private static PyInstance CompileFor(object[] a, Dictionary<string, object>? kwargs) =>
        MakeCompiled(a[0], FlagsOf(a, kwargs, 2));

    private static PyInstance CompileForAt(object[] a, Dictionary<string, object>? kwargs, int patternIdx) =>
        MakeCompiled(a[patternIdx], FlagsOf(a, kwargs, 4));

    private static int CountOf(object[] a, Dictionary<string, object>? kwargs, int argIndex) =>
        a.Length > argIndex ? (int)PyOps.AsBigInt(a[argIndex], "count")
        : kwargs is not null && kwargs.TryGetValue("count", out var c) ? (int)PyOps.AsBigInt(c, "count")
        : 0;

    private static int PosOf(object[] a, Dictionary<string, object>? kwargs, int argIndex) =>
        a.Length > argIndex ? (int)PyOps.AsBigInt(a[argIndex], "pos")
        : kwargs is not null && kwargs.TryGetValue("pos", out var p) ? (int)PyOps.AsBigInt(p, "pos")
        : 0;

    private static int? EndposOf(object[] a, Dictionary<string, object>? kwargs, int argIndex) =>
        a.Length > argIndex ? (int)PyOps.AsBigInt(a[argIndex], "endpos")
        : kwargs is not null && kwargs.TryGetValue("endpos", out var e) ? (int)PyOps.AsBigInt(e, "endpos")
        : null;

    // ------------------------------------------------------------------ matching

    // pos/endpos (Pattern.match/search/finditer's real 2nd/3rd args, restricting the scan to a
    // substring window without allocating one) — found via a real bug hunt: a hand-ported
    // http.cookies._unquote (itself a real CPython algorithm, ported for starlette's real
    // `http_cookies._unquote` call) advances `pos` between successive `pattern.search(s, pos)`
    // calls; since this method silently ignored `pos` entirely, every call re-matched from
    // position 0, `pos` never actually advanced, and the loop spun forever. Not a cookies-specific
    // fix — pos/endpos are a normal, commonly-relied-on part of the real `Pattern` API.
    private static object MatchAt(Regex regex, string s, bool isBytes, bool anchor, bool full, int pos = 0, int? endpos = null)
    {
        int end = Math.Clamp(endpos ?? s.Length, 0, s.Length);
        pos = Math.Clamp(pos, 0, s.Length);
        var m = pos <= end ? regex.Match(s, pos, end - pos) : Match.Empty;
        if (!m.Success || m.Index != pos)
            return PyNone.Instance;
        if (full && m.Index + m.Length != end)
            return PyNone.Instance;
        return MakeMatch(m, s, isBytes);
    }

    private static object SearchIn(Regex regex, string s, bool isBytes, int pos = 0, int? endpos = null)
    {
        int end = Math.Clamp(endpos ?? s.Length, 0, s.Length);
        pos = Math.Clamp(pos, 0, s.Length);
        var m = pos <= end ? regex.Match(s, pos, end - pos) : Match.Empty;
        return m.Success ? MakeMatch(m, s, isBytes) : PyNone.Instance;
    }

    private static PyList FindAll(Regex regex, string s, bool isBytes)
    {
        var results = new List<object>();
        foreach (Match m in regex.Matches(s))
        {
            if (m.Groups.Count > 2)
                results.Add(new PyTuple(GroupValues(m, isBytes).Skip(1).ToArray()));
            else if (m.Groups.Count == 2)
                results.Add(m.Groups[1].Success ? WrapResult(m.Groups[1].Value, isBytes) : WrapResult("", isBytes));
            else
                results.Add(WrapResult(m.Value, isBytes));
        }
        return new PyList(results);
    }

    private static PyIterator FindIter(Regex regex, string s, bool isBytes, int pos = 0, int? endpos = null)
    {
        int end = Math.Clamp(endpos ?? s.Length, 0, s.Length);
        pos = Math.Clamp(pos, 0, s.Length);
        IEnumerable<object> Gen()
        {
            if (pos > end)
                yield break;
            foreach (Match m in regex.Matches(s, pos))
            {
                if (m.Index + m.Length > end)
                    yield break;
                yield return MakeMatch(m, s, isBytes);
            }
        }
        return new PyIterator(Gen().GetEnumerator());
    }

    private static object[] GroupValues(Match m, bool isBytes) =>
        m.Groups.Cast<Group>().Select(g => g.Success ? WrapResult(g.Value, isBytes) : (object)PyNone.Instance).ToArray();

    private static PyInstance MakeMatch(Match m, string s, bool isBytes)
    {
        var inst = new PyInstance(MatchClass);
        inst.Dict[MatchKey] = m;
        inst.Dict[StringKey] = WrapResult(s, isBytes);
        inst.Dict[BytesModeKey] = isBytes;
        return inst;
    }

    // ------------------------------------------------------------------ sub / subn / split

    private static object Sub(Interpretation.Interp interp, Regex regex, object repl, string s, bool isBytes, int count)
    {
        int n = 0;
        string result = regex.Replace(s, m =>
        {
            if (count > 0 && n >= count)
                return m.Value;
            n++;
            if (repl is string or PyBytes)
                return ExpandTemplate(ToWorkingString(repl, out _), m);
            var callResult = interp.Call(repl, new object[] { MakeMatch(m, s, isBytes) });
            return ToWorkingString(callResult, out _);
        }, count > 0 ? count : int.MaxValue);
        return WrapResult(result, isBytes);
    }

    private static PyTuple SubN(Interpretation.Interp interp, Regex regex, object repl, string s, bool isBytes, int count)
    {
        int n = 0;
        string result = regex.Replace(s, m =>
        {
            if (count > 0 && n >= count)
                return m.Value;
            n++;
            if (repl is string or PyBytes)
                return ExpandTemplate(ToWorkingString(repl, out _), m);
            var callResult = interp.Call(repl, new object[] { MakeMatch(m, s, isBytes) });
            return ToWorkingString(callResult, out _);
        }, count > 0 ? count : int.MaxValue);
        return new PyTuple(new object[] { WrapResult(result, isBytes), new BigInteger(n) });
    }

    private static string ExpandTemplate(string template, Match m)
    {
        // Python replacement syntax: \1, \g<1>, \g<name> ; .NET's $1/${name} differ, so translate.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '\\' && i + 1 < template.Length)
            {
                if (char.IsDigit(template[i + 1]))
                {
                    int j = i + 1;
                    while (j < template.Length && char.IsDigit(template[j])) j++;
                    int idx = int.Parse(template[(i + 1)..j]);
                    sb.Append(idx < m.Groups.Count && m.Groups[idx].Success ? m.Groups[idx].Value : "");
                    i = j - 1;
                    continue;
                }
                if (template[i + 1] == 'g' && i + 2 < template.Length && template[i + 2] == '<')
                {
                    int close = template.IndexOf('>', i + 3);
                    if (close > 0)
                    {
                        string name = template[(i + 3)..close];
                        var g = int.TryParse(name, out var gi) ? m.Groups[gi] : m.Groups[name];
                        sb.Append(g.Success ? g.Value : "");
                        i = close;
                        continue;
                    }
                }
            }
            sb.Append(template[i]);
        }
        return sb.ToString();
    }

    private static PyList Split(Regex regex, string s, bool isBytes, int maxSplit)
    {
        var parts = regex.Split(s, maxSplit > 0 ? maxSplit + 1 : int.MaxValue);
        return new PyList(parts.Select(p => WrapResult(p, isBytes)));
    }

    // ------------------------------------------------------------------ Pattern class

    private static PyClass BuildPatternClass()
    {
        var cls = new PyClass("Pattern", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"Pattern.{n}", fn);

        Regex RxOf(object self) => (Regex)((PyInstance)self).Dict[RegexKey];

        cls.Dict["pattern"] = new PyProperty { Getter = new PyBuiltinFunction("Pattern.pattern", (_, a, _) => ((PyInstance)a[0]).Dict[PatternKey]) };

        Add("match", (_, a, kwargs) =>
        {
            var (s, isBytes) = ResolveSubject((PyInstance)a[0], a[1]);
            return MatchAt(RxOf(a[0]), s, isBytes, anchor: true, full: false, PosOf(a, kwargs, 2), EndposOf(a, kwargs, 3));
        });
        Add("fullmatch", (_, a, kwargs) =>
        {
            var (s, isBytes) = ResolveSubject((PyInstance)a[0], a[1]);
            return MatchAt(RxOf(a[0]), s, isBytes, anchor: true, full: true, PosOf(a, kwargs, 2), EndposOf(a, kwargs, 3));
        });
        Add("search", (_, a, kwargs) =>
        {
            var (s, isBytes) = ResolveSubject((PyInstance)a[0], a[1]);
            return SearchIn(RxOf(a[0]), s, isBytes, PosOf(a, kwargs, 2), EndposOf(a, kwargs, 3));
        });
        Add("findall", (_, a, _) =>
        {
            var (s, isBytes) = ResolveSubject((PyInstance)a[0], a[1]);
            return FindAll(RxOf(a[0]), s, isBytes);
        });
        Add("finditer", (_, a, kwargs) =>
        {
            var (s, isBytes) = ResolveSubject((PyInstance)a[0], a[1]);
            return FindIter(RxOf(a[0]), s, isBytes, PosOf(a, kwargs, 2), EndposOf(a, kwargs, 3));
        });
        Add("sub", (interp, a, _) =>
        {
            var (s, isBytes) = ResolveSubject((PyInstance)a[0], a[2]);
            return Sub(interp, RxOf(a[0]), a[1], s, isBytes, a.Length > 3 ? (int)PyOps.AsBigInt(a[3], "count") : 0);
        });
        Add("subn", (interp, a, _) =>
        {
            var (s, isBytes) = ResolveSubject((PyInstance)a[0], a[2]);
            return SubN(interp, RxOf(a[0]), a[1], s, isBytes, a.Length > 3 ? (int)PyOps.AsBigInt(a[3], "count") : 0);
        });
        Add("split", (_, a, _) =>
        {
            var (s, isBytes) = ResolveSubject((PyInstance)a[0], a[1]);
            return Split(RxOf(a[0]), s, isBytes, a.Length > 2 ? (int)PyOps.AsBigInt(a[2], "maxsplit") : 0);
        });
        Add("__repr__", (_, a, _) => $"re.compile({PyReprOfPattern(((PyInstance)a[0]).Dict[PatternKey])})");

        return cls;
    }

    private static string PyReprOfPattern(object pattern) => pattern switch
    {
        string s => $"'{s}'",
        PyBytes b => $"b'{Encoding.Latin1.GetString(b.Data)}'",
        _ => pattern.ToString() ?? "",
    };

    // ------------------------------------------------------------------ Match class

    private static PyClass BuildMatchClass()
    {
        var cls = new PyClass("Match", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"Match.{n}", fn);

        Match M(object self) => (Match)((PyInstance)self).Dict[MatchKey];
        bool IsBytes(object self) => (bool)((PyInstance)self).Dict[BytesModeKey];

        Add("group", (_, a, _) =>
        {
            var m = M(a[0]);
            bool isBytes = IsBytes(a[0]);
            if (a.Length <= 1)
                return WrapResult(m.Value, isBytes);
            if (a.Length == 2)
                return GroupValue(m, a[1], isBytes);
            return new PyTuple(a.Skip(1).Select(g => GroupValue(m, g, isBytes)).ToArray());
        });
        Add("groups", (_, a, kwargs) =>
        {
            var m = M(a[0]);
            bool isBytes = IsBytes(a[0]);
            // Real Match.groups(default=None): a normal positional-or-keyword parameter, not
            // keyword-only — found via starlette's real `match.groups("str")` (routing.py's
            // compile_path, passed positionally to default an unmatched optional `:type` group to
            // "str" instead of None).
            object def = a.Length > 1 ? a[1]
                : kwargs is not null && kwargs.TryGetValue("default", out var d) ? d
                : PyNone.Instance;
            var items = new object[m.Groups.Count - 1];
            for (int i = 1; i < m.Groups.Count; i++)
                items[i - 1] = m.Groups[i].Success ? WrapResult(m.Groups[i].Value, isBytes) : def;
            return new PyTuple(items);
        });
        Add("groupdict", (interpArg, a, kwargs) =>
        {
            var m = M(a[0]);
            bool isBytes = IsBytes(a[0]);
            object def = kwargs is not null && kwargs.TryGetValue("default", out var d) ? d : PyNone.Instance;
            var dict = new PyDict();
            foreach (Group g in m.Groups)
            {
                if (int.TryParse(g.Name, out int _unused))
                    continue; // numbered groups aren't part of groupdict()
                dict[g.Name] = g.Success ? WrapResult(g.Value, isBytes) : def;
            }
            return dict;
        });
        Add("start", (_, a, _) => new BigInteger(a.Length > 1 ? GroupOf(M(a[0]), a[1]).Index : M(a[0]).Index));
        Add("end", (_, a, _) => new BigInteger(a.Length > 1 ? GroupOf(M(a[0]), a[1]).Index + GroupOf(M(a[0]), a[1]).Length : M(a[0]).Index + M(a[0]).Length));
        Add("span", (_, a, _) =>
        {
            var g = a.Length > 1 ? GroupOf(M(a[0]), a[1]) : M(a[0]);
            return new PyTuple(new object[] { new BigInteger(g.Index), new BigInteger(g.Index + g.Length) });
        });
        cls.Dict["string"] = new PyProperty { Getter = new PyBuiltinFunction("Match.string", (_, a, _) => ((PyInstance)a[0]).Dict[StringKey]) };
        Add("__repr__", (interp, a, _) => $"<re.Match object; span={PyOps.Str(interp, interp.CallMethod(a[0], "span", Array.Empty<object>()))}, match='{M(a[0]).Value}'>");

        return cls;
    }

    private static object GroupValue(Match m, object key, bool isBytes) =>
        GroupOf(m, key) is { Success: true } g ? WrapResult(g.Value, isBytes) : PyNone.Instance;

    private static Group GroupOf(Match m, object key) => key switch
    {
        BigInteger bi => m.Groups[(int)bi],
        string s => m.Groups[s],
        _ => m.Groups[Convert.ToInt32(key)],
    };
}
