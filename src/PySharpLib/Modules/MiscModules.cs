// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Linq;
using System.Numerics;
using PySharpLib.Interpretation;
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

    private static readonly string[] PythonKeywords =
    {
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class",
        "continue", "def", "del", "elif", "else", "except", "finally", "for", "from", "global",
        "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return",
        "try", "while", "with", "yield",
    };
    private static readonly string[] PythonSoftKeywords = { "_", "case", "match", "type" };

    public static PyModule CreateKeyword()
    {
        var m = new PyModule("keyword");
        var d = m.Dict;
        d["kwlist"] = new PyList(PythonKeywords.Cast<object>());
        d["softkwlist"] = new PyList(PythonSoftKeywords.Cast<object>());
        d["iskeyword"] = new PyBuiltinFunction("iskeyword", (interp, a, _) =>
            PythonKeywords.Contains(PyOps.Str(interp, a[0])));
        d["issoftkeyword"] = new PyBuiltinFunction("issoftkeyword", (interp, a, _) =>
            PythonSoftKeywords.Contains(PyOps.Str(interp, a[0])));
        return m;
    }

    /// <summary>typing stub: generic names that accept subscription and calling.</summary>
    public static PyModule CreateTyping(Interp interp)
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
            "SupportsComplex", "SupportsBytes", "SupportsIndex",
            "ByteString", "AnyStr", "NoReturn", "Text", "Concatenate", "Self", "TypeAlias",
            "Unpack", "Annotated",
            "ForwardRef", "_Final", "_BaseGenericAlias",
            "_SpecialGenericAlias", "_AnnotatedAlias", "_UnionGenericAlias",
            "_ConcatenateGenericAlias", "_ProtocolMeta", "_TypedDictMeta",
            "ContextManager", "AsyncContextManager",
        })
        {
            d[name] = new PyClass(name, new List<PyClass>());
        }
        // The real class behind List[int]/Dict[str, int]/etc. (see GenericAliasModule) — not a
        // bare placeholder, so isinstance(List[int], typing._GenericAlias) is correct too.
        d["_GenericAlias"] = GenericAliasModule.GenericAliasClass;
        // Real CPython's ForwardRef declares __slots__ (typing_extensions checks for the presence
        // of '__forward_is_class__' in it at import time, e.g. `typing.ForwardRef.__slots__`) —
        // give our stub the same shape so that check doesn't crash.
        ((PyClass)d["ForwardRef"]).Dict["__slots__"] = new PyTuple(new object[]
        {
            "__forward_arg__", "__forward_code__", "__forward_evaluated__", "__forward_value__",
            "__forward_is_argument__", "__forward_is_class__", "__forward_module__",
        });
        // TYPE_CHECKING is False at runtime
        d["TYPE_CHECKING"] = false;
        // cast(t, v) → v ; overload → decorator identità
        d["cast"] = new PyBuiltinFunction("cast", (_, a, _) => a[1]);
        d["overload"] = new PyBuiltinFunction("overload", (_, a, _) => a[0]);
        d["final"] = new PyBuiltinFunction("final", (_, a, _) => a[0]);
        d["runtime_checkable"] = new PyBuiltinFunction("runtime_checkable", (_, a, _) => a[0]);
        d["no_type_check"] = new PyBuiltinFunction("no_type_check", (_, a, _) => a[0]);
        // Type-checker-only marker (no runtime effect): a decorator *factory* — the call itself
        // (with whatever eq_default/order_default/etc. kwargs) returns an identity decorator.
        d["dataclass_transform"] = new PyBuiltinFunction("dataclass_transform", (_, _, _) =>
            new PyBuiltinFunction("dataclass_transform.<locals>.decorator", (_, a2, _) => a2[0]));
        d["TypeVar"] = new PyBuiltinFunction("TypeVar", (_, a, _) => new PyClass((string)a[0], new List<PyClass>()));
        d["ParamSpec"] = new PyBuiltinFunction("ParamSpec", (_, a, _) => new PyClass((string)a[0], new List<PyClass>()));
        d["TypeVarTuple"] = new PyBuiltinFunction("TypeVarTuple", (_, a, _) => new PyClass((string)a[0], new List<PyClass>()));
        d["NewType"] = new PyBuiltinFunction("NewType", (_, a, _) => a[1]);
        // Real CPython caches generic-alias construction with this; a passthrough is correct,
        // just uncached (no scenario here depends on the caching itself, only on the name existing
        // and behaving as a decorator).
        d["_tp_cache"] = new PyBuiltinFunction("_tp_cache", (_, a, _) => a[0]);
        // Real CPython validates `arg` looks like a type hint and raises TypeError if not; no
        // scenario here has needed that validation, so this is a passthrough. It has to be a real
        // *Python* function (not a PyBuiltinFunction) because typing_extensions inspects its actual
        // signature as a version probe: `"module" in inspect.signature(_type_check).parameters` —
        // parsed and run into this module the same way a real .py file would define it.
        interp.RunModule(
            Parsing.Parser.Parse(
                "def _type_check(arg, msg, is_argument=True, module=None, *, allow_special_forms=False):\n"
                + "    return arg\n"),
            m);
        // Real behavior, ported from CPython (not a stub): _SpecialForm wraps a `getitem` function
        // (the body of a `@_SpecialForm def X(self, parameters): ...`-decorated special form like
        // Literal/ClassVar/Final) and delegates subscription to it. A real Python class, like
        // _type_check above, since typing_extensions subclasses it for real (_ExtensionsSpecialForm)
        // and instantiates it via the decorator pattern, which needs real __init__/__getitem__.
        interp.RunModule(
            Parsing.Parser.Parse(
                "class _SpecialForm:\n"
                + "    def __init__(self, getitem):\n"
                + "        self._getitem = getitem\n"
                + "        self._name = getitem.__name__\n"
                + "    def __repr__(self):\n"
                + "        return 'typing.' + self._name\n"
                + "    def __call__(self, *args, **kwds):\n"
                + "        raise TypeError('Cannot instantiate ' + repr(self))\n"
                + "    def __getitem__(self, parameters):\n"
                + "        return self._getitem(self, parameters)\n"
                + "    def __mro_entries__(self, bases):\n"
                + "        raise TypeError('Cannot subclass ' + repr(self))\n"),
            m);
        d["EXCLUDED_ATTRIBUTES"] = new PySet(new object[]
        {
            "__abstractmethods__", "__annotations__", "__dict__", "__doc__", "__init__",
            "__module__", "__new__", "__slots__", "__subclasshook__", "__weakref__",
            "__class_getitem__",
        });
        // Real CPython constant: names collections.namedtuple (which typing.NamedTuple reuses)
        // refuses as field names, since they'd collide with the tuple's own machinery.
        d["_prohibited"] = new PySet(new object[]
        {
            "__new__", "__init__", "__slots__", "__getnewargs__",
            "_fields", "_field_defaults", "_field_types", "_make", "_replace", "_asdict", "_source",
        });
        d["_overload_dummy"] = new PyBuiltinFunction("_overload_dummy", (_, _, _) =>
            throw PyErr.NotImplementedError(
                "You should not call an overloaded function. A series of @overload-decorated " +
                "functions outside a stub module should always be followed by an implementation."));

        // Real generic-alias tracking (List[int] etc. -> an object with __origin__/__args__, not a
        // no-op) — see GenericAliasModule. Map the container aliases to their real runtime
        // counterpart so `get_origin(List[int]) is list`, matching CPython; unmapped names (Union,
        // arbitrary user generics via Generic[T]) default to origin = the class itself, also correct.
        var b = interp.BuiltinsModule.Dict;
        GenericAliasModule.MapOrigin((PyClass)d["List"], b["list"]);
        GenericAliasModule.MapOrigin((PyClass)d["Dict"], b["dict"]);
        GenericAliasModule.MapOrigin((PyClass)d["Tuple"], b["tuple"]);
        GenericAliasModule.MapOrigin((PyClass)d["Set"], b["set"]);
        GenericAliasModule.MapOrigin((PyClass)d["FrozenSet"], b["frozenset"]);
        GenericAliasModule.MapOrigin((PyClass)d["Type"], b["type"]);
        // Optional[X] is really Union[X, NoneType]: same origin as Union, and NoneType appended
        // to the args CPython would report.
        GenericAliasModule.MapOrigin((PyClass)d["Optional"], d["Union"]);
        GenericAliasModule.MapArgsTransform((PyClass)d["Optional"],
            args => args.Append((object)NoneTypeClass).ToArray());

        d["get_origin"] = new PyBuiltinFunction("get_origin", (_, a, _) => GenericAliasModule.GetOrigin(a[0]));
        d["get_args"] = new PyBuiltinFunction("get_args", (_, a, _) => GenericAliasModule.GetArgs(a[0]));

        return m;
    }

    /// <summary>type(None) — a singleton so callers can compare by identity, matching CPython.</summary>
    public static readonly PyClass NoneTypeClass = new("NoneType", new List<PyClass>());

    /// <summary>Minimal types module: just the names real-world scripts have needed so far.</summary>
    public static PyModule CreateTypes()
    {
        var m = new PyModule("types");
        var d = m.Dict;
        foreach (var name in new[] { "TracebackType", "FunctionType", "ModuleType", "GeneratorType" })
            d[name] = new PyClass(name, new List<PyClass>());
        d["NoneType"] = NoneTypeClass;
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

        d["dataclass"] = new PyBuiltinFunction("dataclass", (interp, a, kwargs) =>
        {
            // usable both as @dataclass and @dataclass(...)
            if (a.Length == 1 && a[0] is PyClass cls)
                return ApplyDataclass(interp, cls, null);
            var deferredKwargs = kwargs;
            return new PyBuiltinFunction("dataclass_deco",
                (interp2, b, _) => ApplyDataclass(interp2, (PyClass)b[0], deferredKwargs));
        });

        d["asdict"] = new PyBuiltinFunction("asdict", (_, a, _) =>
            a[0] is PyInstance inst ? inst.Dict.Copy() : throw PyErr.TypeError("asdict() should be called on dataclass instances"));

        return m;
    }

    /// <summary>
    /// Generates __init__/__repr__/__eq__ (and, if frozen, a __setattr__ guard) from the class's
    /// annotated fields — walking the MRO base-to-derived so a subclass that adds no fields of its
    /// own (like aiomqtt's `Topic(Wildcard)`) still inherits its base's fields. Does not override
    /// a method the class already defines itself. Calls __post_init__ if present, matching CPython.
    /// </summary>
    private static PyClass ApplyDataclass(Interp interp, PyClass cls, Dictionary<string, object>? kwargs)
    {
        var fields = new List<string>();
        var defaults = new Dictionary<string, object>();
        for (int i = cls.Mro.Count - 1; i >= 0; i--)
        {
            if (!cls.Mro[i].Dict.TryGet("__annotations__", out var lvlAnnObj) || lvlAnnObj is not PyDict lvlAnn)
                continue;
            foreach (var key in lvlAnn.Keys.OfType<string>())
            {
                if (!fields.Contains(key))
                    fields.Add(key);
                if (cls.Mro[i].Dict.TryGet(key, out var def))
                    defaults[key] = def;
            }
        }
        if (fields.Count == 0)
            return cls; // nothing to generate (e.g. a dataclass with only methods)

        if (!cls.Dict.TryGet("__init__", out _))
        {
            cls.Dict["__init__"] = new PyBuiltinFunction($"{cls.Name}.__init__", (interp2, a, callKwargs) =>
            {
                var inst = (PyInstance)a[0];
                for (int i = 0; i < fields.Count; i++)
                {
                    if (i + 1 < a.Length)
                        inst.Dict[fields[i]] = a[i + 1];
                    else if (callKwargs is not null && callKwargs.TryGetValue(fields[i], out var kv))
                        inst.Dict[fields[i]] = kv;
                    else if (defaults.TryGetValue(fields[i], out var def))
                        inst.Dict[fields[i]] = def;
                    else
                        throw PyErr.TypeError(
                            $"{cls.Name}.__init__() missing required positional argument: '{fields[i]}'");
                }
                if (cls.TryLookup("__post_init__", out var postInit))
                    interp2.Call(postInit, new object[] { inst });
                return PyNone.Instance;
            });
        }

        if (!cls.Dict.TryGet("__repr__", out _))
        {
            cls.Dict["__repr__"] = new PyBuiltinFunction($"{cls.Name}.__repr__", (interp2, a, _) =>
            {
                var inst = (PyInstance)a[0];
                string body = string.Join(", ", fields.Select(f =>
                    $"{f}={PyOps.Repr(interp2, inst.Dict.TryGet(f, out var v) ? v : PyNone.Instance)}"));
                return $"{cls.Name}({body})";
            });
        }

        if (!cls.Dict.TryGet("__eq__", out _))
        {
            cls.Dict["__eq__"] = new PyBuiltinFunction($"{cls.Name}.__eq__", (interp2, a, _) =>
            {
                if (a[1] is not PyInstance other || other.Class != cls)
                    return PyNotImplemented.Instance;
                var self = (PyInstance)a[0];
                return fields.All(f => interp2.RichEquals(
                    self.Dict.TryGet(f, out var v1) ? v1 : PyNone.Instance,
                    other.Dict.TryGet(f, out var v2) ? v2 : PyNone.Instance));
            });
        }

        bool frozen = kwargs is not null && kwargs.TryGetValue("frozen", out var fz) && PyOps.Truthy(interp, fz);
        if (frozen && !cls.Dict.TryGet("__setattr__", out _))
        {
            cls.Dict["__setattr__"] = new PyBuiltinFunction($"{cls.Name}.__setattr__", (_, a, _) =>
            {
                var inst = (PyInstance)a[0];
                string name = (string)a[1];
                if (inst.Dict.TryGet(name, out _))
                    throw PyErr.TypeError($"cannot assign to field '{name}' (frozen dataclass)");
                inst.Dict[name] = a[2];
                return PyNone.Instance;
            });
        }

        return cls;
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
