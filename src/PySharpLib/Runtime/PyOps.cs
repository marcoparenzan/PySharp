// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using System.Text;
using PySharpLib.Interpretation;

namespace PySharpLib.Runtime;

/// <summary>Python iterator over a C# IEnumerator.</summary>
public sealed class PyIterator
{
    public IEnumerator<object> Enumerator { get; }
    public PyIterator(IEnumerator<object> enumerator) => Enumerator = enumerator;
}

/// <summary>Fundamental operations on Python values.</summary>
public static class PyOps
{
    // ---------------------------------------------------------------- types

    public static string TypeName(object o) => o switch
    {
        PyNone => "NoneType",
        bool => "bool",
        BigInteger => "int",
        double => "float",
        string => "str",
        PyBytes => "bytes",
        PyByteArray => "bytearray",
        PyList => "list",
        PyTuple => "tuple",
        PyDict => "dict",
        PySet => "set",
        PyFrozenSet => "frozenset",
        PyRange => "range",
        PySlice => "slice",
        PyFunction or PyBuiltinFunction => "function",
        PyCode => "code",
        PyBoundMethod => "method",
        PyClass => "type",
        PyInstance i => i.Class.Name,
        PyModule => "module",
        PyIterator or PyGenerator => "iterator",
        PyCoroutine => "coroutine",
        PyTask => "Task",
        PyFuture => "Future",
        PyEventLoop => "AbstractEventLoop",
        ClrObject clr => clr.Type.Name,
        ClrType => "type",
        ClrMethod => "builtin_function_or_method",
        PyEllipsis => "ellipsis",
        PyNotImplemented => "NotImplementedType",
        _ => o.GetType().Name,
    };

    // ---------------------------------------------------------------- truthiness

    public static bool Truthy(Interp interp, object o) => o switch
    {
        PyNone => false,
        bool b => b,
        BigInteger i => !i.IsZero,
        double d => d != 0.0,
        string s => s.Length > 0,
        PyBytes b => b.Length > 0,
        PyByteArray b => b.Data.Count > 0,
        PyList l => l.Items.Count > 0,
        PyTuple t => t.Items.Length > 0,
        PyDict d => d.Count > 0,
        PySet s => s.Items.Count > 0,
        PyFrozenSet s => s.Items.Count > 0,
        PyRange r => r.Count > 0,
        PyInstance inst => InstanceTruthy(interp, inst),
        _ => true,
    };

    private static bool InstanceTruthy(Interp interp, PyInstance inst)
    {
        if (interp.TryCallMethod(inst, "__bool__", Array.Empty<object>(), out var r))
            return r is bool b ? b : throw PyErr.TypeError("__bool__ should return bool");
        if (interp.TryCallMethod(inst, "__len__", Array.Empty<object>(), out var len))
            return !AsBigInt(len, "__len__").IsZero;
        return true;
    }

    // ---------------------------------------------------------------- numbers

    public static bool IsNumber(object o) => o is bool or BigInteger or double;

    public static BigInteger AsBigInt(object o, string what) => o switch
    {
        BigInteger i => i,
        bool b => b ? BigInteger.One : BigInteger.Zero,
        // IntEnum members: the value attribute is the underlying integer
        PyInstance inst when inst.Dict.TryGet("value", out var v) && v is BigInteger ev => ev,
        _ => throw PyErr.TypeError($"{what}: expected int, got {TypeName(o)}"),
    };

    public static double AsDouble(object o) => o switch
    {
        double d => d,
        BigInteger i => (double)i,
        bool b => b ? 1.0 : 0.0,
        PyInstance inst when inst.Dict.TryGet("value", out var v) && v is BigInteger ev => (double)ev,
        _ => throw PyErr.TypeError($"expected number, got {TypeName(o)}"),
    };

    /// <summary>Sequence index (supports negatives). Raises IndexError if out of range.</summary>
    public static int SeqIndex(object index, int len, string typeName)
    {
        var bi = index is bool b ? (b ? BigInteger.One : BigInteger.Zero)
            : index as BigInteger? ?? throw PyErr.TypeError(
                $"{typeName} indices must be integers, not {TypeName(index)}");
        var i = (int)bi;
        if (i < 0)
            i += len;
        if (i < 0 || i >= len)
            throw PyErr.IndexError($"{typeName} index out of range");
        return i;
    }

    // ---------------------------------------------------------------- equality and hash

    /// <summary>Python equality for builtin types (instances use identity; __eq__ is handled by Interp.RichCompare).</summary>
    public static bool PyEquals(object a, object b)
    {
        if (ReferenceEquals(a, b))
            return true;
        switch (a)
        {
            case bool ab when b is bool bb: return ab == bb;
            case bool or BigInteger or double when IsNumber(b):
            {
                if (a is double || b is double)
                {
                    double da = AsDouble(a), db = AsDouble(b);
                    // large ints vs double: comparison via double is sufficient for our uses
                    return da == db;
                }
                return AsBigInt(a, "eq") == AsBigInt(b, "eq");
            }
            case string sa when b is string sb: return sa == sb;
            case PyBytes ba when b is PyBytes bb: return ba.Equals(bb);
            case PyByteArray baa when b is PyByteArray bab: return baa.Data.SequenceEqual(bab.Data);
            case PyByteArray baa when b is PyBytes bab: return baa.Data.SequenceEqual(bab.Data);
            case PyBytes bab2 when b is PyByteArray baa2: return baa2.Data.SequenceEqual(bab2.Data);
            case PyTuple ta when b is PyTuple tb:
                if (ta.Items.Length != tb.Items.Length)
                    return false;
                for (int i = 0; i < ta.Items.Length; i++)
                    if (!PyEquals(ta.Items[i], tb.Items[i]))
                        return false;
                return true;
            case PyList la when b is PyList lb:
                if (la.Items.Count != lb.Items.Count)
                    return false;
                for (int i = 0; i < la.Items.Count; i++)
                    if (!PyEquals(la.Items[i], lb.Items[i]))
                        return false;
                return true;
            case PyDict da when b is PyDict db:
                if (da.Count != db.Count)
                    return false;
                foreach (var e in da.Entries)
                {
                    if (!db.TryGet(e.Key, out var v) || !PyEquals(e.Value, v))
                        return false;
                }
                return true;
            case PySet seta when b is PySet setb:
                return seta.Items.SetEquals(setb.Items);
            case PyFrozenSet fa when b is PyFrozenSet fb:
                return fa.Items.SetEquals(fb.Items);
            case PyNone when b is PyNone: return true;
            case PyRange ra when b is PyRange rb:
            {
                // two ranges are equal if they represent the same sequence
                var lenA = ra.Count;
                if (lenA != rb.Count)
                    return false;
                if (lenA == 0)
                    return true;
                if (ra.Start != rb.Start)
                    return false;
                return lenA == 1 || ra.Step == rb.Step;
            }
        }
        return false;
    }

    public static int PyHash(object o) => o switch
    {
        PyNone => 0,
        bool b => b ? 1 : 0,
        BigInteger i => i.GetHashCode() == int.MinValue ? 0 : HashBigInt(i),
        double d => HashDouble(d),
        string s => s.GetHashCode(),
        PyBytes b => b.GetHashCode(),
        PyTuple t => HashTuple(t),
        PyFrozenSet f => f.Items.Aggregate(0, (acc, x) => acc ^ PyHash(x)),
        PyList or PyDict or PySet or PyByteArray
            => throw PyErr.TypeError($"unhashable type: '{TypeName(o)}'"),
        _ => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o),
    };

    private static int HashBigInt(BigInteger i) => i.GetHashCode();

    private static int HashDouble(double d)
        // Guarantees hash(1) == hash(1.0) == hash(True)
        => d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e18
            ? HashBigInt(new BigInteger(d))
            : d.GetHashCode();

    private static int HashTuple(PyTuple t)
    {
        var h = new HashCode();
        foreach (var item in t.Items)
            h.Add(PyHash(item));
        return h.ToHashCode();
    }

    // ---------------------------------------------------------------- repr / str

    /// <summary>Containers being repr'd on the same thread, to catch cycles.</summary>
    [ThreadStatic]
    private static HashSet<object>? _reprGuard;

    public static string Repr(Interp interp, object o)
    {
        switch (o)
        {
            case PyNone: return "None";
            case bool b: return b ? "True" : "False";
            case BigInteger i: return i.ToString(CultureInfo.InvariantCulture);
            case double d: return ReprDouble(d);
            case string s: return ReprString(s);
            case PyBytes b: return ReprBytes(b.Data);
            case PyByteArray b: return $"bytearray({ReprBytes(b.Data.ToArray())})";
            case PyList l:
                if (!ReprEnter(l)) return "[...]";
                try { return $"[{string.Join(", ", l.Items.Select(x => Repr(interp, x)))}]"; }
                finally { ReprLeave(l); }
            case PyTuple t:
                if (!ReprEnter(t)) return "(...)";
                try
                {
                    return t.Items.Length == 1
                        ? $"({Repr(interp, t.Items[0])},)"
                        : $"({string.Join(", ", t.Items.Select(x => Repr(interp, x)))})";
                }
                finally { ReprLeave(t); }
            case PyDict d:
                if (!ReprEnter(d)) return "{...}";
                try { return $"{{{string.Join(", ", d.Entries.Select(e => $"{Repr(interp, e.Key)}: {Repr(interp, e.Value)}"))}}}"; }
                finally { ReprLeave(d); }
            case PySet s: return s.Items.Count == 0
                ? "set()"
                : $"{{{string.Join(", ", s.Items.Select(x => Repr(interp, x)))}}}";
            case PyFrozenSet s: return $"frozenset({{{string.Join(", ", s.Items.Select(x => Repr(interp, x)))}}})";
            case PyRange r: return r.Step.IsOne
                ? $"range({r.Start}, {r.Stop})"
                : $"range({r.Start}, {r.Stop}, {r.Step})";
            case PySlice s: return $"slice({Repr(interp, s.Start)}, {Repr(interp, s.Stop)}, {Repr(interp, s.Step)})";
            case PyClass c: return $"<class '{c.Name}'>";
            case PyModule m: return $"<module '{m.Name}'>";
            case PyInstance inst:
                if (interp.TryCallMethod(inst, "__repr__", Array.Empty<object>(), out var r2))
                    return r2 as string ?? throw PyErr.TypeError("__repr__ returned non-string");
                return $"<{inst.Class.Name} object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(inst):x8}>";
            default:
                return o.ToString() ?? "<unknown>";
        }
    }

    /// <summary>Registers a container in the guard; false if already present (cycle).</summary>
    private static bool ReprEnter(object container)
    {
        _reprGuard ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        return _reprGuard.Add(container);
    }

    private static void ReprLeave(object container) => _reprGuard?.Remove(container);

    public static string Str(Interp interp, object o)
    {
        switch (o)
        {
            case string s: return s;
            case PyInstance inst:
                if (interp.TryCallMethod(inst, "__str__", Array.Empty<object>(), out var r))
                    return r as string ?? throw PyErr.TypeError("__str__ returned non-string");
                return Repr(interp, o);
            default:
                return Repr(interp, o);
        }
    }

    public static string ReprDouble(double d)
    {
        if (double.IsPositiveInfinity(d)) return "inf";
        if (double.IsNegativeInfinity(d)) return "-inf";
        if (double.IsNaN(d)) return "nan";
        string s = d.ToString("R", CultureInfo.InvariantCulture);
        if (!s.Contains('.') && !s.Contains('e') && !s.Contains('E'))
            s += ".0";
        return s.Replace("E", "e");
    }

    public static string ReprString(string s)
    {
        char quote = s.Contains('\'') && !s.Contains('"') ? '"' : '\'';
        var sb = new StringBuilder(s.Length + 2);
        sb.Append(quote);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c == quote)
                        sb.Append('\\').Append(c);
                    else if (c < 32 || c == 127)
                        sb.Append($"\\x{(int)c:x2}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append(quote);
        return sb.ToString();
    }

    public static string ReprBytes(IReadOnlyList<byte> data)
    {
        var sb = new StringBuilder(data.Count + 3);
        sb.Append("b'");
        foreach (byte bt in data)
        {
            switch (bt)
            {
                case (byte)'\\': sb.Append("\\\\"); break;
                case (byte)'\'': sb.Append("\\'"); break;
                case (byte)'\n': sb.Append("\\n"); break;
                case (byte)'\r': sb.Append("\\r"); break;
                case (byte)'\t': sb.Append("\\t"); break;
                default:
                    if (bt < 32 || bt > 126)
                        sb.Append($"\\x{bt:x2}");
                    else
                        sb.Append((char)bt);
                    break;
            }
        }
        sb.Append('\'');
        return sb.ToString();
    }

    // ---------------------------------------------------------------- iteration

    public static object GetIter(Interp interp, object o)
    {
        switch (o)
        {
            case PyIterator or PyGenerator:
                return o;
            case PyList l: return new PyIterator(SnapshotList(l).GetEnumerator());
            case PyTuple t: return new PyIterator(((IEnumerable<object>)t.Items).GetEnumerator());
            case string s: return new PyIterator(s.Select(c => (object)c.ToString()).GetEnumerator());
            case PyBytes b: return new PyIterator(b.Data.Select(x => (object)new BigInteger(x)).GetEnumerator());
            case PyByteArray b: return new PyIterator(b.Data.Select(x => (object)new BigInteger(x)).ToList().GetEnumerator());
            case PyDict d: return new PyIterator(d.Keys.ToList().GetEnumerator());
            case PySet s: return new PyIterator(s.Items.ToList().GetEnumerator());
            case PyFrozenSet s: return new PyIterator(s.Items.ToList().GetEnumerator());
            case PyRange r: return new PyIterator(r.Enumerate().GetEnumerator());
            case PyInstance inst:
            {
                if (interp.TryCallMethod(inst, "__iter__", Array.Empty<object>(), out var it))
                    return it;
                if (inst.Class.TryLookup("__getitem__", out _))
                    return new PyIterator(GetItemIterator(interp, inst).GetEnumerator());
                throw PyErr.TypeError($"'{TypeName(o)}' object is not iterable");
            }
            case PyClass cls when cls.Dict.TryGet("__members__", out var membersObj) && membersObj is PyDict members:
                // iteration over an enum class → its members
                return new PyIterator(members.Values.ToList().GetEnumerator());
            case ClrObject clr when ClrBinder.TryEnumerate(clr) is { } items:
                return new PyIterator(items.GetEnumerator());
            default:
                throw PyErr.TypeError($"'{TypeName(o)}' object is not iterable");
        }
    }

    private static IEnumerable<object> SnapshotList(PyList l)
    {
        // Iterate by index like CPython (allows append during the loop)
        for (int i = 0; i < l.Items.Count; i++)
            yield return l.Items[i];
    }

    private static IEnumerable<object> GetItemIterator(Interp interp, PyInstance inst)
    {
        var i = BigInteger.Zero;
        while (true)
        {
            object v;
            try
            {
                v = interp.CallMethod(inst, "__getitem__", new object[] { i });
            }
            catch (PyRaise ex) when (PyErr.Matches(ex.Value, PyErr.IndexErrorClass))
            {
                yield break;
            }
            yield return v;
            i += 1;
        }
    }

    /// <summary>Advances the iterator. False if exhausted (StopIteration absorbed).</summary>
    public static bool IterNext(Interp interp, object iter, out object value)
    {
        switch (iter)
        {
            case PyIterator it:
                if (it.Enumerator.MoveNext())
                {
                    value = it.Enumerator.Current;
                    return true;
                }
                value = PyNone.Instance;
                return false;
            case PyGenerator gen:
                return gen.MoveNext(interp, out value);
            case PyInstance inst:
                try
                {
                    value = interp.CallMethod(inst, "__next__", Array.Empty<object>());
                    return true;
                }
                catch (PyRaise ex) when (PyErr.Matches(ex.Value, PyErr.StopIterationClass))
                {
                    value = PyNone.Instance;
                    return false;
                }
            default:
                throw PyErr.TypeError($"'{TypeName(iter)}' object is not an iterator");
        }
    }

    public static IEnumerable<object> Iterate(Interp interp, object o)
    {
        var iter = GetIter(interp, o);
        while (IterNext(interp, iter, out var v))
            yield return v;
    }

    // ---------------------------------------------------------------- len / contains

    public static int Len(Interp interp, object o) => o switch
    {
        string s => s.Length,
        PyBytes b => b.Length,
        PyByteArray b => b.Data.Count,
        PyList l => l.Items.Count,
        PyTuple t => t.Items.Length,
        PyDict d => d.Count,
        PySet s => s.Items.Count,
        PyFrozenSet s => s.Items.Count,
        PyRange r => (int)r.Count,
        PyInstance inst when interp.TryCallMethod(inst, "__len__", Array.Empty<object>(), out var len)
            => (int)AsBigInt(len, "__len__"),
        _ => throw PyErr.TypeError($"object of type '{TypeName(o)}' has no len()"),
    };

    public static bool Contains(Interp interp, object container, object item)
    {
        switch (container)
        {
            case string s when item is string sub: return s.Contains(sub);
            case string: throw PyErr.TypeError("'in <string>' requires string as left operand");
            case PyBytes b when item is PyBytes sub:
                return sub.Length == 0 || b.Data.AsSpan().IndexOf(sub.Data) >= 0;
            case PyBytes b when item is BigInteger i: return b.Data.Contains((byte)i);
            case PyDict d: return d.ContainsKey(item);
            case PySet s: return s.Items.Contains(item);
            case PyFrozenSet s: return s.Items.Contains(item);
            case PyInstance inst when interp.TryCallMethod(inst, "__contains__", new[] { item }, out var r):
                return Truthy(interp, r);
            default:
                foreach (var v in Iterate(interp, container))
                {
                    if (interp.RichEquals(v, item))
                        return true;
                }
                return false;
        }
    }
}
