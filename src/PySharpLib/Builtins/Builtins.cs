// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Modules;
using PySharpLib.Runtime;

namespace PySharpLib.Builtins;

/// <summary>Builds the builtins module: functions, types and exception classes.</summary>
public static class BuiltinsFactory
{
    /// <summary>memoryview: a real (if simplified) view over bytes/bytearray — a bytearray-backed
    /// view shares the SAME underlying storage (mutations through either side are visible on the
    /// other, matching real CPython), a bytes-backed view is read-only. Implemented as a PyClass
    /// (like StringIO/BytesIO) rather than a new native runtime type: get isinstance/GetItem/`|`
    /// union-operand support for free from the existing generic PyInstance/PyClass machinery,
    /// instead of needing new native-type plumbing throughout the interpreter. Found via
    /// starlette's real `Content = str | bytes | memoryview` module-level type alias
    /// (responses.py — evaluated eagerly despite `from __future__ import annotations`, since it's a
    /// plain assignment, not a deferred annotation), reachable from `import starlette`. See
    /// FASTAPI_PLAN.md Phase 3.</summary>
    private static readonly PyClass MemoryViewClass = BuildMemoryViewClass();

    private static PyClass BuildMemoryViewClass()
    {
        var cls = new PyClass("memoryview", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"memoryview.{name}", fn);
        const string dataKey = "__data__";
        const string roKey = "__readonly__";

        List<byte> Data(object self) => (List<byte>)((PyInstance)self).Dict[dataKey];

        Add("__init__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            switch (a[1])
            {
                case PyByteArray ba:
                    inst.Dict[dataKey] = ba.Data; // shared reference: real view semantics
                    inst.Dict[roKey] = false;
                    break;
                case PyBytes b:
                    inst.Dict[dataKey] = new List<byte>(b.Data);
                    inst.Dict[roKey] = true;
                    break;
                default:
                    throw PyErr.TypeError("memoryview: a bytes-like object is required");
            }
            return PyNone.Instance;
        });
        Add("__len__", (_, a, _) => new BigInteger(Data(a[0]).Count));
        Add("__getitem__", (_, a, _) =>
        {
            var data = Data(a[0]);
            if (a[1] is PySlice slice)
            {
                var (start, _, step, count) = slice.Indices(data.Count);
                var items = new byte[count];
                for (int k = 0, idx = start; k < count; k++, idx += step)
                    items[k] = data[idx];
                return new PyBytes(items);
            }
            int i = PyOps.SeqIndex(a[1], data.Count, "memoryview");
            return new BigInteger(data[i]);
        });
        Add("__setitem__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            if (inst.Dict[roKey] is true)
                throw PyErr.TypeError("cannot modify read-only memory");
            Data(inst)[PyOps.SeqIndex(a[1], Data(inst).Count, "memoryview")] = (byte)PyOps.AsBigInt(a[2], "value");
            return PyNone.Instance;
        });
        Add("__eq__", (_, a, _) =>
        {
            var self = Data(a[0]);
            var other = a[1] switch
            {
                PyBytes b => b.Data,
                PyByteArray b => (IReadOnlyList<byte>)b.Data,
                PyInstance i when i.Class == cls => Data(i),
                _ => null,
            };
            return other is not null && self.SequenceEqual(other);
        });
        Add("__iter__", (_, a, _) => new PyIterator(Data(a[0]).Select(b => (object)new BigInteger(b)).GetEnumerator()));
        Add("tobytes", (_, a, _) => new PyBytes(Data(a[0]).ToArray()));
        Add("tolist", (_, a, _) => new PyList(Data(a[0]).Select(b => (object)new BigInteger(b))));
        Add("release", (_, _, _) => PyNone.Instance);
        Add("__repr__", (_, a, _) => $"<memory at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0]):x8}>");
        cls.Dict["nbytes"] = new PyProperty { Getter = new PyBuiltinFunction("memoryview.nbytes", (_, a, _) => new BigInteger(Data(a[0]).Count)) };
        cls.Dict["readonly"] = new PyProperty { Getter = new PyBuiltinFunction("memoryview.readonly", (_, a, _) => ((PyInstance)a[0]).Dict[roKey]) };
        cls.Dict["obj"] = new PyProperty { Getter = new PyBuiltinFunction("memoryview.obj", (_, a, _) => new PyBytes(Data(a[0]).ToArray())) };

        return cls;
    }

    public static PyModule Create()
    {
        var module = new PyModule("builtins");
        var d = module.Dict;

        foreach (var cls in PyErr.AllClasses())
            d[cls.Name] = cls;

        d["None"] = PyNone.Instance;
        d["True"] = true;
        d["False"] = false;
        d["NotImplemented"] = PyNotImplemented.Instance;
        d["Ellipsis"] = PyEllipsis.Instance;
        var objectClass = new PyClass("object", new List<PyClass>());
        objectClass.Dict["__setattr__"] = new PyBuiltinFunction("object.__setattr__", (_, a, _) =>
        {
            // Real CPython: `obj.__dict__ = newdict` replaces the instance's whole namespace —
            // pydantic's real `object_setattr(self, '__dict__', values)` idiom relies on exactly
            // this (BaseModel.__init__ sets every validated field at once, not one at a time).
            var inst = (PyInstance)a[0];
            string name = (string)a[1];
            if (name == "__dict__")
            {
                inst.Dict.Clear();
                if (a[2] is PyDict newDict)
                    foreach (var e in newDict.Entries)
                        inst.Dict[e.Key] = e.Value;
                return PyNone.Instance;
            }
            inst.Dict[name] = a[2];
            return PyNone.Instance;
        });
        objectClass.Dict["__delattr__"] = new PyBuiltinFunction("object.__delattr__", (_, a, _) =>
        {
            ((PyInstance)a[0]).Dict.Remove((string)a[1]);
            return PyNone.Instance;
        });
        objectClass.Dict["__init__"] = new PyBuiltinFunction("object.__init__", (_, _, _) => PyNone.Instance);
        // Real CPython default dunders — previously only reachable via hardcoded fallback branches
        // in PyOps.Repr/Str/RichEquals when TryCallMethod found nothing (same output, so this is a
        // transparent refactor for normal repr()/str()/== use), but that meant direct/unbound access
        // (`object.__eq__`, `SomeClass.__eq__` when SomeClass never overrides it, `super().__repr__()`)
        // raised AttributeError instead of finding a real method. Found via starlette's real
        // `cls.__eq__ is object.__eq__`-style idiom (a common way real Python libraries — dataclass-
        // like code, ORMs — detect whether a class defines custom equality), reachable from `import
        // starlette`. __hash__ is added for the same direct-access parity but note real CPython's own
        // `hash()` doesn't consult it here either (PyOps.PyHash has its own separate, pre-existing,
        // standing gap: no user-defined `__hash__` override is dispatched at all yet).
        objectClass.Dict["__eq__"] = new PyBuiltinFunction("object.__eq__", (_, a, _) =>
            ReferenceEquals(a[0], a[1]) ? true : (object)PyNotImplemented.Instance);
        objectClass.Dict["__ne__"] = new PyBuiltinFunction("object.__ne__", (interp, a, _) =>
        {
            var eq = interp.CallMethod(a[0], "__eq__", new[] { a[1] });
            return eq is PyNotImplemented ? eq : !PyOps.Truthy(interp, eq);
        });
        objectClass.Dict["__hash__"] = new PyBuiltinFunction("object.__hash__", (_, a, _) =>
            new BigInteger(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0])));
        objectClass.Dict["__repr__"] = new PyBuiltinFunction("object.__repr__", (_, a, _) =>
            $"<{((PyInstance)a[0]).Class.Name} object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0]):x8}>");
        objectClass.Dict["__str__"] = new PyBuiltinFunction("object.__str__", (interp, a, _) =>
            interp.CallMethod(a[0], "__repr__", Array.Empty<object>()));
        d["object"] = objectClass;

        void Add(string name, BuiltinFn fn) => d[name] = new PyBuiltinFunction(name, fn);

        // ---------------------------------------------------------------- I/O

        Add("print", (interp, args, kwargs) =>
        {
            string sep = " ", end = "\n";
            object? file = null;
            if (kwargs is not null)
            {
                if (kwargs.TryGetValue("sep", out var s) && s is not PyNone)
                    sep = s as string ?? throw PyErr.TypeError("sep must be a string");
                if (kwargs.TryGetValue("end", out var e) && e is not PyNone)
                    end = e as string ?? throw PyErr.TypeError("end must be a string");
                if (kwargs.TryGetValue("file", out var f) && f is not PyNone)
                    file = f;
            }
            string text = string.Join(sep, args.Select(a => PyOps.Str(interp, a))) + end;
            if (file is not null)
                interp.CallMethod(file, "write", new object[] { text });
            else
                interp.Out.Write(text);
            return PyNone.Instance;
        });

        Add("input", (interp, args, _) =>
        {
            if (args.Length > 0)
                interp.Out.Write(PyOps.Str(interp, args[0]));
            return Console.ReadLine() ?? "";
        });

        // ---------------------------------------------------------------- conversions/types

        Add("int", (interp, args, kwargs) =>
        {
            if (args.Length == 0)
                return BigInteger.Zero;
            object baseArg = kwargs is not null && kwargs.TryGetValue("base", out var b) ? b
                : args.Length > 1 ? args[1] : PyNone.Instance;
            if (baseArg is not PyNone)
            {
                int numBase = (int)PyOps.AsBigInt(baseArg, "base");
                string text = args[0] switch
                {
                    string s => s,
                    PyBytes by => System.Text.Encoding.ASCII.GetString(by.Data),
                    _ => throw PyErr.TypeError("int() can't convert non-string with explicit base"),
                };
                return ParseIntWithBase(text.Trim(), numBase);
            }
            return args[0] switch
            {
                BigInteger i => i,
                bool bo => bo ? BigInteger.One : BigInteger.Zero,
                double db => new BigInteger(Math.Truncate(db)),
                string s => ParseIntLiteral(s.Trim()),
                PyBytes by => ParseIntLiteral(System.Text.Encoding.ASCII.GetString(by.Data).Trim()),
                PyInstance inst when interp.TryCallMethod(inst, "__int__", Array.Empty<object>(), out var r) => r,
                _ => throw PyErr.TypeError(
                    $"int() argument must be a string or a number, not '{PyOps.TypeName(args[0])}'"),
            };
        });

        Add("float", (interp, args, _) =>
        {
            if (args.Length == 0)
                return 0.0;
            return args[0] switch
            {
                double db => db,
                BigInteger i => (double)i,
                bool bo => bo ? 1.0 : 0.0,
                string s => ParseFloat(s.Trim()),
                PyInstance inst when interp.TryCallMethod(inst, "__float__", Array.Empty<object>(), out var r) => r,
                _ => throw PyErr.TypeError($"float() argument must be a string or a number"),
            };
        });

        d["complex"] = ComplexType.ComplexClass;

        Add("bool", (interp, args, _) => args.Length > 0 && PyOps.Truthy(interp, args[0]));
        Add("str", (interp, args, _) => args.Length == 0 ? "" : PyOps.Str(interp, args[0]));
        Add("repr", (interp, args, _) => PyOps.Repr(interp, args[0]));

        Add("bytes", (interp, args, _) =>
        {
            if (args.Length == 0)
                return PyBytes.Empty;
            return args[0] switch
            {
                PyBytes b => b,
                PyByteArray b => new PyBytes(b.Data.ToArray()),
                BigInteger n => new PyBytes(new byte[(int)n]),
                string s when args.Length > 1 =>
                    new PyBytes(StrModules.GetEncoding((string)args[1]).GetBytes(s)),
                string => throw PyErr.TypeError("string argument without an encoding"),
                _ => new PyBytes(PyOps.Iterate(interp, args[0])
                    .Select(x => (byte)PyOps.AsBigInt(x, "bytes item")).ToArray()),
            };
        });

        Add("bytearray", (interp, args, _) =>
        {
            if (args.Length == 0)
                return new PyByteArray();
            return args[0] switch
            {
                PyBytes b => new PyByteArray(b.Data),
                PyByteArray b => new PyByteArray(b.Data),
                BigInteger n => new PyByteArray(new byte[(int)n]),
                string s when args.Length > 1 =>
                    new PyByteArray(StrModules.GetEncoding((string)args[1]).GetBytes(s)),
                _ => new PyByteArray(PyOps.Iterate(interp, args[0])
                    .Select(x => (byte)PyOps.AsBigInt(x, "bytes item"))),
            };
        });

        d["memoryview"] = MemoryViewClass;

        Add("list", (interp, args, _) =>
            args.Length == 0 ? new PyList() : new PyList(PyOps.Iterate(interp, args[0])));
        Add("tuple", (interp, args, _) =>
            args.Length == 0 ? PyTuple.Empty : new PyTuple(PyOps.Iterate(interp, args[0]).ToArray()));
        Add("set", (interp, args, _) =>
            args.Length == 0 ? new PySet() : new PySet(PyOps.Iterate(interp, args[0])));
        Add("frozenset", (interp, args, _) =>
            args.Length == 0 ? new PyFrozenSet(Array.Empty<object>()) : new PyFrozenSet(PyOps.Iterate(interp, args[0])));

        Add("dict", (interp, args, kwargs) =>
        {
            var result = new PyDict();
            if (args.Length > 0)
            {
                if (args[0] is PyDict src)
                {
                    result.Update(src);
                }
                else
                {
                    foreach (var pair in PyOps.Iterate(interp, args[0]))
                    {
                        var kv = PyOps.Iterate(interp, pair).ToList();
                        if (kv.Count != 2)
                            throw PyErr.ValueError("dictionary update sequence element is not a pair");
                        result[kv[0]] = kv[1];
                    }
                }
            }
            if (kwargs is not null)
                foreach (var kv in kwargs)
                    result[kv.Key] = kv.Value;
            return result;
        });

        // 3-arg form: type(name, bases, namespace) builds a class dynamically, the same way
        // `class Name(bases): body` would — real behavior (not a stub), needed by metaprogramming
        // that goes through types.prepare_class/new_class (e.g. pydantic's create_model()). Custom
        // metaclasses aren't supported (see ExecClassDef), so this is the only "meta" callers get.
        Add("type", (interp, args, _) => args.Length >= 3
            ? PySharpLib.Runtime.TypeConstructorMethods.BuildClass((string)args[0], args[1], args[2])
            : args[0] switch
            {
                PyInstance inst => inst.Class,
                _ => TypeNamePseudoClass(interp, args[0]),
            });

        Add("isinstance", (_, args, _) => IsInstance(args[0], args[1]));
        Add("issubclass", (_, args, _) =>
        {
            // `int`/`str`/etc. themselves (as issubclass's 1st arg, e.g. `issubclass(int, X)`) are
            // real classes too — resolved to the SAME pseudo-base-class `class Foo(int): ...` uses,
            // so a class that really does subclass a builtin type compares correctly against it.
            var cls = args[0] as PyClass
                ?? (args[0] is PyBuiltinFunction bf0 && BuiltinTypeNames.Contains(bf0.Name)
                    ? Interp.GetPseudoBaseClass(bf0.Name)
                    : null)
                ?? throw PyErr.TypeError("issubclass() arg 1 must be a class");
            return IsSubclass(cls, args[1]);
        });

        Add("callable", (_, args, _) => args[0] is PyFunction or PyBuiltinFunction or PyBoundMethod or PyClass
            || (args[0] is PyInstance inst && inst.Class.TryLookup("__call__", out _)));

        // ---------------------------------------------------------------- sequences

        Add("len", (interp, args, _) => new BigInteger(PyOps.Len(interp, args[0])));

        Add("range", (_, args, _) => args.Length switch
        {
            1 => new PyRange(0, PyOps.AsBigInt(args[0], "stop"), 1),
            2 => new PyRange(PyOps.AsBigInt(args[0], "start"), PyOps.AsBigInt(args[1], "stop"), 1),
            _ => new PyRange(PyOps.AsBigInt(args[0], "start"), PyOps.AsBigInt(args[1], "stop"),
                PyOps.AsBigInt(args[2], "step")),
        });

        Add("enumerate", (interp, args, _) =>
        {
            var start = args.Length > 1 ? PyOps.AsBigInt(args[1], "start") : BigInteger.Zero;
            return new PyIterator(EnumerateImpl(interp, args[0], start).GetEnumerator());
        });

        Add("zip", (interp, args, _) => new PyIterator(ZipImpl(interp, args).GetEnumerator()));

        Add("map", (interp, args, _) => new PyIterator(MapImpl(interp, args).GetEnumerator()));

        Add("filter", (interp, args, _) => new PyIterator(
            PyOps.Iterate(interp, args[1])
                .Where(x => args[0] is PyNone ? PyOps.Truthy(interp, x)
                    : PyOps.Truthy(interp, interp.Call(args[0], new[] { x })))
                .GetEnumerator()));

        Add("reversed", (interp, args, _) =>
        {
            var items = args[0] switch
            {
                PyList l => l.Items.ToList(),
                PyTuple t => t.Items.ToList(),
                string s => s.Reverse().Select(c => (object)c.ToString()).ToList(),
                PyRange r => r.Enumerate().ToList(),
                _ => PyOps.Iterate(interp, args[0]).ToList(),
            };
            if (args[0] is not string)
                items.Reverse();
            return new PyIterator(items.GetEnumerator());
        });

        Add("sorted", (interp, args, kwargs) =>
        {
            var items = PyOps.Iterate(interp, args[0]).ToList();
            ListMethods.SortInPlace(interp, items, kwargs);
            return new PyList(items);
        });

        Add("next", (interp, args, _) =>
        {
            if (PyOps.IterNext(interp, args[0], out var v))
                return v;
            if (args.Length > 1)
                return args[1];
            throw PyErr.StopIteration();
        });

        Add("sum", (interp, args, _) =>
        {
            object acc = args.Length > 1 ? args[1] : BigInteger.Zero;
            foreach (var x in PyOps.Iterate(interp, args[0]))
                acc = interp.BinaryOp("+", acc, x);
            return acc;
        });

        Add("min", (interp, args, kwargs) => MinMax(interp, args, kwargs, min: true));
        Add("max", (interp, args, kwargs) => MinMax(interp, args, kwargs, min: false));

        Add("any", (interp, args, _) =>
            PyOps.Iterate(interp, args[0]).Any(x => PyOps.Truthy(interp, x)));
        Add("all", (interp, args, _) =>
            PyOps.Iterate(interp, args[0]).All(x => PyOps.Truthy(interp, x)));

        // ---------------------------------------------------------------- numeric

        Add("abs", (interp, args, _) => args[0] switch
        {
            BigInteger i => BigInteger.Abs(i),
            double db => Math.Abs(db),
            bool bo => bo ? BigInteger.One : BigInteger.Zero,
            PyInstance inst when interp.TryCallMethod(inst, "__abs__", Array.Empty<object>(), out var r) => r,
            _ => throw PyErr.TypeError($"bad operand type for abs(): '{PyOps.TypeName(args[0])}'"),
        });

        Add("round", (_, args, _) =>
        {
            double value = PyOps.AsDouble(args[0]);
            int digits = args.Length > 1 && args[1] is not PyNone ? (int)PyOps.AsBigInt(args[1], "ndigits") : 0;
            double rounded = Math.Round(value, digits, MidpointRounding.ToEven);
            return args.Length > 1 && args[1] is not PyNone ? rounded
                : args[0] is double ? new BigInteger(rounded)
                : PyOps.AsBigInt(args[0], "round");
        });

        Add("divmod", (interp, args, _) => new PyTuple(new[]
        {
            interp.BinaryOp("//", args[0], args[1]),
            interp.BinaryOp("%", args[0], args[1]),
        }));

        Add("pow", (interp, args, _) => args.Length > 2
            ? BigInteger.ModPow(PyOps.AsBigInt(args[0], "base"), PyOps.AsBigInt(args[1], "exp"),
                PyOps.AsBigInt(args[2], "mod"))
            : interp.BinaryOp("**", args[0], args[1]));

        Add("hash", (_, args, _) => new BigInteger(PyOps.PyHash(args[0])));
        Add("id", (_, args, _) => new BigInteger(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(args[0])));

        Add("ord", (_, args, _) =>
        {
            switch (args[0])
            {
                case string s:
                    // count code points, not UTF-16 code units (handles astral characters)
                    var runes = s.EnumerateRunes().ToArray();
                    if (runes.Length != 1)
                        throw PyErr.TypeError(
                            $"ord() expected a character, but string of length {runes.Length} found");
                    return new BigInteger(runes[0].Value);
                case PyBytes b when b.Length == 1:
                    return new BigInteger(b.Data[0]);
                case PyBytes b:
                    throw PyErr.TypeError(
                        $"ord() expected a character, but string of length {b.Length} found");
                default:
                    throw PyErr.TypeError(
                        $"ord() expected string of length 1, but {PyOps.TypeName(args[0])} found");
            }
        });

        Add("chr", (_, args, _) =>
            char.ConvertFromUtf32((int)PyOps.AsBigInt(args[0], "chr")));

        Add("hex", (_, args, _) =>
        {
            var n = PyOps.AsBigInt(args[0], "hex");
            var abs = BigInteger.Abs(n);
            var sb = new System.Text.StringBuilder();
            if (abs.IsZero)
                sb.Append('0');
            const string hexDigits = "0123456789abcdef";
            while (!abs.IsZero)
            {
                sb.Insert(0, hexDigits[(int)(abs % 16)]);
                abs /= 16;
            }
            return (n.Sign < 0 ? "-0x" : "0x") + sb;
        });
        Add("bin", (_, args, _) =>
        {
            var n = PyOps.AsBigInt(args[0], "bin");
            var abs = BigInteger.Abs(n);
            var sb = new System.Text.StringBuilder();
            if (abs.IsZero)
                sb.Append('0');
            while (!abs.IsZero)
            {
                sb.Insert(0, (char)('0' + (int)(abs % 2)));
                abs /= 2;
            }
            return (n.Sign < 0 ? "-0b" : "0b") + sb;
        });
        Add("oct", (_, args, _) =>
        {
            var n = PyOps.AsBigInt(args[0], "oct");
            var abs = BigInteger.Abs(n);
            var sb = new System.Text.StringBuilder();
            if (abs.IsZero)
                sb.Append('0');
            while (!abs.IsZero)
            {
                sb.Insert(0, (char)('0' + (int)(abs % 8)));
                abs /= 8;
            }
            return (n.Sign < 0 ? "-0o" : "0o") + sb;
        });

        // ---------------------------------------------------------------- attributes/reflection

        Add("getattr", (interp, args, _) =>
        {
            string name = (string)args[1];
            if (interp.TryGetAttr(args[0], name, out var v))
                return v;
            if (args.Length > 2)
                return args[2];
            throw PyErr.AttributeError($"'{PyOps.TypeName(args[0])}' object has no attribute '{name}'");
        });
        Add("setattr", (interp, args, _) =>
        {
            interp.SetAttr(args[0], (string)args[1], args[2]);
            return PyNone.Instance;
        });
        Add("hasattr", (interp, args, _) => interp.TryGetAttr(args[0], (string)args[1], out var _ignored));
        Add("delattr", (interp, args, _) =>
        {
            interp.DelAttr(args[0], (string)args[1]);
            return PyNone.Instance;
        });
        Add("vars", (_, args, _) => args[0] switch
        {
            PyInstance inst => inst.Dict,
            PyModule m => m.Dict,
            PyClass c => c.Dict,
            _ => throw PyErr.TypeError("vars() argument must have __dict__"),
        });
        Add("dir", (interp, args, _) =>
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            switch (args[0])
            {
                case PyInstance inst:
                    foreach (var k in inst.Dict.Keys.OfType<string>()) names.Add(k);
                    foreach (var cls in inst.Class.Mro)
                        foreach (var k in cls.Dict.Keys.OfType<string>()) names.Add(k);
                    break;
                case PyClass c:
                    foreach (var cls in c.Mro)
                        foreach (var k in cls.Dict.Keys.OfType<string>()) names.Add(k);
                    break;
                case PyModule m:
                    foreach (var k in m.Dict.Keys.OfType<string>()) names.Add(k);
                    break;
            }
            return new PyList(names.Select(x => (object)x));
        });

        Add("format", (interp, args, _) =>
            interp.FormatValue(args[0], args.Length > 1 ? (string)args[1] : ""));

        Add("staticmethod", (_, args, _) => new PyStaticMethod(args[0]));
        Add("classmethod", (_, args, _) => new PyClassMethod(args[0]));
        Add("property", (_, args, kwargs) => new PyProperty
        {
            Getter = args.Length > 0 && args[0] is not PyNone ? args[0] : null,
            Setter = args.Length > 1 && args[1] is not PyNone ? args[1] : null,
            Deleter = args.Length > 2 && args[2] is not PyNone ? args[2] : null,
        });

        Add("super", (_, args, _) =>
        {
            if (args.Length >= 2)
            {
                var cls = args[0] as PyClass ?? throw PyErr.TypeError("super() argument 1 must be a type");
                return new PySuper(cls, args[1]);
            }
            var frame = Interp.CurrentFrame
                        ?? throw PyErr.RuntimeError("super(): no current frame");
            var definingClass = frame.Fn!.DefiningClass
                                ?? throw PyErr.RuntimeError("super(): __class__ cell not found");
            if (frame.Fn.Params.Positional.Count == 0
                || !frame.Env.TryGet(frame.Fn.Params.Positional[0].Name, out var self))
                throw PyErr.RuntimeError("super(): no arguments");
            return new PySuper(definingClass, self);
        });

        Add("exec", (interp, args, _) => throw PyErr.NotImplementedError("exec() not supported"));
        Add("eval", (interp, args, _) => throw PyErr.NotImplementedError("eval() not supported"));

        Add("open", (interp, args, kwargs) => FileObject.Open(interp, args, kwargs));

        Add("slice", (_, args, _) => args.Length switch
        {
            1 => new PySlice(PyNone.Instance, args[0], PyNone.Instance),
            2 => new PySlice(args[0], args[1], PyNone.Instance),
            3 => new PySlice(args[0], args[1], args[2]),
            _ => throw PyErr.TypeError("slice expected 1 to 3 arguments"),
        });

        Add("locals", (interp, _, _) =>
        {
            var frame = Interp.CurrentFrame;
            if (frame is null)
                // Module level: same as globals() — the real currently-executing module's dict,
                // not the builtins module `module` (this closure's own variable) would give.
                return Interp.InnermostFrame?.Env.Module.Dict ?? module.Dict;
            var d2 = new PyDict();
            foreach (var kv in frame.Env.Locals)
                d2[kv.Key] = kv.Value;
            return d2;
        });
        Add("globals", (interp, _, _) =>
        {
            var frame = Interp.CurrentFrame;
            if (frame is not null)
                return frame.Fn!.Module.Dict;
            // No function call active: the innermost frame IS the module frame itself.
            return Interp.InnermostFrame?.Env.Module.Dict ?? module.Dict;
        });

        Add("iter", (interp, args, _) =>
        {
            // iter(callable, sentinel)
            if (args.Length == 2)
                return new PyIterator(CallableIter(interp, args[0], args[1]).GetEnumerator());
            return PyOps.GetIter(interp, args[0]);
        });

        return module;
    }

    // ---------------------------------------------------------------- helper

    private static BigInteger ParseIntLiteral(string s)
    {
        try
        {
            s = s.Replace("_", "");
            bool neg = s.StartsWith('-');
            if (s.StartsWith("+") || s.StartsWith("-"))
                s = s[1..];
            BigInteger v;
            if (s.StartsWith("0x") || s.StartsWith("0X"))
                v = ParseDigits(s[2..], 16);
            else if (s.StartsWith("0o") || s.StartsWith("0O"))
                v = ParseDigits(s[2..], 8);
            else if (s.StartsWith("0b") || s.StartsWith("0B"))
                v = ParseDigits(s[2..], 2);
            else
                v = BigInteger.Parse(s, CultureInfo.InvariantCulture);
            return neg ? -v : v;
        }
        catch (FormatException)
        {
            throw PyErr.ValueError($"invalid literal for int() with base 10: '{s}'");
        }
    }

    private static BigInteger ParseIntWithBase(string s, int numBase)
    {
        if (numBase == 0)
            return ParseIntLiteral(s);
        bool neg = s.StartsWith('-');
        if (s.StartsWith("+") || s.StartsWith("-"))
            s = s[1..];
        if (numBase == 16 && (s.StartsWith("0x") || s.StartsWith("0X")))
            s = s[2..];
        if (numBase == 8 && (s.StartsWith("0o") || s.StartsWith("0O")))
            s = s[2..];
        if (numBase == 2 && (s.StartsWith("0b") || s.StartsWith("0B")))
            s = s[2..];
        var v = ParseDigits(s.Replace("_", ""), numBase);
        return neg ? -v : v;
    }

    private static BigInteger ParseDigits(string digits, int numBase)
    {
        if (digits.Length == 0)
            throw PyErr.ValueError("invalid literal for int()");
        BigInteger v = 0;
        foreach (char c in digits)
        {
            int d = char.IsAsciiDigit(c) ? c - '0'
                : char.IsAsciiLetter(c) ? char.ToLowerInvariant(c) - 'a' + 10
                : -1;
            if (d < 0 || d >= numBase)
                throw PyErr.ValueError($"invalid literal for int() with base {numBase}: '{digits}'");
            v = v * numBase + d;
        }
        return v;
    }

    private static double ParseFloat(string s)
    {
        s = s.Replace("_", "");
        return s.ToLowerInvariant() switch
        {
            "inf" or "+inf" or "infinity" or "+infinity" => double.PositiveInfinity,
            "-inf" or "-infinity" => double.NegativeInfinity,
            "nan" or "+nan" or "-nan" => double.NaN,
            _ => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : throw PyErr.ValueError($"could not convert string to float: '{s}'"),
        };
    }

    private static readonly Dictionary<string, PyClass> PseudoClasses = new();

    /// <summary>type(x) for builtin values. For types with a real, correctly-behaving builtin
    /// constructor (int/str/list/set/dict/.../type itself), returns THAT — not a bare
    /// non-constructible stand-in — so idioms like `v.__class__(new_items)` (clone a container in
    /// its own concrete type, or `type.__call__`-style dynamic construction) actually work. Found
    /// via pydantic's real `v.__class__(seq_args)` (BaseModel._get_value, used by model.dict()).
    /// Falls back to a singleton pseudo-class (for comparisons like type(x) == type(y)) for
    /// non-constructible types (function/method/module/NoneType/...).</summary>
    internal static object TypeNamePseudoClass(Interp interp, object o)
    {
        string name = PyOps.TypeName(o);
        if (interp.BuiltinsModule.Dict.TryGet(name, out var real) && real is PyBuiltinFunction)
            return real;
        lock (PseudoClasses)
        {
            if (!PseudoClasses.TryGetValue(name, out var cls))
            {
                cls = new PyClass(name, new List<PyClass>());
                PseudoClasses[name] = cls;
            }
            return cls;
        }
    }

    internal static bool IsInstance(object obj, object classInfo)
    {
        switch (classInfo)
        {
            case PyTuple t:
                return t.Items.Any(x => IsInstance(obj, x));
            case PyClass cls:
                if (obj is PyInstance inst)
                    return inst.Class.IsSubclassOf(cls);
                if (cls.Name == "object")
                    return true;
                return TypeMatchesBuiltinName(obj, cls.Name);
            case PyBuiltinFunction bf:
                // isinstance(x, int) where int is the builtin conversion function
                return TypeMatchesBuiltinName(obj, bf.Name);
            // isinstance(x, A | B): real CPython (3.10+) accepts a types.UnionType directly as
            // isinstance's 2nd arg, same as a tuple of types. Found via starlette's real `Content =
            // str | bytes | memoryview` combined with real `isinstance(content, bytes | memoryview)`
            // calls (responses.py) — reachable once memoryview and the PEP 604 union itself both
            // existed, but this recursive-membership check never had.
            case PyInstance ui when ui.Class == Modules.GenericAliasModule.GenericAliasClass
                && ReferenceEquals(Modules.GenericAliasModule.GetOrigin(ui), Modules.MiscModules.UnionTypeClass):
                return Modules.GenericAliasModule.GetArgs(ui).Items.Any(x => IsInstance(obj, x));
            default:
                throw PyErr.TypeError("isinstance() arg 2 must be a type or tuple of types");
        }
    }

    internal static bool IsSubclass(PyClass cls, object classInfo)
    {
        switch (classInfo)
        {
            case PyTuple t:
                return t.Items.Any(x => IsSubclass(cls, x));
            case PyClass other:
                return cls.IsSubclassOf(other);
            case PyBuiltinFunction bf:
                // issubclass(cls, dict) where dict is the builtin conversion function: true if cls
                // (or an ancestor) IS the pseudo-base-class a builtin base produces (see
                // ExecClassDef's `class Foo(dict): ...` handling), matching isinstance's equivalent
                // TypeMatchesBuiltinName check. Found via pydantic's real `lenient_issubclass(type_,
                // dict)` (typing.py's is_typeddict) — `dict`/`list`/`str`/etc. as issubclass's 2nd
                // arg previously always raised, since they're PyBuiltinFunction, not PyClass.
                return cls.Mro.Any(m => m.Name == bf.Name);
            // issubclass(X, A | B): same real CPython 3.10+ acceptance as isinstance's case above.
            case PyInstance ui when ui.Class == Modules.GenericAliasModule.GenericAliasClass
                && ReferenceEquals(Modules.GenericAliasModule.GetOrigin(ui), Modules.MiscModules.UnionTypeClass):
                return Modules.GenericAliasModule.GetArgs(ui).Items.Any(x => IsSubclass(cls, x));
            default:
                throw PyErr.TypeError("issubclass() arg 2 must be a class or tuple of classes");
        }
    }

    // enum.IntEnum members are real Python ints (IntEnum derives from int), so
    // isinstance(x, int) must be true for them too — found via aiomqtt/paho-mqtt's
    // ConnackCode(enum.IntEnum), whose ReasonCode.__eq__ relies on exactly this
    // (see AIOMQTT_PLAN.md Phase 5).
    private static bool IsIntEnumMember(object obj) =>
        obj is PyInstance inst && inst.Class.IsSubclassOf(EnumModule.IntEnumClass);

    private static bool TypeMatchesBuiltinName(object obj, string name) => name switch
    {
        "int" => obj is BigInteger or bool || IsIntEnumMember(obj),
        "float" => obj is double,
        "bool" => obj is bool,
        "str" => obj is string,
        "bytes" => obj is PyBytes,
        "bytearray" => obj is PyByteArray,
        "list" => obj is PyList,
        "tuple" => obj is PyTuple,
        "dict" => obj is PyDict,
        "set" => obj is PySet,
        "frozenset" => obj is PyFrozenSet,
        "range" => obj is PyRange,
        "slice" => obj is PySlice,
        // `dict`/`str`/etc. themselves (the builtin conversion functions used as pseudo-classes) are
        // real instances of `type` too — e.g. `isinstance(dict, type)` — but a builtin FUNCTION like
        // `len`/`print` is not, so this can't just be `obj is PyBuiltinFunction`; only names that are
        // themselves one of these known builtin-type pseudo-classes count. Found via pydantic's real
        // `isinstance(cls, type) and issubclass(cls, class_or_tuple)` idiom (utils.lenient_issubclass)
        // being called with a builtin type itself as `cls`.
        "type" => obj is PyClass || (obj is PyBuiltinFunction btf && BuiltinTypeNames.Contains(btf.Name)),
        "NoneType" => obj is PyNone,
        // Real CPython: `class Task(Future):` — a Task genuinely IS-A Future, so
        // `isinstance(some_task, asyncio.Future)` must be True too, not just `isinstance(x,
        // asyncio.Task)`. The generic fallback below is a flat name-equality check
        // (PyOps.TypeName reports the *most specific* name, "Task", for a PyTask) and can't see
        // through PyTask's real C# inheritance from PyFuture on its own. Found via anyio's real
        // Task/Future interchangeable use in its own type checks (_backends/_asyncio.py), reachable
        // from `import starlette`.
        "Future" => obj is PyFuture,
        _ => PyOps.TypeName(obj) == name,
    };

    internal static readonly HashSet<string> BuiltinTypeNames = new()
    {
        "int", "float", "bool", "str", "bytes", "bytearray", "list", "tuple",
        "dict", "set", "frozenset", "range", "slice", "type",
    };

    private static IEnumerable<object> CallableIter(Interp interp, object callable, object sentinel)
    {
        while (true)
        {
            var v = interp.Call(callable, Array.Empty<object>());
            if (interp.RichEquals(v, sentinel))
                yield break;
            yield return v;
        }
    }

    private static IEnumerable<object> EnumerateImpl(Interp interp, object iterable, BigInteger start)
    {
        var i = start;
        foreach (var x in PyOps.Iterate(interp, iterable))
        {
            yield return new PyTuple(new object[] { i, x });
            i += 1;
        }
    }

    private static IEnumerable<object> MapImpl(Interp interp, object[] args)
    {
        var fn = args[0];
        var iters = args.Skip(1).Select(x => PyOps.GetIter(interp, x)).ToArray();
        if (iters.Length == 0)
            yield break;
        while (true)
        {
            var row = new object[iters.Length];
            for (int i = 0; i < iters.Length; i++)
            {
                if (!PyOps.IterNext(interp, iters[i], out row[i]))
                    yield break;
            }
            yield return interp.Call(fn, row);
        }
    }

    private static IEnumerable<object> ZipImpl(Interp interp, object[] iterables)
    {
        // zip() with no arguments → empty iterator (avoids an infinite loop)
        if (iterables.Length == 0)
            yield break;
        var iters = iterables.Select(x => PyOps.GetIter(interp, x)).ToArray();
        while (true)
        {
            var row = new object[iters.Length];
            for (int i = 0; i < iters.Length; i++)
            {
                if (!PyOps.IterNext(interp, iters[i], out row[i]))
                    yield break;
            }
            yield return new PyTuple(row);
        }
    }

    private static object MinMax(Interp interp, object[] args, Dictionary<string, object>? kwargs, bool min)
    {
        object? key = null;
        object? defaultValue = null;
        if (kwargs is not null)
        {
            if (kwargs.TryGetValue("key", out var k) && k is not PyNone)
                key = k;
            if (kwargs.TryGetValue("default", out var dv))
                defaultValue = dv;
        }

        string fname = min ? "min" : "max";
        if (args.Length == 0)
            throw PyErr.TypeError($"{fname} expected at least 1 argument, got 0");

        var items = args.Length == 1 ? PyOps.Iterate(interp, args[0]).ToList() : args.ToList();
        if (items.Count == 0)
        {
            if (defaultValue is not null)
                return defaultValue;
            throw PyErr.ValueError($"{fname}() arg is an empty sequence");
        }

        object best = items[0];
        object bestKey = key is null ? best : interp.Call(key, new[] { best });
        foreach (var item in items.Skip(1))
        {
            var itemKey = key is null ? item : interp.Call(key, new[] { item });
            int cmp = interp.Compare(itemKey, bestKey);
            if (min ? cmp < 0 : cmp > 0)
            {
                best = item;
                bestKey = itemKey;
            }
        }
        return best;
    }
}
