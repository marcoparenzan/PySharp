// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text.RegularExpressions;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Modules;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary>Time zones: the tz object, <c>tz_localize</c> / <c>tz_convert</c>, tz-aware <c>Timestamp</c> behaviour and offset strings.</summary>
internal static class PdTz
{
    public static readonly PyClass TzClass = new("ZoneInfo", new List<PyClass>());
    public static readonly PyClass ZoneInfoNotFoundError = new("ZoneInfoNotFoundError", new List<PyClass> { PyErr.KeyErrorClass });

    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a, k, names);
    private static PyBuiltinFunction Fn(string name, BuiltinFn f) => PdClasses.Fn(name, f);

    // ------------------------------------------------------------------ the tz object

    public static object Wrap(TzInfo? tz) => tz is null ? PyNone.Instance : new PyInstance(TzClass) { Native = tz };

    public static TzInfo? TzArg(object? o)
    {
        switch (o)
        {
            case null: case PyNone: return null;
            case string s: return TzInfo.Parse(s);
            case PyInstance { Native: TzInfo t }: return t;
            case PyInstance pi when pi.Class == DateTimeModule.TimeZoneClass && pi.Dict.TryGet("__offset__", out var off) && off is TimeSpan span: return TzInfo.Fixed((int)span.TotalSeconds);
        }
        throw PyErr.TypeError($"Cannot convert {PyOps.TypeName(o)} to a time zone");
    }

    private static string Repr(TzInfo tz)
        => tz == TzInfo.Utc ? "datetime.timezone.utc"
         : tz.IsFixed ? $"datetime.timezone(datetime.timedelta(seconds={tz.FixedSeconds}))"
         : $"zoneinfo.ZoneInfo(key='{tz.Name}')";

    private static void BuildTzClass()
    {
        var c = TzClass;
        TzInfo Me(object o) => (TzInfo)((PyInstance)o).Native!;
        void Def(string n, BuiltinFn f) => c.Dict[n] = Fn("tz." + n, f);
        Def("__str__", (_, a, _) => Me(a[0]).Name);
        Def("__repr__", (_, a, _) => Repr(Me(a[0])));
        Def("__eq__", (_, a, _) => a[1] is PyInstance { Native: TzInfo o } && o == Me(a[0]) || a[1] is string s && s == Me(a[0]).Name);
        Def("__hash__", (_, a, _) => new BigInteger(Me(a[0]).Name.GetHashCode()));
        c.Dict["key"] = new PyProperty { Getter = Fn("key", (_, a, _) => Me(a[0]).Name) };
        c.Dict["zone"] = c.Dict["key"];
        Def("utcoffset", (_, a, _) => a.Length > 1 && PdTime.TryTs(a[1], out var t) ? DateTimeModule.MakeTimeDelta(TimeSpan.FromSeconds(Me(a[0]).OffsetSeconds(t.Ticks, t.Unit))) : DateTimeModule.MakeTimeDelta(TimeSpan.FromSeconds(Me(a[0]).FixedSeconds)));
    }

    // ------------------------------------------------------------------ localize / convert

    private static string Amb(object? o) => o switch { null or PyNone => "raise", string s => s, bool b => b ? "first" : "last", _ => "raise" };

    /// <summary>tz_localize of a naive datetime column.</summary>
    public static Column Localize(Column c, TzInfo? tz, object? ambiguous, object? nonexistent, bool scalar = false)
    {
        if (c.Kind != Kind.DateTime) throw PyErr.TypeError($"Cannot localize a column of dtype {c.DTypeName}");
        if (c.Tz is not null)
        {
            if (tz is null) return c.ToWall();
            throw PyErr.TypeError(scalar ? "Cannot localize tz-aware Timestamp, use tz_convert for conversions" : "Already tz-aware, use tz_convert to convert.");
        }
        if (tz is null) return c;
        string nex = nonexistent is string ns ? ns : "raise";
        long shift = 0;
        if (nonexistent is not null and not string and not PyNone && PdTime.TryTd(nonexistent, out var td)) { nex = "shift"; shift = DateTimeCore.Scale(td.Ticks, td.Unit, c.Unit); }
        string amb = ambiguous is string s ? s : "raise";
        IReadOnlyList<bool>? flags = null;
        if (ambiguous is bool single) flags = Enumerable.Repeat(single, c.Length).ToList();
        else if (ambiguous is not null and not string and not PyNone && PdConv.IsListLike(ambiguous)) flags = PdConv.Cells(ambiguous).Select(x => x is true).ToList();
        if (amb == "infer")
        {
            var inferred = new List<bool>(); var seen = new HashSet<long>();
            foreach (var t in c.Ticks) inferred.Add(seen.Add(t));        // first occurrence = DST, second = standard
            flags = inferred; amb = "raise";
        }
        var res = c.Localize(tz, amb, nex, flags, shift);
        return res;
    }

    /// <summary>tz_convert: the same instants shown in another zone (None converts to UTC and drops the zone).</summary>
    public static Column Convert(Column c, TzInfo? tz, bool scalar = false)
    {
        if (c.Kind != Kind.DateTime) throw PyErr.TypeError($"Cannot convert a column of dtype {c.DTypeName}");
        if (c.Tz is null) throw PyErr.TypeError(scalar ? "Cannot convert tz-naive Timestamp, use tz_localize to localize" : "Cannot convert tz-naive timestamps, use tz_localize to localize");
        if (tz is null) return c.WithTz(null);
        return c.WithTz(tz);
    }

    // ------------------------------------------------------------------ offsets on aware data

    /// <summary>Adds a date offset to every instant: fixed sub-day ticks move the instant, calendar offsets (and days) move the wall clock.</summary>
    public static bool IsAbsolute(DateOffsetSpec spec) => spec is DateOffsetSpec.TickOffset t && t.Nanos < 86400_000_000_000;

    public static Column ApplyOffsetAware(Column c, DateOffsetSpec spec)
    {
        var u = DateTimeCore.Finer(c.Unit, spec.RequiredUnit);
        var src = c.WithUnit(u);
        var tz = src.Tz!;
        if (IsAbsolute(spec))
            return Column.FromDateTime(src.Ticks.Select(t => t == DateTimeCore.NaT ? t : spec.Add(t, u)).ToArray(), u, tz);
        var wall = src.Ticks.Select(t => t == DateTimeCore.NaT ? t : spec.Add(tz.ToWall(t, u), u)).ToArray();
        return Column.FromDateTime(wall.Select(w => w == DateTimeCore.NaT ? w : tz.FromWall(w, u, "first", "shift_forward")).ToArray(), u, tz);
    }

    // ------------------------------------------------------------------ offset strings

    private static readonly Regex OffsetString = new(@"^(\d{4}-\d{2}-\d{2}[T ]\d{2}(?::\d{2}(?::\d{2}(?:\.\d+)?)?)?)\s*(Z|[+-]\d{2}(?::?\d{2})?)$", RegexOptions.IgnoreCase);

    /// <summary>Splits <c>2020-01-01T10:00:00+01:00</c> into the naive text and the offset in seconds.</summary>
    public static bool SplitOffset(string s, out string naive, out int seconds)
    {
        naive = s; seconds = 0;
        var m = OffsetString.Match(s.Trim());
        if (!m.Success) return false;
        naive = m.Groups[1].Value;
        string o = m.Groups[2].Value;
        if (o is "Z" or "z") return true;
        int sign = o[0] == '-' ? -1 : 1;
        string digits = o.Substring(1).Replace(":", "");
        seconds = sign * (int.Parse(digits.Substring(0, 2)) * 3600 + (digits.Length > 2 ? int.Parse(digits.Substring(2, 2)) * 60 : 0));
        return true;
    }

    public static bool TryParseAware(string s, out Ts ts)
    {
        ts = default;
        if (!SplitOffset(s, out var naive, out int secs) || !DateTimeCore.TryParseIso(naive, out var t, out var u)) return false;
        ts = new Ts(t - secs * DateTimeCore.PerSecond(u), u, TzInfo.Fixed(secs));
        return true;
    }

    // ------------------------------------------------------------------ install

    private static Ts Me(object o) => (Ts)((PyInstance)o).Native!;

    private static string TzRepr(Ts t)
        => $"Timestamp('{DateTimeCore.FormatTimestamp(t.Tz!.ToWall(t.Ticks, t.Unit), t.Unit)}{DateTimeCore.OffsetText(t.Tz, t.Ticks, t.Unit, false)}', tz='{t.Tz.Name}')";

    private static object Reloc(Ts original, Ts wallResult) => PdTime.Wrap(new Ts(original.Tz!.FromWall(wallResult.Ticks, wallResult.Unit, "raise", "raise"), wallResult.Unit, original.Tz));

    public static void Install(PyModule m)
    {
        BuildTzClass();
        var ts = PdTime.TimestampClass;
        void Def(string n, BuiltinFn f) => ts.Dict[n] = Fn("Timestamp." + n, f);
        var prevRepr = (PyBuiltinFunction)ts.Dict["__repr__"];
        Def("__repr__", (i, a, k) => Me(a[0]).Tz is null ? i.Call(prevRepr, a, k) : TzRepr(Me(a[0])));
        ts.Dict["tz"] = new PyProperty { Getter = Fn("tz", (_, a, _) => Wrap(Me(a[0]).Tz)) };
        ts.Dict["tzinfo"] = ts.Dict["tz"];
        Def("tz_localize", (i, a, k) =>
        {
            var p = A("tz_localize", i, a.Skip(1).ToArray(), k, "tz", "ambiguous", "nonexistent");
            var t = Me(a[0]);
            var col = Localize(Column.FromDateTime(new[] { t.Ticks }, t.Unit, t.Tz), TzArg(p[0]), p[1], p[2], true);
            return PdTime.Wrap(new Ts(col.Ticks[0], col.Unit, col.Tz));
        });
        Def("tz_convert", (i, a, k) =>
        {
            var t = Me(a[0]);
            var col = Convert(Column.FromDateTime(new[] { t.Ticks }, t.Unit, t.Tz), TzArg(A("tz_convert", i, a.Skip(1).ToArray(), k, "tz")[0]), true);
            return PdTime.Wrap(new Ts(col.Ticks[0], col.Unit, col.Tz));
        });
        Def("utcoffset", (_, a, _) => Me(a[0]).Tz is { } z ? DateTimeModule.MakeTimeDelta(TimeSpan.FromSeconds(z.OffsetSeconds(Me(a[0]).Ticks, Me(a[0]).Unit))) : PyNone.Instance);
        Def("dst", (_, a, _) => Me(a[0]).Tz is not null ? DateTimeModule.MakeTimeDelta(TimeSpan.Zero) : PyNone.Instance);
        Def("tzname", (_, a, _) => Me(a[0]).Tz is { } z ? z.Abbreviation(Me(a[0]).Ticks, Me(a[0]).Unit) : PyNone.Instance);
        var prevIso = (PyBuiltinFunction)ts.Dict["isoformat"];
        Def("isoformat", (i, a, k) =>
        {
            var t = Me(a[0]);
            if (t.Tz is null) return i.Call(prevIso, a, k);
            return DateTimeCore.FormatIso(t.Tz.ToWall(t.Ticks, t.Unit), t.Unit) + DateTimeCore.OffsetText(t.Tz, t.Ticks, t.Unit);
        });
        var prevStrf = (PyBuiltinFunction)ts.Dict["strftime"];
        Def("strftime", (i, a, k) =>
        {
            var t = Me(a[0]);
            if (t.Tz is null) return i.Call(prevStrf, a, k);
            return DateTimeCore.StrftimeAware(t.Ticks, t.Unit, t.Tz, (string)A("strftime", i, a.Skip(1).ToArray(), k, "format").Required(0));
        });
        foreach (var name in new[] { "floor", "ceil", "round", "normalize", "replace", "as_unit" })
        {
            var nm = name;
            var prev = (PyBuiltinFunction)ts.Dict[nm];
            Def(nm, (i, a, k) =>
            {
                var t = Me(a[0]);
                if (t.Tz is null) return i.Call(prev, a, k);
                var wall = new Ts(t.Tz.ToWall(t.Ticks, t.Unit), t.Unit);
                var args = (object[])a.Clone(); args[0] = PdTime.Wrap(wall);
                var r = i.Call(prev, args, k);
                if (nm == "as_unit") { var rr = (Ts)((PyInstance)r).Native!; return PdTime.Wrap(new Ts(DateTimeCore.Scale(t.Ticks, t.Unit, rr.Unit), rr.Unit, t.Tz)); }
                return Reloc(t, (Ts)((PyInstance)r).Native!);
            });
        }
        var prevPy = (PyBuiltinFunction)ts.Dict["to_pydatetime"];
        Def("to_pydatetime", (i, a, k) => Me(a[0]).Tz is not null ? throw PyErr.NotImplementedError("to_pydatetime() of a tz-aware Timestamp") : i.Call(prevPy, a, k));
        // Timestamp(..., tz=) is handled in the constructor wrapper below
        var prevNew = (PyBuiltinFunction)ts.Dict["__new__"];
        ts.Dict["__new__"] = Fn("Timestamp.__new__", (i, a, k) =>
        {
            object? tzArg = null;
            if (k is not null && k.TryGetValue("tz", out var tzv)) { tzArg = tzv; k = k.Where(e => e.Key != "tz").ToDictionary(e => e.Key, e => e.Value); }
            if (k is not null && k.TryGetValue("tzinfo", out var tzi)) { tzArg ??= tzi; k = k.Where(e => e.Key != "tzinfo").ToDictionary(e => e.Key, e => e.Value); }
            var res = i.Call(prevNew, a, k);
            var zone = TzArg(tzArg);
            if (res is PyInstance { Native: Ts t })
            {
                if (zone is null) return res;
                if (t.Tz is null) return PdTime.Wrap(new Ts(zone.FromWall(t.Ticks, t.Unit, "raise", "raise"), t.Unit, zone));
                return PdTime.Wrap(new Ts(t.Ticks, t.Unit, zone));
            }
            return res;
        });
        InstallColumnApi();
        m.Dict["Timestamp"] = ts;
    }

    // ------------------------------------------------------------------ Series / DataFrame / Index / .dt

    /// <summary>A fixed sub-day frequency survives a zone conversion; calendar frequencies (days, months ...) do not.</summary>
    private static string? KeepFreq(FIndex ix) => ix.Freq is { } f && IsAbsolute(DateOffsetSpec.Parse(f)) ? f : null;

    private static void InstallColumnApi()
    {
        // on Series and DataFrame the methods act on the index
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            bool isSeries = cls == PdClasses.Series;
            FIndex IndexOf(object o) => isSeries ? PdConv.S(o).Index : PdConv.D(o).Index;
            object Rebuild(object o, FIndex ix) => isSeries ? PdConv.Wrap(new Series(PdConv.S(o).Values, ix, PdConv.S(o).Name)) : PdConv.Wrap(new DataFrame(PdConv.D(o).Data, PdConv.D(o).Columns, ix));
            cls.Dict["tz_localize"] = Fn("tz_localize", (i, a, k) =>
            {
                var p = A("tz_localize", i, a.Skip(1).ToArray(), k, "tz", "axis", "level", "copy", "ambiguous", "nonexistent");
                var ix = IndexOf(a[0]);
                return Rebuild(a[0], new FIndex(Localize(ix.Labels, TzArg(p[0]), p[4], p[5]), ix.Name));
            });
            cls.Dict["tz_convert"] = Fn("tz_convert", (i, a, k) =>
            {
                var p = A("tz_convert", i, a.Skip(1).ToArray(), k, "tz", "axis", "level", "copy");
                var ix = IndexOf(a[0]);
                return Rebuild(a[0], new FIndex(Convert(ix.Labels, TzArg(p[0])), ix.Name) { Freq = KeepFreq(ix) });
            });
        }
        // DatetimeIndex
        var idx = PdClasses.Index;
        FIndex Ix(object o) => (FIndex)((PyInstance)o).Native!;
        idx.Dict["tz"] = new PyProperty { Getter = Fn("tz", (_, a, _) => Ix(a[0]).Labels.Kind == Kind.DateTime ? Wrap(Ix(a[0]).Labels.Tz) : throw PyErr.AttributeError("'Index' object has no attribute 'tz'")) };
        idx.Dict["tzinfo"] = idx.Dict["tz"];
        idx.Dict["tz_localize"] = Fn("tz_localize", (i, a, k) =>
        {
            var p = A("tz_localize", i, a.Skip(1).ToArray(), k, "tz", "ambiguous", "nonexistent");
            var ix = Ix(a[0]);
            return PdConv.Wrap(new FIndex(Localize(ix.Labels, TzArg(p[0]), p[1], p[2]), ix.Name) { Freq = ix.Labels.Tz is null && p[0] is not null and not PyNone ? ix.Freq : null });
        });
        idx.Dict["tz_convert"] = Fn("tz_convert", (i, a, k) =>
        {
            var ix = Ix(a[0]);
            return PdConv.Wrap(new FIndex(Convert(ix.Labels, TzArg(A("tz_convert", i, a.Skip(1).ToArray(), k, "tz")[0])), ix.Name) { Freq = KeepFreq(ix) });
        });
        // .dt
        var dt = PdDates.DtAccessor;
        Series Own(object o) => (Series)((PyInstance)o).Native!;
        dt.Dict["tz_localize"] = Fn("tz_localize", (i, a, k) =>
        {
            var p = A("tz_localize", i, a.Skip(1).ToArray(), k, "tz", "ambiguous", "nonexistent");
            var s = Own(a[0]);
            return PdConv.Wrap(new Series(Localize(s.Values, TzArg(p[0]), p[1], p[2]), s.Index, s.Name));
        });
        dt.Dict["tz_convert"] = Fn("tz_convert", (i, a, k) =>
        {
            var s = Own(a[0]);
            return PdConv.Wrap(new Series(Convert(s.Values, TzArg(A("tz_convert", i, a.Skip(1).ToArray(), k, "tz")[0])), s.Index, s.Name));
        });
    }
}

/// <summary>Hash/equality of a Python instance (a date, say) by the interpreter's own rules, so that equal objects group together.</summary>
internal sealed class PyKey : IEquatable<PyKey>
{
    private readonly PyInstance _value;
    public PyKey(PyInstance value) => _value = value;
    public bool Equals(PyKey? other) => other is not null && (ReferenceEquals(_value, other._value) || PdConv.Interp is { } ip && ip.RichEquals(_value, other._value));
    public override bool Equals(object? obj) => obj is PyKey k && Equals(k);
    public override int GetHashCode()
    {
        if (_value.Dict.TryGet("__value__", out var raw)) return raw.GetHashCode();
        try { if (PdConv.Interp is { } ip && ip.CallMethod(_value, "__hash__", Array.Empty<object>()) is BigInteger h) return h.GetHashCode(); } catch (PyRaise) { }
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_value);
    }
}
