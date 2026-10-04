// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Builtins;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>The native payload of a numpy scalar instance (an <c>np.uint8(5)</c>, <c>np.float32(1.5)</c>
/// ...): a 0-d <see cref="NDArray"/>. Implements <see cref="IPyNumberLike"/> so the interpreter
/// accepts it wherever a Python int/float is needed.</summary>
/// <summary>Implemented by another binding's array-like object (a torch tensor) so numpy and matplotlib accept it
/// wherever they take array-likes.</summary>
public interface INdArrayConvertible
{
    NDArray ToNDArray();
}

/// <summary>A tuple-like of integers owned by another binding (torch.Size), accepted as a shape / axes by numpy.</summary>
public interface IIntSequence
{
    long[] Values { get; }
}

internal sealed class ScalarBox : IPyNumberLike
{
    public NDArray Array { get; }
    public ScalarBox(NDArray array) => Array = array;

    public bool TryAsBigInt(out BigInteger value)
    {
        switch (Array.DType.Kind())
        {
            case 'b': value = (bool)Array.GetAt(0) ? BigInteger.One : BigInteger.Zero; return true;
            case 'i': value = (long)Convert.ChangeType(Array.GetAt(0), typeof(long)); return true;
            case 'u': value = (ulong)Convert.ChangeType(Array.GetAt(0), typeof(ulong)); return true;
            default: value = default; return false;
        }
    }

    public bool TryAsDouble(out double value)
    {
        value = Array.GetDouble(0);
        return true;
    }
}

/// <summary>The native payload of a <c>numpy.dtype</c> instance.</summary>
internal sealed class DTypeBox
{
    public DType Value { get; }
    public DTypeBox(DType value) => Value = value;
}

/// <summary>Conversions between Python values and NDSharp arrays.</summary>
internal static class Conv
{
    // ------------------------------------------------------------------ wrapping NDSharp → Python

    /// <summary>The Python object for an array. One wrapper per array is cached on the array itself so
    /// identity survives round trips (<c>view.base is arr</c>).</summary>
    public static PyInstance Wrap(NDArray a)
    {
        if (a.Tag is PyInstance cached && ReferenceEquals(cached.Native, a))
            return cached;
        var inst = new PyInstance(Classes.NdArray) { Native = a };
        a.Tag = inst;
        return inst;
    }

    /// <summary>A result as Python sees it: 0-d becomes a scalar, otherwise an ndarray. (numpy turns
    /// every 0-d ufunc/reduction result into an array scalar the same way.)</summary>
    public static object Result(NDArray a) => a.Ndim == 0 ? Scalarize(a) : Wrap(a);

    /// <summary>float64 → Python float, int64 → Python int, bool → Python bool; every other dtype
    /// becomes a numpy scalar instance (<c>np.uint8(200)</c>) so its width and wrap-around survive.</summary>
    public static object Scalarize(NDArray a)
    {
        var v = a.GetAt(0);
        return a.DType switch
        {
            DType.Float64 => (double)v,
            DType.Int64 => new BigInteger((long)v),
            DType.Bool => (bool)v,
            DType.Complex128 => ComplexType.Make((Complex)v),
            _ => new PyInstance(Classes.ScalarClass(a.DType)) { Native = new ScalarBox(a.Ndim == 0 ? a.Copy() : a) },
        };
    }

    /// <summary>A CLR boxed element (bool/long/double/...) as a Python value, ignoring dtype width.</summary>
    public static object BoxedToPython(object v) => v switch
    {
        bool b => b,
        double d => d,
        Complex c => ComplexType.Make(c),
        float f => (double)f,
        Half h => (double)h,
        sbyte x => new BigInteger(x),
        byte x => new BigInteger(x),
        short x => new BigInteger(x),
        ushort x => new BigInteger(x),
        int x => new BigInteger(x),
        uint x => new BigInteger(x),
        long x => new BigInteger(x),
        ulong x => new BigInteger(x),
        _ => throw new NDTypeException($"cannot convert {v.GetType().Name} to a Python value"),
    };

    // ------------------------------------------------------------------ unwrapping Python → NDSharp

    public static NDArray? TryUnwrap(object o) => o switch
    {
        PyInstance { Native: NDArray nd } => nd,
        PyInstance { Native: ScalarBox sb } => sb.Array,
        _ => null,
    };

    public static bool IsNdArray(object o) => o is PyInstance { Native: NDArray };

    /// <summary>Operand conversion: ndarray/numpy scalar as is, Python numbers as *weak* scalars
    /// (NEP 50: they adapt to the other operand's dtype), sequences as arrays.</summary>
    public static bool TryND(object o, out NDArray nd)
    {
        switch (o)
        {
            case PyInstance { Native: NDArray a }: nd = a; return true;
            case PyInstance { Native: ScalarBox sb }: nd = sb.Array; return true;
            case bool b: nd = NDArray.WeakScalar(b); return true;
            case BigInteger bi:
                if (bi >= long.MinValue && bi <= long.MaxValue) nd = NDArray.WeakScalar((long)bi);
                else if (bi > 0 && bi <= ulong.MaxValue) nd = NDArray.WeakScalar((ulong)bi);
                else throw new NDOverflowException($"Python int too large to convert to C long");
                return true;
            case double d: nd = NDArray.WeakScalar(d); return true;
            case PyInstance ci when ci.Class == ComplexType.ComplexClass && ci.Dict.TryGet("__value__", out var cv) && cv is Complex cc:
                nd = NDArray.WeakScalar(cc); return true;
            case PyList or PyTuple: nd = FromSequence(o, null); return true;
            case PyRange rg: nd = FromSequence(new PyList(rg.Enumerate()), null); return true;
            case ClrObject { Instance: Array arr }: nd = FromClrArray(arr); return true;
            case PyInstance { Native: INdArrayConvertible conv }: nd = conv.ToNDArray(); return true;
            default:
                foreach (var hook in Converters)
                    if (hook(o) is { } converted) { nd = converted; return true; }
                nd = null!; return false;
        }
    }

    /// <summary>Hooks other bindings install to make their objects array-likes (pandas Series/DataFrame), tried when no built-in case matches.</summary>
    public static readonly List<Func<object, NDArray?>> Converters = new();

    public static NDArray ND(object o)
        => TryND(o, out var nd) ? nd : throw PyErr.TypeError($"unsupported operand type for numpy: '{PyOps.TypeName(o)}'");

    /// <summary>Like <see cref="ND"/> but Python numbers get their default (non-weak) dtype —
    /// the conversion <c>np.array(x)</c>/<c>np.asarray(x)</c> performs.</summary>
    public static NDArray NDStrong(object o) => o switch
    {
        bool b => NDArray.Scalar(b),
        BigInteger bi => bi >= long.MinValue && bi <= long.MaxValue ? NDArray.Scalar((long)bi)
            : bi > 0 && bi <= ulong.MaxValue ? np.Reshape(NDArray.FromArray(new[] { (ulong)bi }), Array.Empty<int>())
            : throw new NDOverflowException("Python int too large to convert to C long"),
        double d => NDArray.Scalar(d),
        _ => ND(o),
    };

    // ------------------------------------------------------------------ sequences → arrays

    /// <summary>numpy <c>array(seq)</c>: shape from nesting, dtype from the leaves (bool → int64 →
    /// float64), nested arrays stacked.</summary>
    public static NDArray FromSequence(object seq, DType? dtype)
    {
        var items = seq switch
        {
            PyList l => (IReadOnlyList<object>)l.Items,
            PyTuple t => t.Items,
            _ => throw new NDTypeException("not a sequence"),
        };
        NDArray result;
        if (items.Count == 0)
            result = np.Zeros(new[] { 0 }, dtype ?? DType.Float64);
        else if (items.All(static x => x is bool or BigInteger or double))
            result = FromScalars(items);
        else
        {
            var children = new List<NDArray>(items.Count);
            foreach (var it in items)
                children.Add(it is PyList or PyTuple ? FromSequence(it, null) : NDStrong(it));
            for (int i = 1; i < children.Count; i++)
                if (!children[i].Shape.SequenceEqual(children[0].Shape))
                    throw new NDValueException(
                        "setting an array element with a sequence. The requested array has an inhomogeneous shape after 1 dimensions. "
                        + $"The detected shape was ({items.Count},) + inhomogeneous part.");
            result = np.Stack(children, 0);
        }
        return dtype is DType dt && dt != result.DType ? result.AsType(dt) : result;
    }

    private static NDArray FromScalars(IReadOnlyList<object> items)
    {
        bool anyFloat = false, anyInt = false, anyBool = false, anyBig = false;
        foreach (var x in items)
        {
            switch (x)
            {
                case double: anyFloat = true; break;
                case BigInteger bi:
                    anyInt = true;
                    if (bi > long.MaxValue || bi < long.MinValue) anyBig = true;
                    break;
                default: anyBool = true; break;
            }
        }
        int n = items.Count;
        if (anyFloat)
        {
            var buf = new double[n];
            for (int i = 0; i < n; i++)
                buf[i] = items[i] switch { double d => d, BigInteger bi => (double)bi, bool b => b ? 1.0 : 0.0, _ => 0.0 };
            return NDArray.FromArray(buf, n);
        }
        if (anyInt)
        {
            if (anyBig)
            {
                var ubuf = new ulong[n];
                for (int i = 0; i < n; i++)
                {
                    var bi = items[i] is BigInteger v ? v : (items[i] is true ? BigInteger.One : BigInteger.Zero);
                    if (bi < 0 || bi > ulong.MaxValue) throw new NDOverflowException("Python int too large to convert to C long");
                    ubuf[i] = (ulong)bi;
                }
                return NDArray.FromArray(ubuf, n);
            }
            var buf = new long[n];
            for (int i = 0; i < n; i++)
                buf[i] = items[i] switch { BigInteger bi => (long)bi, bool b => b ? 1L : 0L, _ => 0L };
            return NDArray.FromArray(buf, n);
        }
        var bb = new bool[n];
        for (int i = 0; i < n; i++) bb[i] = (bool)items[i];
        _ = anyBool;
        return NDArray.FromArray(bb, n);
    }

    /// <summary>A host <c>double[]</c>/<c>int[]</c>/<c>byte[]</c>/... (arriving as a ClrObject) → array (copied).</summary>
    public static NDArray FromClrArray(Array arr)
    {
        var et = arr.GetType().GetElementType()!;
        if (arr.Rank == 1 && DTypesHas(et))
        {
            var dt = DTypes.FromClrType(et);
            var copy = (Array)arr.Clone();
            return new NDArray(dt, copy, new[] { arr.Length });
        }
        if (DTypesHas(et)) return NDArray.FromMultiDim(arr);
        throw new NDTypeException($"cannot make an array from a .NET {et.Name}[]");
    }

    private static bool DTypesHas(Type t)
    {
        try { DTypes.FromClrType(t); return true; }
        catch (NDException) { return false; }
    }

    // ------------------------------------------------------------------ shapes, axes, dtypes

    public static int ToInt(object o, string what)
    {
        var big = PyOps.AsBigInt(o is PyInstance { Native: NDArray { Ndim: 0 } nd0 } ? Scalarize(nd0) : o, what);
        if (big > int.MaxValue || big < int.MinValue) throw PyErr.OverflowError("Python int too large to convert to C int");
        return (int)big;
    }

    public static int[] ToShape(object o) => o switch
    {
        PyTuple t => t.Items.Select(x => ToInt(x, "shape")).ToArray(),
        PyList l => l.Items.Select(x => ToInt(x, "shape")).ToArray(),
        PyInstance { Native: IIntSequence sq } => sq.Values.Select(x => (int)x).ToArray(),
        _ when IsNdArray(o) => TryUnwrap(o)!.ToArray<long>().Select(x => (int)x).ToArray(),
        _ => new[] { ToInt(o, "shape") },
    };

    public static int[]? ToAxes(object? o) => o switch
    {
        null or PyNone => null,
        PyTuple t => t.Items.Select(x => ToInt(x, "axis")).ToArray(),
        PyList l => l.Items.Select(x => ToInt(x, "axis")).ToArray(),
        PyInstance { Native: IIntSequence sq } => sq.Values.Select(x => (int)x).ToArray(),
        _ => new[] { ToInt(o, "axis") },
    };

    public static int? ToAxis(object? o) => o is null or PyNone ? null : ToInt(o, "axis");

    public static DType? ToDType(object? o)
    {
        switch (o)
        {
            case null or PyNone: return null;
            case PyInstance { Native: DTypeBox box }: return box.Value;
            case PyClass cls when Classes.TryDTypeOfClass(cls, out var dt): return dt;
            case string s: return DTypes.TryFromName(s, out var d) ? d : throw PyErr.TypeError($"data type '{s}' not understood");
            // a dtype name unpickled from a Python-2 file (encoding="bytes") arrives as bytes, e.g. b'u1' or b'<f4'
            case PyBytes pb:
                return ToDType(System.Text.Encoding.Latin1.GetString(pb.Data));
            case PyClass c when c == ComplexType.ComplexClass: return DType.Complex128;
            case PyBuiltinFunction { Name: "float" }: return DType.Float64;
            case PyBuiltinFunction { Name: "int" }: return DType.Int64;
            case PyBuiltinFunction { Name: "bool" }: return DType.Bool;
            default: throw PyErr.TypeError($"Cannot interpret '{PyOps.TypeName(o)}' object as a data type");
        }
    }

    // ------------------------------------------------------------------ Python index → NDIndex

    public static (NDIndex[] items, bool allInts) ParseIndex(object index)
    {
        var parts = index is PyTuple t ? t.Items : new[] { index };
        var items = new NDIndex[parts.Length];
        bool allInts = true;
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            switch (p)
            {
                case PyNone: items[i] = NDIndex.NewAxis; allInts = false; break;
                case PyEllipsis: items[i] = NDIndex.Ellipsis; allInts = false; break;
                case PySlice s:
                    items[i] = new Slice(SliceBound(s.Start), SliceBound(s.Stop), SliceBound(s.Step));
                    allInts = false;
                    break;
                case BigInteger bi: items[i] = (int)bi; break;
                case bool: throw PyErr.IndexError("boolean scalar indices are not supported");
                case PyList or PyTuple:
                {
                    var arr = FromSequence(p, null);
                    if (arr.Size == 0) arr = arr.AsType(DType.Int64);
                    items[i] = arr;
                    allInts = false;
                    break;
                }
                default:
                {
                    var nd = TryUnwrap(p);
                    if (nd is null)
                        throw PyErr.IndexError("only integers, slices (`:`), ellipsis (`...`), numpy.newaxis (`None`) and integer or boolean arrays are valid indices");
                    if (nd.Ndim == 0 && nd.DType.IsInteger()) items[i] = ToInt(Scalarize(nd), "index");
                    else
                    {
                        if (!nd.DType.IsInteger() && nd.DType != DType.Bool)
                            throw PyErr.IndexError("arrays used as indices must be of integer (or boolean) type");
                        items[i] = nd;
                        allInts = false;
                    }
                    break;
                }
            }
        }
        return (items, allInts);
    }

    private static int? SliceBound(object o)
    {
        if (o is PyNone) return null;
        var big = PyOps.AsBigInt(o is PyInstance { Native: NDArray { Ndim: 0 } nd0 } ? Scalarize(nd0) : o, "slice index");
        return big > int.MaxValue ? int.MaxValue : big < int.MinValue ? int.MinValue : (int)big;
    }
}
