// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Modules;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary><c>Period</c>, <c>PeriodIndex</c>, <c>period_range</c>, <c>to_period</c> / <c>to_timestamp</c> and the <c>.dt</c> accessor of period data.</summary>
internal static class PdPeriod
{
    public static readonly PyClass PeriodClass = new("Period", new List<PyClass>());
    public static readonly PyClass PeriodAccessor = new("PeriodProperties", new List<PyClass>());
    public static readonly PyClass IncompatibleFrequency = new("IncompatibleFrequency", new List<PyClass> { PyErr.ValueErrorClass });

    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a, k, names);
    private static PyBuiltinFunction Fn(string name, BuiltinFn f) => PdClasses.Fn(name, f);

    // ------------------------------------------------------------------ wrapping

    public static object Wrap(Per p) => p.Ordinal == PeriodCore.NaT ? PdTime.NaT : new PyInstance(PeriodClass) { Native = p };

    private static string[] MonthNames = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    /// <summary>The date offset that names a period frequency (<c>MonthEnd</c> for <c>M</c> ...).</summary>
    public static DateOffsetSpec OffsetFor(PeriodFreq f, int n = 1)
    {
        string name = f.Unit switch
        {
            PUnit.Year => "YE-" + MonthNames[f.Anchor - 1],
            PUnit.Quarter => "QE-" + MonthNames[f.Anchor - 1],
            PUnit.Month => "ME",
            _ => f.Name,
        };
        return DateOffsetSpec.Parse(name).WithN(n * f.Mult);
    }

    public static object WrapDiff(PerDiff d) => d.IsNa ? PdTime.NaT : PdTime.WrapOffset(OffsetFor(d.Freq.Base, (int)d.N));

    private static PyRaise Incompatible(string msg) => PyErr.Raise(IncompatibleFrequency, msg);

    public static bool TryPer(object? o, out Per p)
    {
        p = default;
        if (o is Per q) { p = q; return true; }
        if (o is PyInstance { Native: Per q2 }) { p = q2; return true; }
        return false;
    }

    private static Per Me(object o) => (Per)((PyInstance)o).Native!;

    public static PeriodFreq FreqArg(object f)
    {
        try
        {
            if (f is string s) return PeriodFreq.Parse(s);
            if (f is PyInstance { Native: DateOffsetSpec spec }) return FromOffset(spec);
        }
        catch (FrameException ex) { throw PyErr.ValueError(ex.Message); }
        throw PyErr.TypeError("freq must be a string or an offset");
    }

    private static PeriodFreq FromOffset(DateOffsetSpec spec)
    {
        string text = spec.FreqString;
        int mult = spec.N;
        text = text.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        string Mult(string baseName) => mult > 1 ? mult + baseName : baseName;
        if (text.StartsWith("ME") || text.StartsWith("MS")) return PeriodFreq.Parse(Mult("M"));
        if (text.StartsWith("YE") || text.StartsWith("YS")) return PeriodFreq.Parse(Mult("Y" + text.Substring(2)));
        if (text.StartsWith("QE") || text.StartsWith("QS")) return PeriodFreq.Parse(Mult("Q" + text.Substring(2)));
        return PeriodFreq.Parse(Mult(text));
    }

    // ------------------------------------------------------------------ conversion helpers

    private static Ts StartOf(Per p, bool end)
    {
        var (s, e) = PeriodCore.Span(p.Freq, p.Ordinal);
        return new Ts(end ? e : s, p.Freq.TickUnit);
    }

    private static long ToPeriodOrdinal(PeriodFreq f, long ticks, DateUnit unit) => PeriodCore.Ordinal(f, ticks, unit);

    public static Column ToPeriodColumn(Column c, PeriodFreq f)
    {
        switch (c.Kind)
        {
            case Kind.DateTime: return Column.FromPeriod(c.Ticks.Select(t => ToPeriodOrdinal(f, t, c.Unit)).ToArray(), f);
            case Kind.Period: return Column.FromPeriod(c.Ticks.Select(o => PeriodCore.Asfreq(o, c.PFreq, f, true)).ToArray(), f);
            case Kind.Str: return Column.FromPeriod(Enumerable.Range(0, c.Length).Select(i => c.StrAt(i) is { } s ? ParseOrdinal(s, f) : PeriodCore.NaT).ToArray(), f);
            case Kind.Object:
            {
                var r = new long[c.Length];
                for (int i = 0; i < r.Length; i++)
                {
                    var v = c[i];
                    r[i] = v is null ? PeriodCore.NaT : v is string s ? ParseOrdinal(s, f) : v is Per p ? PeriodCore.Asfreq(p.Ordinal, p.Freq, f, true) : v is Ts t ? ToPeriodOrdinal(f, t.Ticks, t.Unit) : throw PyErr.TypeError($"cannot convert {PyOps.TypeName(PdConv.FromCell(v, Kind.Object))} to a period");
                }
                return Column.FromPeriod(r, f);
            }
        }
        throw PyErr.TypeError($"Cannot convert a column of dtype {c.DTypeName} to period[{f.Name}]");
    }

    private static long ParseOrdinal(string s, PeriodFreq f)
        => PeriodCore.TryParse(s, f, out var o, out _) ? o : throw PyErr.Raise(PdTime.DateParseErrorClass, $"Unknown datetime string format, unable to parse: {s.ToUpperInvariant()}");

    public static Column ToTimestampColumn(Column c, string how, PeriodFreq? freq)
    {
        bool end = how is "end" or "E" or "e";
        var f = c.PFreq;
        var unit = (freq ?? f).TickUnit == DateUnit.Nano || f.Unit == PUnit.Nano ? DateUnit.Nano : DateUnit.Micro;
        var ticks = new long[c.Length];
        for (int i = 0; i < ticks.Length; i++)
        {
            long ord = c.Ticks[i];
            if (ord == PeriodCore.NaT) { ticks[i] = DateTimeCore.NaT; continue; }
            if (freq is not null) { ord = PeriodCore.Asfreq(ord, f, freq, end); var (s2, e2) = PeriodCore.Span(freq, ord, unit); ticks[i] = end ? e2 : s2; continue; }
            var (s, e) = PeriodCore.Span(f, ord, unit);
            ticks[i] = end ? e : s;
        }
        return Column.FromDateTime(ticks, unit);
    }

    public static Column FieldColumn(Column c, string name)
    {
        var f = c.PFreq;
        if (name == "is_leap_year") return Column.FromBools(c.Ticks.Select(o => o != PeriodCore.NaT && PeriodCore.Field(f, o, name) == 1).ToArray());
        if (c.Ticks.Any(o => o == PeriodCore.NaT)) return Column.FromDoubles(c.Ticks.Select(o => o == PeriodCore.NaT ? double.NaN : (double)PeriodCore.Field(f, o, name)).ToArray());
        return Column.FromLongs(c.Ticks.Select(o => PeriodCore.Field(f, o, name)).ToArray());
    }

    private static string HowArg(Args p, int idx) => p.Has(idx) ? (string)p[idx]! : "start";

    // ------------------------------------------------------------------ the Period class

    private static void BuildPeriod()
    {
        var c = PeriodClass;
        void Def(string n, BuiltinFn f) => c.Dict[n] = Fn("Period." + n, f);
        void Prop(string n, Func<Per, object> f) => c.Dict[n] = new PyProperty { Getter = Fn(n, (_, a, _) => f(Me(a[0]))) };

        c.Dict["__new__"] = Fn("Period.__new__", (i, a, k) =>
        {
            var p = A("Period", i, a.Skip(1).ToArray(), k, "value", "freq", "ordinal", "year", "month", "quarter", "day", "hour", "minute", "second");
            PeriodFreq? freq = p.Has(1) ? FreqArg(p[1]!) : null;
            if (p.Has(3))
            {
                freq ??= PeriodFreq.Of(p.Has(6) ? PUnit.Day : p.Has(4) ? PUnit.Month : p.Has(5) ? PUnit.Quarter : PUnit.Year);
                int y = p.Int(3, 1970), mo = p.Has(5) ? (p.Int(5, 1) - 1) * 3 + 1 : p.Int(4, 1);
                long t = DateTimeCore.Compose(y, mo, p.Int(6, 1), p.Int(7, 0), p.Int(8, 0), p.Int(9, 0), 0, DateUnit.Micro);
                return Wrap(new Per(PeriodCore.Ordinal(freq, t, DateUnit.Micro), freq));
            }
            if (p.Has(2)) return Wrap(new Per(PdConv.ToInt(p[2]!), freq ?? throw PyErr.ValueError("Must supply freq for ordinal value")));
            var v = p[0];
            if (v is null or PyNone || v is PyInstance { Native: NaTMarker }) return PdTime.NaT;
            if (TryPer(v, out var src)) return Wrap(freq is null ? src : new Per(PeriodCore.Asfreq(src.Ordinal, src.Freq, freq, true), freq));
            if (v is string s)
            {
                if (!PeriodCore.TryParse(s, freq, out var ord, out var fr)) throw PyErr.Raise(PdTime.DateParseErrorClass, $"Unknown datetime string format, unable to parse: {s.ToUpperInvariant()}");
                return Wrap(new Per(ord, fr));
            }
            if (v is BigInteger bi)
            {
                var f2 = freq ?? throw PyErr.ValueError("Must supply freq for ordinal value");
                if (f2.Unit == PUnit.Year) return Wrap(new Per((long)bi - 1970, f2));
                throw PyErr.NotImplementedError("Period(int) is only supported for annual frequencies");
            }
            if (PdTime.TryTs(v, out var ts) && v is not string)
                return Wrap(new Per(PeriodCore.Ordinal(freq ?? throw PyErr.ValueError("Must supply freq for datetime value"), ts.Ticks, ts.Unit), freq));
            throw PyErr.TypeError($"Cannot convert input [{PyOps.Str(i, v)}] of type {PyOps.TypeName(v)} to Period");
        });
        Def("__init__", (_, _, _) => PyNone.Instance);
        Def("__str__", (_, a, _) => Me(a[0]).ToString());
        Def("__repr__", (_, a, _) => $"Period('{Me(a[0])}', '{Me(a[0]).Freq.Name}')");
        Def("__hash__", (_, a, _) => new BigInteger(Column.Key(Me(a[0]))!.GetHashCode()));
        foreach (var op in new[] { "eq", "ne", "lt", "le", "gt", "ge" })
        {
            var o2 = op;
            Def($"__{o2}__", (_, a, _) =>
            {
                var x = Me(a[0]);
                if (!TryPer(a[1], out var y))
                {
                    if (a[1] is string str && PeriodCore.TryParse(str, x.Freq, out var po, out _)) y = new Per(po, x.Freq);
                    else if (o2 is "eq" or "ne") return o2 == "ne";
                    else return PyNotImplemented.Instance;
                }
                if (x.Freq != y.Freq)
                {
                    if (o2 is "eq" or "ne") return o2 == "ne";
                    throw Incompatible($"Input has different freq={y.Freq.Name} from Period(freq={x.Freq.Name})");
                }
                int cmp = x.Ordinal.CompareTo(y.Ordinal);
                return o2 switch { "eq" => cmp == 0, "ne" => cmp != 0, "lt" => cmp < 0, "le" => cmp <= 0, "gt" => cmp > 0, _ => cmp >= 0 };
            });
        }
        long? Steps(Per x, object other, int sign)
        {
            if (other is BigInteger n) return sign * (long)n * x.Freq.Mult;
            if (other is PyInstance { Native: DateOffsetSpec off })
            {
                var mine = OffsetFor(x.Freq.Base);
                if (off.WithN(1).FreqString != mine.FreqString) throw Incompatible($"Input has different freq={PdTime.OffsetText(off)} from Period(freq={x.Freq.Name})");
                return sign * off.N;
            }
            return null;
        }
        BuiltinFn add = (_, a, _) => Steps(Me(a[0]), a[1], 1) is long st ? Wrap(new Per(Me(a[0]).Ordinal + st, Me(a[0]).Freq)) : PyNotImplemented.Instance;
        Def("__add__", add);
        Def("__radd__", add);
        Def("__sub__", (_, a, _) =>
        {
            var x = Me(a[0]);
            if (Steps(x, a[1], -1) is long st) return Wrap(new Per(x.Ordinal + st, x.Freq));
            if (TryPer(a[1], out var y))
            {
                if (x.Freq != y.Freq) throw Incompatible($"Input has different freq={y.Freq.Name} from Period(freq={x.Freq.Name})");
                return WrapDiff(new PerDiff(x.Ordinal - y.Ordinal, x.Freq));
            }
            return PyNotImplemented.Instance;
        });
        Prop("ordinal", p => new BigInteger(p.Ordinal));
        Prop("freqstr", p => p.Freq.Name);
        Prop("freq", p => PdTime.WrapOffset(OffsetFor(p.Freq)));
        foreach (var f in PeriodCore.Fields)
        {
            var name = f;
            Prop(name, p => name == "is_leap_year" ? PeriodCore.Field(p.Freq, p.Ordinal, name) == 1 : new BigInteger(PeriodCore.Field(p.Freq, p.Ordinal, name)));
        }
        Prop("start_time", p => PdTime.Wrap(StartOf(p, false)));
        Prop("end_time", p => PdTime.Wrap(StartOf(p, true)));
        Def("asfreq", (i, a, k) =>
        {
            var p = A("asfreq", i, a.Skip(1).ToArray(), k, "freq", "how");
            var x = Me(a[0]); var f = FreqArg(p.Required(0));
            string how = p.Has(1) ? (string)p[1]! : "E";
            return Wrap(new Per(PeriodCore.Asfreq(x.Ordinal, x.Freq, f, how is "E" or "end" or "e"), f));
        });
        Def("to_timestamp", (i, a, k) =>
        {
            var p = A("to_timestamp", i, a.Skip(1).ToArray(), k, "freq", "how");
            var x = Me(a[0]);
            var col = ToTimestampColumn(Column.FromPeriod(new[] { x.Ordinal }, x.Freq), HowArg(p, 1), p.Has(0) ? FreqArg(p[0]!) : null);
            return PdTime.Wrap(new Ts(col.Ticks[0], col.Unit));
        });
        Def("strftime", (i, a, k) => PeriodCore.Strftime(Me(a[0]).Ordinal, Me(a[0]).Freq, (string)A("strftime", i, a.Skip(1).ToArray(), k, "fmt").Required(0)));
    }

    // ------------------------------------------------------------------ module functions

    private static Per ToPeriod(object? v, PeriodFreq? freq)
    {
        if (TryPer(v, out var p)) return freq is null ? p : new Per(PeriodCore.Asfreq(p.Ordinal, p.Freq, freq, true), freq);
        if (v is string s)
        {
            if (!PeriodCore.TryParse(s, freq, out var o, out var f)) throw PyErr.Raise(PdTime.DateParseErrorClass, $"Unknown datetime string format, unable to parse: {s.ToUpperInvariant()}");
            return new Per(o, f);
        }
        if (PdTime.TryTs(v, out var ts)) { var f = freq ?? PeriodFreq.Of(PUnit.Day); return new Per(PeriodCore.Ordinal(f, ts.Ticks, ts.Unit), f); }
        throw PyErr.TypeError("expected a Period, string or Timestamp");
    }

    private static FIndex PeriodRange(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var p = A("period_range", i, a, k, "start", "end", "periods", "freq", "name");
        PeriodFreq? freq = p.Has(3) ? FreqArg(p[3]!) : null;
        Per? start = p.Has(0) ? ToPeriod(p[0], freq) : null, end = p.Has(1) ? ToPeriod(p[1], freq) : null;
        freq ??= start?.Freq ?? end?.Freq ?? PeriodFreq.Of(PUnit.Day);
        if (start is null && end is null) throw PyErr.ValueError("Of the three parameters: start, end, and periods, exactly two must be specified");
        int? periods = p.IntOrNull(2);
        long s0, n;
        if (start is Per sp && end is Per ep) { s0 = sp.Ordinal; n = (ep.Ordinal - sp.Ordinal) / freq.Mult + 1; if (periods is not null) throw PyErr.ValueError("Of the three parameters: start, end, and periods, exactly two must be specified"); }
        else if (start is Per sp2) { s0 = sp2.Ordinal; n = periods ?? throw PyErr.ValueError("Of the three parameters: start, end, and periods, exactly two must be specified"); }
        else { n = periods ?? throw PyErr.ValueError("Of the three parameters: start, end, and periods, exactly two must be specified"); s0 = end!.Value.Ordinal - (n - 1) * freq.Mult; }
        var ords = Enumerable.Range(0, (int)Math.Max(0, n)).Select(x => s0 + x * freq.Mult).ToArray();
        return new FIndex(Column.FromPeriod(ords, freq), p.Has(4) ? PdConv.ToCell(p[4]) : null);
    }

    public static void Install(PyModule m)
    {
        BuildPeriod();
        m.Dict["Period"] = PeriodClass;
        void Def(string name, BuiltinFn fn) => m.Dict[name] = Fn(name, fn);
        Def("period_range", (i, a, k) => PdConv.Wrap(PeriodRange(i, a, k)));
        Def("PeriodIndex", (i, a, k) =>
        {
            var p = A("PeriodIndex", i, a, k, "data", "ordinal", "freq", "dtype", "copy", "name");
            var data = p.Has(0) ? p[0]! : new PyList();
            PeriodFreq? freq = p.Has(2) ? FreqArg(p[2]!) : null;
            var col = PdConv.ToColumn(data);
            if (col.Kind == Kind.Period) { if (freq is not null && freq != col.PFreq) col = ToPeriodColumn(col, freq); }
            else
            {
                if (freq is null && col.Kind == Kind.Str)
                {
                    PeriodCore.TryParse(col.Strings.FirstOrDefault(s => s is not null) ?? "", null, out _, out var f0);
                    freq = f0;
                }
                if (freq is null && col.Kind == Kind.DateTime) throw PyErr.ValueError("Must pass freq");
                col = ToPeriodColumn(col, freq ?? PeriodFreq.Of(PUnit.Day));
            }
            return PdConv.Wrap(new FIndex(col, p.Has(5) ? PdConv.ToCell(p[5]) : data is PyInstance { Native: FIndex src } ? src.Name : null));
        });
        InstallConversions();
        InstallAccessor();
        InstallIndexMembers();
        PdTime.TimestampClass.Dict["to_period"] = Fn("Timestamp.to_period", (i, a, k) =>
        {
            var p = A("to_period", i, a.Skip(1).ToArray(), k, "freq");
            var t = (Ts)((PyInstance)a[0]).Native!;
            var f = FreqArg(p.Required(0));
            return Wrap(new Per(PeriodCore.Ordinal(f, t.Ticks, t.Unit), f));
        });
    }

    private static PeriodFreq? FreqOfIndex(FIndex ix)
        => ix.Freq is { } fs ? FromOffset(DateOffsetSpec.Parse(fs)) : null;

    private static void InstallConversions()
    {
        // Series.dt.to_period / DatetimeIndex.to_period / Series|DataFrame.to_period (on the index) and to_timestamp
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            bool isSeries = cls == PdClasses.Series;
            cls.Dict["to_period"] = Fn("to_period", (i, a, k) =>
            {
                var p = A("to_period", i, a.Skip(1).ToArray(), k, "freq", "axis", "copy");
                var ix = isSeries ? PdConv.S(a[0]).Index : PdConv.D(a[0]).Index;
                var f = p.Has(0) ? FreqArg(p[0]!) : FreqOfIndex(ix) ?? throw PyErr.ValueError("You must pass a freq argument as current index has none.");
                var nix = new FIndex(ToPeriodColumn(ix.Labels, f), ix.Name);
                return isSeries ? PdConv.Wrap(new Series(PdConv.S(a[0]).Values, nix, PdConv.S(a[0]).Name)) : PdConv.Wrap(new DataFrame(PdConv.D(a[0]).Data, PdConv.D(a[0]).Columns, nix));
            });
            cls.Dict["to_timestamp"] = Fn("to_timestamp", (i, a, k) =>
            {
                var p = A("to_timestamp", i, a.Skip(1).ToArray(), k, "freq", "how", "axis", "copy");
                var ix = isSeries ? PdConv.S(a[0]).Index : PdConv.D(a[0]).Index;
                if (ix.Labels.Kind != Kind.Period) throw PyErr.TypeError($"unsupported Type {(ix.IsRange ? "RangeIndex" : "Index")}");
                var nix = IndexToTimestamp(ix, HowArg(p, 1), p.Has(0) ? FreqArg(p[0]!) : null);
                return isSeries ? PdConv.Wrap(new Series(PdConv.S(a[0]).Values, nix, PdConv.S(a[0]).Name)) : PdConv.Wrap(new DataFrame(PdConv.D(a[0]).Data, PdConv.D(a[0]).Columns, nix));
            });
        }
    }

    private static FIndex IndexToTimestamp(FIndex ix, string how, PeriodFreq? freq)
    {
        var col = ToTimestampColumn(ix.Labels, how, freq);
        var res = new FIndex(col, ix.Name);
        res.Freq = col.Length >= 3 ? PdDates.InferFreq(res) : null;
        return res;
    }

    // ------------------------------------------------------------------ .dt on period data

    private static Series Own(object self) => (Series)((PyInstance)self).Native!;

    private static void InstallAccessor()
    {
        var cls = PeriodAccessor;
        void Def(string n, BuiltinFn f) => cls.Dict[n] = Fn("PeriodProperties." + n, f);
        foreach (var f in PeriodCore.Fields)
        {
            var name = f;
            cls.Dict[name] = new PyProperty { Getter = Fn(name, (_, a, _) => { var s = Own(a[0]); return PdConv.Wrap(new Series(FieldColumn(s.Values, name), s.Index, s.Name)); }) };
        }
        cls.Dict["start_time"] = new PyProperty { Getter = Fn("start_time", (_, a, _) => { var s = Own(a[0]); return PdConv.Wrap(new Series(ToTimestampColumn(s.Values, "start", null), s.Index, s.Name)); }) };
        cls.Dict["end_time"] = new PyProperty { Getter = Fn("end_time", (_, a, _) => { var s = Own(a[0]); return PdConv.Wrap(new Series(ToTimestampColumn(s.Values, "end", null), s.Index, s.Name)); }) };
        cls.Dict["freq"] = new PyProperty { Getter = Fn("freq", (_, a, _) => PdTime.WrapOffset(OffsetFor(Own(a[0]).Values.PFreq))) };
        Def("to_timestamp", (i, a, k) =>
        {
            var p = A("to_timestamp", i, a.Skip(1).ToArray(), k, "freq", "how");
            var s = Own(a[0]);
            return PdConv.Wrap(new Series(ToTimestampColumn(s.Values, HowArg(p, 1), p.Has(0) ? FreqArg(p[0]!) : null), s.Index, s.Name));
        });
        Def("strftime", (i, a, k) =>
        {
            var s = Own(a[0]);
            string fmt = (string)A("strftime", i, a.Skip(1).ToArray(), k, "date_format").Required(0);
            var f = s.Values.PFreq;
            return PdConv.Wrap(new Series(Column.FromStrings(s.Values.Ticks.Select(o => o == PeriodCore.NaT ? null : PeriodCore.Strftime(o, f, fmt)).ToArray()), s.Index, s.Name));
        });
        Def("asfreq", (i, a, k) =>
        {
            var p = A("asfreq", i, a.Skip(1).ToArray(), k, "freq", "how");
            var s = Own(a[0]);
            var f = FreqArg(p.Required(0));
            bool end = !p.Has(1) || (string)p[1]! is "E" or "end" or "e";
            var src = s.Values;
            return PdConv.Wrap(new Series(Column.FromPeriod(src.Ticks.Select(o => PeriodCore.Asfreq(o, src.PFreq, f, end)).ToArray(), f), s.Index, s.Name));
        });
        // dt.to_period on datetime data, and the .dt dispatcher
        var prev = (PyProperty)PdClasses.Series.Dict["dt"];
        PdClasses.Series.Dict["dt"] = new PyProperty
        {
            Getter = Fn("dt", (i, a, _) =>
            {
                var s = Own(a[0]);
                if (s.Values.Kind == Kind.Period) return new PyInstance(PeriodAccessor) { Native = s };
                return i.Call(prev.Getter!, new[] { a[0] });
            }),
        };
        PdDates.DtAccessor.Dict["to_period"] = Fn("to_period", (i, a, k) =>
        {
            var p = A("to_period", i, a.Skip(1).ToArray(), k, "freq");
            var s = Own(a[0]);
            var f = FreqArg(p.Required(0));
            return PdConv.Wrap(new Series(ToPeriodColumn(s.Values, f), s.Index, s.Name));
        });
    }

    // ------------------------------------------------------------------ PeriodIndex members

    private static void InstallIndexMembers()
    {
        var cls = PdClasses.Index;
        FIndex Ix(object o) => (FIndex)((PyInstance)o).Native!;
        foreach (var f in PeriodCore.Fields)
        {
            var name = f;
            var prev = cls.Dict.TryGet(name, out var pv) ? pv as PyProperty : null;
            cls.Dict[name] = new PyProperty
            {
                Getter = Fn(name, (i, a, _) =>
                {
                    var ix = Ix(a[0]);
                    if (ix.Labels.Kind != Kind.Period)
                    {
                        if (prev?.Getter is { } g) return i.Call(g, new[] { a[0] });
                        throw PyErr.AttributeError($"'Index' object has no attribute '{name}'");
                    }
                    var col = FieldColumn(ix.Labels, name);
                    if (col.Kind == Kind.Bool) return Conv.Wrap(NDArray.FromArray(Enumerable.Range(0, col.Length).Select(col.BoolAt).ToArray()));
                    return PdConv.Wrap(new FIndex(col, ix.Name));
                }),
            };
        }
        foreach (var (name, end) in new[] { ("start_time", false), ("end_time", true) })
        {
            var e2 = end;
            cls.Dict[name] = new PyProperty { Getter = Fn(name, (_, a, _) => { var ix = Ix(a[0]); if (ix.Labels.Kind != Kind.Period) throw PyErr.AttributeError($"'Index' object has no attribute '{name}'"); return PdConv.Wrap(IndexToTimestamp(ix, e2 ? "end" : "start", null)); }) };
        }
        void Def(string n, BuiltinFn f) => cls.Dict[n] = Fn("Index." + n, f);
        PyBuiltinFunction? Prev(string n) => cls.Dict.TryGet(n, out var v) ? v as PyBuiltinFunction : null;
        var prevFreq = cls.Dict["freq"] as PyProperty; var prevFreqStr = cls.Dict["freqstr"] as PyProperty;
        cls.Dict["freq"] = new PyProperty { Getter = Fn("freq", (i, a, _) => Ix(a[0]).Labels.Kind == Kind.Period ? PdTime.WrapOffset(OffsetFor(Ix(a[0]).Labels.PFreq)) : i.Call(prevFreq!.Getter!, new[] { a[0] })) };
        cls.Dict["freqstr"] = new PyProperty { Getter = Fn("freqstr", (i, a, _) => Ix(a[0]).Labels.Kind == Kind.Period ? Ix(a[0]).Labels.PFreq.Name : i.Call(prevFreqStr!.Getter!, new[] { a[0] })) };
        Def("to_timestamp", (i, a, k) =>
        {
            var p = A("to_timestamp", i, a.Skip(1).ToArray(), k, "freq", "how");
            var ix = Ix(a[0]);
            if (ix.Labels.Kind != Kind.Period) throw PyErr.AttributeError("'Index' object has no attribute 'to_timestamp'");
            return PdConv.Wrap(IndexToTimestamp(ix, HowArg(p, 1), p.Has(0) ? FreqArg(p[0]!) : null));
        });
        Def("to_period", (i, a, k) =>
        {
            var p = A("to_period", i, a.Skip(1).ToArray(), k, "freq");
            var ix = Ix(a[0]);
            if (ix.Labels.Kind != Kind.DateTime) throw PyErr.AttributeError("'Index' object has no attribute 'to_period'");
            var f = p.Has(0) ? FreqArg(p[0]!) : FreqOfIndex(ix) ?? throw PyErr.ValueError("You must pass a freq argument as current index has none.");
            return PdConv.Wrap(new FIndex(ToPeriodColumn(ix.Labels, f), ix.Name));
        });
        Def("asfreq", (i, a, k) =>
        {
            var p = A("asfreq", i, a.Skip(1).ToArray(), k, "freq", "how");
            var ix = Ix(a[0]);
            if (ix.Labels.Kind != Kind.Period) throw PyErr.AttributeError("'Index' object has no attribute 'asfreq'");
            var f = FreqArg(p.Required(0));
            bool end = !p.Has(1) || (string)p[1]! is "E" or "end" or "e";
            return PdConv.Wrap(new FIndex(Column.FromPeriod(ix.Labels.Ticks.Select(o => PeriodCore.Asfreq(o, ix.Labels.PFreq, f, end)).ToArray(), f), ix.Name));
        });
        var prevStrftime = Prev("strftime");
        Def("strftime", (i, a, k) =>
        {
            var ix = Ix(a[0]);
            if (ix.Labels.Kind != Kind.Period) return i.Call(prevStrftime!, a, k);
            string fmt = (string)A("strftime", i, a.Skip(1).ToArray(), k, "date_format").Required(0);
            var f = ix.Labels.PFreq;
            return PdConv.Wrap(new FIndex(Column.FromStrings(ix.Labels.Ticks.Select(o => o == PeriodCore.NaT ? null : PeriodCore.Strftime(o, f, fmt)).ToArray()), ix.Name));
        });
        var prevShift = Prev("shift");
        Def("shift", (i, a, k) =>
        {
            var ix = Ix(a[0]);
            if (ix.Labels.Kind != Kind.Period) return i.Call(prevShift!, a, k);
            int n = A("shift", i, a.Skip(1).ToArray(), k, "periods").Int(0, 1);
            return PdConv.Wrap(new FIndex(Column.FromPeriod(ix.Labels.Ticks.Select(o => o == PeriodCore.NaT ? o : o + n * ix.Labels.PFreq.Mult).ToArray(), ix.Labels.PFreq), ix.Name));
        });
    }
}
