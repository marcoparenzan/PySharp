// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using TorchSharp;
using static TorchSharp.torch;
using Tensor = TorchSharp.torch.Tensor;

namespace PySharpLib.Torch;

/// <summary>What a Python tensor object carries: the libtorch tensor. Implements <see cref="IPyNumberLike"/> so a
/// one-element tensor works as an index / <c>float(t)</c>, and <see cref="INdArrayConvertible"/> so numpy and matplotlib
/// accept tensors.</summary>
internal sealed class TBox : IPyNumberLike, INdArrayConvertible
{
    public Tensor T { get; }
    public TBox(Tensor t) => T = t;

    public bool TryAsBigInt(out BigInteger value)
    {
        value = default;
        if (T.numel() != 1) return false;
        if (T.dtype is ScalarType.Float16 or ScalarType.Float32 or ScalarType.Float64 or ScalarType.BFloat16) return false;
        value = new BigInteger(T.item<long>());
        return true;
    }

    public bool TryAsDouble(out double value)
    {
        value = default;
        if (T.numel() != 1) return false;
        value = T.to(ScalarType.Float64).item<double>();
        return true;
    }

    public NDArray ToNDArray() => TC.ToNd(T);
}

/// <summary>Python ⇄ libtorch conversions.</summary>
internal static class TC
{
    public static PyClass TensorClass = null!;
    public static PyClass ParameterClass = null!;
    public static PyClass DTypeClass = null!;
    /// <summary>Set by the Python side (<c>torch.Size</c>, a tuple subclass with torch's repr).</summary>
    public static object? SizeClass;
    public static Interp? Interp;

    // ------------------------------------------------------------------ dtypes

    private static readonly Dictionary<ScalarType, PyInstance> DTypes = new();
    private static readonly (string name, ScalarType t, bool isFloat, bool signed, int bits)[] DTypeTable =
    {
        ("float32", ScalarType.Float32, true, true, 32), ("float64", ScalarType.Float64, true, true, 64),
        ("float16", ScalarType.Float16, true, true, 16), ("bfloat16", ScalarType.BFloat16, true, true, 16),
        ("int64", ScalarType.Int64, false, true, 64), ("int32", ScalarType.Int32, false, true, 32),
        ("int16", ScalarType.Int16, false, true, 16), ("int8", ScalarType.Int8, false, true, 8),
        ("uint8", ScalarType.Byte, false, false, 8), ("bool", ScalarType.Bool, false, false, 8),
        ("complex64", ScalarType.ComplexFloat32, false, true, 64), ("complex128", ScalarType.ComplexFloat64, false, true, 128),
    };

    public static void InitDTypes(PyClass dtypeClass)
    {
        DTypeClass = dtypeClass;
        foreach (var (name, t, isFloat, _, bits) in DTypeTable)
        {
            var inst = new PyInstance(dtypeClass) { Native = t };
            inst.Dict["__name__"] = name;
            inst.Dict["is_floating_point"] = isFloat;
            inst.Dict["itemsize"] = new BigInteger(bits / 8);
            DTypes[t] = inst;
        }
    }

    public static PyInstance DTypeObj(ScalarType t) => DTypes[t];
    public static string DTypeName(ScalarType t) => DTypeTable.First(d => d.t == t).name;
    public static IEnumerable<(string name, ScalarType t)> AllDTypes => DTypeTable.Select(d => (d.name, d.t));

    public static ScalarType? ToDType(object? o) => o switch
    {
        null or PyNone => null,
        PyInstance { Native: ScalarType s } => s,
        string s => DTypeTable.FirstOrDefault(d => d.name == s.Replace("torch.", "")).t is var st && DTypeTable.Any(d => d.name == s.Replace("torch.", "")) ? st
            : throw PyErr.TypeError($"unknown dtype '{s}'"),
        _ => throw PyErr.TypeError($"expected a torch.dtype, got {PyOps.TypeName(o)}"),
    };

    public static bool IsFloat(ScalarType t) => t is ScalarType.Float16 or ScalarType.Float32 or ScalarType.Float64 or ScalarType.BFloat16;

    // ------------------------------------------------------------------ wrapping

    public static PyInstance Wrap(Tensor t) => new(TensorClass) { Native = new TBox(t) };

    public static PyInstance WrapParameter(Tensor t) => new(ParameterClass) { Native = new TBox(t) };

    public static Tensor? TryUnwrap(object? o) => o is PyInstance { Native: TBox b } ? b.T : null;

    public static bool IsTensor(object? o) => o is PyInstance { Native: TBox };

    public static Tensor Unwrap(object o) => TryUnwrap(o) ?? throw PyErr.TypeError($"expected a Tensor, got {PyOps.TypeName(o)}");

    /// <summary>A shape as Python sees it (a <c>torch.Size</c> when the Python side registered one).</summary>
    public static object SizeObj(long[] shape)
    {
        return TSize.Make((long[])shape.Clone());
    }

    // ------------------------------------------------------------------ Python → tensor

    public static ScalarType DefaultFloat => get_default_dtype();

    public static Tensor ToTensor(object o, ScalarType? dtype = null)
    {
        switch (o)
        {
            case PyInstance { Native: TBox b }: return dtype is { } d && b.T.dtype != d ? b.T.to(d) : b.T;
            case bool bv: return Scalar(bv ? 1L : 0L, dtype ?? ScalarType.Bool);
            case BigInteger bi: return Scalar((long)bi, dtype ?? ScalarType.Int64);
            case double dv: return FromDouble(dv, dtype ?? DefaultFloat);
            case PyList or PyTuple: return FromNested(o, dtype);
            default:
                if (Conv.TryND(o, out var nd)) return FromNd(nd, dtype);
                throw PyErr.TypeError($"cannot convert {PyOps.TypeName(o)} to a tensor");
        }
    }

    private static Tensor Scalar(long v, ScalarType t) => torch.tensor(v).to(t);
    private static Tensor FromDouble(double v, ScalarType t) => torch.tensor(v, ScalarType.Float64).to(t);

    /// <summary>A Python number as a 0-d tensor that takes part in promotion like a Python scalar: it follows <paramref name="like"/>'s
    /// dtype within a kind, and uses the default float dtype when a float meets an integer tensor.</summary>
    public static Tensor ScalarLike(Tensor like, object o)
    {
        switch (o)
        {
            case bool bv: return torch.tensor(bv ? 1L : 0L).to(like.dtype == ScalarType.Bool ? ScalarType.Bool : like.dtype == ScalarType.Bool ? ScalarType.Int64 : (IsFloat(like.dtype) ? like.dtype : like.dtype == ScalarType.Bool ? ScalarType.Bool : like.dtype));
            case BigInteger bi:
                if (like.dtype == ScalarType.Bool) return torch.tensor((long)bi);
                return torch.tensor((long)bi).to(IsFloat(like.dtype) ? like.dtype : IsComplex(like.dtype) ? like.dtype : like.dtype);
            case double dv:
                return FromDouble(dv, IsFloat(like.dtype) || IsComplex(like.dtype) ? like.dtype : DefaultFloat);
            default: return ToTensor(o);
        }
    }

    public static bool IsComplex(ScalarType t) => t is ScalarType.ComplexFloat32 or ScalarType.ComplexFloat64;

    public static bool IsPyNumber(object o) => o is bool or BigInteger or double;

    /// <summary>The operand of a binary op: a tensor as is, a Python number following <paramref name="like"/>, a sequence/array as a tensor.</summary>
    public static Tensor Operand(Tensor like, object o) => o switch
    {
        PyInstance { Native: TBox b } => b.T,
        bool or BigInteger or double => ScalarLike(like, o),
        _ => ToTensor(o),
    };

    private static void Walk(object o, int depth, List<long> shape, List<double> vals, int[] kind)
    {
        // kind: 0 bool, 1 int, 2 float
        switch (o)
        {
            case PyList l: Seq(l.Items); return;
            case PyTuple t: Seq(t.Items); return;
            case bool b: vals.Add(b ? 1 : 0); return;
            case BigInteger bi: vals.Add((double)bi); kind[0] = Math.Max(kind[0], 1); return;
            case double d: vals.Add(d); kind[0] = 2; return;
            case PyInstance { Native: TBox tb }:
            {
                var t = tb.T;
                var dims = t.shape;
                for (int i = 0; i < dims.Length; i++) { if (depth + i < shape.Count) { if (shape[depth + i] != dims[i]) throw PyErr.ValueError("expected sequence of equal length"); } else shape.Add(dims[i]); }
                var flat = t.to(ScalarType.Float64).flatten().data<double>().ToArray();
                vals.AddRange(flat);
                kind[0] = Math.Max(kind[0], t.dtype == ScalarType.Bool ? 0 : IsFloat(t.dtype) ? 2 : 1);
                return;
            }
            default:
                if (Conv.TryND(o, out var nd))
                {
                    var dims = nd.Shape;
                    for (int i = 0; i < dims.Length; i++) { if (depth + i < shape.Count) { if (shape[depth + i] != dims[i]) throw PyErr.ValueError("expected sequence of equal length"); } else shape.Add(dims[i]); }
                    vals.AddRange(nd.ToArray<double>());
                    kind[0] = Math.Max(kind[0], nd.DType == NDSharp.DType.Bool ? 0 : nd.DType.IsFloat() ? 2 : 1);
                    return;
                }
                throw PyErr.TypeError($"new(): invalid data type '{PyOps.TypeName(o)}'");
        }

        void Seq(IList<object> items)
        {
            if (depth < shape.Count) { if (shape[depth] != items.Count) throw PyErr.ValueError($"expected sequence of length {shape[depth]} at dim {depth} (got {items.Count})"); }
            else shape.Add(items.Count);
            foreach (var it in items) Walk(it, depth + 1, shape, vals, kind);
        }
    }

    private static Tensor FromNested(object o, ScalarType? dtype)
    {
        var shape = new List<long>(); var vals = new List<double>(); var kind = new int[1];
        Walk(o, 0, shape, vals, kind);
        ScalarType nat = kind[0] == 2 ? DefaultFloat : kind[0] == 1 ? ScalarType.Int64 : ScalarType.Bool;
        var t = torch.tensor(vals.ToArray(), shape.ToArray(), ScalarType.Float64);
        if (vals.Count == 0) t = torch.empty(shape.ToArray(), DefaultFloat);
        var target = dtype ?? (vals.Count == 0 ? DefaultFloat : nat);
        return t.dtype == target ? t : t.to(target);
    }

    public static ScalarType FromNdDType(NDSharp.DType d) => d switch
    {
        NDSharp.DType.Float32 => ScalarType.Float32, NDSharp.DType.Float64 => ScalarType.Float64, NDSharp.DType.Float16 => ScalarType.Float16,
        NDSharp.DType.Int8 => ScalarType.Int8, NDSharp.DType.Int16 => ScalarType.Int16, NDSharp.DType.Int32 => ScalarType.Int32,
        NDSharp.DType.Int64 => ScalarType.Int64, NDSharp.DType.UInt8 => ScalarType.Byte, NDSharp.DType.Bool => ScalarType.Bool,
        NDSharp.DType.UInt16 or NDSharp.DType.UInt32 or NDSharp.DType.UInt64 => ScalarType.Int64,
        _ => ScalarType.Float64,
    };

    /// <summary>A tensor holding a copy of <paramref name="data"/>: allocate, then one memcpy (torch.tensor(T[]) marshals per element).</summary>
    private static Tensor Raw<T>(T[] data, long[] dims, ScalarType st) where T : unmanaged
    {
        var t = torch.empty(dims, st);
        if (data.Length > 0) System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan()).CopyTo(t.bytes);
        return t;
    }

    public static Tensor FromNd(NDArray nd, ScalarType? dtype)
    {
        var dims = nd.Shape.Select(v => (long)v).ToArray();
        Tensor t;
        switch (nd.DType)
        {
            case NDSharp.DType.Float32: t = Raw(nd.ToArray<float>(), dims, ScalarType.Float32); break;
            case NDSharp.DType.Float64: t = Raw(nd.ToArray<double>(), dims, ScalarType.Float64); break;
            case NDSharp.DType.Int64: t = Raw(nd.ToArray<long>(), dims, ScalarType.Int64); break;
            case NDSharp.DType.Int32: t = Raw(nd.ToArray<int>(), dims, ScalarType.Int32); break;
            case NDSharp.DType.Int16: t = Raw(nd.ToArray<short>(), dims, ScalarType.Int16); break;
            case NDSharp.DType.Int8: t = Raw(nd.ToArray<sbyte>(), dims, ScalarType.Int8); break;
            case NDSharp.DType.UInt8: t = Raw(nd.ToArray<byte>(), dims, ScalarType.Byte); break;
            case NDSharp.DType.Bool: t = Raw(nd.ToArray<bool>(), dims, ScalarType.Bool); break;
            case NDSharp.DType.UInt16: case NDSharp.DType.UInt32: case NDSharp.DType.UInt64: t = Raw(nd.ToArray<long>(), dims, ScalarType.Int64); break;
            default: t = Raw(nd.ToArray<double>(), dims, ScalarType.Float64); break;
        }
        return dtype is { } d && d != t.dtype ? t.to(d) : t;
    }

    // ------------------------------------------------------------------ tensor → Python / numpy

    public static NDArray ToNd(Tensor t)
    {
        var c = t.detach().cpu().contiguous();
        var shape = c.shape.Select(v => (int)v).ToArray();
        switch (c.dtype)
        {
            case ScalarType.Float32: return NDArray.FromArray(c.data<float>().ToArray(), shape);
            case ScalarType.Float64: return NDArray.FromArray(c.data<double>().ToArray(), shape);
            case ScalarType.Int64: return NDArray.FromArray(c.data<long>().ToArray(), shape);
            case ScalarType.Int32: return NDArray.FromArray(c.data<int>().ToArray(), shape);
            case ScalarType.Int16: return NDArray.FromArray(c.data<short>().ToArray(), shape);
            case ScalarType.Int8: return NDArray.FromArray(c.data<sbyte>().ToArray(), shape);
            case ScalarType.Byte: return NDArray.FromArray(c.data<byte>().ToArray(), shape);
            case ScalarType.Bool: return NDArray.FromArray(c.data<bool>().ToArray(), shape);
            default: return NDArray.FromArray(c.to(ScalarType.Float32).data<float>().ToArray(), shape);
        }
    }

    /// <summary>The Python value of a one-element tensor (<c>t.item()</c>).</summary>
    public static object Item(Tensor t)
    {
        if (t.numel() != 1) throw PyErr.ValueError("only one element tensors can be converted to Python scalars");
        return t.dtype switch
        {
            ScalarType.Bool => t.item<bool>(),
            ScalarType.Float32 or ScalarType.Float64 or ScalarType.Float16 or ScalarType.BFloat16 => t.to(ScalarType.Float64).item<double>(),
            ScalarType.ComplexFloat32 or ScalarType.ComplexFloat64 => (object)t.item<Complex>(),
            _ => new BigInteger(t.item<long>()),
        };
    }

    public static object ToList(Tensor t)
    {
        if (t.Dimensions == 0) return Item(t);
        var c = t.detach().cpu().contiguous();
        var shape = c.shape;
        object[] flat = c.dtype switch
        {
            ScalarType.Bool => c.data<bool>().ToArray().Select(v => (object)v).ToArray(),
            ScalarType.Float32 or ScalarType.Float64 or ScalarType.Float16 or ScalarType.BFloat16 => c.to(ScalarType.Float64).data<double>().ToArray().Select(v => (object)v).ToArray(),
            _ => c.to(ScalarType.Int64).data<long>().ToArray().Select(v => (object)new BigInteger(v)).ToArray(),
        };
        int pos = 0;
        object Build(int d)
        {
            var items = new List<object>();
            for (long i = 0; i < shape[d]; i++) items.Add(d == shape.Length - 1 ? flat[pos++] : Build(d + 1));
            return new PyList(items);
        }
        return Build(0);
    }

    // ------------------------------------------------------------------ argument parsing

    public static long ToLong(object o, string what = "argument")
        => o switch
        {
            bool b => b ? 1 : 0,
            BigInteger bi => (long)bi,
            PyInstance { Native: IPyNumberLike nl } when nl.TryAsBigInt(out var v) => (long)v,
            double d when d == Math.Floor(d) => throw PyErr.TypeError($"'float' object cannot be interpreted as an integer ({what})"),
            _ => throw PyErr.TypeError($"{what} must be an integer, not {PyOps.TypeName(o)}"),
        };

    public static double ToDouble(object o)
        => o switch
        {
            bool b => b ? 1 : 0,
            BigInteger bi => (double)bi,
            double d => d,
            PyInstance { Native: IPyNumberLike nl } when nl.TryAsDouble(out var v) => v,
            _ => throw PyErr.TypeError($"expected a number, got {PyOps.TypeName(o)}"),
        };

    public static long[] ToLongs(object o)
        => o switch
        {
            PyList l => l.Items.Select(i => ToLong(i)).ToArray(),
            PyTuple t => t.Items.Select(i => ToLong(i)).ToArray(),
            PyRange rg => rg.Enumerate().Select(i => ToLong(i)).ToArray(),
            PyInstance { Native: SizeBox sb } => (long[])sb.Values.Clone(),
            PyInstance { Native: TBox b } when b.T.Dimensions >= 1 => b.T.to(ScalarType.Int64).data<long>().ToArray(),
            _ => new[] { ToLong(o) },
        };

    /// <summary>A shape given as varargs (<c>zeros(2, 3)</c>) or as one sequence (<c>zeros((2, 3))</c>).</summary>
    public static long[] ShapeArgs(object[] a, int from)
    {
        if (a.Length - from == 1 && (a[from] is PyList or PyTuple || a[from] is PyInstance { Native: SizeBox })) return ToLongs(a[from]);
        if (a.Length - from == 1 && a[from] is PyInstance { Native: TBox b } && b.T.Dimensions >= 1) return ToLongs(a[from]);
        return a.Skip(from).Select(v => ToLong(v, "size")).ToArray();
    }

    // ------------------------------------------------------------------ indexing

    public static TensorIndex[] ParseIndex(object index)
    {
        var parts = index is PyTuple t ? t.Items : new[] { index };
        var res = new List<TensorIndex>();
        foreach (var p in parts)
        {
            switch (p)
            {
                case PyNone: res.Add(TensorIndex.Null); break;
                case PyEllipsis: res.Add(TensorIndex.Ellipsis); break;
                case bool b: res.Add(TensorIndex.Bool(b)); break;
                case BigInteger bi: res.Add(TensorIndex.Single((long)bi)); break;
                case PySlice s:
                    res.Add(TensorIndex.Slice(Bound(s.Start), Bound(s.Stop), Bound(s.Step)));
                    break;
                case PyList or PyTuple: res.Add(TensorIndex.Tensor(ToTensor(p))); break;
                case PyInstance { Native: TBox tb }:
                    res.Add(tb.T.Dimensions == 0 && tb.T.dtype != ScalarType.Bool && !IsFloat(tb.T.dtype) ? TensorIndex.Single(tb.T.item<long>()) : TensorIndex.Tensor(tb.T));
                    break;
                default:
                    if (Conv.TryND(p, out var nd)) { res.Add(TensorIndex.Tensor(FromNd(nd, null))); break; }
                    throw PyErr.IndexError("only integers, slices (`:`), ellipsis (`...`), None and long or byte Variables are valid indices");
            }
        }
        return res.ToArray();

        static long? Bound(object o) => o is PyNone ? null : ToLong(o, "slice index");
    }
}
