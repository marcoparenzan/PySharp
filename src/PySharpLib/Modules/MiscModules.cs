using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>Moduli piccoli: errno, platform, string, uuid, warnings, typing, dataclasses, copy.</summary>
public static class MiscModules
{
    public static PyModule CreateErrno()
    {
        var m = new PyModule("errno");
        var d = m.Dict;
        // standard POSIX values (paho uses them for symbolic comparisons)
        d["EAGAIN"] = new BigInteger(11);
        d["EWOULDBLOCK"] = new BigInteger(11);
        d["EINTR"] = new BigInteger(4);
        d["EINPROGRESS"] = new BigInteger(115);
        d["ECONNRESET"] = new BigInteger(104);
        d["ECONNREFUSED"] = new BigInteger(111);
        d["ECONNABORTED"] = new BigInteger(103);
        d["EPIPE"] = new BigInteger(32);
        d["ENOTCONN"] = new BigInteger(107);
        d["EBADF"] = new BigInteger(9);
        d["ENOENT"] = new BigInteger(2);
        d["EACCES"] = new BigInteger(13);
        d["ETIMEDOUT"] = new BigInteger(110);
        d["EHOSTUNREACH"] = new BigInteger(113);
        d["ENETUNREACH"] = new BigInteger(101);
        // codici Winsock (usati da paho su Windows)
        d["WSAEWOULDBLOCK"] = new BigInteger(10035);
        d["WSAEINPROGRESS"] = new BigInteger(10036);
        d["WSAECONNABORTED"] = new BigInteger(10053);
        d["WSAECONNRESET"] = new BigInteger(10054);
        d["WSAECONNREFUSED"] = new BigInteger(10061);
        d["WSAETIMEDOUT"] = new BigInteger(10060);
        d["WSAENOTCONN"] = new BigInteger(10057);
        return m;
    }

    public static PyModule CreatePlatform()
    {
        var m = new PyModule("platform");
        var d = m.Dict;
        d["system"] = new PyBuiltinFunction("system", (_, _, _) =>
            OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "Darwin" : "Linux");
        d["machine"] = new PyBuiltinFunction("machine", (_, _, _) =>
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        d["python_version"] = new PyBuiltinFunction("python_version", (_, _, _) => "3.12.0");
        d["python_implementation"] = new PyBuiltinFunction("python_implementation", (_, _, _) => "PySharp");
        d["release"] = new PyBuiltinFunction("release", (_, _, _) => Environment.OSVersion.Version.ToString());
        return m;
    }

    public static PyModule CreateString()
    {
        var m = new PyModule("string");
        var d = m.Dict;
        d["ascii_lowercase"] = "abcdefghijklmnopqrstuvwxyz";
        d["ascii_uppercase"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        d["ascii_letters"] = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        d["digits"] = "0123456789";
        d["hexdigits"] = "0123456789abcdefABCDEF";
        d["octdigits"] = "01234567";
        d["punctuation"] = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
        d["whitespace"] = " \t\n\r\v\f";
        d["printable"] = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~ \t\n\r\v\f";
        d["capwords"] = new PyBuiltinFunction("capwords", (_, a, _) =>
        {
            string s = (string)a[0];
            string? sep = a.Length > 1 && a[1] is string sp ? sp : null;
            var words = sep is null
                ? s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                : s.Split(sep);
            var capped = words.Select(w => w.Length == 0 ? w
                : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
            return string.Join(sep ?? " ", capped);
        });
        return m;
    }

    public static PyModule CreateUuid()
    {
        var m = new PyModule("uuid");
        var d = m.Dict;

        var uuidClass = new PyClass("UUID", new List<PyClass>());
        uuidClass.Dict["__str__"] = new PyBuiltinFunction("__str__", (_, a, _) =>
            (string)((PyInstance)a[0]).Dict["hex_str"]);
        uuidClass.Dict["__repr__"] = new PyBuiltinFunction("__repr__", (_, a, _) =>
            $"UUID('{(string)((PyInstance)a[0]).Dict["hex_str"]}')");

        object MakeUuid(Guid g)
        {
            var inst = new PyInstance(uuidClass);
            inst.Dict["hex_str"] = g.ToString("D");
            inst.Dict["hex"] = g.ToString("N");
            inst.Dict["bytes"] = new PyBytes(g.ToByteArray(bigEndian: true));
            return inst;
        }

        d["uuid4"] = new PyBuiltinFunction("uuid4", (_, _, _) => MakeUuid(Guid.NewGuid()));
        d["uuid1"] = new PyBuiltinFunction("uuid1", (_, _, _) => MakeUuid(Guid.NewGuid()));
        d["UUID"] = uuidClass;
        return m;
    }

    public static PyModule CreateWarnings()
    {
        var m = new PyModule("warnings");
        m.Dict["warn"] = new PyBuiltinFunction("warn", (_, _, _) => PyNone.Instance);
        m.Dict["filterwarnings"] = new PyBuiltinFunction("filterwarnings", (_, _, _) => PyNone.Instance);
        m.Dict["simplefilter"] = new PyBuiltinFunction("simplefilter", (_, _, _) => PyNone.Instance);
        return m;
    }

    /// <summary>typing stub: generic names that accept subscription and calling.</summary>
    public static PyModule CreateTyping()
    {
        var m = new PyModule("typing");
        var d = m.Dict;
        foreach (var name in new[]
        {
            "Any", "Optional", "Union", "List", "Dict", "Tuple", "Set", "FrozenSet",
            "Callable", "Iterator", "Iterable", "Sequence", "Mapping", "MutableMapping",
            "Type", "TypeVar", "Generic", "ClassVar", "Final", "Literal", "Protocol",
            "NamedTuple", "TypedDict", "cast", "overload", "IO", "BinaryIO", "TextIO",
            "Deque", "DefaultDict", "OrderedDict", "Counter", "ChainMap", "Awaitable",
            "Coroutine", "AsyncIterator", "AsyncIterable", "Generator", "AbstractSet",
            "MutableSequence", "MutableSet", "Hashable", "Sized", "Container", "Collection",
            "Reversible", "SupportsInt", "SupportsFloat", "SupportsAbs", "SupportsRound",
            "ByteString", "AnyStr", "NoReturn", "Text",
        })
        {
            d[name] = new PyClass(name, new List<PyClass>());
        }
        // TYPE_CHECKING is False at runtime
        d["TYPE_CHECKING"] = false;
        // cast(t, v) → v ; overload → decorator identità
        d["cast"] = new PyBuiltinFunction("cast", (_, a, _) => a[1]);
        d["overload"] = new PyBuiltinFunction("overload", (_, a, _) => a[0]);
        d["TypeVar"] = new PyBuiltinFunction("TypeVar", (_, a, _) => new PyClass((string)a[0], new List<PyClass>()));
        d["NewType"] = new PyBuiltinFunction("NewType", (_, a, _) => a[1]);
        return m;
    }

    /// <summary>Minimal dataclasses stub: @dataclass generates __init__ from the annotated fields with defaults.</summary>
    public static PyModule CreateDataclasses()
    {
        var m = new PyModule("dataclasses");
        var d = m.Dict;

        d["field"] = new PyBuiltinFunction("field", (_, _, kwargs) =>
        {
            if (kwargs is not null && kwargs.TryGetValue("default", out var def))
                return def;
            return PyNone.Instance;
        });

        d["dataclass"] = new PyBuiltinFunction("dataclass", (interp, a, _) =>
        {
            // usable both as @dataclass and @dataclass(...)
            if (a.Length == 1 && a[0] is PyClass cls)
                return cls; // v1: the class must define __init__ itself or use the class defaults
            return new PyBuiltinFunction("dataclass_deco", (_, b, _) => b[0]);
        });

        d["asdict"] = new PyBuiltinFunction("asdict", (_, a, _) =>
            a[0] is PyInstance inst ? inst.Dict.Copy() : throw PyErr.TypeError("asdict() should be called on dataclass instances"));

        return m;
    }

    public static PyModule CreateCopy()
    {
        var m = new PyModule("copy");
        m.Dict["copy"] = new PyBuiltinFunction("copy", (_, a, _) => a[0] switch
        {
            PyList l => new PyList(l.Items),
            PyDict pd => pd.Copy(),
            PySet s => new PySet(s.Items),
            PyInstance inst => CopyInstance(inst),
            _ => a[0],
        });
        m.Dict["deepcopy"] = new PyBuiltinFunction("deepcopy", (interp, a, _) => DeepCopy(a[0]));
        return m;
    }

    private static PyInstance CopyInstance(PyInstance inst)
    {
        var copy = new PyInstance(inst.Class);
        foreach (var e in inst.Dict.Entries)
            copy.Dict[e.Key] = e.Value;
        return copy;
    }

    private static object DeepCopy(object o) => o switch
    {
        PyList l => new PyList(l.Items.Select(DeepCopy)),
        PyTuple t => new PyTuple(t.Items.Select(DeepCopy).ToArray()),
        PySet s => new PySet(s.Items.Select(DeepCopy)),
        PyDict d => DeepCopyDict(d),
        PyInstance inst => DeepCopyInstance(inst),
        _ => o,
    };

    private static PyDict DeepCopyDict(PyDict d)
    {
        var copy = new PyDict();
        foreach (var e in d.Entries)
            copy[DeepCopy(e.Key)] = DeepCopy(e.Value);
        return copy;
    }

    private static PyInstance DeepCopyInstance(PyInstance inst)
    {
        var copy = new PyInstance(inst.Class);
        foreach (var e in inst.Dict.Entries)
            copy.Dict[e.Key] = DeepCopy(e.Value);
        return copy;
    }
}
