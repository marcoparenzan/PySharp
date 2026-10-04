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

/// <summary>The vectorised side of time series: <c>to_datetime</c>, <c>to_timedelta</c>, <c>date_range</c>, the <c>.dt</c> accessor, DatetimeIndex attributes and <c>resample</c>.</summary>
internal static class PdDates
{
    public static readonly PyClass DtAccessor = new("DatetimeProperties", new List<PyClass>());
    public static readonly PyClass TdAccessor = new("TimedeltaProperties", new List<PyClass>());
    public static readonly PyClass Resampler = new("Resampler", new List<PyClass>());

    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a, k, names);
    private static PyBuiltinFunction Fn(string name, BuiltinFn f) => PdClasses.Fn(name, f);

    public static readonly Dictionary<string, DateUnit> UnitNames = new()
    {
        ["s"] = DateUnit.Second, ["ms"] = DateUnit.Milli, ["us"] = DateUnit.Micro, ["ns"] = DateUnit.Nano,
    };

    // ------------------------------------------------------------------ results as pandas objects

    private static object Rewrap(object source, Column result)
        => source is PyInstance { Native: Series s } ? PdConv.Wrap(new Series(result, s.Index, s.Name))
         : source is PyInstance { Native: FIndex ix } ? PdConv.Wrap(new FIndex(result, ix.Name))
         : PdConv.Wrap(new FIndex(result, null));

    private static Column ColumnOf(object o) => PdConv.ToColumn(o);

    // ------------------------------------------------------------------ numbers → datetime / timedelta

    private static (long mult, DateUnit unit) EpochUnit(string unit) => unit switch
    {
        "D" or "d" => (86400, DateUnit.Second),
        "h" => (3600, DateUnit.Second),
        "m" or "min" => (60, DateUnit.Second),
        "s" => (1, DateUnit.Second),
        "ms" => (1, DateUnit.Milli),
        "us" or "µs" => (1, DateUnit.Micro),
        "ns" => (1, DateUnit.Nano),
        _ => throw PyErr.ValueError($"invalid unit abbreviation: {unit}"),
    };

    private static (long[] ticks, DateUnit unit) Numbers(Column c, string? unit, string errors)
    {
        var (mult, du) = EpochUnit(unit ?? "ns");
        if (c.Kind == Kind.Int || c.Kind == Kind.Bool)
        {
            var t = new long[c.Length];
            for (int i = 0; i < t.Length; i++) t[i] = c.Kind == Kind.Bool ? throw PyErr.TypeError("<class 'bool'> is not convertible to datetime") : checked(c.LongAt(i) * mult);
            return (t, du);
        }
        double per = (double)DateTimeCore.PerSecond(DateUnit.Nano) / DateTimeCore.PerSecond(du);
        var r = new long[c.Length];
        for (int i = 0; i < r.Length; i++)
        {
            double v = c.DoubleAt(i);
            r[i] = double.IsNaN(v) ? DateTimeCore.NaT : (long)Math.Round(v * mult * per);
        }
        return (r, DateUnit.Nano);
    }

    // ------------------------------------------------------------------ to_datetime

    private static string Opt(Args p, int i, string dflt) => p.Has(i) ? (string)p[i]! : dflt;

    public static Column ToDatetimeColumn(Column c, string errors, bool dayFirst, string? format, string? unit)
    {
        switch (c.Kind)
        {
            case Kind.DateTime: return c;
            case Kind.Category: return ToDatetimeColumn(c.Decategorized(), errors, dayFirst, format, unit);
            case Kind.Int or Kind.Float:
            {
                var (t, u) = Numbers(c, unit, errors);
                return Column.FromDateTime(t, u);
            }
            case Kind.Str:
            {
                var (t, u) = DateParse.ParseStrings(Enumerable.Range(0, c.Length).Select(i => c.StrAt(i)).ToList(), format, dayFirst, errors);
                return Column.FromDateTime(t, u);
            }
            case Kind.Object:
            {
                var strs = new List<string?>();
                bool allStr = true;
                for (int i = 0; i < c.Length; i++) { var v = c[i]; if (v is string s) strs.Add(s); else if (v is null || v is double d && double.IsNaN(d)) strs.Add(null); else allStr = false; }
                if (allStr)
                {
                    var (t, u) = DateParse.ParseStrings(strs, format, dayFirst, errors);
                    return Column.FromDateTime(t, u);
                }
                var cells = new List<object?>();
                for (int i = 0; i < c.Length; i++)
                {
                    var v = c[i];
                    if (v is null || v is double dd && double.IsNaN(dd)) cells.Add(null);
                    else if (v is Ts) cells.Add(v);
                    else if (v is string s2)
                    {
                        if (PdTime.TryTs(s2, out var ts)) cells.Add(ts);
                        else if (errors == "coerce") cells.Add(null);
                        else throw PyErr.ValueError($"Unknown datetime string format, unable to parse: {s2}, at position {i}");
                    }
                    else if (errors == "coerce") cells.Add(null);
                    else throw PyErr.TypeError($"<class '{PyOps.TypeName(PdConv.FromCell(v, Kind.Object))}'> is not convertible to datetime, at position {i}");
                }
                return Column.Infer(cells).Kind == Kind.DateTime ? Column.Infer(cells) : Column.FromDateTime(cells.Select(_ => DateTimeCore.NaT).ToArray(), DateUnit.Second);
            }
        }
        throw PyErr.TypeError($"<class '{c.DTypeName}'> is not convertible to datetime");
    }

    private static object ScalarDatetime(object arg, string errors, bool dayFirst, string? format, string? unit)
    {
        if (arg is PyNone || arg is PyInstance { Native: NaTMarker }) return PdTime.NaT;
        if (arg is PyInstance { Native: Ts }) return arg;
        if (arg is string s)
        {
            var (t, u) = DateParse.ParseStrings(new List<string?> { s }, format, dayFirst, errors);
            return t[0] == DateTimeCore.NaT ? PdTime.NaT : PdTime.Wrap(new Ts(t[0], u));
        }
        if (arg is BigInteger or double)
        {
            var (t, u) = Numbers(Column.Infer(new List<object?> { PdConv.ToCell(arg) }), unit, errors);
            return t[0] == DateTimeCore.NaT ? PdTime.NaT : PdTime.Wrap(new Ts(t[0], u));
        }
        if (PdTime.TryTs(arg, out var ts)) return PdTime.Wrap(ts);
        if (errors == "coerce") return PdTime.NaT;
        throw PyErr.TypeError($"<class '{PyOps.TypeName(arg)}'> is not convertible to datetime");
    }

    private static object DataFrameDatetime(DataFrame d, string errors)
    {
        Column Get(params string[] names)
        {
            foreach (var n in names)
                for (int j = 0; j < d.NCols; j++)
                    if (d.Columns.Labels[j] is string lab && lab.Equals(n, StringComparison.OrdinalIgnoreCase)) return d.Data[j];
            return null!;
        }
        var y = Get("year"); var mo = Get("month"); var dd = Get("day");
        if (y is null || mo is null || dd is null)
        {
            var missing = new[] { ("year", y), ("month", mo), ("day", dd) }.Where(x => x.Item2 is null).Select(x => x.Item1);
            throw PyErr.ValueError($"to assemble mappings requires at least that [year, month, day] be specified: [{string.Join(", ", missing)}] is missing");
        }
        var h = Get("hour", "h"); var mi = Get("minute", "min", "m"); var sec = Get("second", "s");
        var ms = Get("ms", "millisecond"); var us = Get("us", "microsecond"); var ns = Get("ns", "nanosecond");
        long L(Column c, int i) => c is null ? 0 : c.Kind == Kind.Float ? (long)c.DoubleAt(i) : c.LongAt(i);
        bool Na(Column c, int i) => c is not null && c.IsNa(i);
        var unit = ns is not null ? DateUnit.Nano : DateUnit.Micro;
        var t = new long[d.NRows];
        for (int i = 0; i < t.Length; i++)
        {
            if (Na(y, i) || Na(mo, i) || Na(dd, i)) { t[i] = DateTimeCore.NaT; continue; }
            long yy = L(y, i); int mm = (int)L(mo, i), da = (int)L(dd, i);
            if (mm < 1 || mm > 12 || da < 1 || da > DateTimeCore.DaysInMonth(yy, mm))
            {
                if (errors == "coerce") { t[i] = DateTimeCore.NaT; continue; }
                throw PyErr.ValueError($"cannot assemble the datetimes: day is out of range for month");
            }
            long nanos = L(ms, i) * 1_000_000 + L(us, i) * 1000 + L(ns, i);
            t[i] = DateTimeCore.Compose(yy, mm, da, (int)L(h, i), (int)L(mi, i), (int)L(sec, i), nanos, unit);
        }
        return PdConv.Wrap(new Series(Column.FromDateTime(t, unit), d.Index));
    }

    private static object ToDatetime(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var p = A("to_datetime", i, a, k, "arg", "errors", "dayfirst", "yearfirst", "utc", "format", "exact", "unit", "origin", "cache");
        var arg = p.Required(0);
        string errors = Opt(p, 1, "raise");
        if (p.Bool(4, false)) throw PyErr.NotImplementedError("to_datetime(utc=True): time zones are not supported");
        bool dayFirst = p.Bool(2, false);
        string? format = p.Has(5) ? (string)p[5]! : null;
        string? unit = p.Has(7) ? (string)p[7]! : null;
        if (arg is PyInstance { Native: DataFrame df }) return DataFrameDatetime(df, errors);
        if (!PdConv.IsListLike(arg)) return ScalarDatetime(arg, errors, dayFirst, format, unit);
        var col = ToDatetimeColumn(ColumnOf(arg), errors, dayFirst, format, unit);
        if (arg is PyInstance { Native: FIndex ixsrc } && ixsrc.Freq is not null && ixsrc.Labels.Kind == Kind.DateTime) return PdConv.Wrap(ixsrc);
        return Rewrap(arg, col);
    }

    // ------------------------------------------------------------------ to_timedelta

    public static Column ToTimedeltaColumn(Column c, string errors, string? unit)
    {
        switch (c.Kind)
        {
            case Kind.Timedelta: return c;
            case Kind.Int or Kind.Float:
            {
                var (t, u) = Numbers(c, unit, errors);
                return Column.FromTimedelta(t, u);
            }
            case Kind.Str:
            {
                var (t, u) = DateParse.ParseTimedeltas(Enumerable.Range(0, c.Length).Select(i => c.StrAt(i)).ToList(), errors);
                return Column.FromTimedelta(t, u);
            }
            case Kind.Object:
            {
                var cells = new List<object?>();
                for (int i = 0; i < c.Length; i++)
                {
                    var v = c[i];
                    if (v is null || v is double d && double.IsNaN(d)) cells.Add(null);
                    else if (v is Td) cells.Add(v);
                    else if (v is string s && PdTime.TryTd(s, out var td)) cells.Add(td);
                    else if (errors == "coerce") cells.Add(null);
                    else throw PyErr.ValueError($"invalid unit abbreviation: {v}");
                }
                var r = Column.Infer(cells);
                return r.Kind == Kind.Timedelta ? r : Column.FromTimedelta(cells.Select(_ => DateTimeCore.NaT).ToArray(), DateUnit.Second);
            }
        }
        throw PyErr.TypeError($"dtype {c.DTypeName} cannot be converted to timedelta64[ns]");
    }

    private static object ToTimedelta(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var p = A("to_timedelta", i, a, k, "arg", "unit", "errors");
        var arg = p.Required(0);
        string errors = Opt(p, 2, "raise");
        string? unit = p.Has(1) ? (string)p[1]! : null;
        if (!PdConv.IsListLike(arg))
        {
            if (arg is PyNone || arg is PyInstance { Native: NaTMarker }) return PdTime.NaT;
            if (arg is BigInteger or double)
            {
                var (t, u) = Numbers(Column.Infer(new List<object?> { PdConv.ToCell(arg) }), unit, errors);
                return t[0] == DateTimeCore.NaT ? PdTime.NaT : PdTime.Wrap(new Td(t[0], u));
            }
            if (PdTime.TryTd(arg, out var td)) return PdTime.Wrap(td);
            if (errors == "coerce") return PdTime.NaT;
            throw PyErr.ValueError($"invalid unit abbreviation: {PyOps.Str(i, arg)}");
        }
        return Rewrap(arg, ToTimedeltaColumn(ColumnOf(arg), errors, unit));
    }

    // ------------------------------------------------------------------ ranges

    internal static DateOffsetSpec OffsetArg(object? freq)
    {
        switch (freq)
        {
            case null: case PyNone: return DateOffsetSpec.Parse("D");
            case string s: return DateOffsetSpec.Parse(s);
            case PyInstance { Native: DateOffsetSpec o }: return o;
        }
        if (PdTime.TryTd(freq, out var td)) return DateOffsetSpec.FromNanos((long)DateTimeCore.ToNanos(td.Ticks, td.Unit));
        throw PyErr.ValueError($"Invalid frequency: {PyOps.Str(PdConv.Interp!, freq)}");
    }

    private static object DateRange(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var p = A("date_range", i, a, k, "start", "end", "periods", "freq", "tz", "normalize", "name", "inclusive", "unit");
        if (p.Has(4)) throw PyErr.NotImplementedError("date_range(tz=...): time zones are not supported");
        Ts? start = null, end = null;
        if (p.Has(0)) start = PdTime.TryTs(p[0], out var s0) ? s0 : throw PyErr.ValueError($"Given date string \"{PyOps.Str(i, p[0]!)}\" not likely a datetime");
        if (p.Has(1)) end = PdTime.TryTs(p[1], out var e0) ? e0 : throw PyErr.ValueError($"Given date string \"{PyOps.Str(i, p[1]!)}\" not likely a datetime");
        int? periods = p.IntOrNull(2);
        bool normalize = p.Bool(5, false);
        string inclusive = Opt(p, 7, "both");
        object? name = p.Has(6) ? PdConv.ToCell(p[6]) : null;
        var unit = DateUnit.Second;
        foreach (var t in new[] { start, end }) if (t is Ts tt) unit = DateTimeCore.Finer(unit, tt.Unit);
        if (p.Has(8)) unit = UnitNames[(string)p[8]!];
        else if (start is null || end is null || true) unit = start is Ts || end is Ts ? (unit < DateUnit.Micro ? unit : unit) : DateUnit.Micro;
        long? st = null, en = null;
        long Conv(Ts t) => DateTimeCore.Scale(t.Ticks, t.Unit, unit);

        if (!p.Has(3) && periods is not null && start is not null && end is not null)
        {
            // linearly spaced
            long a0 = Conv(start.Value), b0 = Conv(end.Value);
            var lin = new long[periods.Value];
            for (int n = 0; n < lin.Length; n++) lin[n] = lin.Length == 1 ? a0 : a0 + (long)Math.Round((double)(b0 - a0) * n / (lin.Length - 1));
            return PdConv.Wrap(new FIndex(Column.FromDateTime(lin, unit), name));
        }
        var off = OffsetArg(p.Has(3) ? p[3] : null);
        if (off.RequiredUnit > unit && p.Has(3)) unit = off.RequiredUnit;
        if (start is Ts s1) st = DateTimeCore.Scale(s1.Ticks, s1.Unit, unit);
        if (end is Ts e1) en = DateTimeCore.Scale(e1.Ticks, e1.Unit, unit);
        if (normalize)
        {
            if (st is long sv) st = TimeSeries.NormalizeTicks(sv, unit);
            if (en is long ev) en = TimeSeries.NormalizeTicks(ev, unit);
        }
        if (start is null && end is null) throw PyErr.ValueError("Of the four parameters: start, end, periods, and freq, exactly three must be specified");
        if (periods is null && (start is null || end is null)) throw PyErr.ValueError("Of the four parameters: start, end, periods, and freq, exactly three must be specified");
        var ticks = TimeSeries.DateRange(st, en, periods, off, unit);
        if (inclusive != "both" && ticks.Length > 0)
        {
            var keep = ticks.AsEnumerable();
            if ((inclusive is "right" or "neither") && st is long s2) keep = keep.Where(t => t != s2);
            if ((inclusive is "left" or "neither") && en is long e2) keep = keep.Where(t => t != e2);
            ticks = keep.ToArray();
        }
        var ix = new FIndex(Column.FromDateTime(ticks, unit), name) { Freq = off.IsTick && off.N == 1 || true ? off.FreqString : null };
        return PdConv.Wrap(ix);
    }

    private static object TimedeltaRange(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var p = A("timedelta_range", i, a, k, "start", "end", "periods", "freq", "name", "closed", "unit");
        Td? start = null, end = null;
        if (p.Has(0)) start = PdTime.TryTd(p[0], out var s0) ? s0 : throw PyErr.ValueError("invalid start");
        if (p.Has(1)) end = PdTime.TryTd(p[1], out var e0) ? e0 : throw PyErr.ValueError("invalid end");
        int? periods = p.IntOrNull(2);
        var unit = DateUnit.Micro;
        foreach (var t in new[] { start, end }) if (t is Td tt) unit = DateTimeCore.Finer(unit, tt.Unit);
        var off = OffsetArg(p.Has(3) ? p[3] : null) as DateOffsetSpec.TickOffset ?? throw PyErr.ValueError("timedelta_range requires a fixed frequency");
        long step = off.StepTicks(unit);
        var res = new List<long>();
        if (start is Td s)
        {
            long cur = DateTimeCore.Scale(s.Ticks, s.Unit, unit);
            long? en = end is Td e ? DateTimeCore.Scale(e.Ticks, e.Unit, unit) : null;
            while ((en is null || cur <= en) && (periods is null || res.Count < periods)) { res.Add(cur); cur += step; if (en is null && periods is null) break; }
        }
        else if (end is Td e2 && periods is int n)
        {
            long cur = DateTimeCore.Scale(e2.Ticks, e2.Unit, unit);
            for (int x = 0; x < n; x++) { res.Add(cur); cur -= step; }
            res.Reverse();
        }
        else throw PyErr.ValueError("Of the four parameters: start, end, periods, and freq, exactly three must be specified");
        return PdConv.Wrap(new FIndex(Column.FromTimedelta(res.ToArray(), unit), p.Has(4) ? PdConv.ToCell(p[4]) : null) { Freq = off.FreqString });
    }

    // ------------------------------------------------------------------ accessor helpers

    private static Column DateCol(Column c, string what)
        => c.Kind == Kind.DateTime ? c : throw PyErr.AttributeError($"'{what}' object has no attribute; Can only use .dt accessor with datetimelike values");

    private static object DateObjects(Column c, bool time)
    {
        var cells = new object?[c.Length];
        for (int i = 0; i < c.Length; i++)
        {
            if (c.Ticks[i] == DateTimeCore.NaT) continue;
            var p = DateTimeCore.Decompose(c.Ticks[i], c.Unit);
            var dt = new DateTime((int)p.Year, p.Month, p.Day, p.Hour, p.Minute, p.Second).AddTicks(p.Nanos / 100);
            cells[i] = time ? DateTimeModule.MakeTime(dt.TimeOfDay) : DateTimeModule.MakeDate(dt.Date);
        }
        return Column.FromObjects(cells);
    }

    private static Column StrColumn(Column c, Func<long, string> f)
        => Column.FromStrings(c.Ticks.Select(t => t == DateTimeCore.NaT ? null : f(t)).ToArray());

    /// <summary>All datetime-like operations that map a column to a column; shared by the .dt accessor and DatetimeIndex.</summary>
    internal static object? TimeMember(Column c, string name, Interp i, object[]? args, Dictionary<string, object>? kw, out bool isMethod)
    {
        isMethod = false;
        if (c.Kind == Kind.DateTime)
        {
            if (Array.IndexOf(TimeSeries.DateTimeFields, name) >= 0) return TimeSeries.Field(c, name);
            switch (name)
            {
                case "date": return DateObjects(c, false);
                case "time": return DateObjects(c, true);
                case "tz": case "tzinfo": return PyNone.Instance;
                case "unit": return DateTimeCore.UnitName(c.Unit);
            }
        }
        else if (c.Kind == Kind.Timedelta)
        {
            if (name is "days" or "seconds" or "microseconds" or "nanoseconds") return TimeSeries.TimedeltaField(c, name);
        }
        isMethod = true;
        return null;
    }

    private static Column MethodOn(Column c, string name, Interp i, Args p)
    {
        bool dt = c.Kind == Kind.DateTime;
        switch (name)
        {
            case "day_name" when dt: return StrColumn(c, t => DateTimeCore.DayNames[DateTimeCore.Decompose(t, c.Unit).DayOfWeek]);
            case "month_name" when dt: return StrColumn(c, t => DateTimeCore.MonthNames[DateTimeCore.Decompose(t, c.Unit).Month - 1]);
            case "strftime" when dt: { var f = (string)p.Required(0); return StrColumn(c, t => DateTimeCore.Strftime(t, c.Unit, f)); }
            case "normalize" when dt: return Column.FromDateTime(c.Ticks.Select(t => TimeSeries.NormalizeTicks(t, c.Unit)).ToArray(), c.Unit);
            case "floor" or "ceil" or "round":
            {
                var spec = PdTime.TickFreq(p.Required(0));
                var u = DateTimeCore.Finer(c.Unit, spec.RequiredUnit);
                var sc = c.WithUnit(u);
                Func<long, DateUnit, DateOffsetSpec.TickOffset, long> f = name == "floor" ? TimeSeries.FloorTicks : name == "ceil" ? TimeSeries.CeilTicks : TimeSeries.RoundTicks;
                var ticks = sc.Ticks.Select(t => f(t, u, spec)).ToArray();
                return dt ? Column.FromDateTime(ticks, u) : Column.FromTimedelta(ticks, u);
            }
            case "total_seconds" when c.Kind == Kind.Timedelta:
                return Column.FromDoubles(c.Ticks.Select(t => t == DateTimeCore.NaT ? double.NaN : (double)DateTimeCore.ToNanos(t, c.Unit) / 1e9).ToArray());
            case "as_unit":
                return c.WithUnit(UnitNames[(string)p.Required(0)]);
            case "to_pydatetime" when dt:
                return Column.FromObjects(c.Ticks.Select(t => t == DateTimeCore.NaT ? null : (object?)PdTime.ToPyDateTime(new Ts(t, c.Unit))).ToArray());
        }
        throw PyErr.AttributeError($"'{(dt ? "DatetimeProperties" : "TimedeltaProperties")}' object has no attribute '{name}'");
    }

    private static readonly string[] DtMethods = { "day_name", "month_name", "strftime", "normalize", "floor", "ceil", "round", "as_unit", "to_pydatetime" };
    private static readonly string[] TdMethods = { "total_seconds", "floor", "ceil", "round", "as_unit" };
    private static readonly string[] TdFields = { "days", "seconds", "microseconds", "nanoseconds" };
    private static readonly string[] DtExtra = { "date", "time", "tz", "tzinfo", "unit" };

    private static object? Own(object self) => ((PyInstance)self).Native;

    private static void BuildAccessor(PyClass cls, bool timedelta)
    {
        var fields = timedelta ? TdFields : TimeSeries.DateTimeFields.Concat(DtExtra).ToArray();
        foreach (var f in fields)
        {
            var name = f;
            cls.Dict[name] = new PyProperty
            {
                Getter = Fn(name, (i, a, _) =>
                {
                    var s = (Series)Own(a[0])!;
                    var r = TimeMember(s.Values, name, i, null, null, out var _mth);
                    return r is Column col ? PdConv.Wrap(new Series(col, s.Index, s.Name)) : r!;
                }),
            };
        }
        foreach (var m in timedelta ? TdMethods : DtMethods)
        {
            var name = m;
            cls.Dict[name] = Fn(name, (i, a, k) =>
            {
                var s = (Series)Own(a[0])!;
                var p = new Args(name, i, a.Skip(1).ToArray(), k, "arg0", "arg1", "arg2");
                var r = MethodOn(s.Values, name, i, p);
                return PdConv.Wrap(new Series(r, s.Index, s.Name));
            });
        }
        if (timedelta) return;
        cls.Dict["isocalendar"] = Fn("isocalendar", (i, a, k) =>
        {
            var s = (Series)Own(a[0])!;
            var c = s.Values;
            var iso = c.Ticks.Select(t => t == DateTimeCore.NaT ? ((long, int, int)?)null : DateTimeCore.IsoCalendar(DateTimeCore.Decompose(t, c.Unit)) is var (y, w, d) ? (y, w, d) : null).ToArray();
            Column Col(Func<(long y, int w, int d), long> f) => iso.Any(x => x is null)
                ? Column.FromDoubles(iso.Select(x => x is null ? double.NaN : (double)f(x.Value)).ToArray())
                : Column.FromLongs(iso.Select(x => f(x!.Value)).ToArray(), DType.UInt32);
            return PdConv.Wrap(new DataFrame(new[] { Col(x => x.y), Col(x => x.w), Col(x => x.d) }, new FIndex(Column.FromStrings(new string?[] { "year", "week", "day" })), s.Index));
        });
        cls.Dict["__repr__"] = Fn("__repr__", (_, a, _) => "<pandas.core.indexes.accessors.DatetimeProperties object>");
    }

    // ------------------------------------------------------------------ DatetimeIndex attributes

    private static void BuildIndexMembers()
    {
        var cls = PdClasses.Index;
        FIndex Me(object o) => (FIndex)((PyInstance)o).Native!;
        foreach (var f in TimeSeries.DateTimeFields.Concat(TdFields).Concat(DtExtra).Distinct())
        {
            var name = f;
            if (cls.Dict.ContainsKey(name)) continue;
            cls.Dict[name] = new PyProperty
            {
                Getter = Fn(name, (i, a, _) =>
                {
                    var ix = Me(a[0]);
                    var r = ix.Labels.Kind is Kind.DateTime or Kind.Timedelta ? TimeMember(ix.Labels, name, i, null, null, out var _mth) : null;
                    if (r is null) throw PyErr.AttributeError($"'Index' object has no attribute '{name}'");
                    if (r is Column col)
                    {
                        if (col.Kind == Kind.Bool) return Conv.Wrap(NDArray.FromArray(Enumerable.Range(0, col.Length).Select(col.BoolAt).ToArray()));
                        return PdConv.Wrap(new FIndex(col, ix.Name));
                    }
                    return r;
                }),
            };
        }
        foreach (var m in DtMethods.Concat(TdMethods).Distinct())
        {
            var name = m;
            cls.Dict[name] = Fn(name, (i, a, k) =>
            {
                var ix = Me(a[0]);
                if (ix.Labels.Kind is not (Kind.DateTime or Kind.Timedelta)) throw PyErr.AttributeError($"'Index' object has no attribute '{name}'");
                var p = new Args(name, i, a.Skip(1).ToArray(), k, "arg0", "arg1", "arg2");
                var r = MethodOn(ix.Labels, name, i, p);
                return PdConv.Wrap(new FIndex(r, ix.Name) { Freq = name == "normalize" ? ix.Freq : null });
            });
        }
        // arithmetic and comparisons of datetime-like indexes go through the Series machinery
        FIndex AsIndexResult(FIndex src, Series r, bool keepFreq) => new(r.Values, src.Name) { Freq = keepFreq ? src.Freq : null };
        Series AsSeries(FIndex ix) => new(ix.Labels, FIndex.Range(ix.Length));
        foreach (var (dunder, op, rev) in new[]
        {
            ("__add__", BinOp.Add, false), ("__radd__", BinOp.Add, true), ("__sub__", BinOp.Sub, false), ("__rsub__", BinOp.Sub, true),
            ("__mul__", BinOp.Mul, false), ("__rmul__", BinOp.Mul, true), ("__truediv__", BinOp.Div, false),
        })
        {
            var d2 = dunder; var op2 = op; var rev2 = rev;
            cls.Dict[d2] = Fn(d2, (i, a, k) =>
            {
                var ix = Me(a[0]);
                object other = a[1] is PyInstance { Native: FIndex oix } ? PdConv.Wrap(AsSeries(oix)) : a[1];
                var r = PdOps.Arith(op2, PdConv.Wrap(AsSeries(ix)), other, rev2);
                if (r is PyInstance { Native: Series rs })
                    return PdConv.Wrap(AsIndexResult(ix, rs, op2 is BinOp.Add or BinOp.Sub && other is not PyInstance { Native: FIndex }));
                return r;
            });
        }
        foreach (var (dunder, op) in new[] { ("__gt__", BinOp.Gt), ("__ge__", BinOp.Ge), ("__lt__", BinOp.Lt), ("__le__", BinOp.Le), ("__eq__", BinOp.Eq), ("__ne__", BinOp.Ne) })
        {
            var d2 = dunder; var op2 = op;
            var prev = cls.Dict[d2];
            cls.Dict[d2] = Fn(d2, (i, a, k) =>
            {
                var ix = Me(a[0]);
                if (ix.Labels.Kind is not (Kind.DateTime or Kind.Timedelta)) return i.Call(prev, a);
                object other = a[1] is PyInstance { Native: FIndex oix } ? PdConv.Wrap(AsSeries(oix)) : a[1];
                var r = PdOps.Arith(op2, PdConv.Wrap(AsSeries(ix)), other, false);
                return r is PyInstance { Native: Series rs } ? Conv.Wrap(NDArray.FromArray(Enumerable.Range(0, rs.Length).Select(rs.Values.BoolAt).ToArray())) : r;
            });
        }
        foreach (var rn in new[] { "min", "max", "mean", "median" })
        {
            var name = rn;
            if (cls.Dict.ContainsKey(name)) continue;
            cls.Dict[name] = Fn(name, (i, a, k) =>
            {
                var ix = Me(a[0]);
                var v = Reduce.Scalar(name, ix.Labels, true, 1, 0);
                return v is null ? (ix.Labels.Kind is Kind.DateTime or Kind.Timedelta ? PdTime.NaT : double.NaN) : PdConv.FromLabel(v);
            });
        }
        foreach (var (rn, mx) in new[] { ("argmin", false), ("argmax", true) })
        {
            var max = mx;
            if (cls.Dict.ContainsKey(rn)) continue;
            cls.Dict[rn] = Fn(rn, (i, a, k) => new BigInteger(Reduce.ArgExtreme(Me(a[0]).Labels, max, true)));
        }
        foreach (var (pn, inc) in new[] { ("is_monotonic_increasing", true), ("is_monotonic_decreasing", false) })
        {
            var increasing = inc;
            if (cls.Dict.ContainsKey(pn)) continue;
            cls.Dict[pn] = new PyProperty
            {
                Getter = Fn(pn, (_, a, _) =>
                {
                    var l = Me(a[0]).Labels;
                    if (l.Kind is Kind.Object or Kind.Category) return false;
                    for (int x = 1; x < l.Length; x++)
                    {
                        if (l.IsNa(x) || l.IsNa(x - 1)) return false;
                        int c = l.Kind switch
                        {
                            Kind.Str => string.CompareOrdinal(l.StrAt(x - 1), l.StrAt(x)),
                            Kind.Float => l.DoubleAt(x - 1).CompareTo(l.DoubleAt(x)),
                            Kind.Bool => l.BoolAt(x - 1).CompareTo(l.BoolAt(x)),
                            _ => l.LongAt(x - 1).CompareTo(l.LongAt(x)),
                        };
                        if (increasing ? c > 0 : c < 0) return false;
                    }
                    return true;
                }),
            };
        }
        cls.Dict["freq"] = new PyProperty { Getter = Fn("freq", (_, a, _) => Me(a[0]).Freq is { } f ? PdTime.WrapOffset(DateOffsetSpec.Parse(f)) : PyNone.Instance) };
        cls.Dict["freqstr"] = new PyProperty { Getter = Fn("freqstr", (_, a, _) => Me(a[0]).Freq is { } f ? f : PyNone.Instance) };
        cls.Dict["inferred_freq"] = new PyProperty { Getter = Fn("inferred_freq", (_, a, _) => InferFreq(Me(a[0])) is { } f ? f : PyNone.Instance) };
        cls.Dict["to_series"] = Fn("to_series", (i, a, k) =>
        {
            var ix = Me(a[0]);
            var p = new Args("to_series", i, a.Skip(1).ToArray(), k, "index", "name");
            return PdConv.Wrap(new Series(ix.Labels, p.Has(0) ? PdBuild.Index(p[0]!, ix.Length) : ix, p.Has(1) ? PdConv.ToCell(p[1]) : ix.Name));
        });
        cls.Dict["shift"] = Fn("shift", (i, a, k) =>
        {
            var ix = Me(a[0]);
            var p = new Args("shift", i, a.Skip(1).ToArray(), k, "periods", "freq");
            int periods = p.Int(0, 1);
            var off = p.Has(1) ? OffsetArg(p[1]) : ix.Freq is { } f ? DateOffsetSpec.Parse(f) : throw PyErr.NotImplementedError("Cannot shift with no freq");
            return PdConv.Wrap(ShiftIndex(ix, off, periods));
        });
    }

    internal static FIndex ShiftIndex(FIndex ix, DateOffsetSpec off, int periods)
    {
        if (ix.Labels.Kind != Kind.DateTime) throw PyErr.TypeError("shift(freq=...) requires a DatetimeIndex");
        var spec = off.WithN(off.N * periods);
        var u = DateTimeCore.Finer(ix.Labels.Unit, spec.RequiredUnit);
        var sc = ix.Labels.WithUnit(u);
        var ticks = sc.Ticks.Select(t => t == DateTimeCore.NaT ? t : spec.Add(t, u)).ToArray();
        return new FIndex(Column.FromDateTime(ticks, u), ix.Name) { Freq = ix.Freq };
    }

    private static string? InferFreq(FIndex ix)
    {
        if (ix.Labels.Kind != Kind.DateTime || ix.Length < 3) return null;
        if (ix.Freq is not null) return ix.Freq;
        foreach (var cand in new[] { "D", "h", "min", "s", "ms", "B", "W-SUN", "ME", "MS", "QE-DEC", "QS-JAN", "YE-DEC", "YS-JAN" })
        {
            var off = DateOffsetSpec.Parse(cand);
            var u = DateTimeCore.Finer(ix.Labels.Unit, off.RequiredUnit);
            var t = ix.Labels.WithUnit(u).Ticks;
            if (t.Length < 2) continue;
            bool ok = true;
            for (int n = 0; n + 1 < t.Length && ok; n++) ok = off.Add(t[n], u) == t[n + 1];
            if (ok) return cand;
        }
        return null;
    }

    // ------------------------------------------------------------------ resample

    private static object Resample(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var p = A("resample", i, a.Skip(1).ToArray(), k, "rule", "axis", "closed", "label", "convention", "kind", "on", "level", "origin", "offset", "group_keys");
        object self = a[0];
        bool isSeries = self is PyInstance { Native: Series };
        var off = OffsetArg(p.Required(0));
        if (p.Has(8) && !(p[8] is string os && os == "start_day") || p.Has(9)) throw PyErr.NotImplementedError("resample(origin=/offset=)");
        DataFrame frame = isSeries ? PdGroupBy.SeriesFrame((Series)((PyInstance)self).Native!) : PdConv.D(self);
        Column keySource;
        object? keyName;
        var keyPos = new List<int>();
        if (p.Has(6))
        {
            var label = PdConv.ToCell(p[6]);
            int pos = frame.ColumnPositions(label)[0];
            keySource = frame.Data[pos]; keyName = label; keyPos.Add(pos);
        }
        else { keySource = frame.Index.Labels; keyName = frame.Index.Name; }
        if (keySource.Kind != Kind.DateTime)
            throw PyErr.TypeError($"Only valid with DatetimeIndex, TimedeltaIndex or PeriodIndex, but got an instance of '{(isSeries ? "RangeIndex" : "Index")}'");
        string? closed = p.Has(2) ? (string)p[2]! : null, label2 = p.Has(3) ? (string)p[3]! : null;
        var bins = TimeSeries.ResampleBins(keySource.Ticks, keySource.Unit, off, closed, label2);
        var unit = keySource.Unit;
        var rowKeys = Column.FromDateTime(bins.BinOfRow.Select(b => b < 0 ? DateTimeCore.NaT : bins.Labels[b]).ToArray(), unit);
        var forced = bins.Labels.Select(l => new object?[] { new Ts(l, unit) }).ToList();
        var g = new Grouping(new[] { rowKeys }, new[] { keyName }, frame.NRows, true, true, true, forced) { Freq = bins.FreqText };
        return PdGroupBy.FromGrouping(self, frame, g, keyPos, isSeries, keySource);
    }

    /// <summary>The row keys of a <c>pd.Grouper</c> (binned when it has a frequency).</summary>
    internal static (Column col, object? name, List<object?[]>? labels, string? freq, int pos) GrouperKey(DataFrame frame, PyDict gd)
    {
        object? keyObj = gd.TryGet("key", out var kv) && kv is not PyNone ? kv : null;
        object? freqObj = gd.TryGet("freq", out var fv) && fv is not PyNone ? fv : null;
        Column source; object? name; int pos = -1;
        if (keyObj is not null) { var lab = PdConv.ToCell(keyObj); pos = frame.ColumnPositions(lab)[0]; source = frame.Data[pos]; name = lab; }
        else { source = frame.Index.Labels; name = frame.Index.Name; }
        if (freqObj is null) return (source, name, null, null, pos);
        if (source.Kind != Kind.DateTime) throw PyErr.TypeError("Only valid with DatetimeIndex, TimedeltaIndex or PeriodIndex");
        var off = OffsetArg(freqObj);
        string? closed = gd.TryGet("closed", out var cv) && cv is string cs ? cs : null, label = gd.TryGet("label", out var lv) && lv is string ls ? ls : null;
        var bins = TimeSeries.ResampleBins(source.Ticks, source.Unit, off, closed, label);
        var rowKeys = Column.FromDateTime(bins.BinOfRow.Select(b => b < 0 ? DateTimeCore.NaT : bins.Labels[b]).ToArray(), source.Unit);
        return (rowKeys, name, bins.Labels.Select(l => new object?[] { new Ts(l, source.Unit) }).ToList(), bins.FreqText, pos);
    }

    // ------------------------------------------------------------------ clip, DatetimeArray

    public static Column ClipTime(Column c, object? lo, object? hi)
    {
        if (c.Kind is not (Kind.DateTime or Kind.Timedelta)) throw PyErr.TypeError($"cannot clip a column of dtype {c.DTypeName} with timestamps");
        var u = c.Unit;
        foreach (var b in new[] { lo, hi }) if (b is Ts t) u = DateTimeCore.Finer(u, t.Unit); else if (b is Td d) u = DateTimeCore.Finer(u, d.Unit);
        long? L(object? b) => b switch { Ts t => DateTimeCore.Scale(t.Ticks, t.Unit, u), Td d => DateTimeCore.Scale(d.Ticks, d.Unit, u), _ => null };
        long? l = L(lo), h = L(hi);
        var ticks = c.WithUnit(u).Ticks.Select(t => t == DateTimeCore.NaT ? t : l is long lv && t < lv ? lv : h is long hv && t > hv ? hv : t).ToArray();
        return c.Kind == Kind.DateTime ? Column.FromDateTime(ticks, u) : Column.FromTimedelta(ticks, u);
    }

    public static readonly PyClass DatetimeArrayClass = new("DatetimeArray", new List<PyClass>());
    public static readonly PyClass TimedeltaArrayClass = new("TimedeltaArray", new List<PyClass>());

    public static object WrapTimeArray(Column c) => new PyInstance(c.Kind == Kind.DateTime ? DatetimeArrayClass : TimedeltaArrayClass) { Native = c };

    private static void BuildTimeArrays()
    {
        foreach (var cls in new[] { DatetimeArrayClass, TimedeltaArrayClass })
        {
            Column Me(object o) => (Column)((PyInstance)o).Native!;
            void Def(string n, BuiltinFn f) => cls.Dict[n] = Fn(cls.Name + "." + n, f);
            Def("__len__", (_, a, _) => new BigInteger(Me(a[0]).Length));
            Def("__iter__", (_, a, _) => new PyIterator(Enumerable.Range(0, Me(a[0]).Length).Select(x => PdConv.FromCell(Me(a[0]), x)).GetEnumerator()));
            Def("__getitem__", (_, a, _) =>
            {
                var c = Me(a[0]);
                if (a[1] is PySlice sl) return WrapTimeArray(c.Take(PdSelect.PositionalSlice(sl, c.Length)));
                int p = PdConv.ToInt(a[1]); if (p < 0) p += c.Length;
                return PdConv.FromCell(c, p);
            });
            Def("tolist", (_, a, _) => new PyList(Enumerable.Range(0, Me(a[0]).Length).Select(x => PdConv.FromCell(Me(a[0]), x))));
            cls.Dict["dtype"] = new PyProperty { Getter = Fn("dtype", (_, a, _) => PdConv.WrapDType(Me(a[0]))) };
            Def("__repr__", (_, a, _) => TimeArrayRepr(Me(a[0]), cls.Name));
            Def("__str__", (_, a, _) => TimeArrayRepr(Me(a[0]), cls.Name));
        }
    }

    private static string TimeArrayRepr(Column c, string className)
    {
        var cells = Formatter.FormatCells(c, PdOptions.Display, false);
        var items = Enumerable.Range(0, c.Length).Select(i => "'" + (c.IsNa(i) ? "NaT" : cells[i]) + "'").ToList();
        var sb = new System.Text.StringBuilder($"<{className}>\n[");
        int width = PdOptions.Display.Width, lineLen = 1;
        for (int i = 0; i < items.Count; i++)
        {
            string it = items[i] + (i < items.Count - 1 ? "," : "");
            if (lineLen + it.Length + (lineLen > 1 ? 1 : 0) > width && lineLen > 1) { sb.Append('\n').Append(' '); lineLen = 1; }
            else if (i > 0) { sb.Append(' '); lineLen++; }
            sb.Append(it); lineLen += it.Length;
        }
        return sb.Append("]\nLength: ").Append(c.Length).Append(", dtype: ").Append(c.DTypeName).ToString();
    }

    // ------------------------------------------------------------------ module install

    public static void Install(PyModule m)
    {
        void Def(string name, BuiltinFn fn) => m.Dict[name] = Fn(name, fn);
        Def("to_datetime", ToDatetime);
        Def("to_timedelta", ToTimedelta);
        Def("date_range", DateRange);
        Def("timedelta_range", TimedeltaRange);
        Def("bdate_range", (i, a, k) =>
        {
            var kk = k is null ? new Dictionary<string, object>() : new Dictionary<string, object>(k);
            if (!kk.ContainsKey("freq") && a.Length < 4) kk["freq"] = "B";
            return DateRange(i, a, kk);
        });
        Def("DatetimeIndex", (i, a, k) =>
        {
            var p = A("DatetimeIndex", i, a, k, "data", "freq", "tz", "normalize", "closed", "ambiguous", "dayfirst", "yearfirst", "dtype", "copy", "name");
            var data = p.Has(0) ? p[0]! : new PyList();
            var col = ToDatetimeColumn(ColumnOf(data), "raise", p.Bool(6, false), null, null);
            string? freq = p.Has(1) && p[1] is not "infer" ? OffsetArg(p[1]).FreqString : null;
            var ix = new FIndex(col, p.Has(10) ? PdConv.ToCell(p[10]) : data is PyInstance { Native: FIndex src } ? src.Name : null) { Freq = freq };
            return PdConv.Wrap(ix);
        });
        Def("TimedeltaIndex", (i, a, k) =>
        {
            var p = A("TimedeltaIndex", i, a, k, "data", "unit", "freq", "closed", "dtype", "copy", "name");
            var data = p.Has(0) ? p[0]! : new PyList();
            var col = ToTimedeltaColumn(ColumnOf(data), "raise", p.Has(1) ? (string)p[1]! : null);
            return PdConv.Wrap(new FIndex(col, p.Has(6) ? PdConv.ToCell(p[6]) : null) { Freq = p.Has(2) && p[2] is not "infer" ? OffsetArg(p[2]).FreqString : null });
        });
        Def("Grouper", (i, a, k) =>
        {
            var p = A("Grouper", i, a, k, "key", "level", "freq", "axis", "sort", "closed", "label", "convention", "origin", "offset");
            var d = new PyDict();
            d["key"] = p.Has(0) ? p[0]! : PyNone.Instance;
            d["freq"] = p.Has(2) ? p[2]! : PyNone.Instance;
            d["closed"] = p.Has(5) ? p[5]! : PyNone.Instance;
            d["label"] = p.Has(6) ? p[6]! : PyNone.Instance;
            return new PyInstance(GrouperClass) { Native = d };
        });
        Def("infer_freq", (i, a, k) => InferFreq(PdBuild.Index(a[0], -1)) is { } f ? f : PyNone.Instance);
        m.Dict["Timestamp"] = PdTime.TimestampClass;
        BuildAccessor(DtAccessor, false);
        BuildAccessor(TdAccessor, true);
        BuildIndexMembers();
        BuildTimeArrays();
        var dtProp = new PyProperty
        {
            Getter = Fn("dt", (_, a, _) =>
            {
                var s = (Series)Own(a[0])!;
                return s.Values.Kind switch
                {
                    Kind.DateTime => new PyInstance(DtAccessor) { Native = s },
                    Kind.Timedelta => new PyInstance(TdAccessor) { Native = s },
                    _ => throw PyErr.AttributeError("Can only use .dt accessor with datetimelike values"),
                };
            }),
        };
        PdClasses.Series.Dict["dt"] = dtProp;
        PdClasses.Series.Dict["resample"] = Fn("resample", Resample);
        PdClasses.DataFrame.Dict["resample"] = Fn("resample", Resample);
        InstallResamplerMethods();
        InstallReindexing();
        PdDatesSupport.Install();
        PdDatesSupport.Install();
    }

    public static readonly PyClass GrouperClass = new("Grouper", new List<PyClass>());

    // ------------------------------------------------------------------ resample-only methods, asfreq, shift(freq)

    private static void InstallResamplerMethods()
    {
        foreach (var cls in new[] { PdGroupBy.DataFrameGroupBy, PdGroupBy.SeriesGroupBy })
        {
            foreach (var name in new[] { "asfreq", "ffill", "bfill", "pad", "backfill", "nearest" })
            {
                var nm = name;
                cls.Dict[nm] = Fn(nm, (i, a, k) => PdGroupBy.ResampleFill(i, a[0], nm, a.Skip(1).ToArray(), k));
            }
            cls.Dict["ohlc"] = Fn("ohlc", (i, a, k) => PdGroupBy.Ohlc(i, a[0]));
        }
    }

    private static void InstallReindexing()
    {
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            cls.Dict["asfreq"] = Fn("asfreq", (i, a, k) =>
            {
                var p = A("asfreq", i, a.Skip(1).ToArray(), k, "freq", "method", "how", "normalize", "fill_value");
                var off = OffsetArg(p.Required(0));
                var (idx, frame) = IndexAndFrame(a[0]);
                if (idx.Labels.Kind != Kind.DateTime) throw PyErr.TypeError("asfreq requires a DatetimeIndex");
                var t = idx.Labels.Ticks.Where(x => x != DateTimeCore.NaT).ToArray();
                var u = idx.Labels.Unit;
                var target = TimeSeries.DateRange(t.Min(), t.Max(), null, off, u);
                var newIdx = new FIndex(Column.FromDateTime(target, u), idx.Name) { Freq = off.FreqString };
                return PdDatesSupport.Reindex(a[0], newIdx, p.Has(1) ? (string)p[1]! : null, p.Has(4) ? PdConv.ToCell(p[4]) : null);
            });
        }
    }

    private static (FIndex, object) IndexAndFrame(object self)
        => self is PyInstance { Native: Series s } ? (s.Index, s) : (((DataFrame)((PyInstance)self).Native!).Index, ((PyInstance)self).Native!);
}
