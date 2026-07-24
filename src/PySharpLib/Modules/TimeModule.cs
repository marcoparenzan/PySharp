using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

public static class TimeModule
{
    public static PyModule Create()
    {
        var m = new PyModule("time");
        var d = m.Dict;

        d["time"] = new PyBuiltinFunction("time", (_, _, _) =>
            (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalSeconds);

        d["monotonic"] = new PyBuiltinFunction("monotonic", (_, _, _) =>
            Environment.TickCount64 / 1000.0);

        d["perf_counter"] = new PyBuiltinFunction("perf_counter", (_, _, _) =>
            System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);

        d["sleep"] = new PyBuiltinFunction("sleep", (_, args, _) =>
        {
            double seconds = PyOps.AsDouble(args[0]);
            if (seconds < 0)
                throw PyErr.ValueError("sleep length must be non-negative");
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            return PyNone.Instance;
        });

        d["strftime"] = new PyBuiltinFunction("strftime", (interp, args, _) =>
        {
            string fmt = (string)args[0];
            var when = args.Length > 1 && args[1] is not PyNone
                ? StructTimeToDate(StructValues(args[1]))
                : DateTime.Now;
            return Strftime(fmt, when);
        });

        d["localtime"] = new PyBuiltinFunction("localtime", (_, args, _) =>
        {
            var when = args.Length > 0 && args[0] is not PyNone
                ? DateTimeOffset.UnixEpoch.AddSeconds(PyOps.AsDouble(args[0])).ToLocalTime().DateTime
                : DateTime.Now;
            return DateToStructTime(when);
        });

        d["gmtime"] = new PyBuiltinFunction("gmtime", (_, args, _) =>
        {
            var when = args.Length > 0 && args[0] is not PyNone
                ? DateTimeOffset.UnixEpoch.AddSeconds(PyOps.AsDouble(args[0])).UtcDateTime
                : DateTime.UtcNow;
            return DateToStructTime(when);
        });

        return m;
    }

    private static readonly PyClass StructTimeClass = BuildStructTimeClass();

    private static PyClass BuildStructTimeClass()
    {
        // struct_time: tuple with tm_* attributes (used by localtime/gmtime)
        var cls = new PyClass("struct_time", new List<PyClass>());
        string[] names = { "tm_year", "tm_mon", "tm_mday", "tm_hour", "tm_min",
            "tm_sec", "tm_wday", "tm_yday", "tm_isdst" };
        cls.Dict["__getitem__"] = new PyBuiltinFunction("struct_time.__getitem__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var values = (object[])inst.Dict["__values__"];
            return interp.GetItem(new PyTuple(values), a[1]);
        });
        cls.Dict["__len__"] = new PyBuiltinFunction("struct_time.__len__", (_, _, _) => new BigInteger(9));
        cls.Dict["__iter__"] = new PyBuiltinFunction("struct_time.__iter__", (_, a, _) =>
            new PyIterator(((object[])((PyInstance)a[0]).Dict["__values__"]).AsEnumerable().GetEnumerator()));
        cls.Dict["__repr__"] = new PyBuiltinFunction("struct_time.__repr__", (interp, a, _) =>
        {
            var inst = (PyInstance)a[0];
            var values = (object[])inst.Dict["__values__"];
            var parts = names.Zip(values, (n, v) => $"{n}={PyOps.Repr(interp, v)}");
            return $"time.struct_time({string.Join(", ", parts)})";
        });
        cls.Dict["__structtime_names__"] = new PyTuple(names.Select(n => (object)n).ToArray());
        return cls;
    }

    private static PyInstance DateToStructTime(DateTime dt)
    {
        var values = new object[]
        {
            new BigInteger(dt.Year), new BigInteger(dt.Month), new BigInteger(dt.Day),
            new BigInteger(dt.Hour), new BigInteger(dt.Minute), new BigInteger(dt.Second),
            new BigInteger(((int)dt.DayOfWeek + 6) % 7), new BigInteger(dt.DayOfYear),
            new BigInteger(-1),
        };
        string[] names = { "tm_year", "tm_mon", "tm_mday", "tm_hour", "tm_min",
            "tm_sec", "tm_wday", "tm_yday", "tm_isdst" };
        var inst = new PyInstance(StructTimeClass);
        inst.Dict["__values__"] = values;
        for (int i = 0; i < names.Length; i++)
            inst.Dict[names[i]] = values[i];
        return inst;
    }

    private static object[] StructValues(object o) => o switch
    {
        PyTuple t => t.Items,
        PyInstance inst when inst.Dict.TryGet("__values__", out var v) => (object[])v,
        _ => throw PyErr.TypeError("Tuple or struct_time argument required"),
    };

    private static DateTime StructTimeToDate(object[] t)
        => new((int)PyOps.AsBigInt(t[0], "y"), (int)PyOps.AsBigInt(t[1], "m"),
            (int)PyOps.AsBigInt(t[2], "d"), (int)PyOps.AsBigInt(t[3], "H"),
            (int)PyOps.AsBigInt(t[4], "M"), (int)PyOps.AsBigInt(t[5], "S"));

    private static string Strftime(string fmt, DateTime dt)
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
                'Y' => dt.Year.ToString("D4"),
                'm' => dt.Month.ToString("D2"),
                'd' => dt.Day.ToString("D2"),
                'H' => dt.Hour.ToString("D2"),
                'M' => dt.Minute.ToString("D2"),
                'S' => dt.Second.ToString("D2"),
                'y' => (dt.Year % 100).ToString("D2"),
                'j' => dt.DayOfYear.ToString("D3"),
                'a' => dt.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture),
                'b' => dt.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture),
                '%' => "%",
                _ => "%" + fmt[i],
            });
        }
        return sb.ToString();
    }
}
