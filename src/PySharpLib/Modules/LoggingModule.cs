using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>Minimal logging, but compatible with paho-mqtt's usage (logger.log(level, fmt, *args)).</summary>
public static class LoggingModule
{
    public static PyModule Create()
    {
        var m = new PyModule("logging");
        var d = m.Dict;

        d["CRITICAL"] = new BigInteger(50);
        d["FATAL"] = new BigInteger(50);
        d["ERROR"] = new BigInteger(40);
        d["WARNING"] = new BigInteger(30);
        d["WARN"] = new BigInteger(30);
        d["INFO"] = new BigInteger(20);
        d["DEBUG"] = new BigInteger(10);
        d["NOTSET"] = new BigInteger(0);

        var loggerClass = BuildLoggerClass();
        var loggers = new PyDict();

        d["getLogger"] = new PyBuiltinFunction("getLogger", (_, a, _) =>
        {
            string name = a.Length > 0 && a[0] is string s ? s : "root";
            if (loggers.TryGet(name, out var existing))
                return existing;
            var inst = new PyInstance(loggerClass);
            inst.Dict["name"] = name;
            inst.Dict["level"] = new BigInteger(30); // WARNING
            inst.Dict["propagate"] = true;
            inst.Dict["handlers"] = new PyList();
            loggers[name] = inst;
            return inst;
        });

        d["basicConfig"] = new PyBuiltinFunction("basicConfig", (_, _, _) => PyNone.Instance);
        d["Logger"] = loggerClass;

        // handler/formatter stub
        foreach (var stub in new[] { "StreamHandler", "NullHandler", "FileHandler" })
        {
            var stubClass = new PyClass(stub, new List<PyClass>());
            stubClass.Dict["__init__"] = new PyBuiltinFunction("__init__", (_, _, _) => PyNone.Instance);
            stubClass.Dict["setLevel"] = new PyBuiltinFunction("setLevel", (_, _, _) => PyNone.Instance);
            stubClass.Dict["setFormatter"] = new PyBuiltinFunction("setFormatter", (_, _, _) => PyNone.Instance);
            d[stub] = stubClass;
        }
        var formatterClass = new PyClass("Formatter", new List<PyClass>());
        formatterClass.Dict["__init__"] = new PyBuiltinFunction("__init__", (_, _, _) => PyNone.Instance);
        d["Formatter"] = formatterClass;

        return m;
    }

    private static PyClass BuildLoggerClass()
    {
        var cls = new PyClass("Logger", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"Logger.{name}", fn);

        void Emit(Interp interp, PyInstance logger, int level, string levelName,
            object msg, object[] args)
        {
            int loggerLevel = (int)PyOps.AsBigInt(logger.Dict["level"], "level");
            if (loggerLevel == 0)
                loggerLevel = 30;
            if (level < loggerLevel)
                return;
            string text = PyOps.Str(interp, msg);
            if (args.Length > 0)
                text = StrModules.PercentFormat(interp, text,
                    args.Length == 1 ? args[0] : new PyTuple(args));
            interp.Out.Write($"{levelName}:{logger.Dict["name"]}:{text}\n");
        }

        string LevelName(int level) => level switch
        {
            >= 50 => "CRITICAL",
            >= 40 => "ERROR",
            >= 30 => "WARNING",
            >= 20 => "INFO",
            >= 10 => "DEBUG",
            _ => "NOTSET",
        };

        Add("setLevel", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict["level"] = PyOps.AsBigInt(a[1], "level");
            return PyNone.Instance;
        });
        Add("getEffectiveLevel", (_, a, _) => ((PyInstance)a[0]).Dict["level"]);
        Add("isEnabledFor", (_, a, _) =>
        {
            int loggerLevel = (int)PyOps.AsBigInt(((PyInstance)a[0]).Dict["level"], "level");
            if (loggerLevel == 0)
                loggerLevel = 30;
            return (int)PyOps.AsBigInt(a[1], "level") >= loggerLevel;
        });
        Add("addHandler", (_, a, _) => PyNone.Instance);
        Add("removeHandler", (_, a, _) => PyNone.Instance);
        Add("hasHandlers", (_, a, _) => false);

        Add("log", (interp, a, _) =>
        {
            int level = (int)PyOps.AsBigInt(a[1], "level");
            Emit(interp, (PyInstance)a[0], level, LevelName(level), a[2], a.Skip(3).ToArray());
            return PyNone.Instance;
        });
        Add("debug", (interp, a, _) =>
        {
            Emit(interp, (PyInstance)a[0], 10, "DEBUG", a[1], a.Skip(2).ToArray());
            return PyNone.Instance;
        });
        Add("info", (interp, a, _) =>
        {
            Emit(interp, (PyInstance)a[0], 20, "INFO", a[1], a.Skip(2).ToArray());
            return PyNone.Instance;
        });
        Add("warning", (interp, a, _) =>
        {
            Emit(interp, (PyInstance)a[0], 30, "WARNING", a[1], a.Skip(2).ToArray());
            return PyNone.Instance;
        });
        Add("error", (interp, a, _) =>
        {
            Emit(interp, (PyInstance)a[0], 40, "ERROR", a[1], a.Skip(2).ToArray());
            return PyNone.Instance;
        });
        Add("exception", (interp, a, _) =>
        {
            Emit(interp, (PyInstance)a[0], 40, "ERROR", a[1], a.Skip(2).ToArray());
            return PyNone.Instance;
        });
        Add("critical", (interp, a, _) =>
        {
            Emit(interp, (PyInstance)a[0], 50, "CRITICAL", a[1], a.Skip(2).ToArray());
            return PyNone.Instance;
        });

        return cls;
    }
}
