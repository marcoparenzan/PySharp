// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text.RegularExpressions;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary><c>apply</c>, <c>map</c>, <c>agg</c>, <c>pipe</c> (calling back into Python) and the <c>.str</c> accessor.</summary>
internal static class PdApply
{
    private static Series S(object o) => PdConv.S(o);
    private static DataFrame D(object o) => PdConv.D(o);
    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);

    public static readonly PyClass StrAccessor = new("StringMethods", new List<PyClass>());

    private static bool IsCallable(object o) => o is PyFunction or PyBuiltinFunction or PyClass || o is PyInstance { Class: var c } && c.Mro.Any(m => m.Dict.ContainsKey("__call__"));

    private static object Call(Interp i, object fn, object arg, object[]? extra = null, Dictionary<string, object>? kw = null)
        => i.Call(fn, extra is null ? new[] { arg } : new[] { arg }.Concat(extra).ToArray(), kw);

    public static void Install()
    {
        var series = PdClasses.Series;
        var frame = PdClasses.DataFrame;
        void SDef(string n, BuiltinFn f) => series.Dict[n] = PdClasses.Fn($"Series.{n}", f);
        void FDef(string n, BuiltinFn f) => frame.Dict[n] = PdClasses.Fn($"DataFrame.{n}", f);

        SDef("apply", (i, a, k) =>
        {
            var p = A("apply", i, a, k, "func", "convert_dtype", "args");
            var s = S(a[0]);
            var extra = p.Has(2) ? ((PyTuple)p[2]!).Items : null;
            var results = Enumerable.Range(0, s.Length).Select(x => PdConv.ToCell(Call(i, p.Required(0), PdConv.FromCell(s.Values, x), extra, k?.Where(e => e.Key is not ("func" or "convert_dtype" or "args")).ToDictionary(e => e.Key, e => e.Value)))).ToList();
            return PdConv.Wrap(new Series(Column.Infer(results), s.Index, s.Name));
        });
        SDef("map", (i, a, k) =>
        {
            var p = A("map", i, a, k, "arg", "na_action");
            var s = S(a[0]);
            var arg = p.Required(0);
            bool skipNa = p[1] is "ignore";
            var results = new List<object?>();
            Dictionary<object, object?>? lookup = null;
            if (arg is PyDict dm) lookup = dm.Entries.ToDictionary(e => Column.Key(PdConv.ToCell(e.Key)) ?? Column.NaNKey, e => PdConv.ToCell(e.Value));
            else if (arg is PyInstance { Native: Series ms }) lookup = Enumerable.Range(0, ms.Length).ToDictionary(x => Column.Key(ms.Index.Labels[x]) ?? Column.NaNKey, x => ms.Values[x]);
            for (int x = 0; x < s.Length; x++)
            {
                if (skipNa && s.Values.IsNa(x)) { results.Add(null); continue; }
                if (lookup is not null) results.Add(lookup.TryGetValue(Column.Key(s.Values[x]) ?? Column.NaNKey, out var v) ? v : null);
                else results.Add(PdConv.ToCell(Call(i, arg, PdConv.FromCell(s.Values, x))));
            }
            return PdConv.Wrap(new Series(Column.Infer(results), s.Index, s.Name));
        });
        FDef("apply", (i, a, k) =>
        {
            var p = A("apply", i, a, k, "func", "axis", "raw", "result_type", "args");
            var d = D(a[0]);
            int axis = p.Has(1) ? PdOps.AxisOf(p[1]!) : 0;
            var fn = p.Required(0);
            var extra = p.Has(4) ? ((PyTuple)p[4]!).Items : null;
            int count = axis == 0 ? d.NCols : d.NRows;
            var outs = new List<object>();
            for (int x = 0; x < count; x++)
            {
                Series arg = axis == 0 ? d.GetColumn(x) : new Series(Column.Infer(d.Data.Select(c => c[x]).ToList()), d.Columns, d.Index.Labels[x]);
                outs.Add(Call(i, fn, PdConv.Wrap(arg), extra));
            }
            var labels = axis == 0 ? d.Columns : d.Index;
            if (outs.Count > 0 && outs.All(o => o is PyInstance { Native: Series }))
            {
                var ss = outs.Select(o => (Series)((PyInstance)o).Native!).ToList();
                var resIndex = ss[0].Index;
                if (axis == 0) return PdConv.Wrap(new DataFrame(ss.Select(x => x.Values), labels, resIndex));
                var cols = Enumerable.Range(0, resIndex.Length).Select(c => Column.Infer(ss.Select(x => x.Values[c]).ToList())).ToList();
                return PdConv.Wrap(new DataFrame(cols, resIndex, labels));
            }
            return PdConv.Wrap(new Series(Column.Infer(outs.Select(PdConv.ToCell).ToList()), labels));
        });
        FDef("map", (i, a, k) =>
        {
            var p = A("map", i, a, k, "func", "na_action");
            var d = D(a[0]);
            bool skipNa = p[1] is "ignore";
            return PdConv.Wrap(new DataFrame(d.Data.Select(c => Column.Infer(Enumerable.Range(0, c.Length)
                .Select(x => skipNa && c.IsNa(x) ? null : PdConv.ToCell(Call(i, p.Required(0), PdConv.FromCell(c, x)))).ToList())), d.Columns, d.Index));
        });
        foreach (var cls in new[] { series, frame })
        {
            var c2 = cls;
            c2.Dict["pipe"] = PdClasses.Fn("pipe", (i, a, k) => i.Call(a[1], new[] { a[0] }.Concat(a.Skip(2)).ToArray(), k));
            BuiltinFn agg = (i, a, k) =>
            {
                var p = A("agg", i, a, k, "func", "axis");
                var fn = p.Required(0);
                if (a[0] is PyInstance { Native: Series s })
                {
                    if (PdConv.IsListLike(fn))
                    {
                        var names = (fn is PyList pl ? pl.Items : ((PyTuple)fn).Items.ToList());
                        var vals = names.Select(n => PdConv.ToCell(RunAgg(i, a[0], n))).ToList();
                        return PdConv.Wrap(new Series(Column.Infer(vals), new FIndex(Column.Infer(names.Select(AggName).Cast<object?>().ToList())), s.Name));
                    }
                    return RunAgg(i, a[0], fn);
                }
                var d = D(a[0]);
                if (fn is PyDict map)
                {
                    bool anyList = map.Values.Any(PdConv.IsListLike);
                    if (!anyList)
                        return PdConv.Wrap(new Series(Column.Infer(map.Entries.Select(e => PdConv.ToCell(RunAgg(i, PdConv.Wrap(d.GetColumn(PdConv.ToCell(e.Key))), e.Value))).ToList()), new FIndex(Column.Infer(map.Keys.Select(PdConv.ToCell).ToList()))));
                    var rowNames = new List<string>();
                    foreach (var v in map.Values) foreach (var n in PdConv.IsListLike(v) ? ((PyList)v).Items : new List<object> { v }) { var an = AggName(n); if (!rowNames.Contains(an)) rowNames.Add(an); }
                    var cols = new List<Column>();
                    foreach (var e in map.Entries)
                    {
                        var fns = PdConv.IsListLike(e.Value) ? ((PyList)e.Value).Items : new List<object> { e.Value };
                        var col = PdConv.Wrap(d.GetColumn(PdConv.ToCell(e.Key)));
                        var byName = fns.ToDictionary(AggName, f => PdConv.ToCell(RunAgg(i, col, f)));
                        cols.Add(Column.Infer(rowNames.Select(n => byName.TryGetValue(n, out var v) ? v : null).ToList()));
                    }
                    return PdConv.Wrap(new DataFrame(cols, new FIndex(Column.Infer(map.Keys.Select(PdConv.ToCell).ToList())), new FIndex(Column.Infer(rowNames.Cast<object?>().ToList()))));
                }
                if (PdConv.IsListLike(fn))
                {
                    var names = fn is PyList pl ? pl.Items : ((PyTuple)fn).Items.ToList();
                    var cols = Enumerable.Range(0, d.NCols).Select(j => Column.Infer(names.Select(n => PdConv.ToCell(RunAgg(i, PdConv.Wrap(d.GetColumn(j)), n))).ToList())).ToList();
                    return PdConv.Wrap(new DataFrame(cols, d.Columns, new FIndex(Column.Infer(names.Select(AggName).Cast<object?>().ToList()))));
                }
                return PdConv.Wrap(new Series(Column.Infer(Enumerable.Range(0, d.NCols).Select(j => PdConv.ToCell(RunAgg(i, PdConv.Wrap(d.GetColumn(j)), fn))).ToList()), d.Columns));
            };
            c2.Dict["agg"] = PdClasses.Fn("agg", agg);
            c2.Dict["aggregate"] = c2.Dict["agg"];
        }
        series.Dict["str"] = new PyProperty { Getter = PdClasses.Fn("str", (i, a, k) => new PyInstance(StrAccessor) { Native = S(a[0]) }) };
        BuildStrAccessor();
    }

    private static string AggName(object f) => f switch
    {
        string s => s,
        PyFunction pf => pf.Name,
        PyBuiltinFunction bf => bf.Name,
        _ => "<lambda>",
    };

    private static object RunAgg(Interp i, object target, object fn)
        => fn is string name ? i.CallMethod(target, name, Array.Empty<object>()) : i.Call(fn, new[] { target });

    // ================================================================== .str

    private static Series Me(object o) => (Series)((PyInstance)o).Native!;

    private static string?[] Strs(Series s)
    {
        var c = s.Values;
        if (c.Kind == Kind.Str) return c.Strings;
        if (c.Kind == Kind.Object) return c.Objects.Select(o => o as string).ToArray();
        throw PyErr.AttributeError("Can only use .str accessor with string values!");
    }

    private static object StrOut(Series s, Column c) => PdConv.Wrap(new Series(c, s.Index, s.Name));

    private static object MapStr(object self, Func<string, string?> f)
    {
        var s = Me(self);
        return StrOut(s, Column.FromStrings(Strs(s).Select(x => x is null ? null : f(x)).ToArray()));
    }

    private static object MapBool(object self, Func<string, bool> f)
    {
        var s = Me(self);
        return StrOut(s, Column.FromBools(Strs(s).Select(x => x is not null && f(x)).ToArray()));
    }

    private static object MapNum(object self, Func<string, long> f)
    {
        var s = Me(self);
        var src = Strs(s);
        if (src.Any(x => x is null)) return StrOut(s, Column.FromDoubles(src.Select(x => x is null ? double.NaN : (double)f(x)).ToArray()));
        return StrOut(s, Column.FromLongs(src.Select(f).ToArray()));
    }

    private static Regex Rx(string pat, bool regex, bool caseSensitive = true)
        => new(regex ? ConvertPattern(pat) : Regex.Escape(pat), caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

    /// <summary>Python <c>re</c> to .NET: named groups <c>(?P&lt;n&gt;…)</c> become <c>(?&lt;n&gt;…)</c>.</summary>
    private static string ConvertPattern(string p) => p.Replace("(?P<", "(?<").Replace("(?P=", "\\k<");

    private static void BuildStrAccessor()
    {
        var cls = StrAccessor;
        void Def(string n, BuiltinFn f) => cls.Dict[n] = PdClasses.Fn($"str.{n}", f);

        Def("lower", (_, a, _) => MapStr(a[0], x => x.ToLowerInvariant()));
        Def("upper", (_, a, _) => MapStr(a[0], x => x.ToUpperInvariant()));
        Def("title", (_, a, _) => MapStr(a[0], Title));
        Def("capitalize", (_, a, _) => MapStr(a[0], x => x.Length == 0 ? x : char.ToUpperInvariant(x[0]) + x[1..].ToLowerInvariant()));
        Def("swapcase", (_, a, _) => MapStr(a[0], x => new string(x.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray())));
        foreach (var (name, mode) in new[] { ("strip", 0), ("lstrip", 1), ("rstrip", 2) })
        {
            var nm = name; var md = mode;
            Def(nm, (i, a, k) =>
            {
                var chars = A(nm, i, a, k, "to_strip").Has(0) ? ((string)A(nm, i, a, k, "to_strip")[0]!).ToCharArray() : null;
                return MapStr(a[0], x => chars is null ? (md == 0 ? x.Trim() : md == 1 ? x.TrimStart() : x.TrimEnd()) : (md == 0 ? x.Trim(chars) : md == 1 ? x.TrimStart(chars) : x.TrimEnd(chars)));
            });
        }
        Def("len", (_, a, _) => MapNum(a[0], x => new System.Globalization.StringInfo(x).LengthInTextElements == x.Length ? x.Length : x.EnumerateRunes().Count()));
        Def("contains", (i, a, k) =>
        {
            var p = A("contains", i, a, k, "pat", "case", "flags", "na", "regex");
            var rx = Rx((string)p.Required(0), p.Bool(4, true), p.Bool(1, true));
            return MapBool(a[0], x => rx.IsMatch(x));
        });
        Def("startswith", (i, a, k) => { var pat = A("startswith", i, a, k, "pat", "na").Required(0); return MapBool(a[0], x => AnyOf(pat, t => x.StartsWith(t, StringComparison.Ordinal))); });
        Def("endswith", (i, a, k) => { var pat = A("endswith", i, a, k, "pat", "na").Required(0); return MapBool(a[0], x => AnyOf(pat, t => x.EndsWith(t, StringComparison.Ordinal))); });
        Def("match", (i, a, k) => { var rx = new Regex("^(?:" + ConvertPattern((string)A("match", i, a, k, "pat", "case", "flags", "na").Required(0)) + ")"); return MapBool(a[0], x => rx.IsMatch(x)); });
        Def("replace", (i, a, k) =>
        {
            var p = A("replace", i, a, k, "pat", "repl", "n", "case", "flags", "regex");
            var rx = Rx((string)p.Required(0), p.Bool(5, false), p.Bool(3, true));
            string repl = (string)p.Required(1);
            if (p.Bool(5, false)) repl = Regex.Replace(repl, @"\\(\d+)", "$${$1}");
            else repl = repl.Replace("$", "$$");
            int n = p.Int(2, -1);
            return MapStr(a[0], x => n < 0 ? rx.Replace(x, repl) : rx.Replace(x, repl, n));
        });
        Def("count", (i, a, k) => { var rx = Rx((string)A("count", i, a, k, "pat", "flags").Required(0), true); return MapNum(a[0], x => rx.Matches(x).Count); });
        Def("find", (i, a, k) => { var sub = (string)A("find", i, a, k, "sub", "start", "end").Required(0); return MapNum(a[0], x => x.IndexOf(sub, StringComparison.Ordinal)); });
        foreach (var (name, f) in new (string, Func<string, bool>)[]
        {
            ("isdigit", x => x.Length > 0 && x.All(char.IsDigit)), ("isalpha", x => x.Length > 0 && x.All(char.IsLetter)),
            ("isnumeric", x => x.Length > 0 && x.All(char.IsNumber)), ("isalnum", x => x.Length > 0 && x.All(char.IsLetterOrDigit)),
            ("isupper", x => x.Any(char.IsLetter) && !x.Any(char.IsLower)), ("islower", x => x.Any(char.IsLetter) && !x.Any(char.IsUpper)),
            ("isspace", x => x.Length > 0 && x.All(char.IsWhiteSpace)),
        })
        {
            var fn = f;
            Def(name, (_, a, _) => MapBool(a[0], fn));
        }
        Def("slice", (i, a, k) =>
        {
            var p = A("slice", i, a, k, "start", "stop", "step");
            var sl = new PySlice(p.Has(0) ? p[0]! : PyNone.Instance, p.Has(1) ? p[1]! : PyNone.Instance, p.Has(2) ? p[2]! : PyNone.Instance);
            return MapStr(a[0], x => { var idx = PdSelect.PositionalSlice(sl, x.Length); return new string(idx.Select(j => x[j]).ToArray()); });
        });
        Def("get", (i, a, k) =>
        {
            int pos = A("get", i, a, k, "i").Int(0, 0);
            var s = Me(a[0]);
            return StrOut(s, Column.FromStrings(Strs(s).Select(x => { if (x is null) return null; int j = pos < 0 ? pos + x.Length : pos; return j >= 0 && j < x.Length ? x[j].ToString() : null; }).ToArray()));
        });
        Def("zfill", (i, a, k) =>
        {
            int w = A("zfill", i, a, k, "width").Int(0, 0);
            return MapStr(a[0], x => { if (x.Length >= w) return x; bool sign = x.Length > 0 && x[0] is '+' or '-'; return sign ? x[0] + x[1..].PadLeft(w - 1, '0') : x.PadLeft(w, '0'); });
        });
        Def("pad", (i, a, k) =>
        {
            var p = A("pad", i, a, k, "width", "side", "fillchar");
            int w = p.Int(0, 0); string side = p.Has(1) ? (string)p[1]! : "left"; char fc = p.Has(2) ? ((string)p[2]!)[0] : ' ';
            return MapStr(a[0], x => side == "left" ? x.PadLeft(w, fc) : side == "right" ? x.PadRight(w, fc) : Center(x, w, fc));
        });
        Def("center", (i, a, k) => { var p = A("center", i, a, k, "width", "fillchar"); int w = p.Int(0, 0); char fc = p.Has(1) ? ((string)p[1]!)[0] : ' '; return MapStr(a[0], x => Center(x, w, fc)); });
        Def("ljust", (i, a, k) => { var p = A("ljust", i, a, k, "width", "fillchar"); int w = p.Int(0, 0); char fc = p.Has(1) ? ((string)p[1]!)[0] : ' '; return MapStr(a[0], x => x.PadRight(w, fc)); });
        Def("rjust", (i, a, k) => { var p = A("rjust", i, a, k, "width", "fillchar"); int w = p.Int(0, 0); char fc = p.Has(1) ? ((string)p[1]!)[0] : ' '; return MapStr(a[0], x => x.PadLeft(w, fc)); });
        Def("repeat", (i, a, k) => { int n = A("repeat", i, a, k, "repeats").Int(0, 1); return MapStr(a[0], x => string.Concat(Enumerable.Repeat(x, Math.Max(0, n)))); });
        Def("removeprefix", (i, a, k) => { var pre = (string)A("removeprefix", i, a, k, "prefix").Required(0); return MapStr(a[0], x => x.StartsWith(pre, StringComparison.Ordinal) ? x[pre.Length..] : x); });
        Def("removesuffix", (i, a, k) => { var suf = (string)A("removesuffix", i, a, k, "suffix").Required(0); return MapStr(a[0], x => x.EndsWith(suf, StringComparison.Ordinal) ? x[..^suf.Length] : x); });
        Def("cat", (i, a, k) =>
        {
            var p = A("cat", i, a, k, "others", "sep", "na_rep", "join");
            var s = Me(a[0]);
            string sep = p.Has(1) ? (string)p[1]! : "";
            var parts = Strs(s);
            if (!p.Has(0))
                return string.Join(sep, parts.Where(x => x is not null).Select(x => x!));
            var other = PdConv.ToColumn(p[0]!);
            string?[] os = other.Kind == Kind.Str ? other.Strings : other.Objects.Select(x => x as string).ToArray();
            return StrOut(s, Column.FromStrings(parts.Select((x, j) => x is null || os[j] is null ? null : x + sep + os[j]).ToArray()));
        });
        Def("split", (i, a, k) =>
        {
            var p = A("split", i, a, k, "pat", "n", "expand", "regex");
            var s = Me(a[0]);
            string? pat = p.Has(0) ? (string)p[0]! : null;
            int n = p.Int(1, -1);
            bool regex = p.Bool(3, false) || (pat is { Length: > 1 } && p[3] is null && false);
            List<string>? Split(string? x)
            {
                if (x is null) return null;
                if (pat is null) return x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList() is var w && n >= 0 && w.Count > n + 1 ? SplitWs(x, n) : x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (regex) return n < 0 ? Regex.Split(x, ConvertPattern(pat)).ToList() : new Regex(ConvertPattern(pat)).Split(x, n + 1).ToList();
                return (n < 0 ? x.Split(pat) : x.Split(pat, n + 1)).ToList();
            }
            var rows = Strs(s).Select(Split).ToList();
            if (!p.Bool(2, false))
                return StrOut(s, Column.FromObjects(rows.Select(r => r is null ? (object?)double.NaN : new PyList(r.Select(t => (object)t))).ToArray()));
            int width = rows.Count == 0 ? 0 : rows.Max(r => r?.Count ?? 0);
            var cols = Enumerable.Range(0, width).Select(c => Column.FromStrings(rows.Select(r => r is not null && c < r.Count ? r[c] : null).ToArray())).ToList();
            return PdConv.Wrap(new DataFrame(cols, FIndex.Range(width), s.Index));
        });
    }

    private static List<string> SplitWs(string x, int n)
    {
        var res = new List<string>();
        var m = Regex.Matches(x, @"\S+");
        for (int j = 0; j < m.Count; j++)
        {
            if (j < n) res.Add(m[j].Value);
            else { res.Add(x[m[j].Index..].TrimEnd()); break; }
        }
        return res;
    }

    private static bool AnyOf(object pat, Func<string, bool> f)
        => pat is string s ? f(s) : pat is PyTuple t ? t.Items.Any(x => f((string)x)) : throw PyErr.TypeError("expected a string or tuple of strings");

    private static string Title(string x)
    {
        var sb = new System.Text.StringBuilder();
        bool start = true;
        foreach (var c in x)
        {
            sb.Append(start ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            start = !char.IsLetter(c);
        }
        return sb.ToString();
    }

    private static string Center(string x, int w, char fc)
    {
        int marg = w - x.Length;
        if (marg <= 0) return x;
        int left = marg / 2 + (marg & w & 1);
        return new string(fc, left) + x + new string(fc, marg - left);
    }
}
