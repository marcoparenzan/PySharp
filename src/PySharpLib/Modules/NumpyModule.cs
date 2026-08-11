// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>The real, if v1-scoped, payload behind a shim `ndarray` — a flat C-order buffer plus
/// shape/strides, mirroring real numpy's own memory model closely enough that later phases (views,
/// `.tobytes()`) aren't fighting the representation. Float64-only for Phase 1 (see
/// NUMPY_PLAN.md's dtype rollout: `bool` lands with comparisons, `int64` later, with promotion).
/// Strides are element counts, not bytes (byte-level strides are a `.tobytes()`-era concern, not
/// needed yet).</summary>
public sealed class NdArrayData
{
    public DType DType { get; }
    public Array Buffer { get; }
    public int[] Shape { get; }
    public int[] Strides { get; }
    public int Ndim => Shape.Length;
    public int Size { get; }

    public NdArrayData(DType dtype, Array buffer, int[] shape)
    {
        DType = dtype;
        Buffer = buffer;
        Shape = shape;
        Strides = ComputeStrides(shape);
        Size = shape.Aggregate(1, (acc, dim) => acc * dim);
    }

    /// <summary>Real C-order (row-major) strides: the last axis is contiguous (stride 1), each
    /// earlier axis's stride is the product of every axis size to its right.</summary>
    private static int[] ComputeStrides(int[] shape)
    {
        var strides = new int[shape.Length];
        int acc = 1;
        for (int i = shape.Length - 1; i >= 0; i--)
        {
            strides[i] = acc;
            acc *= shape[i];
        }
        return strides;
    }
}

public enum DType
{
    Float64,
}

/// <summary>numpy: a C# `numpy`-shaped shim, NOT the real numpy — real numpy is a CPython C
/// extension a from-scratch interpreter cannot load. See NUMPY_PLAN.md for the full phased plan
/// and the architecture decisions (`ndarray` as a `PyClass` + C# wrap, exactly like the `socket`
/// module, so arithmetic/indexing/iteration reuse the interpreter's existing dunder dispatch with
/// no core changes).
///
/// Phase 1 (this file, so far): the `ndarray` core — attributes and repr/str only. Not yet
/// user-constructible from Python (no `np.array`/`np.zeros`/... — that's Phase 2); the internal
/// `_fromflat` builtin exists purely so tests can build one to exercise the attributes/formatting
/// before real construction lands.</summary>
public static class NumpyModule
{
    public const string ShimVersion = "0.0.1 (PySharp shim)";

    public static readonly PyClass NdArrayClass = BuildNdArrayClass();
    public static readonly PyClass DTypeClass = BuildDTypeClass();
    public static readonly PyInstance Float64DType = MakeDType("float64");

    public static PyModule Create()
    {
        var m = new PyModule("numpy");
        m.Dict["__version__"] = ShimVersion;

        m.Dict["_fromflat"] = new PyBuiltinFunction("_fromflat", (interp, a, _) =>
        {
            double[] flat = PyOps.Iterate(interp, a[0]).Select(PyOps.AsDouble).ToArray();
            int[] shape = PyOps.Iterate(interp, a[1]).Select(x => (int)PyOps.AsBigInt(x, "shape")).ToArray();
            int expected = shape.Aggregate(1, (acc, dim) => acc * dim);
            if (flat.Length != expected)
                throw PyErr.ValueError($"_fromflat: {flat.Length} values do not match shape size {expected}");
            return Wrap(new NdArrayData(DType.Float64, flat, shape));
        });

        return m;
    }

    public static PyInstance Wrap(NdArrayData data)
    {
        var inst = new PyInstance(NdArrayClass);
        inst.Dict["__ndarray__"] = data;
        return inst;
    }

    private static NdArrayData Data(object self) => (NdArrayData)((PyInstance)self).Dict["__ndarray__"];

    private static PyClass BuildNdArrayClass()
    {
        var cls = new PyClass("ndarray", new List<PyClass>());
        void Add(string name, BuiltinFn fn) => cls.Dict[name] = new PyBuiltinFunction($"ndarray.{name}", fn);

        cls.Dict["ndim"] = MakeProperty(self => (BigInteger)Data(self).Ndim);
        cls.Dict["size"] = MakeProperty(self => (BigInteger)Data(self).Size);
        cls.Dict["shape"] = MakeProperty(self =>
            new PyTuple(Data(self).Shape.Select(dim => (object)(BigInteger)dim).ToArray()));
        cls.Dict["dtype"] = MakeProperty(self => DTypeInstance(Data(self).DType));

        Add("__len__", (_, a, _) =>
        {
            var d = Data(a[0]);
            if (d.Ndim == 0)
                throw PyErr.TypeError("len() of unsized object");
            return (BigInteger)d.Shape[0];
        });

        Add("__repr__", (_, a, _) => $"array({FormatArray(Data(a[0]))})");
        Add("__str__", (_, a, _) => FormatArray(Data(a[0])));

        return cls;
    }

    private static PyClass BuildDTypeClass()
    {
        var cls = new PyClass("dtype", new List<PyClass>());
        cls.Dict["name"] = MakeProperty(self => ((PyInstance)self).Dict["__name__"]);
        cls.Dict["__repr__"] = new PyBuiltinFunction("dtype.__repr__",
            (_, a, _) => $"dtype('{((PyInstance)a[0]).Dict["__name__"]}')");
        cls.Dict["__str__"] = new PyBuiltinFunction("dtype.__str__",
            (_, a, _) => (string)((PyInstance)a[0]).Dict["__name__"]);
        return cls;
    }

    private static PyInstance MakeDType(string name)
    {
        var inst = new PyInstance(DTypeClass);
        inst.Dict["__name__"] = name;
        return inst;
    }

    /// <summary>Real numpy dtype objects are singletons per dtype (`a.dtype is b.dtype` for two
    /// float64 arrays) — a single cached instance per `DType` matches that instead of allocating a
    /// fresh dtype object on every attribute access.</summary>
    private static PyInstance DTypeInstance(DType dtype) => dtype switch
    {
        DType.Float64 => Float64DType,
        _ => throw new NotSupportedException($"no dtype instance for {dtype}"),
    };

    /// <summary>Real numpy's own `str()` array formatting: space-separated elements (not
    /// comma-separated like a Python list), nested brackets per dimension, continuation rows
    /// indented to align under the opening bracket. `__repr__` just wraps this in `array(...)` —
    /// real numpy also re-indents the continuation rows to align past "array(", which this v1
    /// skips (documented simplification, see NUMPY_PLAN.md Phase 1.6 "keep it simple").</summary>
    private static string FormatArray(NdArrayData d)
    {
        if (d.Ndim == 0)
            return FormatElement(((double[])d.Buffer)[0]);
        return FormatDim((double[])d.Buffer, d.Shape, d.Strides, 0, 0);
    }

    private static string FormatDim(double[] buf, int[] shape, int[] strides, int dim, int baseOffset)
    {
        int n = shape[dim];
        if (dim == shape.Length - 1)
        {
            var parts = new string[n];
            for (int i = 0; i < n; i++)
                parts[i] = FormatElement(buf[baseOffset + i * strides[dim]]);
            return "[" + string.Join(" ", parts) + "]";
        }
        var rows = new string[n];
        for (int i = 0; i < n; i++)
            rows[i] = FormatDim(buf, shape, strides, dim + 1, baseOffset + i * strides[dim]);
        string pad = new string(' ', dim + 1);
        return "[" + string.Join("\n" + pad, rows) + "]";
    }

    /// <summary>Real numpy shows a whole-number float with a trailing "." and no "0" (`1.` not
    /// `1.0`) in array printing; every other value formats the same as Python's own float repr.</summary>
    private static string FormatElement(double v)
    {
        string s = PyOps.ReprDouble(v);
        return s.EndsWith(".0", StringComparison.Ordinal) ? s[..^1] : s;
    }

    private static PyProperty MakeProperty(Func<object, object> getter)
        => new() { Getter = new PyBuiltinFunction("ndarray.<property>", (_, a, _) => getter(a[0])) };
}
