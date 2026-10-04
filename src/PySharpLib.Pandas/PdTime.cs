// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Modules;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary>Marks the Python <c>NaT</c> singleton.</summary>
internal sealed class NaTMarker { }

/// <summary>The scalar side of pandas' time series support: <c>Timestamp</c>, <c>Timedelta</c>, <c>NaT</c> and the date offsets.</summary>
internal static class PdTime
{
    public static readonly PyClass TimestampClass = new("Timestamp", new List<PyClass>());
    public static readonly PyClass TimedeltaClass = new("Timedelta", new List<PyClass>());
    public static readonly PyClass NaTClass = new("NaTType", new List<PyClass>());
    public static readonly PyClass OffsetClass = new("DateOffset", new List<PyClass>());
    public static readonly PyClass IsoCalendarDateClass = BuildIsoCalendarDate();

    private static PyClass BuildIsoCalendarDate()
    {
        var cls = new PyClass("IsoCalendarDate", new List<PyClass>());
        long[] V(object o) => (long[])((PyInstance)o).Native!;
        void Def(string n, BuiltinFn f) => cls.Dict[n] = new PyBuiltinFunction("IsoCalendarDate." + n, f);
        Def("__repr__", (_, a, _) => $"datetime.IsoCalendarDate(year={V(a[0])[0]}, week={V(a[0])[1]}, weekday={V(a[0])[2]})");
        Def("__str__", (_, a, _) => $"({V(a[0])[0]}, {V(a[0])[1]}, {V(a[0])[2]})");
        Def("__len__", (_, _, _) => new BigInteger(3));
        Def("__iter__", (_, a, _) => new PyIterator(V(a[0]).Select(x => (object)new BigInteger(x)).GetEnumerator()));
        Def("__getitem__", (_, a, _) => { int i = (int)(BigInteger)a[1]; if (i < 0) i += 3; return new BigInteger(V(a[0])[i]); });
        Def("__eq__", (_, a, _) => a[1] is PyTuple t ? t.Items.Length == 3 && Enumerable.Range(0, 3).All(i => t.Items[i] is BigInteger b && (long)b == V(a[0])[i]) : a[1] is PyInstance { Native: long[] o } && o.SequenceEqual(V(a[0])));
        Def("__hash__", (_, a, _) => new BigInteger(V(a[0]).Aggregate(17L, (h, x) => h * 31 + x)));
        foreach (var (n, i) in new[] { ("year", 0), ("week", 1), ("weekday", 2) })
        {
            int idx = i;
            cls.Dict[n] = new PyProperty { Getter = new PyBuiltinFunction(n, (_, a, _) => new BigInteger(V(a[0])[idx])) };
        }
        return cls;
    }

    public static readonly PyClass DateParseErrorClass = new("DateParseError", new List<PyClass> { PyErr.ValueErrorClass });
    public static readonly PyInstance NaT = new(NaTClass) { Native = new NaTMarker() };

    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);
    private static PyBuiltinFunction Fn(string name, BuiltinFn f) => PdClasses.Fn(name, f);

    // ------------------------------------------------------------------ wrapping and conversion

    public static object Wrap(Ts t) => t.Ticks == DateTimeCore.NaT ? NaT : new PyInstance(TimestampClass) { Native = t };
    public static object Wrap(Td t) => t.Ticks == DateTimeCore.NaT ? NaT : new PyInstance(TimedeltaClass) { Native = t };

    private static string OffsetRepr(DateOffsetSpec s)
    {
        string name = s switch
        {
            DateOffsetSpec.TickOffset t => t.Alias switch { "D" => "Day", "h" => "Hour", "min" => "Minute", "s" => "Second", "ms" => "Milli", "us" => "Micro", _ => "Nano" },
            DateOffsetSpec.WeekOffset => "Week",
            DateOffsetSpec.BusinessDay => "BusinessDay",
            DateOffsetSpec.MonthLike => s.FreqString.TrimStart('-', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9') switch
            {
                var f when f.StartsWith("ME") => "MonthEnd", var f when f.StartsWith("MS") => "MonthBegin", var f when f.StartsWith("QE") => "QuarterEnd",
                var f when f.StartsWith("QS") => "QuarterBegin", var f when f.StartsWith("YE") => "YearEnd", _ => "YearBegin",
            },
            _ => "DateOffset",
        };
        if (s is DateOffsetSpec.Relative r) return "<DateOffset: " + r.Describe + ">";
        string extra = s is DateOffsetSpec.WeekOffset w ? $": weekday={w.Weekday}" : s is DateOffsetSpec.MonthLike && s.FreqString.Contains('-') ? $": startingMonth=" : "";
        if (s is DateOffsetSpec.WeekOffset ww && ww.Weekday == 6) extra = ": weekday=6";
        return s.N == 1 ? $"<{name}{(s is DateOffsetSpec.WeekOffset ? extra : "")}>" : $"<{s.N} * {name}s{(s is DateOffsetSpec.WeekOffset ? extra : "")}>";
    }

    public static string OffsetText(DateOffsetSpec s) => s is DateOffsetSpec.Relative ? OffsetRepr(s) : s.FreqString;

    public static object WrapOffset(DateOffsetSpec s) => new PyInstance(OffsetClass) { Native = s };

    /// <summary>A Python value as a timestamp: Timestamp, datetime.datetime, ISO/guessable string.</summary>
    public static bool TryTs(object? o, out Ts ts)
    {
        ts = default;
        switch (o)
        {
            case Ts t: ts = t; return true;
            case PyInstance { Native: Ts t2 }: ts = t2; return true;
            case PyInstance pi when pi.Class == DateTimeModule.DateTimeClass && pi.Dict.TryGet("__value__", out var v) && v is DateTime dt:
            {
                long ticks = (dt - DateTime.UnixEpoch).Ticks;
                ts = ticks % 10 == 0 ? new Ts(ticks / 10, DateUnit.Micro) : new Ts(ticks * 100, DateUnit.Nano);
                return true;
            }
            case string s:
                if (DateTimeCore.TryParseIso(s, out var tk, out var tu)) { ts = new Ts(tk, tu); return true; }
                var g = DateTimeCore.GuessFormat(s);
                if (g is not null && DateTimeCore.TryStrptime(s, g, out tk, out tu)) { ts = new Ts(tk, tu); return true; }
                return false;
        }
        return false;
    }

    public static bool TryTd(object? o, out Td td)
    {
        td = default;
        switch (o)
        {
            case Td t: td = t; return true;
            case PyInstance { Native: Td t2 }: td = t2; return true;
            case PyInstance pi when pi.Class == DateTimeModule.TimeDeltaClass && pi.Dict.TryGet("__value__", out var v) && v is TimeSpan span:
                td = span.Ticks % 10 == 0 ? new Td(span.Ticks / 10, DateUnit.Micro) : new Td(span.Ticks * 100, DateUnit.Nano);
                return true;
            case string s:
                if (DateTimeCore.TryParseTimedelta(s, out var tk, out var tu)) { td = new Td(tk, tu); return true; }
                return false;
        }
        return false;
    }

    /// <summary>Cell conversion hook of <see cref="PdConv.ToCell"/> for the time types.</summary>
    public static bool TryCell(object v, out object? cell)
    {
        cell = null;
        if (v is PyInstance { Native: NaTMarker }) return true;
        if (v is PyInstance { Native: Ts or Td } pi) { cell = pi.Native; return true; }
        if (v is PyInstance pi2 && pi2.Class == DateTimeModule.DateTimeClass && TryTs(v, out var ts)) { cell = ts; return true; }
        if (v is PyInstance pi3 && pi3.Class == DateTimeModule.TimeDeltaClass && TryTd(v, out var td)) { cell = td; return true; }
        return false;
    }

    private static Ts GetTs(object self) => (Ts)((PyInstance)self).Native!;
    private static Td GetTd(object self) => (Td)((PyInstance)self).Native!;
    private static DateTimeCore.Parts PartsOf(Ts t) => DateTimeCore.Decompose(t.Ticks, t.Unit);

    private static DateTime ToDotNet(Ts t)
    {
        long ticks100 = t.Unit switch { DateUnit.Second => t.Ticks * 10_000_000, DateUnit.Milli => t.Ticks * 10_000, DateUnit.Micro => t.Ticks * 10, _ => DateTimeCore.FloorDiv(t.Ticks, 100) };
        return DateTime.UnixEpoch.AddTicks(ticks100);
    }

    public static object ToPyDateTime(Ts t) => DateTimeModule.MakeDateTime(ToDotNet(t));

    private static long ToNanosLong(Ts t) => checked(t.Ticks * DateTimeCore.NanosPerTick(t.Unit));

    private static object Cmp(BinOp op, object? a, object? b)
    {
        // used by scalar comparisons
        return null!;
    }

    private static DateOffsetSpec? OffsetOf(object o) => o is PyInstance { Native: DateOffsetSpec s } ? s : null;

    // ------------------------------------------------------------------ arithmetic on scalars

    private static object AddTd(Ts a, Td b)
    {
        if (a.Ticks == DateTimeCore.NaT || b.Ticks == DateTimeCore.NaT) return NaT;
        var u = DateTimeCore.Finer(a.Unit, b.Unit);
        return Wrap(new Ts(checked(DateTimeCore.Scale(a.Ticks, a.Unit, u) + DateTimeCore.Scale(b.Ticks, b.Unit, u)), u));
    }

    private static object ScalarAdd(object self, object other, bool reversed)
    {
        if (self is PyInstance { Native: Ts a })
        {
            if (other is PyInstance { Native: NaTMarker }) return NaT;
            if (TryTd(other is string ? null : other, out var td)) return AddTd(a, td);
            if (OffsetOf(other) is { } off)
            {
                var u = DateTimeCore.Finer(a.Unit, off.RequiredUnit);
                return Wrap(new Ts(off.Add(DateTimeCore.Scale(a.Ticks, a.Unit, u), u), u));
            }
            if (other is BigInteger) throw PyErr.TypeError("Addition/subtraction of integers and integer-arrays with Timestamp is no longer supported.  Instead of adding/subtracting `n`, use `n * obj.freq`");
            return PyNotImplemented.Instance;
        }
        var d = GetTd(self);
        if (other is PyInstance { Native: NaTMarker }) return NaT;
        if (TryTs(other is string ? null : other, out var ts) && ts.Ticks != DateTimeCore.NaT) return AddTd(ts, d);
        if (TryTd(other is string ? null : other, out var td2))
        {
            var u = DateTimeCore.Finer(d.Unit, td2.Unit);
            return Wrap(new Td(checked(DateTimeCore.Scale(d.Ticks, d.Unit, u) + DateTimeCore.Scale(td2.Ticks, td2.Unit, u)), u));
        }
        return PyNotImplemented.Instance;
    }

    private static object ScalarSub(object self, object other, bool reversed)
    {
        if (self is PyInstance { Native: Ts a })
        {
            if (other is PyInstance { Native: NaTMarker }) return NaT;
            if (!reversed)
            {
                if (TryTd(other is string ? null : other, out var td) && !(other is PyInstance { Native: Ts }))
                {
                    if (a.Ticks == DateTimeCore.NaT || td.Ticks == DateTimeCore.NaT) return NaT;
                    var u = DateTimeCore.Finer(a.Unit, td.Unit);
                    return Wrap(new Ts(checked(DateTimeCore.Scale(a.Ticks, a.Unit, u) - DateTimeCore.Scale(td.Ticks, td.Unit, u)), u));
                }
                if (OffsetOf(other) is { } off)
                {
                    var u = DateTimeCore.Finer(a.Unit, off.RequiredUnit);
                    return Wrap(new Ts(off.WithN(-off.N).Add(DateTimeCore.Scale(a.Ticks, a.Unit, u), u), u));
                }
            }
            if (TryTs(other is string ? null : other, out var b))
            {
                if (a.Ticks == DateTimeCore.NaT || b.Ticks == DateTimeCore.NaT) return NaT;
                var u = DateTimeCore.Finer(a.Unit, b.Unit);
                long x = DateTimeCore.Scale(a.Ticks, a.Unit, u), y = DateTimeCore.Scale(b.Ticks, b.Unit, u);
                return Wrap(new Td(reversed ? checked(y - x) : checked(x - y), u));
            }
            return PyNotImplemented.Instance;
        }
        var d = GetTd(self);
        if (other is PyInstance { Native: NaTMarker }) return NaT;
        if (TryTd(other is string ? null : other, out var e))
        {
            var u = DateTimeCore.Finer(d.Unit, e.Unit);
            long x = DateTimeCore.Scale(d.Ticks, d.Unit, u), y = DateTimeCore.Scale(e.Ticks, e.Unit, u);
            return Wrap(new Td(reversed ? checked(y - x) : checked(x - y), u));
        }
        return PyNotImplemented.Instance;
    }

    private static bool CompareScalars(string op, object self, object other, out object result)
    {
        result = false;
        long ca, cb; // comparison keys in nanoseconds (Int128 for range)
        Int128 x, y;
        string sym = op switch { "lt" => "<", "le" => "<=", "gt" => ">", "ge" => ">=", _ => op };
        PyRaise Unorderable(object a, object b) => PyErr.TypeError($"'{sym}' not supported between instances of '{PyOps.TypeName(a)}' and '{PyOps.TypeName(b)}'");
        if (other is PyInstance { Native: NaTMarker }) { result = op == "ne"; return true; }
        if (self is PyInstance { Native: Ts a })
        {
            if (other is string || !TryTs(other, out var b))
            {
                if (op is "eq" or "ne") { result = op == "ne"; return true; }
                throw Unorderable(self, other);
            }
            if (a.Ticks == DateTimeCore.NaT || b.Ticks == DateTimeCore.NaT) { result = op == "ne"; return true; }
            int c = DateTimeCore.Compare(a.Ticks, a.Unit, b.Ticks, b.Unit);
            result = Decide(op, c); return true;
        }
        var d = GetTd(self);
        if (!TryTd(other is string ? null : other, out var e))
        {
            if (op is "eq" or "ne") { result = op == "ne"; return true; }
            throw Unorderable(self, other);
        }
        if (d.Ticks == DateTimeCore.NaT || e.Ticks == DateTimeCore.NaT) { result = op == "ne"; return true; }
        x = DateTimeCore.ToNanos(d.Ticks, d.Unit); y = DateTimeCore.ToNanos(e.Ticks, e.Unit);
        _ = ca = cb = 0;
        result = Decide(op, x.CompareTo(y)); return true;
    }

    private static bool Decide(string op, int c) => op switch { "eq" => c == 0, "ne" => c != 0, "lt" => c < 0, "le" => c <= 0, "gt" => c > 0, _ => c >= 0 };

    // ------------------------------------------------------------------ construction

    private static readonly Dictionary<string, DateUnit> UnitNames = new()
    {
        ["s"] = DateUnit.Second, ["ms"] = DateUnit.Milli, ["us"] = DateUnit.Micro, ["ns"] = DateUnit.Nano,
    };

    /// <summary>Epoch number → timestamp ticks for <c>unit=</c> ('D', 'h', 'm', 's', 'ms', 'us', 'ns').</summary>
    public static Ts FromEpoch(double value, string unit)
    {
        switch (unit)
        {
            case "D": return new Ts(checked((long)(value * 86400)), DateUnit.Second);
            case "h": return new Ts(checked((long)(value * 3600)), DateUnit.Second);
            case "m": case "min": return new Ts(checked((long)(value * 60)), DateUnit.Second);
            case "s": return value == Math.Floor(value) ? new Ts((long)value, DateUnit.Second) : new Ts((long)Math.Round(value * 1e9), DateUnit.Nano);
            case "ms": return value == Math.Floor(value) ? new Ts((long)value, DateUnit.Milli) : new Ts((long)Math.Round(value * 1e6), DateUnit.Nano);
            case "us": return value == Math.Floor(value) ? new Ts((long)value, DateUnit.Micro) : new Ts((long)Math.Round(value * 1e3), DateUnit.Nano);
            case "ns": return new Ts((long)value, DateUnit.Nano);
        }
        throw PyErr.ValueError($"invalid unit abbreviation: {unit}");
    }

    private static Ts MakeTimestamp(Args p)
    {
        if (p.Has(0) && !p.Has(1))
        {
            var v = p[0]!;
            if (v is PyInstance { Native: NaTMarker }) return new Ts(DateTimeCore.NaT, DateUnit.Micro);
            if (v is BigInteger or double or PyInstance { Native: ScalarBox })
            {
                string unit = p.Has(11) ? (string)p[11]! : "ns";
                double dv = Column.ToDouble(PdConv.ToCell(v)!);
                return FromEpoch(dv, unit);
            }
            if (TryTs(v, out var ts)) return p.Has(11) ? new Ts(DateTimeCore.Scale(ts.Ticks, ts.Unit, UnitNames[(string)p[11]!]), UnitNames[(string)p[11]!]) : ts;
            throw PyErr.Raise(DateParseErrorClass, $"Unknown datetime string format, unable to parse: {PyOps.Str(PdConv.Interp!, v)}");
        }
        throw PyErr.TypeError("Timestamp() requires a value or year, month, day");
    }

    private static Ts MakeFromFields(Args p)
    {
        int Part(int idx, int dflt) => p.Has(idx) ? PdConv.ToInt(p[idx]!) : dflt;
        int year = Part(0, 0), month = Part(1, 0), day = Part(2, 0);
        if (!p.Has(0) || !p.Has(1) || !p.Has(2)) throw PyErr.TypeError("Timestamp() missing required argument 'year', 'month' or 'day'");
        int nanosec = Part(8, 0);
        long nanos = Part(6, 0) * 1000L + nanosec;
        var unitK = nanosec != 0 ? DateUnit.Nano : DateUnit.Micro;
        if (month < 1 || month > 12 || day < 1 || day > DateTimeCore.DaysInMonth(year, month)) throw PyErr.ValueError("day is out of range for month");
        return new Ts(DateTimeCore.Compose(year, month, day, Part(3, 0), Part(4, 0), Part(5, 0), nanos, unitK), unitK);
    }

    public static Td MakeTimedelta(Args p, Dictionary<string, object>? k)
    {
        // Timedelta(value, unit) / Timedelta(days=, hours=, ...)
        if (p.Has(0))
        {
            var v = p[0]!;
            if (v is PyInstance { Native: NaTMarker }) return new Td(DateTimeCore.NaT, DateUnit.Micro);
            if (TryTd(v, out var td) && p.Has(1) == false) return td;
            if (v is BigInteger or double or PyInstance { Native: ScalarBox })
            {
                string unit = p.Has(1) ? (string)p[1]! : "ns";
                double dv = Column.ToDouble(PdConv.ToCell(v)!);
                bool whole = dv == Math.Floor(dv);
                switch (unit)
                {
                    case "ns": return new Td((long)dv, DateUnit.Nano);
                    case "us": return whole ? new Td((long)dv, DateUnit.Micro) : new Td((long)Math.Round(dv * 1000), DateUnit.Nano);
                    case "ms": return whole ? new Td((long)dv, DateUnit.Milli) : new Td((long)Math.Round(dv * 1e6), DateUnit.Nano);
                    case "s": case "sec": return whole ? new Td((long)dv, DateUnit.Second) : new Td((long)Math.Round(dv * 1e9), DateUnit.Nano);
                    case "m": case "min": return whole ? new Td((long)dv * 60, DateUnit.Second) : new Td((long)Math.Round(dv * 60e9), DateUnit.Nano);
                    case "h": case "hr": return whole ? new Td((long)dv * 3600, DateUnit.Second) : new Td((long)Math.Round(dv * 3600e9), DateUnit.Nano);
                    case "D": case "d": return whole ? new Td((long)dv * 86400, DateUnit.Second) : new Td((long)Math.Round(dv * 86400e9), DateUnit.Nano);
                    case "W": case "w": return whole ? new Td((long)dv * 7 * 86400, DateUnit.Second) : new Td((long)Math.Round(dv * 7 * 86400e9), DateUnit.Nano);
                }
                throw PyErr.ValueError($"invalid unit abbreviation: {unit}");
            }
            if (v is string s)
            {
                if (!DateTimeCore.TryParseTimedelta(s, out var tk, out var tu, out var perr)) throw PyErr.ValueError(perr ?? $"Invalid Timedelta string: {s}");
                return new Td(tk, tu);
            }
            throw PyErr.TypeError("Value must be Timedelta, string, integer, float, timedelta or convertible");
        }
        System.Numerics.BigInteger totalNanos = 0;
        void Add(string key, long nanosPer)
        {
            if (k is not null && k.TryGetValue(key, out var v) && v is not PyNone)
                totalNanos += (System.Numerics.BigInteger)Math.Round(Column.ToDouble(PdConv.ToCell(v)!) * nanosPer);
        }
        Add("weeks", 7L * 86400_000_000_000); Add("days", 86400_000_000_000); Add("hours", 3600_000_000_000); Add("minutes", 60_000_000_000);
        Add("seconds", 1_000_000_000); Add("milliseconds", 1_000_000); Add("microseconds", 1_000); Add("nanoseconds", 1);
        bool ns = k is not null && k.TryGetValue("nanoseconds", out var nv) && nv is not PyNone && totalNanos % 1000 != 0 || totalNanos % 1000 != 0;
        return ns ? new Td((long)totalNanos, DateUnit.Nano) : new Td((long)(totalNanos / 1000), DateUnit.Micro);
    }

    // ------------------------------------------------------------------ class construction

    public static void Install(PyModule m)
    {
        BuildTimestamp();
        BuildTimedelta();
        BuildNaT();
        BuildOffsets(m);
        m.Dict["Timestamp"] = TimestampClass;
        m.Dict["Timedelta"] = TimedeltaClass;
        m.Dict["NaT"] = NaT;
        m.Dict["DateOffset"] = OffsetClass;
    }

    private static void BuildTimestamp()
    {
        var c = TimestampClass;
        void Def(string n, BuiltinFn f) => c.Dict[n] = Fn("Timestamp." + n, f);
        void Prop(string n, Func<Ts, object> f) => c.Dict[n] = new PyProperty { Getter = Fn(n, (_, a, _) => f(GetTs(a[0]))) };
        c.Dict["__new__"] = Fn("Timestamp.__new__", (i, a, k) =>
        {
            var pos = a.Skip(1).ToArray();
            if ((pos.Length >= 2 && pos[0] is BigInteger) || (k is not null && k.ContainsKey("year")))
                return Wrap(MakeFromFields(new Args("Timestamp", i, pos, k, "year", "month", "day", "hour", "minute", "second", "microsecond", "tzinfo", "nanosecond", "tz", "unit", "fold")));
            return Wrap(MakeTimestamp(new Args("Timestamp", i, pos, k, "ts_input", "year", "month", "day", "hour", "minute", "second", "microsecond", "tzinfo", "nanosecond", "tz", "unit", "fold")));
        });
        Def("__init__", (_, _, _) => PyNone.Instance);
        Def("__str__", (_, a, _) => GetTs(a[0]).ToString());
        Def("__repr__", (_, a, _) => $"Timestamp('{GetTs(a[0])}')");
        Def("__hash__", (_, a, _) => new BigInteger(Column.Key(GetTs(a[0]))!.GetHashCode()));
        foreach (var op in new[] { "eq", "ne", "lt", "le", "gt", "ge" })
        {
            var o2 = op;
            Def($"__{o2}__", (_, a, _) => CompareScalars(o2, a[0], a[1], out var r) ? r : PyNotImplemented.Instance);
        }
        Def("__add__", (_, a, _) => ScalarAdd(a[0], a[1], false));
        Def("__radd__", (_, a, _) => ScalarAdd(a[0], a[1], true));
        Def("__sub__", (_, a, _) => ScalarSub(a[0], a[1], false));
        Def("__rsub__", (_, a, _) => ScalarSub(a[0], a[1], true));
        Prop("year", t => new BigInteger(PartsOf(t).Year)); Prop("month", t => new BigInteger(PartsOf(t).Month)); Prop("day", t => new BigInteger(PartsOf(t).Day));
        Prop("hour", t => new BigInteger(PartsOf(t).Hour)); Prop("minute", t => new BigInteger(PartsOf(t).Minute)); Prop("second", t => new BigInteger(PartsOf(t).Second));
        Prop("microsecond", t => new BigInteger(PartsOf(t).Nanos / 1000)); Prop("nanosecond", t => new BigInteger(PartsOf(t).Nanos % 1000));
        Prop("dayofweek", t => new BigInteger(PartsOf(t).DayOfWeek)); c.Dict["day_of_week"] = c.Dict["dayofweek"];
        Prop("dayofyear", t => new BigInteger(PartsOf(t).DayOfYear)); c.Dict["day_of_year"] = c.Dict["dayofyear"];
        Prop("quarter", t => new BigInteger(PartsOf(t).Quarter));
        Prop("days_in_month", t => new BigInteger(DateTimeCore.DaysInMonth(PartsOf(t).Year, PartsOf(t).Month))); c.Dict["daysinmonth"] = c.Dict["days_in_month"];
        Prop("week", t => new BigInteger(DateTimeCore.IsoCalendar(PartsOf(t)).week)); c.Dict["weekofyear"] = c.Dict["week"];
        Prop("is_leap_year", t => DateTimeCore.IsLeap(PartsOf(t).Year));
        Prop("is_month_start", t => PartsOf(t).Day == 1);
        Prop("is_month_end", t => PartsOf(t).Day == DateTimeCore.DaysInMonth(PartsOf(t).Year, PartsOf(t).Month));
        Prop("is_quarter_start", t => PartsOf(t).Day == 1 && (PartsOf(t).Month - 1) % 3 == 0);
        Prop("is_quarter_end", t => PartsOf(t).Day == DateTimeCore.DaysInMonth(PartsOf(t).Year, PartsOf(t).Month) && PartsOf(t).Month % 3 == 0);
        Prop("is_year_start", t => PartsOf(t).Day == 1 && PartsOf(t).Month == 1);
        Prop("is_year_end", t => PartsOf(t).Day == 31 && PartsOf(t).Month == 12);
        Prop("value", t => new BigInteger((long)DateTimeCore.ToNanos(t.Ticks, t.Unit)));
        Prop("unit", t => DateTimeCore.UnitName(t.Unit));
        Prop("tz", _ => PyNone.Instance); Prop("tzinfo", _ => PyNone.Instance);
        Def("weekday", (_, a, _) => new BigInteger(PartsOf(GetTs(a[0])).DayOfWeek));
        Def("isoweekday", (_, a, _) => new BigInteger(PartsOf(GetTs(a[0])).DayOfWeek + 1));
        Def("isocalendar", (_, a, _) => { var (y, w, d) = DateTimeCore.IsoCalendar(PartsOf(GetTs(a[0]))); return new PyInstance(IsoCalendarDateClass) { Native = new[] { y, (long)w, (long)d } }; });
        Def("day_name", (_, a, _) => DateTimeCore.DayNames[PartsOf(GetTs(a[0])).DayOfWeek]);
        Def("month_name", (_, a, _) => DateTimeCore.MonthNames[PartsOf(GetTs(a[0])).Month - 1]);
        Def("strftime", (i, a, k) => { var t = GetTs(a[0]); return DateTimeCore.Strftime(t.Ticks, t.Unit, (string)A("strftime", i, a, k, "format").Required(0)); });
        Def("isoformat", (i, a, k) =>
        {
            var t = GetTs(a[0]);
            var p = A("isoformat", i, a, k, "sep", "timespec");
            string sep = p.Has(0) ? (string)p[0]! : "T";
            return DateTimeCore.FormatIso(t.Ticks, t.Unit).Replace("T", sep);
        });
        Def("date", (_, a, _) => DateTimeModule.MakeDate(ToDotNet(GetTs(a[0])).Date));
        Def("time", (_, a, _) => DateTimeModule.MakeTime(ToDotNet(GetTs(a[0])).TimeOfDay));
        Def("to_pydatetime", (_, a, _) => DateTimeModule.MakeDateTime(ToDotNet(GetTs(a[0]))));
        Def("timestamp", (_, a, _) => (double)DateTimeCore.ToNanos(GetTs(a[0]).Ticks, GetTs(a[0]).Unit) / 1e9);
        Def("normalize", (_, a, _) => { var t = GetTs(a[0]); return Wrap(new Ts(TimeSeries.NormalizeTicks(t.Ticks, t.Unit), t.Unit)); });
        foreach (var (name, fn) in new (string, Func<long, DateUnit, DateOffsetSpec.TickOffset, long>)[] { ("floor", TimeSeries.FloorTicks), ("ceil", TimeSeries.CeilTicks), ("round", TimeSeries.RoundTicks) })
        {
            var f2 = fn;
            Def(name, (i, a, k) =>
            {
                var t = GetTs(a[0]);
                var spec = TickFreq(A(name, i, a, k, "freq").Required(0));
                var u = DateTimeCore.Finer(t.Unit, spec.RequiredUnit);
                return Wrap(new Ts(f2(DateTimeCore.Scale(t.Ticks, t.Unit, u), u, spec), u));
            });
        }
        Def("replace", (i, a, k) =>
        {
            var t = GetTs(a[0]);
            var d = PartsOf(t);
            int G(string key, int dflt) => k is not null && k.TryGetValue(key, out var v) ? PdConv.ToInt(v) : dflt;
            int us = G("microsecond", d.Nanos / 1000), ns = G("nanosecond", d.Nanos % 1000);
            var u = ns != 0 || t.Unit == DateUnit.Nano ? DateUnit.Nano : t.Unit;
            return Wrap(new Ts(DateTimeCore.Compose(G("year", (int)d.Year), G("month", d.Month), G("day", d.Day), G("hour", d.Hour), G("minute", d.Minute), G("second", d.Second), us * 1000L + ns, u), u));
        });
        Def("as_unit", (i, a, k) => { var t = GetTs(a[0]); var u = UnitNames[(string)A("as_unit", i, a, k, "unit").Required(0)]; return Wrap(new Ts(DateTimeCore.Scale(t.Ticks, t.Unit, u), u)); });
        Def("to_datetime64", (_, a, _) => throw PyErr.NotImplementedError("numpy datetime64 scalars are not supported"));
        c.Dict["now"] = Fn("Timestamp.now", (_, _, _) => Wrap(FromDotNet(DateTime.Now)));
        c.Dict["today"] = c.Dict["now"];
        c.Dict["utcnow"] = Fn("Timestamp.utcnow", (_, _, _) => Wrap(FromDotNet(DateTime.UtcNow)));
        c.Dict["fromtimestamp"] = Fn("Timestamp.fromtimestamp", (_, a, _) => Wrap(FromEpoch(Column.ToDouble(PdConv.ToCell(a[a.Length - 1])!), "s")));
        c.Dict["fromisoformat"] = Fn("Timestamp.fromisoformat", (_, a, _) => TryTs(a[a.Length - 1], out var t) ? Wrap(t) : throw PyErr.ValueError("Invalid isoformat string"));
    }

    public static Ts FromDotNet(DateTime dt)
    {
        long ticks = (dt - DateTime.UnixEpoch).Ticks;
        return new Ts(ticks / 10, DateUnit.Micro);
    }

    public static DateOffsetSpec.TickOffset TickFreq(object freq)
    {
        if (freq is string s) return DateOffsetSpec.Parse(s) as DateOffsetSpec.TickOffset ?? throw PyErr.ValueError($"<{s}> is a non-fixed frequency");
        if (freq is PyInstance { Native: DateOffsetSpec { IsTick: true } t }) return (DateOffsetSpec.TickOffset)t;
        if (TryTd(freq, out var td)) return DateOffsetSpec.FromNanos((long)DateTimeCore.ToNanos(td.Ticks, td.Unit));
        throw PyErr.ValueError("Invalid frequency");
    }

    private static void BuildTimedelta()
    {
        var c = TimedeltaClass;
        void Def(string n, BuiltinFn f) => c.Dict[n] = Fn("Timedelta." + n, f);
        void Prop(string n, Func<Td, object> f) => c.Dict[n] = new PyProperty { Getter = Fn(n, (_, a, _) => f(GetTd(a[0]))) };
        c.Dict["__new__"] = Fn("Timedelta.__new__", (i, a, k) =>
        {
            var p = new Args("Timedelta", i, a.Skip(1).ToArray(), k?.Where(e => e.Key is "value" or "unit").ToDictionary(e => e.Key, e => e.Value), "value", "unit");
            var rest = k?.Where(e => e.Key is not ("value" or "unit")).ToDictionary(e => e.Key, e => e.Value);
            return Wrap(MakeTimedelta(p, rest));
        });
        Def("__init__", (_, _, _) => PyNone.Instance);
        Def("__str__", (_, a, _) => GetTd(a[0]).ToString());
        Def("__repr__", (_, a, _) => $"Timedelta('{GetTd(a[0])}')");
        Def("__hash__", (_, a, _) => new BigInteger(Column.Key(GetTd(a[0]))!.GetHashCode()));
        foreach (var op in new[] { "eq", "ne", "lt", "le", "gt", "ge" })
        {
            var o2 = op;
            Def($"__{o2}__", (_, a, _) => CompareScalars(o2, a[0], a[1], out var r) ? r : PyNotImplemented.Instance);
        }
        Def("__add__", (_, a, _) => ScalarAdd(a[0], a[1], false));
        Def("__radd__", (_, a, _) => ScalarAdd(a[0], a[1], true));
        Def("__sub__", (_, a, _) => ScalarSub(a[0], a[1], false));
        Def("__rsub__", (_, a, _) => ScalarSub(a[0], a[1], true));
        Def("__neg__", (_, a, _) => { var t = GetTd(a[0]); return Wrap(new Td(t.Ticks == DateTimeCore.NaT ? t.Ticks : -t.Ticks, t.Unit)); });
        Def("__pos__", (_, a, _) => a[0]);
        Def("__abs__", (_, a, _) => { var t = GetTd(a[0]); return Wrap(new Td(t.Ticks == DateTimeCore.NaT ? t.Ticks : Math.Abs(t.Ticks), t.Unit)); });
        c.Dict["abs"] = c.Dict["__abs__"];
        Def("__bool__", (_, a, _) => GetTd(a[0]).Ticks != 0);
        BuiltinFn mul = (_, a, _) =>
        {
            var t = GetTd(a[0]);
            if (a[1] is BigInteger or double or bool) return Wrap(new Td((long)(t.Ticks * Column.ToDouble(PdConv.ToCell(a[1])!)), t.Unit));
            return PyNotImplemented.Instance;
        };
        Def("__mul__", mul); Def("__rmul__", mul);
        Def("__truediv__", (_, a, _) =>
        {
            var t = GetTd(a[0]);
            if (a[1] is BigInteger or double)
            {
                double dv = Column.ToDouble(PdConv.ToCell(a[1])!);
                if (dv == 0) throw PyErr.ZeroDivisionError("division by zero");
                return Wrap(new Td((long)(t.Ticks / dv), t.Unit));
            }
            if (TryTd(a[1] is string ? null : a[1], out var o)) return (double)DateTimeCore.ToNanos(t.Ticks, t.Unit) / (double)DateTimeCore.ToNanos(o.Ticks, o.Unit);
            return PyNotImplemented.Instance;
        });
        Def("__rtruediv__", (_, a, _) =>
        {
            var t = GetTd(a[0]);
            if (TryTd(a[1] is string ? null : a[1], out var o)) return (double)DateTimeCore.ToNanos(o.Ticks, o.Unit) / (double)DateTimeCore.ToNanos(t.Ticks, t.Unit);
            return PyNotImplemented.Instance;
        });
        Def("__floordiv__", (_, a, _) =>
        {
            var t = GetTd(a[0]);
            if (a[1] is BigInteger b) return Wrap(new Td(DateTimeCore.FloorDiv(t.Ticks, (long)b), t.Unit));
            if (TryTd(a[1] is string ? null : a[1], out var o)) return new BigInteger((long)Int128.CreateTruncating(Floor(DateTimeCore.ToNanos(t.Ticks, t.Unit), DateTimeCore.ToNanos(o.Ticks, o.Unit))));
            return PyNotImplemented.Instance;
        });
        Def("__mod__", (_, a, _) =>
        {
            var t = GetTd(a[0]);
            if (TryTd(a[1] is string ? null : a[1], out var o))
            {
                var u = DateTimeCore.Finer(t.Unit, o.Unit);
                return Wrap(new Td(DateTimeCore.FloorMod(DateTimeCore.Scale(t.Ticks, t.Unit, u), DateTimeCore.Scale(o.Ticks, o.Unit, u)), u));
            }
            return PyNotImplemented.Instance;
        });
        long Per(Td t) => DateTimeCore.PerSecond(t.Unit);
        Prop("days", t => new BigInteger(DateTimeCore.FloorDiv(t.Ticks, 86400 * Per(t))));
        Prop("seconds", t => new BigInteger(DateTimeCore.FloorMod(DateTimeCore.FloorDiv(t.Ticks, Per(t)), 86400)));
        Prop("microseconds", t => new BigInteger(DateTimeCore.FloorMod(t.Ticks * DateTimeCore.NanosPerTick(t.Unit) / 1000, 1_000_000)));
        Prop("nanoseconds", t => new BigInteger(DateTimeCore.FloorMod(t.Ticks * DateTimeCore.NanosPerTick(t.Unit), 1000)));
        Prop("value", t => new BigInteger((long)DateTimeCore.ToNanos(t.Ticks, t.Unit)));
        Prop("unit", t => DateTimeCore.UnitName(t.Unit));
        Def("total_seconds", (_, a, _) => { var t = GetTd(a[0]); return (double)DateTimeCore.ToNanos(t.Ticks, t.Unit) / 1e9; });
        Def("to_pytimedelta", (_, a, _) => { var t = GetTd(a[0]); return DateTimeModule.MakeTimeDelta(TimeSpan.FromTicks((long)(DateTimeCore.ToNanos(t.Ticks, t.Unit) / 100))); });
        Def("isoformat", (_, a, _) =>
        {
            var t = GetTd(a[0]);
            long totalSecs = DateTimeCore.FloorDiv(t.Ticks, Per(t));
            long days = DateTimeCore.FloorDiv(totalSecs, 86400), sod = DateTimeCore.FloorMod(totalSecs, 86400);
            return $"P{days}DT{sod / 3600}H{sod % 3600 / 60}M{sod % 60}S";
        });
        Def("as_unit", (i, a, k) => { var t = GetTd(a[0]); var u = UnitNames[(string)A("as_unit", i, a, k, "unit").Required(0)]; return Wrap(new Td(DateTimeCore.Scale(t.Ticks, t.Unit, u), u)); });
        foreach (var (name, fn) in new (string, Func<long, DateUnit, DateOffsetSpec.TickOffset, long>)[] { ("floor", TimeSeries.FloorTicks), ("ceil", TimeSeries.CeilTicks), ("round", TimeSeries.RoundTicks) })
        {
            var f2 = fn;
            Def(name, (i, a, k) =>
            {
                var t = GetTd(a[0]);
                var spec = TickFreq(A(name, i, a, k, "freq").Required(0));
                var u = DateTimeCore.Finer(t.Unit, spec.RequiredUnit);
                return Wrap(new Td(f2(DateTimeCore.Scale(t.Ticks, t.Unit, u), u, spec), u));
            });
        }
    }

    private static Int128 Floor(Int128 a, Int128 b)
    {
        var q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q -= 1;
        return q;
    }

    private static void BuildNaT()
    {
        var c = NaTClass;
        void Def(string n, BuiltinFn f) => c.Dict[n] = Fn("NaT." + n, f);
        Def("__str__", (_, _, _) => "NaT");
        Def("__repr__", (_, _, _) => "NaT");
        Def("__hash__", (_, _, _) => new BigInteger(-9223372036854775808L));
        Def("__bool__", (_, _, _) => true);
        Def("__eq__", (_, _, _) => false);
        Def("__ne__", (_, _, _) => true);
        foreach (var n in new[] { "__lt__", "__le__", "__gt__", "__ge__" }) Def(n, (_, _, _) => false);
        foreach (var n in new[] { "__add__", "__radd__", "__sub__", "__rsub__" }) Def(n, (_, a, _) => a[1] is BigInteger or double or string ? PyNotImplemented.Instance : NaT);
        Def("__mul__", (_, _, _) => NaT);
        Def("__truediv__", (_, _, _) => double.NaN);
        c.Dict["year"] = new PyProperty { Getter = Fn("year", (_, _, _) => double.NaN) };
        foreach (var n in new[] { "month", "day", "hour", "minute", "second" }) c.Dict[n] = c.Dict["year"];
        Def("strftime", (_, _, _) => "NaT");
        Def("isoformat", (_, _, _) => "NaT");
    }

    // ------------------------------------------------------------------ offsets

    private static void BuildOffsets(PyModule m)
    {
        var c = OffsetClass;
        void Def(string n, BuiltinFn f) => c.Dict[n] = Fn("DateOffset." + n, f);
        DateOffsetSpec Spec(object o) => (DateOffsetSpec)((PyInstance)o).Native!;
        c.Dict["__new__"] = Fn("DateOffset.__new__", (i, a, k) => WrapOffset(MakeRelative(k)));
        Def("__init__", (_, _, _) => PyNone.Instance);
        Def("__repr__", (_, a, _) => OffsetRepr(Spec(a[0])));
        Def("__str__", (_, a, _) => OffsetRepr(Spec(a[0])));
        Def("__hash__", (_, a, _) => new BigInteger(Spec(a[0]).FreqString.GetHashCode()));
        Def("__eq__", (_, a, _) => a[1] is PyInstance { Native: DateOffsetSpec o } ? Spec(a[0]).FreqString == o.FreqString : false);
        c.Dict["n"] = new PyProperty { Getter = Fn("n", (_, a, _) => new BigInteger(Spec(a[0]).N)) };
        c.Dict["freqstr"] = new PyProperty { Getter = Fn("freqstr", (_, a, _) => OffsetText(Spec(a[0]))) };
        c.Dict["name"] = c.Dict["freqstr"];
        Def("__add__", (_, a, _) => OffsetAdd(Spec(a[0]), a[1]));
        Def("__radd__", (_, a, _) => OffsetAdd(Spec(a[0]), a[1]));
        Def("__sub__", (_, a, _) => OffsetAdd(Spec(a[0]).WithN(-Spec(a[0]).N), a[1]));
        Def("__neg__", (_, a, _) => WrapOffset(Spec(a[0]).WithN(-Spec(a[0]).N)));
        Def("__mul__", (_, a, _) => a[1] is BigInteger b ? WrapOffset(Spec(a[0]).WithN(Spec(a[0]).N * (int)b)) : PyNotImplemented.Instance);
        c.Dict["__rmul__"] = c.Dict["__mul__"];
        BuiltinFn Roll(Func<DateOffsetSpec, long, DateUnit, long> f) => (i, a, k) =>
        {
            var spec = Spec(a[0]);
            if (!TryTs(a[1], out var t)) throw PyErr.TypeError("expected a Timestamp");
            var u = DateTimeCore.Finer(t.Unit, spec.RequiredUnit);
            return Wrap(new Ts(f(spec, DateTimeCore.Scale(t.Ticks, t.Unit, u), u), u));
        };
        Def("rollforward", Roll((s, t, u) => s.RollForward(t, u)));
        Def("rollback", Roll((s, t, u) => s.RollBack(t, u)));
        Def("is_on_offset", (_, a, _) => TryTs(a[1], out var t) && Spec(a[0]).OnOffset(t.Ticks, t.Unit));

        // concrete offsets: classes sharing the dunders
        var offsets = new PyModule("pandas.offsets");
        PyClass Make(string name, Func<Dictionary<string, object>?, object[], DateOffsetSpec> make)
        {
            var cls = new PyClass(name, new List<PyClass> { OffsetClass });
            cls.Dict["__new__"] = Fn(name + ".__new__", (i, a, k) => WrapOffset(make(k, a.Skip(1).ToArray())));
            return cls;
        }
        int N(Dictionary<string, object>? k, object[] a) => a.Length > 0 && a[0] is BigInteger b ? (int)b : k is not null && k.TryGetValue("n", out var v) ? PdConv.ToInt(v) : 1;
        offsets.Dict["Day"] = Make("Day", (k, a) => new DateOffsetSpec.TickOffset(N(k, a), 86400_000_000_000, "D"));
        offsets.Dict["Hour"] = Make("Hour", (k, a) => new DateOffsetSpec.TickOffset(N(k, a), 3600_000_000_000, "h"));
        offsets.Dict["Minute"] = Make("Minute", (k, a) => new DateOffsetSpec.TickOffset(N(k, a), 60_000_000_000, "min"));
        offsets.Dict["Second"] = Make("Second", (k, a) => new DateOffsetSpec.TickOffset(N(k, a), 1_000_000_000, "s"));
        offsets.Dict["Milli"] = Make("Milli", (k, a) => new DateOffsetSpec.TickOffset(N(k, a), 1_000_000, "ms"));
        offsets.Dict["Micro"] = Make("Micro", (k, a) => new DateOffsetSpec.TickOffset(N(k, a), 1_000, "us"));
        offsets.Dict["Nano"] = Make("Nano", (k, a) => new DateOffsetSpec.TickOffset(N(k, a), 1, "ns"));
        offsets.Dict["Week"] = Make("Week", (k, a) => new DateOffsetSpec.WeekOffset(N(k, a), k is not null && k.TryGetValue("weekday", out var w) ? PdConv.ToInt(w) : 6));
        offsets.Dict["MonthEnd"] = Make("MonthEnd", (k, a) => DateOffsetSpec.Parse((N(k, a) == 1 ? "" : N(k, a).ToString()) + "ME"));
        offsets.Dict["MonthBegin"] = Make("MonthBegin", (k, a) => DateOffsetSpec.Parse((N(k, a) == 1 ? "" : N(k, a).ToString()) + "MS"));
        offsets.Dict["QuarterEnd"] = Make("QuarterEnd", (k, a) => DateOffsetSpec.Parse((N(k, a) == 1 ? "" : N(k, a).ToString()) + "QE"));
        offsets.Dict["QuarterBegin"] = Make("QuarterBegin", (k, a) => DateOffsetSpec.Parse((N(k, a) == 1 ? "" : N(k, a).ToString()) + "QS"));
        offsets.Dict["YearEnd"] = Make("YearEnd", (k, a) => DateOffsetSpec.Parse((N(k, a) == 1 ? "" : N(k, a).ToString()) + "YE"));
        offsets.Dict["YearBegin"] = Make("YearBegin", (k, a) => DateOffsetSpec.Parse((N(k, a) == 1 ? "" : N(k, a).ToString()) + "YS"));
        offsets.Dict["BDay"] = Make("BDay", (k, a) => new DateOffsetSpec.BusinessDay(N(k, a)));
        offsets.Dict["BusinessDay"] = offsets.Dict["BDay"];
        offsets.Dict["DateOffset"] = OffsetClass;
        m.Dict["offsets"] = offsets;
    }

    private static DateOffsetSpec MakeRelative(Dictionary<string, object>? k)
    {
        int n = k is not null && k.TryGetValue("n", out var nv) ? PdConv.ToInt(nv) : 1;
        int Get(string key) => k is not null && k.TryGetValue(key, out var v) ? PdConv.ToInt(v) : 0;
        long extra = (long)Get("weeks") * 7 * 86400_000_000_000 + (long)Get("days") * 86400_000_000_000 + (long)Get("hours") * 3600_000_000_000 + (long)Get("minutes") * 60_000_000_000
                     + (long)Get("seconds") * 1_000_000_000 + (long)Get("milliseconds") * 1_000_000 + (long)Get("microseconds") * 1000 + Get("nanoseconds");
        var parts = new List<string>();
        foreach (var key in new[] { "days", "hours", "microseconds", "milliseconds", "minutes", "months", "nanoseconds", "seconds", "weeks", "years" })
            if (Get(key) != 0) parts.Add($"{key}={Get(key)}");
        return new DateOffsetSpec.Relative(n, Get("years"), Get("months"), extra, string.Join(", ", parts));
    }

    private static object OffsetAdd(DateOffsetSpec spec, object other)
    {
        if (TryTs(other is string ? null : other, out var t) && other is not Td)
        {
            if (t.Ticks == DateTimeCore.NaT) return NaT;
            var u = DateTimeCore.Finer(t.Unit, spec.RequiredUnit);
            return Wrap(new Ts(spec.Add(DateTimeCore.Scale(t.Ticks, t.Unit, u), u), u));
        }
        if (other is PyInstance { Native: DateOffsetSpec o2 } && spec is DateOffsetSpec.TickOffset a && o2 is DateOffsetSpec.TickOffset b && a.Nanos == b.Nanos && a.Alias == b.Alias)
            return WrapOffset(a.WithN(a.N + b.N));
        return PyNotImplemented.Instance;
    }

    // ------------------------------------------------------------------ vectorised offsets (Series + DateOffset)

    public static Column ApplyOffset(Column c, DateOffsetSpec spec)
    {
        if (c.Kind != Kind.DateTime) throw PyErr.TypeError($"cannot add DateOffset to a column of dtype {c.DTypeName}");
        var u = DateTimeCore.Finer(c.Unit, spec.RequiredUnit);
        var scaled = c.WithUnit(u);
        return Column.FromDateTime(scaled.Ticks.Select(t => t == DateTimeCore.NaT ? t : spec.Add(t, u)).ToArray(), u);
    }
}
