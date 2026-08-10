// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// datetime: date/time/datetime/timedelta/timezone, backed by .NET's DateOnly/TimeOnly/DateTime/
/// TimeSpan. Arithmetic/comparison dunders ride the interpreter's existing generic PyInstance
/// dunder dispatch, same approach as decimal.Decimal/complex. v1 scope: the common surface
/// (construction, arithmetic, comparison, isoformat/strftime, now/today/utcnow, replace) — not
/// full API parity (no full strptime format-code coverage, no fold/full tzinfo machinery beyond
/// timezone.utc). See FASTAPI_PLAN.md Phase 1.9 (originally flagged as item 1.5 at the very start
/// of this plan).
/// </summary>
public static class DateTimeModule
{
    private const string ValueKey = "__value__";

    /// <summary>Real CPython `date`/`time`/`datetime` accept year/month/day/hour/... either
    /// positionally or by keyword (`datetime(year=2024, month=1, day=15, ...)`) — found via real
    /// pydantic v1's own `datetime_parse.py` (`parse_date`/`parse_time`/`parse_datetime`), which
    /// constructs every one of these exclusively via `**kwargs` after regex-parsing an ISO string.
    /// The previous positional-args-only reading meant any real ISO-datetime-string field silently
    /// hit "missing required argument" for every real pydantic model using one — not an edge case.</summary>
    private static int RequiredArg(object[] a, Dictionary<string, object>? kwargs, int index, string name)
    {
        if (a.Length > index)
            return Int(a[index]);
        if (kwargs is not null && kwargs.TryGetValue(name, out var v))
            return Int(v);
        throw PyErr.TypeError($"function missing required argument '{name}' (pos {index})");
    }

    private static int OptionalArg(object[] a, Dictionary<string, object>? kwargs, int index, string name, int def)
    {
        if (a.Length > index)
            return Int(a[index]);
        if (kwargs is not null && kwargs.TryGetValue(name, out var v))
            return Int(v);
        return def;
    }

    public static readonly PyClass TimeDeltaClass = BuildTimeDeltaClass();
    public static readonly PyClass DateClass = BuildDateClass();
    public static readonly PyClass TimeClass = BuildTimeClass();
    public static readonly PyClass DateTimeClass = BuildDateTimeClass();
    public static readonly PyClass TimeZoneClass = BuildTimeZoneClass();

    public static PyModule Create()
    {
        var m = new PyModule("datetime");
        m.Dict["timedelta"] = TimeDeltaClass;
        m.Dict["date"] = DateClass;
        m.Dict["time"] = TimeClass;
        m.Dict["datetime"] = DateTimeClass;
        m.Dict["timezone"] = TimeZoneClass;
        m.Dict["MINYEAR"] = new BigInteger(1);
        m.Dict["MAXYEAR"] = new BigInteger(9999);
        return m;
    }

    // ------------------------------------------------------------------ shared helpers

    private static object Field(object self, string key) => ((PyInstance)self).Dict[key];
    private static int Int(object o) => (int)PyOps.AsBigInt(o, "value");

    private static string Strftime(DateTime dt, string fmt)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] != '%' || i + 1 >= fmt.Length)
            {
                sb.Append(fmt[i]);
                continue;
            }
            i++;
            sb.Append(fmt[i] switch
            {
                'Y' => dt.Year.ToString("D4", CultureInfo.InvariantCulture),
                'y' => (dt.Year % 100).ToString("D2", CultureInfo.InvariantCulture),
                'm' => dt.Month.ToString("D2", CultureInfo.InvariantCulture),
                'd' => dt.Day.ToString("D2", CultureInfo.InvariantCulture),
                'H' => dt.Hour.ToString("D2", CultureInfo.InvariantCulture),
                'I' => (((dt.Hour + 11) % 12) + 1).ToString("D2", CultureInfo.InvariantCulture),
                'M' => dt.Minute.ToString("D2", CultureInfo.InvariantCulture),
                'S' => dt.Second.ToString("D2", CultureInfo.InvariantCulture),
                'f' => (dt.Ticks % TimeSpan.TicksPerSecond / 10).ToString("D6", CultureInfo.InvariantCulture),
                'p' => dt.Hour < 12 ? "AM" : "PM",
                'A' => dt.ToString("dddd", CultureInfo.InvariantCulture),
                'a' => dt.ToString("ddd", CultureInfo.InvariantCulture),
                'B' => dt.ToString("MMMM", CultureInfo.InvariantCulture),
                'b' => dt.ToString("MMM", CultureInfo.InvariantCulture),
                'j' => dt.DayOfYear.ToString("D3", CultureInfo.InvariantCulture),
                'w' => ((int)dt.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                'Z' => "UTC",
                'z' => "+0000",
                '%' => "%",
                _ => "%" + fmt[i],
            });
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ timedelta

    private static PyClass BuildTimeDeltaClass()
    {
        var cls = new PyClass("timedelta", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"timedelta.{name}", fn);

        double KwDouble(Dictionary<string, object>? kwargs, string name) =>
            kwargs is not null && kwargs.TryGetValue(name, out var v) ? PyOps.AsDouble(v) : 0.0;

        Add("__init__", (_, a, kwargs) =>
        {
            double days = a.Length > 1 ? PyOps.AsDouble(a[1]) : KwDouble(kwargs, "days");
            double seconds = a.Length > 2 ? PyOps.AsDouble(a[2]) : KwDouble(kwargs, "seconds");
            double microseconds = a.Length > 3 ? PyOps.AsDouble(a[3]) : KwDouble(kwargs, "microseconds");
            double milliseconds = a.Length > 4 ? PyOps.AsDouble(a[4]) : KwDouble(kwargs, "milliseconds");
            double minutes = a.Length > 5 ? PyOps.AsDouble(a[5]) : KwDouble(kwargs, "minutes");
            double hours = a.Length > 6 ? PyOps.AsDouble(a[6]) : KwDouble(kwargs, "hours");
            double weeks = a.Length > 7 ? PyOps.AsDouble(a[7]) : KwDouble(kwargs, "weeks");

            double totalDays = days + weeks * 7;
            double totalSeconds = seconds + minutes * 60 + hours * 3600;
            double totalMicros = microseconds + milliseconds * 1000;
            long ticks = (long)(totalDays * TimeSpan.TicksPerDay)
                         + (long)(totalSeconds * TimeSpan.TicksPerSecond)
                         + (long)(totalMicros * 10);
            ((PyInstance)a[0]).Dict[ValueKey] = new TimeSpan(ticks);
            return PyNone.Instance;
        });

        TimeSpan Value(object self) => (TimeSpan)Field(self, ValueKey);

        cls.Dict["days"] = new PyProperty { Getter = new PyBuiltinFunction("timedelta.days", (_, a, _) => new BigInteger(Value(a[0]).Days)) };
        cls.Dict["seconds"] = new PyProperty { Getter = new PyBuiltinFunction("timedelta.seconds", (_, a, _) =>
        {
            var ts = Value(a[0]);
            long secOfDay = ((ts.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay / TimeSpan.TicksPerSecond;
            return new BigInteger(secOfDay);
        }) };
        cls.Dict["microseconds"] = new PyProperty { Getter = new PyBuiltinFunction("timedelta.microseconds", (_, a, _) =>
        {
            var ts = Value(a[0]);
            long microOfSec = ((ts.Ticks % TimeSpan.TicksPerSecond) + TimeSpan.TicksPerSecond) % TimeSpan.TicksPerSecond / 10;
            return new BigInteger(microOfSec);
        }) };

        Add("total_seconds", (_, a, _) => Value(a[0]).TotalSeconds);

        Add("__add__", (_, a, _) => a[1] is PyInstance i && i.Class == TimeDeltaClass
            ? MakeTimeDelta(Value(a[0]) + Value(a[1])) : (object)PyNotImplemented.Instance);
        Add("__sub__", (_, a, _) => a[1] is PyInstance i && i.Class == TimeDeltaClass
            ? MakeTimeDelta(Value(a[0]) - Value(a[1])) : (object)PyNotImplemented.Instance);
        Add("__neg__", (_, a, _) => MakeTimeDelta(-Value(a[0])));
        Add("__mul__", (_, a, _) => MakeTimeDelta(Value(a[0]) * PyOps.AsDouble(a[1])));
        Add("__rmul__", (_, a, _) => MakeTimeDelta(Value(a[0]) * PyOps.AsDouble(a[1])));
        Add("__truediv__", (_, a, _) => a[1] is PyInstance i && i.Class == TimeDeltaClass
            ? (object)(Value(a[0]).Ticks / (double)Value(a[1]).Ticks)
            : MakeTimeDelta(Value(a[0]) / PyOps.AsDouble(a[1])));
        Add("__eq__", (_, a, _) => a[1] is PyInstance i && i.Class == TimeDeltaClass && Value(a[0]) == Value(a[1]));
        Add("__lt__", (_, a, _) => Value(a[0]) < Value((PyInstance)a[1]));
        Add("__le__", (_, a, _) => Value(a[0]) <= Value((PyInstance)a[1]));
        Add("__gt__", (_, a, _) => Value(a[0]) > Value((PyInstance)a[1]));
        Add("__ge__", (_, a, _) => Value(a[0]) >= Value((PyInstance)a[1]));
        Add("__bool__", (_, a, _) => Value(a[0]) != TimeSpan.Zero);
        Add("__str__", (_, a, _) =>
        {
            // Matches CPython's exact format: "[-][D day[s], ]H:MM:SS[.ffffff]" — hours are NOT
            // zero-padded (real Python: "0:00:00", not "00:00:00"), minutes/seconds are.
            var ts = Value(a[0]);
            var dayPart = ts.Days != 0 ? $"{ts.Days} day{(Math.Abs(ts.Days) == 1 ? "" : "s")}, " : "";
            long microseconds = Math.Abs(ts.Ticks % TimeSpan.TicksPerSecond) / 10;
            var fraction = microseconds != 0 ? $".{microseconds:D6}" : "";
            return $"{dayPart}{Math.Abs(ts.Hours)}:{Math.Abs(ts.Minutes):D2}:{Math.Abs(ts.Seconds):D2}{fraction}";
        });
        Add("__repr__", (_, a, _) =>
        {
            var ts = Value(a[0]);
            return $"datetime.timedelta(days={ts.Days}, seconds={((ts.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay / TimeSpan.TicksPerSecond})";
        });

        return cls;
    }

    public static PyInstance MakeTimeDelta(TimeSpan ts)
    {
        var inst = new PyInstance(TimeDeltaClass);
        inst.Dict[ValueKey] = ts;
        return inst;
    }

    // ------------------------------------------------------------------ date

    private static PyClass BuildDateClass()
    {
        var cls = new PyClass("date", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"date.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            int year = RequiredArg(a, kwargs, 1, "year");
            int month = RequiredArg(a, kwargs, 2, "month");
            int day = RequiredArg(a, kwargs, 3, "day");
            ((PyInstance)a[0]).Dict[ValueKey] = new DateTime(year, month, day);
            return PyNone.Instance;
        });

        DateTime Value(object self) => (DateTime)Field(self, ValueKey);

        cls.Dict["year"] = new PyProperty { Getter = new PyBuiltinFunction("date.year", (_, a, _) => new BigInteger(Value(a[0]).Year)) };
        cls.Dict["month"] = new PyProperty { Getter = new PyBuiltinFunction("date.month", (_, a, _) => new BigInteger(Value(a[0]).Month)) };
        cls.Dict["day"] = new PyProperty { Getter = new PyBuiltinFunction("date.day", (_, a, _) => new BigInteger(Value(a[0]).Day)) };

        Add("isoformat", (_, a, _) => Value(a[0]).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("__str__", (_, a, _) => Value(a[0]).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("__repr__", (_, a, _) => $"datetime.date({Value(a[0]).Year}, {Value(a[0]).Month}, {Value(a[0]).Day})");
        Add("strftime", (_, a, _) => Strftime(Value(a[0]), (string)a[1]));
        Add("weekday", (_, a, _) => new BigInteger(((int)Value(a[0]).DayOfWeek + 6) % 7));
        Add("isoweekday", (_, a, _) => new BigInteger(((int)Value(a[0]).DayOfWeek + 6) % 7 + 1));
        Add("today", (_, _, _) => MakeDate(DateTime.Today));
        Add("replace", (_, a, kwargs) =>
        {
            var dt = Value(a[0]);
            int year = kwargs is not null && kwargs.TryGetValue("year", out var y) ? Int(y) : dt.Year;
            int month = kwargs is not null && kwargs.TryGetValue("month", out var mo) ? Int(mo) : dt.Month;
            int day = kwargs is not null && kwargs.TryGetValue("day", out var d) ? Int(d) : dt.Day;
            return MakeDate(new DateTime(year, month, day));
        });

        Add("__add__", (_, a, _) => a[1] is PyInstance i && i.Class == TimeDeltaClass
            ? MakeDate(Value(a[0]) + (TimeSpan)i.Dict[ValueKey]) : (object)PyNotImplemented.Instance);
        Add("__sub__", (_, a, _) => a[1] switch
        {
            PyInstance i when i.Class == TimeDeltaClass => MakeDate(Value(a[0]) - (TimeSpan)i.Dict[ValueKey]),
            PyInstance i when i.Class == DateClass => (object)MakeTimeDelta(Value(a[0]) - Value(i)),
            _ => PyNotImplemented.Instance,
        });
        Add("__eq__", (_, a, _) => a[1] is PyInstance i && i.Class == DateClass && Value(a[0]) == Value(a[1]));
        Add("__lt__", (_, a, _) => Value(a[0]) < Value((PyInstance)a[1]));
        Add("__le__", (_, a, _) => Value(a[0]) <= Value((PyInstance)a[1]));
        Add("__gt__", (_, a, _) => Value(a[0]) > Value((PyInstance)a[1]));
        Add("__ge__", (_, a, _) => Value(a[0]) >= Value((PyInstance)a[1]));

        // Not MakeDate(...): that references the DateClass *static field*, which is still being
        // assigned while this method runs (BuildDateClass() is DateClass's own initializer) — use
        // the local `cls` instead, or these come out as instances of a null class.
        var min = new PyInstance(cls);
        min.Dict[ValueKey] = DateTime.MinValue;
        cls.Dict["min"] = min;
        var max = new PyInstance(cls);
        max.Dict[ValueKey] = DateTime.MaxValue.Date;
        cls.Dict["max"] = max;

        return cls;
    }

    public static PyInstance MakeDate(DateTime dt)
    {
        var inst = new PyInstance(DateClass);
        inst.Dict[ValueKey] = dt.Date;
        return inst;
    }

    // ------------------------------------------------------------------ time (of day)

    private static PyClass BuildTimeClass()
    {
        var cls = new PyClass("time", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"time.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            int hour = OptionalArg(a, kwargs, 1, "hour", 0);
            int minute = OptionalArg(a, kwargs, 2, "minute", 0);
            int second = OptionalArg(a, kwargs, 3, "second", 0);
            int micro = OptionalArg(a, kwargs, 4, "microsecond", 0);
            ((PyInstance)a[0]).Dict[ValueKey] = new TimeSpan(0, hour, minute, second, micro / 1000, micro % 1000 * 10 / 10);
            return PyNone.Instance;
        });

        TimeSpan Value(object self) => (TimeSpan)Field(self, ValueKey);

        cls.Dict["hour"] = new PyProperty { Getter = new PyBuiltinFunction("time.hour", (_, a, _) => new BigInteger(Value(a[0]).Hours)) };
        cls.Dict["minute"] = new PyProperty { Getter = new PyBuiltinFunction("time.minute", (_, a, _) => new BigInteger(Value(a[0]).Minutes)) };
        cls.Dict["second"] = new PyProperty { Getter = new PyBuiltinFunction("time.second", (_, a, _) => new BigInteger(Value(a[0]).Seconds)) };
        // Real CPython: microsecond is full sub-second precision (0-999999), not just the
        // millisecond component — `Milliseconds * 1000` silently dropped the last 3 digits (e.g.
        // 123456 read back as 123000). Ticks-based, matching datetime.microsecond's own (already
        // correct) formula below.
        cls.Dict["microsecond"] = new PyProperty { Getter = new PyBuiltinFunction("time.microsecond", (_, a, _) => new BigInteger(Value(a[0]).Ticks % TimeSpan.TicksPerSecond / 10)) };

        Add("isoformat", (_, a, _) => Value(a[0]).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        Add("__str__", (_, a, _) => Value(a[0]).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        Add("__repr__", (_, a, _) => $"datetime.time({Value(a[0]).Hours}, {Value(a[0]).Minutes}, {Value(a[0]).Seconds})");
        Add("strftime", (_, a, _) => Strftime(DateTime.MinValue + Value(a[0]), (string)a[1]));
        Add("__eq__", (_, a, _) => a[1] is PyInstance i && i.Class == TimeClass && Value(a[0]) == Value(a[1]));
        Add("__lt__", (_, a, _) => Value(a[0]) < Value((PyInstance)a[1]));
        Add("__le__", (_, a, _) => Value(a[0]) <= Value((PyInstance)a[1]));
        Add("__gt__", (_, a, _) => Value(a[0]) > Value((PyInstance)a[1]));
        Add("__ge__", (_, a, _) => Value(a[0]) >= Value((PyInstance)a[1]));

        return cls;
    }

    public static PyInstance MakeTime(TimeSpan ts)
    {
        var inst = new PyInstance(TimeClass);
        inst.Dict[ValueKey] = ts;
        return inst;
    }

    // ------------------------------------------------------------------ datetime

    private static PyClass BuildDateTimeClass()
    {
        var cls = new PyClass("datetime", new List<PyClass> { DateClass });
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"datetime.{name}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            int year = RequiredArg(a, kwargs, 1, "year");
            int month = RequiredArg(a, kwargs, 2, "month");
            int day = RequiredArg(a, kwargs, 3, "day");
            int hour = OptionalArg(a, kwargs, 4, "hour", 0);
            int minute = OptionalArg(a, kwargs, 5, "minute", 0);
            int second = OptionalArg(a, kwargs, 6, "second", 0);
            int micro = OptionalArg(a, kwargs, 7, "microsecond", 0);
            var inst = (PyInstance)a[0];
            inst.Dict[ValueKey] = new DateTime(year, month, day, hour, minute, second, micro / 1000, DateTimeKind.Unspecified)
                .AddTicks(micro % 1000 * 10);
            inst.Dict["tzinfo"] = kwargs is not null && kwargs.TryGetValue("tzinfo", out var tz) ? tz : PyNone.Instance;
            return PyNone.Instance;
        });

        DateTime Value(object self) => (DateTime)Field(self, ValueKey);

        cls.Dict["year"] = new PyProperty { Getter = new PyBuiltinFunction("datetime.year", (_, a, _) => new BigInteger(Value(a[0]).Year)) };
        cls.Dict["month"] = new PyProperty { Getter = new PyBuiltinFunction("datetime.month", (_, a, _) => new BigInteger(Value(a[0]).Month)) };
        cls.Dict["day"] = new PyProperty { Getter = new PyBuiltinFunction("datetime.day", (_, a, _) => new BigInteger(Value(a[0]).Day)) };
        cls.Dict["hour"] = new PyProperty { Getter = new PyBuiltinFunction("datetime.hour", (_, a, _) => new BigInteger(Value(a[0]).Hour)) };
        cls.Dict["minute"] = new PyProperty { Getter = new PyBuiltinFunction("datetime.minute", (_, a, _) => new BigInteger(Value(a[0]).Minute)) };
        cls.Dict["second"] = new PyProperty { Getter = new PyBuiltinFunction("datetime.second", (_, a, _) => new BigInteger(Value(a[0]).Second)) };
        cls.Dict["microsecond"] = new PyProperty { Getter = new PyBuiltinFunction("datetime.microsecond", (_, a, _) => new BigInteger(Value(a[0]).Ticks % TimeSpan.TicksPerSecond / 10)) };
        cls.Dict["tzinfo"] = new PyProperty { Getter = new PyBuiltinFunction("datetime.tzinfo", (_, a, _) => Field(a[0], "tzinfo")) };

        Add("now", (_, _, _) => MakeDateTime(DateTime.Now));
        Add("utcnow", (_, _, _) => MakeDateTime(DateTime.UtcNow));
        Add("today", (_, _, _) => MakeDateTime(DateTime.Now));

        Add("date", (_, a, _) => MakeDate(Value(a[0])));
        Add("time", (_, a, _) => MakeTime(Value(a[0]).TimeOfDay));
        Add("isoformat", (_, a, kwargs) =>
        {
            string sep = kwargs is not null && kwargs.TryGetValue("sep", out var s) ? (string)s : "T";
            return Value(a[0]).ToString($"yyyy-MM-dd{sep}HH:mm:ss", CultureInfo.InvariantCulture);
        });
        Add("__str__", (_, a, _) => Value(a[0]).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Add("__repr__", (_, a, _) =>
        {
            var v = Value(a[0]);
            return $"datetime.datetime({v.Year}, {v.Month}, {v.Day}, {v.Hour}, {v.Minute}, {v.Second})";
        });
        Add("strftime", (_, a, _) => Strftime(Value(a[0]), (string)a[1]));
        Add("timestamp", (_, a, _) => (Value(a[0]) - new DateTime(1970, 1, 1)).TotalSeconds);
        Add("weekday", (_, a, _) => new BigInteger(((int)Value(a[0]).DayOfWeek + 6) % 7));
        Add("replace", (_, a, kwargs) =>
        {
            var dt = Value(a[0]);
            int Get(string name, int cur) => kwargs is not null && kwargs.TryGetValue(name, out var v) ? Int(v) : cur;
            var replaced = new DateTime(
                Get("year", dt.Year), Get("month", dt.Month), Get("day", dt.Day),
                Get("hour", dt.Hour), Get("minute", dt.Minute), Get("second", dt.Second), dt.Millisecond);
            var inst = MakeDateTime(replaced);
            inst.Dict["tzinfo"] = kwargs is not null && kwargs.TryGetValue("tzinfo", out var tz) ? tz : Field(a[0], "tzinfo");
            return inst;
        });

        Add("__add__", (_, a, _) => a[1] is PyInstance i && i.Class == TimeDeltaClass
            ? MakeDateTime(Value(a[0]) + (TimeSpan)i.Dict[ValueKey]) : (object)PyNotImplemented.Instance);
        Add("__sub__", (_, a, _) => a[1] switch
        {
            PyInstance i when i.Class == TimeDeltaClass => MakeDateTime(Value(a[0]) - (TimeSpan)i.Dict[ValueKey]),
            PyInstance i when i.Class == DateTimeClass => (object)MakeTimeDelta(Value(a[0]) - Value(i)),
            _ => PyNotImplemented.Instance,
        });
        Add("__eq__", (_, a, _) => a[1] is PyInstance i && i.Class == DateTimeClass && Value(a[0]) == Value(a[1]));
        Add("__lt__", (_, a, _) => Value(a[0]) < Value((PyInstance)a[1]));
        Add("__le__", (_, a, _) => Value(a[0]) <= Value((PyInstance)a[1]));
        Add("__gt__", (_, a, _) => Value(a[0]) > Value((PyInstance)a[1]));
        Add("__ge__", (_, a, _) => Value(a[0]) >= Value((PyInstance)a[1]));

        // Same circular-init reason as date's min/max: build directly against the local `cls`.
        var min = new PyInstance(cls);
        min.Dict[ValueKey] = DateTime.MinValue;
        min.Dict["tzinfo"] = PyNone.Instance;
        cls.Dict["min"] = min;
        var max = new PyInstance(cls);
        max.Dict[ValueKey] = DateTime.MaxValue;
        max.Dict["tzinfo"] = PyNone.Instance;
        cls.Dict["max"] = max;

        return cls;
    }

    public static PyInstance MakeDateTime(DateTime dt)
    {
        var inst = new PyInstance(DateTimeClass);
        inst.Dict[ValueKey] = dt;
        inst.Dict["tzinfo"] = PyNone.Instance;
        return inst;
    }

    // ------------------------------------------------------------------ timezone

    private static PyClass BuildTimeZoneClass()
    {
        var cls = new PyClass("timezone", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"timezone.{name}", fn);

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var offset = a.Length > 1 && a[1] is PyInstance td && td.Class == TimeDeltaClass
                ? (TimeSpan)td.Dict[ValueKey]
                : TimeSpan.Zero;
            inst.Dict["__offset__"] = offset;
            inst.Dict["__name__"] = a.Length > 2 ? a[2] : PyNone.Instance;
            return PyNone.Instance;
        });
        Add("utcoffset", (_, a, _) => MakeTimeDelta((TimeSpan)Field(a[0], "__offset__")));
        Add("__eq__", (_, a, _) => a[1] is PyInstance i && i.Class == TimeZoneClass
            && (TimeSpan)Field(a[0], "__offset__") == (TimeSpan)Field(i, "__offset__"));
        Add("__repr__", (_, a, _) =>
        {
            var off = (TimeSpan)Field(a[0], "__offset__");
            return off == TimeSpan.Zero ? "datetime.timezone.utc" : $"datetime.timezone({off})";
        });

        cls.Dict["utc"] = MakeTimeZoneFor(cls);
        return cls;
    }

    private static PyInstance MakeTimeZoneFor(PyClass cls)
    {
        var inst = new PyInstance(cls);
        inst.Dict["__offset__"] = TimeSpan.Zero;
        inst.Dict["__name__"] = "UTC";
        return inst;
    }

}
