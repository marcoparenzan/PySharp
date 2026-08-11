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
    /// earlier axis's stride is the product of every axis size to its right. Internal (not just
    /// private) so `NumpyModule`'s own reduction machinery (Phase 6) can compute a *reduced*
    /// shape's strides without duplicating this formula.</summary>
    internal static int[] ComputeStrides(int[] shape)
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
    Bool,
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
    public static readonly PyInstance BoolDType = MakeDType("bool");

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

        // Phase 2 — construction. Every function here builds a real, independent float64 buffer
        // (no dtype= keyword yet — that's Phase 9's dtype/promotion work) and shape-infers/
        // validates exactly like real numpy's own error messages describe.
        m.Dict["array"] = new PyBuiltinFunction("array", (_, a, _) => Wrap(ArrayFromPython(a[0])));

        m.Dict["zeros"] = new PyBuiltinFunction("zeros", (_, a, kwargs) =>
        {
            int[] shape = ShapeArg(RequireArg(a, kwargs, 0, "shape"));
            return Wrap(new NdArrayData(DType.Float64, new double[SizeOf(shape)], shape));
        });
        m.Dict["ones"] = new PyBuiltinFunction("ones", (_, a, kwargs) =>
        {
            int[] shape = ShapeArg(RequireArg(a, kwargs, 0, "shape"));
            var buf = new double[SizeOf(shape)];
            Array.Fill(buf, 1.0);
            return Wrap(new NdArrayData(DType.Float64, buf, shape));
        });
        m.Dict["full"] = new PyBuiltinFunction("full", (_, a, kwargs) =>
        {
            int[] shape = ShapeArg(RequireArg(a, kwargs, 0, "shape"));
            double value = PyOps.AsDouble(RequireArg(a, kwargs, 1, "fill_value"));
            var buf = new double[SizeOf(shape)];
            Array.Fill(buf, value);
            return Wrap(new NdArrayData(DType.Float64, buf, shape));
        });
        // Real numpy's `empty` returns genuinely uninitialized memory (whatever garbage was
        // already there) — a real correctness trap real code is only supposed to rely on when it
        // immediately overwrites every element. A deterministic zero-filled buffer (what a real
        // C# array already starts as) is a safe, simpler v1 stand-in: any script that reads
        // `empty`'s contents before writing them was already relying on undefined behavior against
        // real numpy too.
        m.Dict["empty"] = new PyBuiltinFunction("empty", (_, a, kwargs) =>
        {
            int[] shape = ShapeArg(RequireArg(a, kwargs, 0, "shape"));
            return Wrap(new NdArrayData(DType.Float64, new double[SizeOf(shape)], shape));
        });

        m.Dict["arange"] = new PyBuiltinFunction("arange", (_, a, kwargs) =>
        {
            double start = 0, stop, step = 1;
            if (a.Length == 1)
                stop = PyOps.AsDouble(a[0]);
            else if (a.Length == 2)
            {
                start = PyOps.AsDouble(a[0]);
                stop = PyOps.AsDouble(a[1]);
            }
            else
            {
                start = PyOps.AsDouble(a[0]);
                stop = PyOps.AsDouble(a[1]);
                step = PyOps.AsDouble(a[2]);
            }
            if (step == 0)
                throw PyErr.ValueError("arange: step cannot be zero");
            int count = Math.Max(0, (int)Math.Ceiling((stop - start) / step));
            var buf = new double[count];
            for (int i = 0; i < count; i++)
                buf[i] = start + i * step;
            return Wrap(new NdArrayData(DType.Float64, buf, new[] { count }));
        });

        m.Dict["linspace"] = new PyBuiltinFunction("linspace", (interp, a, kwargs) =>
        {
            double start = PyOps.AsDouble(a[0]);
            double stop = PyOps.AsDouble(a[1]);
            int num = a.Length > 2 ? (int)PyOps.AsBigInt(a[2], "num")
                : kwargs is not null && kwargs.TryGetValue("num", out var n) ? (int)PyOps.AsBigInt(n, "num") : 50;
            bool endpoint = a.Length > 3 ? PyOps.Truthy(interp, a[3])
                : kwargs is not null && kwargs.TryGetValue("endpoint", out var e) ? PyOps.Truthy(interp, e) : true;
            if (num < 0)
                throw PyErr.ValueError("Number of samples, num, must be non-negative.");
            var buf = new double[num];
            if (num == 1)
                buf[0] = start;
            else if (num > 1)
            {
                double stepVal = (stop - start) / (endpoint ? num - 1 : num);
                for (int i = 0; i < num; i++)
                    buf[i] = start + i * stepVal;
                if (endpoint)
                    buf[num - 1] = stop; // exact endpoint, avoiding float drift from the step math
            }
            return Wrap(new NdArrayData(DType.Float64, buf, new[] { num }));
        });

        m.Dict["eye"] = new PyBuiltinFunction("eye", (_, a, kwargs) =>
        {
            int rows = (int)PyOps.AsBigInt(a[0], "N");
            int cols = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "M")
                : kwargs is not null && kwargs.TryGetValue("M", out var mm) ? (int)PyOps.AsBigInt(mm, "M") : rows;
            var buf = new double[rows * cols];
            for (int i = 0; i < Math.Min(rows, cols); i++)
                buf[i * cols + i] = 1.0;
            return Wrap(new NdArrayData(DType.Float64, buf, new[] { rows, cols }));
        });
        m.Dict["identity"] = new PyBuiltinFunction("identity", (_, a, _) =>
        {
            int n = (int)PyOps.AsBigInt(a[0], "n");
            var buf = new double[n * n];
            for (int i = 0; i < n; i++)
                buf[i * n + i] = 1.0;
            return Wrap(new NdArrayData(DType.Float64, buf, new[] { n, n }));
        });

        m.Dict["copy"] = new PyBuiltinFunction("copy", (_, a, _) => Wrap(CopyOf(Data(a[0]))));

        // Phase 5.7 — np.where(cond, x, y): a real 3-way broadcast (cond, x, and y all broadcast
        // together — broadcasting is associative, so this is just two ordinary 2-way broadcasts
        // chained), selecting x's element where cond is truthy, else y's. Result dtype matches x/y
        // when they agree; falls back to float64 (coercing any bool operand to 1.0/0.0) when mixed,
        // since only float64/bool exist yet — no promotion rules until Phase 9.
        m.Dict["where"] = new PyBuiltinFunction("where", (_, a, _) =>
        {
            var cond = OperandData(a[0]);
            var x = OperandData(a[1]);
            var y = OperandData(a[2]);
            int[] shapeCondX = BroadcastShape(cond.Shape, x.Shape);
            int[] shape = BroadcastShape(shapeCondX, y.Shape);
            int[] stridesCond = BroadcastStrides(cond.Shape, cond.Strides, shape);
            int[] stridesX = BroadcastStrides(x.Shape, x.Strides, shape);
            int[] stridesY = BroadcastStrides(y.Shape, y.Strides, shape);
            DType outDType = x.DType == y.DType ? x.DType : DType.Float64;
            var outBuf = MakeBuffer(outDType, SizeOf(shape));
            int flat = 0;
            ForEachBroadcastIndex(shape, index =>
            {
                bool truthy = AsComparableDouble(cond, DotProduct(index, stridesCond)) != 0.0;
                var (chosen, chosenStrides) = truthy ? (x, stridesX) : (y, stridesY);
                object value = GetElement(chosen, DotProduct(index, chosenStrides));
                SetBufferElement(outBuf, outDType, flat++, CoerceTo(outDType, value));
            });
            return Wrap(new NdArrayData(outDType, outBuf, shape));
        });

        // Phase 6 — module-level reduction functions (`np.sum(a)`, not just `a.sum()`) — real
        // numpy has both forms; these just delegate to the exact same reduction machinery the
        // instance methods above use.
        m.Dict["sum"] = new PyBuiltinFunction("sum", (_, a, kwargs) =>
            ReduceDispatch(Data(a[0]), AxisArg(a, kwargs, 1), static (x, y) => x + y, 0.0));
        m.Dict["prod"] = new PyBuiltinFunction("prod", (_, a, kwargs) =>
            ReduceDispatch(Data(a[0]), AxisArg(a, kwargs, 1), static (x, y) => x * y, 1.0));
        m.Dict["min"] = new PyBuiltinFunction("min", (_, a, kwargs) =>
            ReduceDispatch(Data(a[0]), AxisArg(a, kwargs, 1), Math.Min, null));
        m.Dict["max"] = new PyBuiltinFunction("max", (_, a, kwargs) =>
            ReduceDispatch(Data(a[0]), AxisArg(a, kwargs, 1), Math.Max, null));
        m.Dict["mean"] = new PyBuiltinFunction("mean", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return axis is int ax ? Wrap(MeanAxis(d, ax)) : MeanAll(d);
        });
        m.Dict["std"] = new PyBuiltinFunction("std", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return axis is int ax ? Wrap(ElementwiseUnary(VarAxis(d, ax), Math.Sqrt)) : Math.Sqrt(VarAll(d));
        });
        m.Dict["var"] = new PyBuiltinFunction("var", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return axis is int ax ? Wrap(VarAxis(d, ax)) : VarAll(d);
        });
        m.Dict["argmin"] = new PyBuiltinFunction("argmin", (_, a, kwargs) =>
            ArgReduce(Data(a[0]), AxisArg(a, kwargs, 1), static (cand, best) => cand < best));
        m.Dict["argmax"] = new PyBuiltinFunction("argmax", (_, a, kwargs) =>
            ArgReduce(Data(a[0]), AxisArg(a, kwargs, 1), static (cand, best) => cand > best));
        m.Dict["cumsum"] = new PyBuiltinFunction("cumsum", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return Wrap(axis is int ax ? CumulateAxis(d, ax, static (x, y) => x + y) : CumulateFlat(d, static (x, y) => x + y));
        });
        m.Dict["cumprod"] = new PyBuiltinFunction("cumprod", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return Wrap(axis is int ax ? CumulateAxis(d, ax, static (x, y) => x * y) : CumulateFlat(d, static (x, y) => x * y));
        });

        // Phase 7 — universal functions (ufuncs). `ApplyUfunc` is the real "ufunc factory" 7.1 asks
        // for — it's just `ElementwiseUnary` (already built in Phase 4) plus a scalar fast path, so
        // `np.sqrt(4.0)` returns a plain Python float exactly like real numpy's own scalar ufunc
        // behavior, not forcing a 0-d array wrap.
        m.Dict["sqrt"] = new PyBuiltinFunction("sqrt", (_, a, _) => ApplyUfunc(a[0], Math.Sqrt));
        m.Dict["exp"] = new PyBuiltinFunction("exp", (_, a, _) => ApplyUfunc(a[0], Math.Exp));
        m.Dict["log"] = new PyBuiltinFunction("log", (_, a, _) => ApplyUfunc(a[0], Math.Log));
        m.Dict["log10"] = new PyBuiltinFunction("log10", (_, a, _) => ApplyUfunc(a[0], Math.Log10));
        m.Dict["abs"] = new PyBuiltinFunction("abs", (_, a, _) => ApplyUfunc(a[0], Math.Abs));

        m.Dict["sin"] = new PyBuiltinFunction("sin", (_, a, _) => ApplyUfunc(a[0], Math.Sin));
        m.Dict["cos"] = new PyBuiltinFunction("cos", (_, a, _) => ApplyUfunc(a[0], Math.Cos));
        m.Dict["tan"] = new PyBuiltinFunction("tan", (_, a, _) => ApplyUfunc(a[0], Math.Tan));
        m.Dict["arcsin"] = new PyBuiltinFunction("arcsin", (_, a, _) => ApplyUfunc(a[0], Math.Asin));
        m.Dict["arccos"] = new PyBuiltinFunction("arccos", (_, a, _) => ApplyUfunc(a[0], Math.Acos));
        m.Dict["arctan"] = new PyBuiltinFunction("arctan", (_, a, _) => ApplyUfunc(a[0], Math.Atan));

        m.Dict["floor"] = new PyBuiltinFunction("floor", (_, a, _) => ApplyUfunc(a[0], Math.Floor));
        m.Dict["ceil"] = new PyBuiltinFunction("ceil", (_, a, _) => ApplyUfunc(a[0], Math.Ceiling));
        m.Dict["sign"] = new PyBuiltinFunction("sign", (_, a, _) => ApplyUfunc(a[0], static x => Math.Sign(x)));
        // Real numpy rounds half-to-even (banker's rounding), same as .NET's own `Math.Round`
        // default `MidpointRounding.ToEven` — no special-casing needed to match.
        m.Dict["round"] = new PyBuiltinFunction("round", (_, a, kwargs) =>
        {
            int decimals = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "decimals")
                : kwargs is not null && kwargs.TryGetValue("decimals", out var dec) ? (int)PyOps.AsBigInt(dec, "decimals") : 0;
            return ApplyUfunc(a[0], x => Math.Round(x, decimals, MidpointRounding.ToEven));
        });
        m.Dict["clip"] = new PyBuiltinFunction("clip", (_, a, _) =>
        {
            double lo = PyOps.AsDouble(a[1]);
            double hi = PyOps.AsDouble(a[2]);
            return ApplyUfunc(a[0], x => Math.Clamp(x, lo, hi));
        });

        // Binary ufuncs — real broadcasting, same machinery as `+`/`-`/etc., just not reachable via
        // an operator (no `minimum`/`maximum` dunder in Python).
        m.Dict["minimum"] = new PyBuiltinFunction("minimum", (_, a, _) => ElementwiseOp(a[0], a[1], Math.Min));
        m.Dict["maximum"] = new PyBuiltinFunction("maximum", (_, a, _) => ElementwiseOp(a[0], a[1], Math.Max));
        m.Dict["power"] = new PyBuiltinFunction("power", (_, a, _) => ElementwiseOp(a[0], a[1], Math.Pow));

        // Constants.
        m.Dict["pi"] = Math.PI;
        m.Dict["e"] = Math.E;
        m.Dict["inf"] = double.PositiveInfinity;
        m.Dict["nan"] = double.NaN;

        return m;
    }

    /// <summary>The real Phase 7.1 "ufunc factory": elementwise on an `ndarray` (via the existing
    /// `ElementwiseUnary`), or a real scalar fast path — `np.sqrt(4.0)` returns a plain Python
    /// `float`, matching real numpy's own scalar ufunc behavior (no forced 0-d array wrap).</summary>
    private static object ApplyUfunc(object arg, Func<double, double> op)
        => arg is PyInstance pi && pi.Class == NdArrayClass ? Wrap(ElementwiseUnary(Data(pi), op)) : op(PyOps.AsDouble(arg));

    private static object CoerceTo(DType dtype, object value) => dtype switch
    {
        DType.Float64 => value is double d ? d : (bool)value ? 1.0 : 0.0,
        DType.Bool => (bool)value,
        _ => value,
    };

    private static object RequireArg(object[] a, Dictionary<string, object>? kwargs, int index, string name)
        => a.Length > index ? a[index]
            : kwargs is not null && kwargs.TryGetValue(name, out var v) ? v
            : throw PyErr.TypeError($"missing required argument: '{name}'");

    private static int SizeOf(int[] shape) => shape.Aggregate(1, (acc, dim) => acc * dim);

    private static int[] ShapeArg(object arg) => arg switch
    {
        BigInteger n => new[] { (int)n },
        PyTuple t => t.Items.Select(x => (int)PyOps.AsBigInt(x, "shape")).ToArray(),
        PyList l => l.Items.Select(x => (int)PyOps.AsBigInt(x, "shape")).ToArray(),
        _ => throw PyErr.TypeError($"expected int or sequence of int for shape, got {PyOps.TypeName(arg)}"),
    };

    private static NdArrayData CopyOf(NdArrayData d)
        => new(d.DType, CloneBuffer(d), (int[])d.Shape.Clone());

    // ---------------------------------------------------------------- dtype-generic element access
    //
    // Phase 1-4 hardcoded `double[]` everywhere (the only dtype that existed). Phase 5 adds a real
    // `Bool` dtype (comparisons/masking), so every place that used to cast `d.Buffer` straight to
    // `double[]` now goes through these three dispatch points instead — one real switch per
    // concern, not scattered per-callsite casts.

    private static object GetElement(NdArrayData d, int offset) => d.DType switch
    {
        DType.Float64 => ((double[])d.Buffer)[offset],
        DType.Bool => ((bool[])d.Buffer)[offset],
        _ => throw new NotSupportedException($"unsupported dtype {d.DType}"),
    };

    private static void SetElement(NdArrayData d, int offset, object value)
    {
        switch (d.DType)
        {
            case DType.Float64:
                ((double[])d.Buffer)[offset] = PyOps.AsDouble(value);
                break;
            case DType.Bool:
                ((bool[])d.Buffer)[offset] = value is bool b ? b
                    : throw PyErr.TypeError($"expected bool, got {PyOps.TypeName(value)}");
                break;
            default:
                throw new NotSupportedException($"unsupported dtype {d.DType}");
        }
    }

    private static Array MakeBuffer(DType dtype, int size) => dtype switch
    {
        DType.Float64 => new double[size],
        DType.Bool => new bool[size],
        _ => throw new NotSupportedException($"unsupported dtype {dtype}"),
    };

    private static void SetBufferElement(Array buffer, DType dtype, int index, object value)
    {
        switch (dtype)
        {
            case DType.Float64:
                ((double[])buffer)[index] = (double)value;
                break;
            case DType.Bool:
                ((bool[])buffer)[index] = (bool)value;
                break;
            default:
                throw new NotSupportedException($"unsupported dtype {dtype}");
        }
    }

    private static Array CloneBuffer(NdArrayData d) => d.DType switch
    {
        DType.Float64 => (double[])((double[])d.Buffer).Clone(),
        DType.Bool => (bool[])((bool[])d.Buffer).Clone(),
        _ => throw new NotSupportedException($"unsupported dtype {d.DType}"),
    };

    /// <summary>Numeric value of an element regardless of dtype (`True`/`False` as `1.0`/`0.0`,
    /// matching `PyOps.AsDouble`'s own bool handling) — lets comparisons work between any dtype
    /// pair through one code path instead of one per combination.</summary>
    private static double AsComparableDouble(NdArrayData d, int offset) => d.DType switch
    {
        DType.Float64 => (double)GetElement(d, offset),
        DType.Bool => (bool)GetElement(d, offset) ? 1.0 : 0.0,
        _ => throw new NotSupportedException($"unsupported dtype {d.DType}"),
    };

    /// <summary>Real numpy shape inference off a (possibly nested) Python list/tuple: the shape is
    /// read by descending through the *first* element of each level (matching real numpy), then a
    /// single validating pass confirms every branch actually has that same shape — a mismatch
    /// anywhere (a ragged row, a scalar where a nested list was expected, or vice versa) raises the
    /// real `ValueError` numpy itself raises for an inhomogeneous shape. A bare scalar (no list at
    /// all) produces a real 0-d array, matching `np.array(5.0)`. Dtype: real numpy infers `bool`
    /// when *every* leaf is a real Python bool (`np.array([True, False]).dtype == bool`), else
    /// `float64` (ints still promote to float — real int64 inference is Phase 9's job).</summary>
    private static NdArrayData ArrayFromPython(object value)
    {
        var shape = new List<int>();
        object? cursor = value;
        while (cursor is PyList or PyTuple)
        {
            var items = SequenceItems(cursor);
            shape.Add(items.Count);
            if (items.Count == 0)
                break;
            cursor = items[0];
        }
        var flat = new List<object>();
        AppendFlat(value, shape.ToArray(), 0, flat);

        DType dtype = flat.Count > 0 && flat.All(static v => v is bool) ? DType.Bool : DType.Float64;
        var buffer = MakeBuffer(dtype, flat.Count);
        for (int i = 0; i < flat.Count; i++)
            SetBufferElement(buffer, dtype, i, dtype == DType.Bool ? flat[i] : PyOps.AsDouble(flat[i]));
        return new NdArrayData(dtype, buffer, shape.ToArray());
    }

    private static IReadOnlyList<object> SequenceItems(object o) => o switch
    {
        PyList l => l.Items,
        PyTuple t => t.Items,
        _ => throw new InvalidOperationException("not a sequence"),
    };

    private static void AppendFlat(object value, int[] shape, int depth, List<object> flat)
    {
        bool isLeaf = depth == shape.Length;
        bool isSequence = value is PyList or PyTuple;
        if (isLeaf)
        {
            if (isSequence)
                throw RaggedArrayError();
            flat.Add(value);
            return;
        }
        if (!isSequence)
            throw RaggedArrayError();
        var items = SequenceItems(value);
        if (items.Count != shape[depth])
            throw RaggedArrayError();
        foreach (var item in items)
            AppendFlat(item, shape, depth + 1, flat);
    }

    private static PyRaise RaggedArrayError() => PyErr.ValueError(
        "setting an array element with a sequence. The requested array has an inhomogeneous shape.");

    // ---------------------------------------------------------------- Phase 3: indexing/slicing

    /// <summary>Resolves a real numpy index (a single int/bool, a `PySlice`, or a `PyTuple` mixing
    /// both — exactly what `Interp.EvalIndex` builds for `a[i]`/`a[1:3]`/`a[i, j]`/`a[1:3, i]`)
    /// into, per axis, the list of source element-offsets along that axis to visit. An axis with an
    /// explicit integer index contributes a single offset and is "reduced" (absent from
    /// `resultShape`); a slice or an axis with no explicit index (real numpy's own implicit
    /// "partial indexing" — `a[i]` on an N-D array only indexes axis 0, keeping the rest) keeps its
    /// full offset list and its size in `resultShape`.</summary>
    private static (int[][] AxisOffsets, int[] ResultShape) ResolveAxes(NdArrayData d, object index)
    {
        var items = index is PyTuple t ? t.Items : new[] { index };
        if (items.Length > d.Ndim)
            throw PyErr.IndexError(
                $"too many indices for array: array is {d.Ndim}-dimensional, but {items.Length} were indexed");

        var axisOffsets = new int[d.Ndim][];
        var resultShape = new List<int>();

        for (int axis = 0; axis < d.Ndim; axis++)
        {
            if (axis < items.Length && items[axis] is PySlice slice)
            {
                var (start, _, step, count) = slice.Indices(d.Shape[axis]);
                var offs = new int[count];
                for (int k = 0; k < count; k++)
                    offs[k] = start + k * step;
                axisOffsets[axis] = offs;
                resultShape.Add(count);
            }
            else if (axis < items.Length)
            {
                axisOffsets[axis] = new[] { ResolveIntIndex(items[axis], d.Shape[axis], axis) };
            }
            else
            {
                int n = d.Shape[axis];
                var offs = new int[n];
                for (int k = 0; k < n; k++)
                    offs[k] = k;
                axisOffsets[axis] = offs;
                resultShape.Add(n);
            }
        }

        return (axisOffsets, resultShape.ToArray());
    }

    private static int ResolveIntIndex(object item, int axisLen, int axis)
    {
        var raw = PyOps.AsBigInt(item, "index");
        int idx = (int)raw;
        int resolved = idx < 0 ? idx + axisLen : idx;
        if (resolved < 0 || resolved >= axisLen)
            throw PyErr.IndexError($"index {idx} is out of bounds for axis {axis} with size {axisLen}");
        return resolved;
    }

    /// <summary>`a[index]`: a fully-reduced index (an explicit int on every axis) returns a real
    /// Python scalar (`float` or `bool`, matching the array's own dtype) — everything else returns
    /// a new, independent `ndarray` **copy** (real strided views are a later, optional phase — see
    /// NUMPY_PLAN.md Phase 12). A real bool-array index (`a[mask]`) is boolean masking (Phase 5.5),
    /// handled entirely separately from the int/slice/tuple axis resolution below.</summary>
    private static object GetItem(NdArrayData d, object index)
    {
        if (BoolMaskOf(index) is { } mask)
            return Wrap(GatherMask(d, mask));

        var (axisOffsets, resultShape) = ResolveAxes(d, index);
        if (resultShape.Length == 0)
            return GetElement(d, FlatOffset(d, axisOffsets));

        var flat = new List<object>();
        GatherRecursive(d, axisOffsets, 0, 0, flat);
        var buffer = MakeBuffer(d.DType, flat.Count);
        for (int i = 0; i < flat.Count; i++)
            SetBufferElement(buffer, d.DType, i, flat[i]);
        return Wrap(new NdArrayData(d.DType, buffer, resultShape));
    }

    /// <summary>`a[index] = value`: a fully-reduced index assigns a single scalar element; any
    /// other index assigns either a broadcast scalar (`a[1:3] = 5.0`) or another array whose shape
    /// must exactly match the indexed region (`a[1:3] = other` — real per-element broadcasting
    /// beyond an exact shape match is Phase 4's job, not this one). `a[mask] = value` (Phase 5.6)
    /// is boolean-mask assignment, handled separately from axis resolution.</summary>
    private static void SetItem(NdArrayData d, object index, object value)
    {
        if (BoolMaskOf(index) is { } mask)
        {
            ScatterMask(d, mask, value);
            return;
        }

        var (axisOffsets, resultShape) = ResolveAxes(d, index);
        if (resultShape.Length == 0)
        {
            SetElement(d, FlatOffset(d, axisOffsets), value);
            return;
        }

        if (value is PyInstance pi && pi.Class == NdArrayClass)
        {
            var src = Data(pi);
            if (!src.Shape.SequenceEqual(resultShape))
                throw PyErr.ValueError(
                    $"could not broadcast input array from shape {ShapeRepr(src.Shape)} into shape {ShapeRepr(resultShape)}");
            int i = 0;
            ScatterRecursive(d, axisOffsets, 0, 0, () => GetElement(src, i++));
        }
        else
        {
            ScatterRecursive(d, axisOffsets, 0, 0, () => value);
        }
    }

    private static int FlatOffset(NdArrayData d, int[][] axisOffsets)
    {
        int offset = 0;
        for (int axis = 0; axis < d.Ndim; axis++)
            offset += axisOffsets[axis][0] * d.Strides[axis];
        return offset;
    }

    private static void GatherRecursive(NdArrayData d, int[][] axisOffsets, int axis, int baseOffset, List<object> flat)
    {
        if (axis == axisOffsets.Length)
        {
            flat.Add(GetElement(d, baseOffset));
            return;
        }
        foreach (int off in axisOffsets[axis])
            GatherRecursive(d, axisOffsets, axis + 1, baseOffset + off * d.Strides[axis], flat);
    }

    private static void ScatterRecursive(NdArrayData d, int[][] axisOffsets, int axis, int baseOffset, Func<object> nextValue)
    {
        if (axis == axisOffsets.Length)
        {
            SetElement(d, baseOffset, nextValue());
            return;
        }
        foreach (int off in axisOffsets[axis])
            ScatterRecursive(d, axisOffsets, axis + 1, baseOffset + off * d.Strides[axis], nextValue);
    }

    // ---------------------------------------------------------------- Phase 5.5/5.6: boolean masking

    /// <summary>Recognizes a real bool-dtype `ndarray` used AS the whole index (`a[mask]`), as
    /// opposed to an int/slice/tuple index — real numpy's boolean (fancy) indexing, a genuinely
    /// different mode from axis-by-axis indexing. Returns null for every other index shape (a plain
    /// int/slice/tuple falls through to the normal `ResolveAxes` path).</summary>
    private static NdArrayData? BoolMaskOf(object index)
        => index is PyInstance pi && pi.Class == NdArrayClass && Data(pi).DType == DType.Bool ? Data(pi) : null;

    /// <summary>`a[mask]`: real numpy requires the mask's shape to exactly match `a`'s (v1 scope —
    /// no partial/broadcast mask), and returns a real 1-D array of every element whose mask
    /// position is `True`, visited in C-order.</summary>
    private static NdArrayData GatherMask(NdArrayData d, NdArrayData mask)
    {
        RequireMatchingMaskShape(d, mask);
        var maskBuf = (bool[])mask.Buffer;
        var selected = new List<object>();
        for (int i = 0; i < d.Size; i++)
            if (maskBuf[i])
                selected.Add(GetElement(d, i));
        var buffer = MakeBuffer(d.DType, selected.Count);
        for (int i = 0; i < selected.Count; i++)
            SetBufferElement(buffer, d.DType, i, selected[i]);
        return new NdArrayData(d.DType, buffer, new[] { selected.Count });
    }

    /// <summary>`a[mask] = value`: assigns a broadcast scalar, or one value per `True` position
    /// (in C-order) from another 1-D array whose length must equal the number of `True`s.</summary>
    private static void ScatterMask(NdArrayData d, NdArrayData mask, object value)
    {
        RequireMatchingMaskShape(d, mask);
        var maskBuf = (bool[])mask.Buffer;
        if (value is PyInstance pi && pi.Class == NdArrayClass)
        {
            var src = Data(pi);
            int trueCount = maskBuf.Count(static x => x);
            if (src.Size != trueCount)
                throw PyErr.ValueError(
                    $"NumPy boolean array indexing assignment cannot assign {src.Size} input values to the {trueCount} output values where the mask is true");
            int i = 0;
            for (int offset = 0; offset < d.Size; offset++)
                if (maskBuf[offset])
                    SetElement(d, offset, GetElement(src, i++));
        }
        else
        {
            for (int offset = 0; offset < d.Size; offset++)
                if (maskBuf[offset])
                    SetElement(d, offset, value);
        }
    }

    private static void RequireMatchingMaskShape(NdArrayData d, NdArrayData mask)
    {
        if (!mask.Shape.SequenceEqual(d.Shape))
            throw PyErr.IndexError(
                $"boolean index did not match indexed array; dimension mismatch: {ShapeRepr(d.Shape)} vs {ShapeRepr(mask.Shape)}");
    }

    private static string ShapeRepr(int[] shape) => shape.Length == 1 ? $"({shape[0]},)" : $"({string.Join(", ", shape)})";

    // ---------------------------------------------------------------- Phase 4: broadcasting

    /// <summary>Real numpy broadcasting: two shapes are compared right-aligned (the shorter one
    /// padded with 1s on the left), and each dimension pair must either match exactly or have one
    /// side equal to 1 (which stretches to the other side's size) — anything else is a real
    /// incompatible-shape `ValueError`. Public (not just internal to this module) so it can be
    /// unit-tested directly in C#, no Python involved — see NUMPY_PLAN.md Phase 4.4.</summary>
    public static int[] BroadcastShape(int[] shapeA, int[] shapeB)
    {
        int ndim = Math.Max(shapeA.Length, shapeB.Length);
        var result = new int[ndim];
        int padA = ndim - shapeA.Length, padB = ndim - shapeB.Length;
        for (int i = 0; i < ndim; i++)
        {
            int da = i < padA ? 1 : shapeA[i - padA];
            int db = i < padB ? 1 : shapeB[i - padB];
            if (da == db)
                result[i] = da;
            else if (da == 1)
                result[i] = db;
            else if (db == 1)
                result[i] = da;
            else
                throw PyErr.ValueError(
                    $"operands could not be broadcast together with shapes {ShapeRepr(shapeA)} {ShapeRepr(shapeB)}");
        }
        return result;
    }

    /// <summary>An operand's own strides, reinterpreted against the (already-computed) broadcast
    /// shape: a dimension this operand doesn't have (shape padding) or has size 1 in but the
    /// broadcast size is bigger gets stride 0 — the real "stride-0 iteration" trick that makes the
    /// same source element get read repeatedly for the stretched dimension, with no data actually
    /// duplicated.</summary>
    private static int[] BroadcastStrides(int[] shape, int[] strides, int[] broadcastShape)
    {
        int ndim = broadcastShape.Length;
        int pad = ndim - shape.Length;
        var result = new int[ndim];
        for (int i = 0; i < ndim; i++)
        {
            if (i < pad)
            {
                result[i] = 0;
                continue;
            }
            int dimSize = shape[i - pad];
            result[i] = dimSize == 1 && broadcastShape[i] != 1 ? 0 : strides[i - pad];
        }
        return result;
    }

    /// <summary>Computes the broadcast shape once and hands back both operands' own strides
    /// reinterpreted against it — the shared setup every broadcasted elementwise operation
    /// (arithmetic, comparison, logical) needs, factored out so each of them is just "iterate the
    /// broadcast shape, read two elements, write one".</summary>
    private static (int[] Shape, int[] StridesA, int[] StridesB) PrepareBroadcast(NdArrayData a, NdArrayData b)
    {
        int[] shape = BroadcastShape(a.Shape, b.Shape);
        return (shape, BroadcastStrides(a.Shape, a.Strides, shape), BroadcastStrides(b.Shape, b.Strides, shape));
    }

    /// <summary>Visits every multi-index of `shape` in real C-order (last axis fastest), the same
    /// odometer-style increment used throughout this module.</summary>
    private static void ForEachBroadcastIndex(int[] shape, Action<int[]> visit)
    {
        int size = shape.Aggregate(1, (acc, dim) => acc * dim);
        var index = new int[shape.Length];
        for (int flat = 0; flat < size; flat++)
        {
            visit(index);
            for (int d = shape.Length - 1; d >= 0; d--)
            {
                if (++index[d] < shape[d])
                    break;
                index[d] = 0;
            }
        }
    }

    private static int DotProduct(int[] index, int[] strides)
    {
        int offset = 0;
        for (int d = 0; d < index.Length; d++)
            offset += index[d] * strides[d];
        return offset;
    }

    // ---------------------------------------------------------------- Phase 6: reductions

    private static int NormalizeAxis(int axis, int ndim)
    {
        int resolved = axis < 0 ? axis + ndim : axis;
        if (resolved < 0 || resolved >= ndim)
            throw PyErr.ValueError($"axis {axis} is out of bounds for array of dimension {ndim}");
        return resolved;
    }

    /// <summary>The flat key identifying "everything except `axis`" for a given multi-index — the
    /// position an axis-reduction/cumulative-op's *result* (whose shape already has `axis` removed)
    /// should read from or write to. `excludedStrides` is `ComputeStrides` over the shape with
    /// `axis` already removed (computed once per call, not once per element).</summary>
    private static int LineKey(int[] index, int axis, int[] excludedStrides)
    {
        int key = 0, ri = 0;
        for (int ax = 0; ax < index.Length; ax++)
        {
            if (ax == axis)
                continue;
            key += index[ax] * excludedStrides[ri];
            ri++;
        }
        return key;
    }

    /// <summary>`axis=None` reduction: folds every element of `d` (visited in real C-order) into a
    /// single scalar with `combine`, seeded by the very first element visited — which works
    /// uniformly for sum/prod/min/max without needing a per-op identity value, *except* an empty
    /// array has no "first element": `emptyIdentity` supplies sum's real `0.0`/prod's real `1.0`
    /// for that case, or (left `null`) a real `ValueError` for min/max, matching real numpy (which
    /// has no identity for those).</summary>
    private static double ReduceAllToScalar(NdArrayData d, Func<double, double, double> combine, double? emptyIdentity)
    {
        if (d.Size == 0)
            return emptyIdentity ?? throw PyErr.ValueError("zero-size array to reduction operation which has no identity");
        double acc = 0;
        bool first = true;
        ForEachBroadcastIndex(d.Shape, index =>
        {
            double v = AsComparableDouble(d, DotProduct(index, d.Strides));
            acc = first ? v : combine(acc, v);
            first = false;
        });
        return acc;
    }

    /// <summary>`axis=k` reduction: folds along one axis only, producing a real array shaped like
    /// `d` with that axis removed (real numpy's own `axis=` reduction shape rule).</summary>
    private static NdArrayData ReduceAxisToArray(NdArrayData d, int axis, Func<double, double, double> combine, double? emptyIdentity)
    {
        axis = NormalizeAxis(axis, d.Ndim);
        int[] resultShape = d.Shape.Where((_, i) => i != axis).ToArray();
        int[] resultStrides = NdArrayData.ComputeStrides(resultShape);
        var accBuf = new double[SizeOf(resultShape)];
        var hasValue = new bool[accBuf.Length];
        ForEachBroadcastIndex(d.Shape, index =>
        {
            double v = AsComparableDouble(d, DotProduct(index, d.Strides));
            int key = LineKey(index, axis, resultStrides);
            accBuf[key] = hasValue[key] ? combine(accBuf[key], v) : v;
            hasValue[key] = true;
        });
        if (emptyIdentity is double id)
        {
            for (int i = 0; i < accBuf.Length; i++)
                if (!hasValue[i])
                    accBuf[i] = id;
        }
        else if (hasValue.Any(static has => !has))
        {
            throw PyErr.ValueError("zero-size array to reduction operation which has no identity");
        }
        return new NdArrayData(DType.Float64, accBuf, resultShape);
    }

    private static object ReduceDispatch(NdArrayData d, int? axis, Func<double, double, double> combine, double? emptyIdentity)
        => axis is int ax ? Wrap(ReduceAxisToArray(d, ax, combine, emptyIdentity)) : ReduceAllToScalar(d, combine, emptyIdentity);

    private static double MeanAll(NdArrayData d)
    {
        if (d.Size == 0)
            throw PyErr.ValueError("Mean of empty slice.");
        return ReduceAllToScalar(d, static (x, y) => x + y, 0.0) / d.Size;
    }

    private static NdArrayData MeanAxis(NdArrayData d, int axis)
    {
        axis = NormalizeAxis(axis, d.Ndim);
        var sums = ReduceAxisToArray(d, axis, static (x, y) => x + y, 0.0);
        double count = d.Shape[axis];
        return ElementwiseUnary(sums, x => x / count);
    }

    private static double VarAll(NdArrayData d)
    {
        double mean = MeanAll(d);
        double sumSq = 0;
        ForEachBroadcastIndex(d.Shape, index =>
        {
            double v = AsComparableDouble(d, DotProduct(index, d.Strides));
            sumSq += (v - mean) * (v - mean);
        });
        return sumSq / d.Size;
    }

    private static NdArrayData VarAxis(NdArrayData d, int axis)
    {
        axis = NormalizeAxis(axis, d.Ndim);
        var means = MeanAxis(d, axis);
        int[] resultStrides = NdArrayData.ComputeStrides(means.Shape);
        var sumSq = new double[means.Size];
        ForEachBroadcastIndex(d.Shape, index =>
        {
            double v = AsComparableDouble(d, DotProduct(index, d.Strides));
            int key = LineKey(index, axis, resultStrides);
            double m = (double)GetElement(means, key);
            sumSq[key] += (v - m) * (v - m);
        });
        double count = d.Shape[axis];
        for (int i = 0; i < sumSq.Length; i++)
            sumSq[i] /= count;
        return new NdArrayData(DType.Float64, sumSq, means.Shape);
    }

    /// <summary>`argmin`/`argmax`: `better(candidate, currentBest)` returns whether `candidate`
    /// should replace `currentBest` (`&lt;` for argmin, `&gt;` for argmax). `axis=None` returns a
    /// real Python `int` (the flat C-order index); `axis=k` returns an array of per-line indices —
    /// stored as `float64` (no `int64` dtype exists yet, Phase 9's job; the values themselves are
    /// always real whole-number indices, a documented v1 simplification).</summary>
    private static object ArgReduce(NdArrayData d, int? axis, Func<double, double, bool> better)
    {
        if (axis is int ax)
        {
            ax = NormalizeAxis(ax, d.Ndim);
            int[] resultShape = d.Shape.Where((_, i) => i != ax).ToArray();
            int[] resultStrides = NdArrayData.ComputeStrides(resultShape);
            var bestVal = new double[SizeOf(resultShape)];
            var bestIdx = new double[bestVal.Length];
            var has = new bool[bestVal.Length];
            ForEachBroadcastIndex(d.Shape, index =>
            {
                double v = AsComparableDouble(d, DotProduct(index, d.Strides));
                int key = LineKey(index, ax, resultStrides);
                if (!has[key] || better(v, bestVal[key]))
                {
                    bestVal[key] = v;
                    bestIdx[key] = index[ax];
                    has[key] = true;
                }
            });
            return Wrap(new NdArrayData(DType.Float64, bestIdx, resultShape));
        }

        double best = 0;
        int bestFlat = -1, flat = 0;
        ForEachBroadcastIndex(d.Shape, index =>
        {
            double v = AsComparableDouble(d, DotProduct(index, d.Strides));
            if (bestFlat < 0 || better(v, best))
            {
                best = v;
                bestFlat = flat;
            }
            flat++;
        });
        if (bestFlat < 0)
            throw PyErr.ValueError("attempt to get argmin/argmax of an empty sequence");
        return (BigInteger)bestFlat;
    }

    /// <summary>`axis=None`: flattens in real C-order, then cumulates — the general form 1-D
    /// cumsum/cumprod is just a special case of (a 1-D array flattened is itself).</summary>
    private static NdArrayData CumulateFlat(NdArrayData d, Func<double, double, double> combine)
    {
        var outBuf = new double[d.Size];
        double acc = 0;
        bool first = true;
        int flat = 0;
        ForEachBroadcastIndex(d.Shape, index =>
        {
            double v = AsComparableDouble(d, DotProduct(index, d.Strides));
            acc = first ? v : combine(acc, v);
            first = false;
            outBuf[flat++] = acc;
        });
        return new NdArrayData(DType.Float64, outBuf, new[] { d.Size });
    }

    /// <summary>`axis=k`: cumulates along one axis, keeping the original shape. Real C-order
    /// traversal visits every smaller index along *any* axis before a larger one (holding all other
    /// axes fixed) — so a simple per-line running total keyed by `LineKey` is correct regardless of
    /// which axis is chosen, not just the last one.</summary>
    private static NdArrayData CumulateAxis(NdArrayData d, int axis, Func<double, double, double> combine)
    {
        axis = NormalizeAxis(axis, d.Ndim);
        int[] excludedStrides = NdArrayData.ComputeStrides(d.Shape.Where((_, i) => i != axis).ToArray());
        var outBuf = new double[d.Size];
        var lineAcc = new Dictionary<int, double>();
        ForEachBroadcastIndex(d.Shape, index =>
        {
            int srcOffset = DotProduct(index, d.Strides);
            double v = AsComparableDouble(d, srcOffset);
            int key = LineKey(index, axis, excludedStrides);
            double acc = lineAcc.TryGetValue(key, out var prev) ? combine(prev, v) : v;
            lineAcc[key] = acc;
            outBuf[srcOffset] = acc;
        });
        return new NdArrayData(DType.Float64, outBuf, (int[])d.Shape.Clone());
    }

    private static int? AxisArg(object[] a, Dictionary<string, object>? kwargs, int positionalIndex)
    {
        object? raw = a.Length > positionalIndex ? a[positionalIndex]
            : kwargs is not null && kwargs.TryGetValue("axis", out var v) ? v : null;
        return raw is null or PyNone ? null : (int)PyOps.AsBigInt(raw, "axis");
    }

    private static NdArrayData ElementwiseBinary(NdArrayData a, NdArrayData b, Func<double, double, double> op)
    {
        var (shape, stridesA, stridesB) = PrepareBroadcast(a, b);
        var bufA = (double[])a.Buffer;
        var bufB = (double[])b.Buffer;
        var outBuf = new double[SizeOf(shape)];
        int flat = 0;
        ForEachBroadcastIndex(shape, index =>
            outBuf[flat++] = op(bufA[DotProduct(index, stridesA)], bufB[DotProduct(index, stridesB)]));
        return new NdArrayData(DType.Float64, outBuf, shape);
    }

    /// <summary>Same broadcasted iteration as `ElementwiseBinary`, but comparing (via
    /// `AsComparableDouble`, so either operand can be `Float64` or `Bool`) into a real `Bool`-dtype
    /// result — the shared machinery behind `==`/`!=`/`&lt;`/`&lt;=`/`&gt;`/`&gt;=` (Phase 5.2).</summary>
    private static NdArrayData ElementwiseCompare(NdArrayData a, NdArrayData b, Func<double, double, bool> cmp)
    {
        var (shape, stridesA, stridesB) = PrepareBroadcast(a, b);
        var outBuf = new bool[SizeOf(shape)];
        int flat = 0;
        ForEachBroadcastIndex(shape, index =>
            outBuf[flat++] = cmp(
                AsComparableDouble(a, DotProduct(index, stridesA)),
                AsComparableDouble(b, DotProduct(index, stridesB))));
        return new NdArrayData(DType.Bool, outBuf, shape);
    }

    /// <summary>The `Bool`-only counterpart for `&amp;`/`|`/`^` (Phase 5.3) — both operands must
    /// already be real bool arrays (no truthiness coercion from float arrays; matches real numpy,
    /// where `&amp;`/`|` on a float array does real bitwise integer operations instead, out of v1
    /// scope here).</summary>
    private static NdArrayData LogicalBinary(NdArrayData a, NdArrayData b, Func<bool, bool, bool> op)
    {
        RequireBoolDType(a);
        RequireBoolDType(b);
        var (shape, stridesA, stridesB) = PrepareBroadcast(a, b);
        var bufA = (bool[])a.Buffer;
        var bufB = (bool[])b.Buffer;
        var outBuf = new bool[SizeOf(shape)];
        int flat = 0;
        ForEachBroadcastIndex(shape, index =>
            outBuf[flat++] = op(bufA[DotProduct(index, stridesA)], bufB[DotProduct(index, stridesB)]));
        return new NdArrayData(DType.Bool, outBuf, shape);
    }

    private static void RequireBoolDType(NdArrayData d)
    {
        if (d.DType != DType.Bool)
            throw PyErr.TypeError(
                "ufunc supports only bool arrays for this v1 shim's logical operators (see NUMPY_PLAN.md Phase 5.3)");
    }

    private static NdArrayData ElementwiseUnary(NdArrayData d, Func<double, double> op)
    {
        var buf = (double[])d.Buffer;
        var outBuf = new double[buf.Length];
        for (int i = 0; i < buf.Length; i++)
            outBuf[i] = op(buf[i]);
        return new NdArrayData(d.DType, outBuf, (int[])d.Shape.Clone());
    }

    private static object ElementwiseOp(object aObj, object bObj, Func<double, double, double> op)
        => Wrap(ElementwiseBinary(OperandData(aObj), OperandData(bObj), op));

    private static object CompareOp(object aObj, object bObj, Func<double, double, bool> cmp)
        => Wrap(ElementwiseCompare(OperandData(aObj), OperandData(bObj), cmp));

    private static object LogicalOp(object aObj, object bObj, Func<bool, bool, bool> op)
        => Wrap(LogicalBinary(LogicalOperandData(aObj), LogicalOperandData(bObj), op));

    /// <summary>Scalar coercion specific to logical ops: a real Python `bool` becomes a real 0-d
    /// `Bool` array (so `mask &amp; True` works), distinct from `OperandData`'s own coercion (which
    /// treats a scalar `bool` as `1.0`/`0.0` for arithmetic/comparison — `True` means different
    /// things in the two contexts, matching real numpy's own type-dependent behavior).</summary>
    private static NdArrayData LogicalOperandData(object o) => o switch
    {
        PyInstance pi when pi.Class == NdArrayClass => Data(pi),
        bool b => new NdArrayData(DType.Bool, new[] { b }, Array.Empty<int>()),
        _ => throw PyErr.TypeError($"expected a bool array or bool, got {PyOps.TypeName(o)}"),
    };

    /// <summary>Lets `ElementwiseBinary`/broadcasting treat a plain Python scalar (int/float/bool)
    /// exactly like a real 0-d array — `2 + arr` and `np.array(2.0) + arr` take the same code path,
    /// no special-casing needed.</summary>
    private static NdArrayData OperandData(object o) => o switch
    {
        PyInstance pi when pi.Class == NdArrayClass => Data(pi),
        _ => new NdArrayData(DType.Float64, new[] { PyOps.AsDouble(o) }, Array.Empty<int>()),
    };

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

        Add("copy", (_, a, _) => Wrap(CopyOf(Data(a[0]))));

        // Phase 7 — the two ufuncs real numpy also exposes as ndarray methods (unlike sqrt/exp/
        // sin/etc., which are module-level only).
        Add("round", (_, a, kwargs) =>
        {
            int decimals = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "decimals")
                : kwargs is not null && kwargs.TryGetValue("decimals", out var dec) ? (int)PyOps.AsBigInt(dec, "decimals") : 0;
            return Wrap(ElementwiseUnary(Data(a[0]), x => Math.Round(x, decimals, MidpointRounding.ToEven)));
        });
        Add("clip", (_, a, _) =>
        {
            double lo = PyOps.AsDouble(a[1]);
            double hi = PyOps.AsDouble(a[2]);
            return Wrap(ElementwiseUnary(Data(a[0]), x => Math.Clamp(x, lo, hi)));
        });

        Add("__getitem__", (_, a, _) => GetItem(Data(a[0]), a[1]));
        Add("__setitem__", (_, a, _) =>
        {
            SetItem(Data(a[0]), a[1], a[2]);
            return PyNone.Instance;
        });

        // Phase 4 — elementwise ops & real broadcasting. `@`/`__matmul__` is already wired into
        // the interpreter's own operator table (`Interp.BinDunders`) and deliberately left
        // unimplemented here — real matrix multiplication is Phase 10 (linear algebra), not this
        // one. `+= -= *= /=` need no dedicated `__iadd__`/etc. here at all: `Interp.ExecAugAssign`
        // already falls back to the plain binary dunder + rebinding the name when no `__i*__` is
        // defined (see NUMPY_PLAN.md Phase 4.8's own note) — a real, deliberate simplification,
        // *not* true in-place mutation (an aliased second reference to the same array does NOT see
        // the update, unlike real numpy's actual in-place buffer mutation; nothing here has real
        // views/aliasing yet to make that difference observable in practice either).
        Add("__add__", (_, a, _) => ElementwiseOp(a[0], a[1], static (x, y) => x + y));
        Add("__radd__", (_, a, _) => ElementwiseOp(a[1], a[0], static (x, y) => x + y));
        Add("__sub__", (_, a, _) => ElementwiseOp(a[0], a[1], static (x, y) => x - y));
        Add("__rsub__", (_, a, _) => ElementwiseOp(a[1], a[0], static (x, y) => x - y));
        Add("__mul__", (_, a, _) => ElementwiseOp(a[0], a[1], static (x, y) => x * y));
        Add("__rmul__", (_, a, _) => ElementwiseOp(a[1], a[0], static (x, y) => x * y));
        Add("__truediv__", (_, a, _) => ElementwiseOp(a[0], a[1], static (x, y) => x / y));
        Add("__rtruediv__", (_, a, _) => ElementwiseOp(a[1], a[0], static (x, y) => x / y));
        Add("__pow__", (_, a, _) => ElementwiseOp(a[0], a[1], Math.Pow));
        Add("__rpow__", (_, a, _) => ElementwiseOp(a[1], a[0], Math.Pow));

        Add("__neg__", (_, a, _) => Wrap(ElementwiseUnary(Data(a[0]), static x => -x)));
        Add("__pos__", (_, a, _) => Wrap(ElementwiseUnary(Data(a[0]), static x => x)));
        Add("__abs__", (_, a, _) => Wrap(ElementwiseUnary(Data(a[0]), Math.Abs)));

        // Phase 5 — comparisons/bool arrays/masking. Reachable through the plain `== != < <= > >=`
        // operators only because `Interp.CompareExpr` now returns a *single* comparison's raw
        // dunder result instead of always collapsing to bool (see the `Interp.cs` change this
        // phase needed — real numpy's `arr1 < arr2` genuinely returns an array, not a bool).
        Add("__eq__", (_, a, _) => CompareOp(a[0], a[1], static (x, y) => x == y));
        Add("__ne__", (_, a, _) => CompareOp(a[0], a[1], static (x, y) => x != y));
        Add("__lt__", (_, a, _) => CompareOp(a[0], a[1], static (x, y) => x < y));
        Add("__le__", (_, a, _) => CompareOp(a[0], a[1], static (x, y) => x <= y));
        Add("__gt__", (_, a, _) => CompareOp(a[0], a[1], static (x, y) => x > y));
        Add("__ge__", (_, a, _) => CompareOp(a[0], a[1], static (x, y) => x >= y));

        Add("__and__", (_, a, _) => LogicalOp(a[0], a[1], static (x, y) => x && y));
        Add("__rand__", (_, a, _) => LogicalOp(a[1], a[0], static (x, y) => x && y));
        Add("__or__", (_, a, _) => LogicalOp(a[0], a[1], static (x, y) => x || y));
        Add("__ror__", (_, a, _) => LogicalOp(a[1], a[0], static (x, y) => x || y));
        Add("__xor__", (_, a, _) => LogicalOp(a[0], a[1], static (x, y) => x ^ y));
        Add("__rxor__", (_, a, _) => LogicalOp(a[1], a[0], static (x, y) => x ^ y));
        Add("__invert__", (_, a, _) =>
        {
            var d = Data(a[0]);
            RequireBoolDType(d);
            var buf = (bool[])d.Buffer;
            var outBuf = new bool[buf.Length];
            for (int i = 0; i < buf.Length; i++)
                outBuf[i] = !buf[i];
            return Wrap(new NdArrayData(DType.Bool, outBuf, (int[])d.Shape.Clone()));
        });

        Add("any", (_, a, _) =>
        {
            var d = Data(a[0]);
            for (int i = 0; i < d.Size; i++)
                if (AsComparableDouble(d, i) != 0.0)
                    return true;
            return false;
        });
        Add("all", (_, a, _) =>
        {
            var d = Data(a[0]);
            for (int i = 0; i < d.Size; i++)
                if (AsComparableDouble(d, i) == 0.0)
                    return false;
            return true;
        });

        // Phase 6 — reductions. Every one of these takes an optional `axis=` (positional or
        // keyword, matching real numpy's own signatures): omitted, it folds the whole array to a
        // scalar; given, it folds along just that axis, producing a real array with that axis
        // removed. `sum`/`prod` have a real empty-array identity (0.0/1.0, matching real numpy);
        // `min`/`max` don't (real numpy has none either) and raise a real `ValueError`.
        Add("sum", (_, a, kwargs) => ReduceDispatch(Data(a[0]), AxisArg(a, kwargs, 1), static (x, y) => x + y, 0.0));
        Add("prod", (_, a, kwargs) => ReduceDispatch(Data(a[0]), AxisArg(a, kwargs, 1), static (x, y) => x * y, 1.0));
        Add("min", (_, a, kwargs) => ReduceDispatch(Data(a[0]), AxisArg(a, kwargs, 1), Math.Min, null));
        Add("max", (_, a, kwargs) => ReduceDispatch(Data(a[0]), AxisArg(a, kwargs, 1), Math.Max, null));
        Add("mean", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return axis is int ax ? Wrap(MeanAxis(d, ax)) : MeanAll(d);
        });
        Add("std", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return axis is int ax ? Wrap(ElementwiseUnary(VarAxis(d, ax), Math.Sqrt)) : Math.Sqrt(VarAll(d));
        });
        Add("var", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return axis is int ax ? Wrap(VarAxis(d, ax)) : VarAll(d);
        });
        Add("argmin", (_, a, kwargs) => ArgReduce(Data(a[0]), AxisArg(a, kwargs, 1), static (cand, best) => cand < best));
        Add("argmax", (_, a, kwargs) => ArgReduce(Data(a[0]), AxisArg(a, kwargs, 1), static (cand, best) => cand > best));
        Add("cumsum", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return Wrap(axis is int ax ? CumulateAxis(d, ax, static (x, y) => x + y) : CumulateFlat(d, static (x, y) => x + y));
        });
        Add("cumprod", (_, a, kwargs) =>
        {
            var d = Data(a[0]);
            int? axis = AxisArg(a, kwargs, 1);
            return Wrap(axis is int ax ? CumulateAxis(d, ax, static (x, y) => x * y) : CumulateFlat(d, static (x, y) => x * y));
        });

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
        DType.Bool => BoolDType,
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
            return FormatElement(d, GetElement(d, 0));
        return FormatDim(d, 0, 0);
    }

    private static string FormatDim(NdArrayData d, int dim, int baseOffset)
    {
        int n = d.Shape[dim];
        if (dim == d.Shape.Length - 1)
        {
            var parts = new string[n];
            for (int i = 0; i < n; i++)
                parts[i] = FormatElement(d, GetElement(d, baseOffset + i * d.Strides[dim]));
            return "[" + string.Join(" ", parts) + "]";
        }
        var rows = new string[n];
        for (int i = 0; i < n; i++)
            rows[i] = FormatDim(d, dim + 1, baseOffset + i * d.Strides[dim]);
        string pad = new string(' ', dim + 1);
        return "[" + string.Join("\n" + pad, rows) + "]";
    }

    /// <summary>Real numpy shows a whole-number float with a trailing "." and no "0" (`1.` not
    /// `1.0`) in array printing; a bool prints as real Python's own `True`/`False` — real numpy
    /// additionally pads bool elements to a common column width (`" True"`/`"False"`), which this
    /// v1 skips (the same documented "no column-width alignment yet" simplification already noted
    /// for float arrays in Phase 1.6).</summary>
    private static string FormatElement(NdArrayData d, object value) => d.DType switch
    {
        DType.Float64 => FormatFloatElement((double)value),
        DType.Bool => (bool)value ? "True" : "False",
        _ => value.ToString() ?? "",
    };

    private static string FormatFloatElement(double v)
    {
        string s = PyOps.ReprDouble(v);
        return s.EndsWith(".0", StringComparison.Ordinal) ? s[..^1] : s;
    }

    private static PyProperty MakeProperty(Func<object, object> getter)
        => new() { Getter = new PyBuiltinFunction("ndarray.<property>", (_, a, _) => getter(a[0])) };
}
