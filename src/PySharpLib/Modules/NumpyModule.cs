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

        return m;
    }

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
        => new(d.DType, (double[])((double[])d.Buffer).Clone(), (int[])d.Shape.Clone());

    /// <summary>Real numpy shape inference off a (possibly nested) Python list/tuple: the shape is
    /// read by descending through the *first* element of each level (matching real numpy), then a
    /// single validating pass confirms every branch actually has that same shape — a mismatch
    /// anywhere (a ragged row, a scalar where a nested list was expected, or vice versa) raises the
    /// real `ValueError` numpy itself raises for an inhomogeneous shape. A bare scalar (no list at
    /// all) produces a real 0-d array, matching `np.array(5.0)`.</summary>
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
        var flat = new List<double>();
        AppendFlat(value, shape.ToArray(), 0, flat);
        return new NdArrayData(DType.Float64, flat.ToArray(), shape.ToArray());
    }

    private static IReadOnlyList<object> SequenceItems(object o) => o switch
    {
        PyList l => l.Items,
        PyTuple t => t.Items,
        _ => throw new InvalidOperationException("not a sequence"),
    };

    private static void AppendFlat(object value, int[] shape, int depth, List<double> flat)
    {
        bool isLeaf = depth == shape.Length;
        bool isSequence = value is PyList or PyTuple;
        if (isLeaf)
        {
            if (isSequence)
                throw RaggedArrayError();
            flat.Add(PyOps.AsDouble(value));
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
    /// Python `float` scalar, matching real numpy — everything else returns a new, independent
    /// `ndarray` **copy** (real strided views are a later, optional phase — see NUMPY_PLAN.md
    /// Phase 12).</summary>
    private static object GetItem(NdArrayData d, object index)
    {
        var (axisOffsets, resultShape) = ResolveAxes(d, index);
        var buf = (double[])d.Buffer;
        if (resultShape.Length == 0)
            return buf[FlatOffset(d, axisOffsets)];

        var flat = new List<double>();
        GatherRecursive(buf, d.Strides, axisOffsets, 0, 0, flat);
        return Wrap(new NdArrayData(d.DType, flat.ToArray(), resultShape));
    }

    /// <summary>`a[index] = value`: a fully-reduced index assigns a single scalar element; any
    /// other index assigns either a broadcast scalar (`a[1:3] = 5.0`) or another array whose shape
    /// must exactly match the indexed region (`a[1:3] = other` — real per-element broadcasting
    /// beyond an exact shape match is Phase 4's job, not this one).</summary>
    private static void SetItem(NdArrayData d, object index, object value)
    {
        var (axisOffsets, resultShape) = ResolveAxes(d, index);
        var buf = (double[])d.Buffer;
        if (resultShape.Length == 0)
        {
            buf[FlatOffset(d, axisOffsets)] = PyOps.AsDouble(value);
            return;
        }

        if (value is PyInstance pi && pi.Class == NdArrayClass)
        {
            var src = Data(pi);
            if (!src.Shape.SequenceEqual(resultShape))
                throw PyErr.ValueError(
                    $"could not broadcast input array from shape {ShapeRepr(src.Shape)} into shape {ShapeRepr(resultShape)}");
            var srcBuf = (double[])src.Buffer;
            int i = 0;
            ScatterRecursive(buf, d.Strides, axisOffsets, 0, 0, () => srcBuf[i++]);
        }
        else
        {
            double scalar = PyOps.AsDouble(value);
            ScatterRecursive(buf, d.Strides, axisOffsets, 0, 0, () => scalar);
        }
    }

    private static int FlatOffset(NdArrayData d, int[][] axisOffsets)
    {
        int offset = 0;
        for (int axis = 0; axis < d.Ndim; axis++)
            offset += axisOffsets[axis][0] * d.Strides[axis];
        return offset;
    }

    private static void GatherRecursive(double[] buf, int[] strides, int[][] axisOffsets, int axis, int baseOffset, List<double> flat)
    {
        if (axis == axisOffsets.Length)
        {
            flat.Add(buf[baseOffset]);
            return;
        }
        foreach (int off in axisOffsets[axis])
            GatherRecursive(buf, strides, axisOffsets, axis + 1, baseOffset + off * strides[axis], flat);
    }

    private static void ScatterRecursive(double[] buf, int[] strides, int[][] axisOffsets, int axis, int baseOffset, Func<double> nextValue)
    {
        if (axis == axisOffsets.Length)
        {
            buf[baseOffset] = nextValue();
            return;
        }
        foreach (int off in axisOffsets[axis])
            ScatterRecursive(buf, strides, axisOffsets, axis + 1, baseOffset + off * strides[axis], nextValue);
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

    private static NdArrayData ElementwiseBinary(NdArrayData a, NdArrayData b, Func<double, double, double> op)
    {
        int[] shape = BroadcastShape(a.Shape, b.Shape);
        int[] stridesA = BroadcastStrides(a.Shape, a.Strides, shape);
        int[] stridesB = BroadcastStrides(b.Shape, b.Strides, shape);
        var bufA = (double[])a.Buffer;
        var bufB = (double[])b.Buffer;
        int size = shape.Aggregate(1, (acc, dim) => acc * dim);
        var outBuf = new double[size];
        var index = new int[shape.Length];
        for (int flat = 0; flat < size; flat++)
        {
            int offA = 0, offB = 0;
            for (int d = 0; d < shape.Length; d++)
            {
                offA += index[d] * stridesA[d];
                offB += index[d] * stridesB[d];
            }
            outBuf[flat] = op(bufA[offA], bufB[offB]);
            for (int d = shape.Length - 1; d >= 0; d--)
            {
                if (++index[d] < shape[d])
                    break;
                index[d] = 0;
            }
        }
        return new NdArrayData(DType.Float64, outBuf, shape);
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
