// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>The real, if v1-scoped, payload behind a shim `ndarray` — a flat C-order buffer plus
/// shape/strides, mirroring real numpy's own memory model closely enough that later phases (views,
/// `.tobytes()`) aren't fighting the representation. Strides are element counts, not bytes
/// (byte-level strides are a `.tobytes()`-era concern, not needed yet).
///
/// `Offset`/`Base` (Phase 12.1): a view shares another array's `Buffer` instead of owning a fresh
/// one — `Offset` is the absolute element position in that shared `Buffer` where *this* array's own
/// logical index 0 lives, and `Base` is the array that actually owns the buffer (real numpy's own
/// `.base`), so a chain of views (e.g. a slice of a transpose) always traces back to one real owner.
/// Every dtype-generic buffer access in this file goes through `GetElement`/`SetElement`, which add
/// `Offset` internally — so a fresh, buffer-owning array (`Offset` 0, `Base` null, the 3-arg
/// constructor below) and a real view are indistinguishable to every other function in this file.</summary>
public sealed class NdArrayData
{
    public DType DType { get; }
    public Array Buffer { get; }
    public int[] Shape { get; }
    public int[] Strides { get; }
    public int Offset { get; }
    public NdArrayData? Base { get; }
    public int Ndim => Shape.Length;
    public int Size { get; }

    /// <summary>Builds a fresh array that owns its own buffer: real C-contiguous strides, offset 0,
    /// no base. Used everywhere a function allocates a brand-new result (construction, arithmetic,
    /// reductions, `.copy()`, ...) — i.e., almost everywhere in this file.</summary>
    public NdArrayData(DType dtype, Array buffer, int[] shape)
        : this(dtype, buffer, shape, ComputeStrides(shape), 0, null)
    {
    }

    /// <summary>Builds a real view: an explicit `strides`/`offset` into someone else's `buffer`,
    /// with `base_` keeping a reference to the true owner alive and reachable. Used by the shape/
    /// indexing operations Phase 12.1 turned into genuine views (`reshape`/`ravel`/`transpose`/`.T`/
    /// `expand_dims`/`squeeze`/basic `__getitem__` indexing).</summary>
    public NdArrayData(DType dtype, Array buffer, int[] shape, int[] strides, int offset, NdArrayData? base_)
    {
        DType = dtype;
        Buffer = buffer;
        Shape = shape;
        Strides = strides;
        Offset = offset;
        Base = base_;
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
    Int64,
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
    public static readonly PyInstance Int64DType = MakeDType("int64");

    /// <summary>`numpy.random`'s global RNG state (Phase 11.4) — a plain C# `Random`, reseeded by
    /// `np.random.seed(n)`; module-level (not per-`Create()`-call) so it persists across repeated
    /// `import numpy` the same way real numpy's own global RNG state does.</summary>
    private static Random _random = new();

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
        m.Dict["array"] = new PyBuiltinFunction("array", (_, a, kwargs) =>
        {
            var arr = ArrayFromPython(a[0]);
            DType? dtype = DTypeArg(a, kwargs, 1);
            return Wrap(dtype is DType dt ? AsDType(arr, dt) : arr);
        });

        m.Dict["zeros"] = new PyBuiltinFunction("zeros", (_, a, kwargs) =>
        {
            int[] shape = ShapeArg(RequireArg(a, kwargs, 0, "shape"));
            DType dtype = DTypeArg(a, kwargs, 1) ?? DType.Float64;
            return Wrap(new NdArrayData(dtype, MakeBuffer(dtype, SizeOf(shape)), shape));
        });
        m.Dict["ones"] = new PyBuiltinFunction("ones", (_, a, kwargs) =>
        {
            int[] shape = ShapeArg(RequireArg(a, kwargs, 0, "shape"));
            DType dtype = DTypeArg(a, kwargs, 1) ?? DType.Float64;
            var buf = MakeBuffer(dtype, SizeOf(shape));
            for (int i = 0; i < buf.Length; i++)
                SetBufferElement(buf, dtype, i, CoerceTo(dtype, 1.0));
            return Wrap(new NdArrayData(dtype, buf, shape));
        });
        m.Dict["full"] = new PyBuiltinFunction("full", (_, a, kwargs) =>
        {
            int[] shape = ShapeArg(RequireArg(a, kwargs, 0, "shape"));
            object rawValue = RequireArg(a, kwargs, 1, "fill_value");
            DType dtype = DTypeArg(a, kwargs, 2) ?? (rawValue is bool ? DType.Bool
                : rawValue is BigInteger ? DType.Int64 : DType.Float64);
            object value = CoerceTo(dtype, rawValue switch
            {
                bool b => b, BigInteger bi => bi, _ => PyOps.AsDouble(rawValue),
            });
            var buf = MakeBuffer(dtype, SizeOf(shape));
            for (int i = 0; i < buf.Length; i++)
                SetBufferElement(buf, dtype, i, value);
            return Wrap(new NdArrayData(dtype, buf, shape));
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
            DType dtype = DTypeArg(a, kwargs, 1) ?? DType.Float64;
            return Wrap(new NdArrayData(dtype, MakeBuffer(dtype, SizeOf(shape)), shape));
        });

        m.Dict["arange"] = new PyBuiltinFunction("arange", (_, a, kwargs) =>
        {
            // Kept float64-by-default here (unlike real numpy's int64-when-all-int-args inference)
            // to avoid changing every existing `np.arange(6)`-based test's printed output — an
            // explicit `dtype=` still selects int64 when wanted (Phase 9.2).
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
            DType dtype = DTypeArg(a, kwargs, 3) ?? DType.Float64;
            var buf = MakeBuffer(dtype, count);
            for (int i = 0; i < count; i++)
                SetBufferElement(buf, dtype, i, CoerceTo(dtype, start + i * step));
            return Wrap(new NdArrayData(dtype, buf, new[] { count }));
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
        m.Dict["sqrt"] = new PyBuiltinFunction("sqrt", (_, a, _) => ApplyUfunc(a[0], Math.Sqrt, DType.Float64));
        m.Dict["exp"] = new PyBuiltinFunction("exp", (_, a, _) => ApplyUfunc(a[0], Math.Exp, DType.Float64));
        m.Dict["log"] = new PyBuiltinFunction("log", (_, a, _) => ApplyUfunc(a[0], Math.Log, DType.Float64));
        m.Dict["log10"] = new PyBuiltinFunction("log10", (_, a, _) => ApplyUfunc(a[0], Math.Log10, DType.Float64));
        m.Dict["abs"] = new PyBuiltinFunction("abs", (_, a, _) => ApplyUfunc(a[0], Math.Abs));

        m.Dict["sin"] = new PyBuiltinFunction("sin", (_, a, _) => ApplyUfunc(a[0], Math.Sin, DType.Float64));
        m.Dict["cos"] = new PyBuiltinFunction("cos", (_, a, _) => ApplyUfunc(a[0], Math.Cos, DType.Float64));
        m.Dict["tan"] = new PyBuiltinFunction("tan", (_, a, _) => ApplyUfunc(a[0], Math.Tan, DType.Float64));
        m.Dict["arcsin"] = new PyBuiltinFunction("arcsin", (_, a, _) => ApplyUfunc(a[0], Math.Asin, DType.Float64));
        m.Dict["arccos"] = new PyBuiltinFunction("arccos", (_, a, _) => ApplyUfunc(a[0], Math.Acos, DType.Float64));
        m.Dict["arctan"] = new PyBuiltinFunction("arctan", (_, a, _) => ApplyUfunc(a[0], Math.Atan, DType.Float64));

        m.Dict["floor"] = new PyBuiltinFunction("floor", (_, a, _) => ApplyUfunc(a[0], Math.Floor, DType.Float64));
        m.Dict["ceil"] = new PyBuiltinFunction("ceil", (_, a, _) => ApplyUfunc(a[0], Math.Ceiling, DType.Float64));
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
        // Real numpy: `np.newaxis is None` — genuinely the same object, not a distinct sentinel.
        m.Dict["newaxis"] = PyNone.Instance;

        // Phase 9.6 — dtype singletons, usable both as `dtype=` values and standalone
        // (`a.dtype == np.float64`, `np.int64(x)` — the latter not supported here, matching this
        // shim's "no dtype-as-constructor-callable" v1 scope).
        m.Dict["float64"] = Float64DType;
        m.Dict["int64"] = Int64DType;
        m.Dict["bool_"] = BoolDType;

        // Phase 8 — shape manipulation. `reshape`/`ravel`/`expand_dims`/`squeeze`/`transpose`/`.T`
        // all share the source buffer as real views (Phase 12.1 gave `transpose`/`.T` real
        // non-canonical strides too, and `reshape`/`ravel` a real fallback to a fresh copy when the
        // source isn't contiguous — see each function's own docstring). `flatten()` is the one
        // deliberate exception: always a real, independent copy, matching real numpy's own actual
        // behavior there.
        m.Dict["reshape"] = new PyBuiltinFunction("reshape", (_, a, _) => Wrap(Reshape(Data(a[0]), ReshapeShapeArg(a, 1))));
        m.Dict["ravel"] = new PyBuiltinFunction("ravel", (_, a, _) => Wrap(Ravel(Data(a[0]))));
        m.Dict["transpose"] = new PyBuiltinFunction("transpose", (_, a, _) =>
            Wrap(Transpose(Data(a[0]), a.Length > 1 ? ReshapeShapeArg(a, 1) : null)));
        m.Dict["concatenate"] = new PyBuiltinFunction("concatenate", (interp, a, kwargs) =>
        {
            var arrays = PyOps.Iterate(interp, a[0]).Select(x => Data(x)).ToList();
            int axis = AxisArg(a, kwargs, 1) ?? 0;
            return Wrap(Concatenate(arrays, axis));
        });
        m.Dict["stack"] = new PyBuiltinFunction("stack", (interp, a, kwargs) =>
        {
            var arrays = PyOps.Iterate(interp, a[0]).Select(x => Data(x)).ToList();
            int axis = AxisArg(a, kwargs, 1) ?? 0;
            return Wrap(Stack(arrays, axis));
        });
        m.Dict["vstack"] = new PyBuiltinFunction("vstack", (interp, a, _) =>
            Wrap(Vstack(PyOps.Iterate(interp, a[0]).Select(x => Data(x)).ToList())));
        m.Dict["hstack"] = new PyBuiltinFunction("hstack", (interp, a, _) =>
            Wrap(Hstack(PyOps.Iterate(interp, a[0]).Select(x => Data(x)).ToList())));
        m.Dict["expand_dims"] = new PyBuiltinFunction("expand_dims", (_, a, kwargs) =>
            Wrap(ExpandDims(Data(a[0]), AxisArg(a, kwargs, 1) ?? 0)));
        m.Dict["squeeze"] = new PyBuiltinFunction("squeeze", (_, a, kwargs) =>
            Wrap(Squeeze(Data(a[0]), AxisArg(a, kwargs, 1))));

        // Phase 10 — basic linear algebra. `dot`/`matmul`/`@` all share the same `MatMul` core for
        // 1-D/2-D operands (real numpy's `dot` and `@` genuinely agree there; they only diverge for
        // N-D "stacked" batches and bare-scalar operands, which this v1 shim doesn't support — see
        // NUMPY_PLAN.md's own Phase 10.4 note). `MatMulOperand` accepts a raw nested Python list too
        // (not just an `ndarray`), matching real numpy's own liberal `np.dot([1, 2], [3, 4])`.
        m.Dict["dot"] = new PyBuiltinFunction("dot", (_, a, _) => MatMulResult(MatMul(MatMulOperand(a[0]), MatMulOperand(a[1]))));
        m.Dict["matmul"] = new PyBuiltinFunction("matmul", (_, a, _) => MatMulResult(MatMul(MatMulOperand(a[0]), MatMulOperand(a[1]))));
        m.Dict["trace"] = new PyBuiltinFunction("trace", (_, a, kwargs) =>
            ReduceAllToScalar(Diagonal(Data(a[0]), OffsetArg(a, kwargs, 1)), static (x, y) => x + y, 0.0));
        m.Dict["diagonal"] = new PyBuiltinFunction("diagonal", (_, a, kwargs) =>
            Wrap(Diagonal(Data(a[0]), OffsetArg(a, kwargs, 1))));

        // `numpy.linalg` — a real nested submodule (same pattern as `os.path`: an attribute reached
        // via `numpy.linalg` after `import numpy`, also separately registered for `import
        // numpy.linalg`/`from numpy.linalg import norm` in StdlibModules.cs).
        var linalg = new PyModule("numpy.linalg");
        linalg.Dict["norm"] = new PyBuiltinFunction("norm", (_, a, _) => Norm(Data(a[0])));
        m.Dict["linalg"] = linalg;

        // `numpy.random` — same real-nested-submodule pattern as `linalg`. This shim's RNG is a
        // plain C# `System.Random`, not real numpy's actual Mersenne Twister/PCG64 algorithm, so
        // `seed(n)` makes *this shim's own* sequence reproducible run-to-run — it does not (and
        // cannot, without porting numpy's real bit-generator) reproduce real numpy's exact values
        // for a given seed. That's the intended v1 scope (NUMPY_PLAN.md 11.4: "small, deterministic
        // with seed", not "bit-identical to real numpy").
        var random = new PyModule("numpy.random");
        random.Dict["seed"] = new PyBuiltinFunction("seed", (_, a, _) =>
        {
            _random = a.Length > 0 && a[0] is not PyNone ? new Random((int)PyOps.AsBigInt(a[0], "seed")) : new Random();
            return PyNone.Instance;
        });
        random.Dict["rand"] = new PyBuiltinFunction("rand", (_, a, _) => RandomArray(a, static rnd => rnd.NextDouble()));
        random.Dict["randn"] = new PyBuiltinFunction("randn", (_, a, _) => RandomArray(a, NextGaussian));
        random.Dict["randint"] = new PyBuiltinFunction("randint", (_, a, kwargs) => RandInt(a, kwargs));
        random.Dict["choice"] = new PyBuiltinFunction("choice", (_, a, kwargs) => Choice(a, kwargs));
        m.Dict["random"] = random;

        return m;
    }

    private static int OffsetArg(object[] a, Dictionary<string, object>? kwargs, int positionalIndex)
    {
        object? raw = a.Length > positionalIndex ? a[positionalIndex]
            : kwargs is not null && kwargs.TryGetValue("offset", out var v) ? v : null;
        return raw is null or PyNone ? 0 : (int)PyOps.AsBigInt(raw, "offset");
    }

    /// <summary>The real Phase 7.1 "ufunc factory": elementwise on an `ndarray` (via the existing
    /// `ElementwiseUnary`), or a real scalar fast path — `np.sqrt(4.0)` returns a plain Python
    /// `float`, matching real numpy's own scalar ufunc behavior (no forced 0-d array wrap).</summary>
    private static object ApplyUfunc(object arg, Func<double, double> op, DType? forceDType = null)
        => arg is PyInstance pi && pi.Class == NdArrayClass
            ? Wrap(ElementwiseUnary(Data(pi), op, forceDType))
            : op(PyOps.AsDouble(arg));

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

    /// <summary>Real numpy `.copy()`: always a fresh, independent, C-contiguous array with no `Base`
    /// — regardless of whether `d` itself is a view (Phase 12.1). `MaterializeContiguousBuffer`
    /// walks `d` correctly for that case; the 3-arg `NdArrayData` constructor makes the result own
    /// its own buffer outright.</summary>
    private static NdArrayData CopyOf(NdArrayData d)
        => new(d.DType, MaterializeContiguousBuffer(d), (int[])d.Shape.Clone());

    // ---------------------------------------------------------------- Phase 8: shape manipulation

    /// <summary>Accepts real numpy's two equivalent call shapes for a shape argument starting at
    /// `startIndex`: a single sequence (`a.reshape((2, 3))`) or separate positional ints
    /// (`a.reshape(2, 3)`) — including the single-int case (`a.reshape(6)`). `-1` entries are left
    /// as-is here; `Reshape` is what actually infers them (this helper is reused by `transpose`
    /// too, which has no `-1` inference at all).</summary>
    private static int[] ReshapeShapeArg(object[] a, int startIndex)
    {
        var rest = a.Skip(startIndex).ToArray();
        if (rest.Length == 1 && rest[0] is PyTuple or PyList)
            return ShapeArg(rest[0]);
        return rest.Select(x => (int)PyOps.AsBigInt(x, "shape")).ToArray();
    }

    /// <summary>Real numpy `reshape`: the total element count must match, with at most one `-1`
    /// entry inferred from the rest. A real view (Phase 12.1) sharing `d`'s own buffer/offset when
    /// `d` is already contiguous — real numpy's own actual rule ("returns a view... unless a copy is
    /// necessary", e.g. reshaping a transposed array); falls back to materializing a fresh
    /// contiguous copy first otherwise, matching real numpy's own documented fallback.</summary>
    private static NdArrayData Reshape(NdArrayData d, int[] newShape)
    {
        int negOneCount = newShape.Count(static s => s == -1);
        if (negOneCount > 1)
            throw PyErr.ValueError("can only specify one unknown dimension");
        int[] resolvedShape = newShape;
        if (negOneCount == 1)
        {
            int known = newShape.Where(static s => s != -1).Aggregate(1, (acc, s) => acc * s);
            if (known == 0 || d.Size % known != 0)
                throw PyErr.ValueError($"cannot reshape array of size {d.Size} into shape {ShapeRepr(newShape)}");
            int inferred = d.Size / known;
            resolvedShape = newShape.Select(s => s == -1 ? inferred : s).ToArray();
        }
        if (SizeOf(resolvedShape) != d.Size)
            throw PyErr.ValueError($"cannot reshape array of size {d.Size} into shape {ShapeRepr(resolvedShape)}");
        if (IsContiguous(d))
            return new NdArrayData(d.DType, d.Buffer, resolvedShape, NdArrayData.ComputeStrides(resolvedShape), d.Offset, d.Base ?? d);
        return new NdArrayData(d.DType, MaterializeContiguousBuffer(d), resolvedShape);
    }

    /// <summary>Real numpy `ravel`: same view-if-contiguous, else-copy rule as `reshape` (it's
    /// really just `reshape(-1)`).</summary>
    private static NdArrayData Ravel(NdArrayData d) => IsContiguous(d)
        ? new NdArrayData(d.DType, d.Buffer, new[] { d.Size }, new[] { 1 }, d.Offset, d.Base ?? d)
        : new NdArrayData(d.DType, MaterializeContiguousBuffer(d), new[] { d.Size });

    /// <summary>Real numpy `flatten`: always a real, independent copy — unlike `ravel`, real numpy
    /// itself never returns a view here (the whole point of `flatten` vs `ravel` is that guarantee).</summary>
    private static NdArrayData Flatten(NdArrayData d) => new(d.DType, MaterializeContiguousBuffer(d), new[] { d.Size });

    /// <summary>Real numpy `.T`/`transpose(axes)`: no `axes` reverses every axis (the classic 2-D
    /// "swap rows and columns" case generalizes to "reverse the axis order" for N-D); explicit
    /// `axes` permutes to that exact order. A real view (Phase 12.1): reordering the `Shape`/
    /// `Strides` arrays IS the entire transpose — no data ever moves, matching real numpy's own
    /// actual `.T` (a transposed array is famously non-contiguous in real numpy for exactly this
    /// reason).</summary>
    private static NdArrayData Transpose(NdArrayData d, int[]? axes)
    {
        int ndim = d.Ndim;
        int[] perm = axes ?? Enumerable.Range(0, ndim).Reverse().ToArray();
        if (perm.Length != ndim || perm.Distinct().Count() != ndim || perm.Any(p => p < 0 || p >= ndim))
            throw PyErr.ValueError("axes don't match array");
        int[] newShape = perm.Select(p => d.Shape[p]).ToArray();
        int[] newStrides = perm.Select(p => d.Strides[p]).ToArray();
        return new NdArrayData(d.DType, d.Buffer, newShape, newStrides, d.Offset, d.Base ?? d);
    }

    /// <summary>Real numpy `concatenate`: joins arrays along an *existing* axis — every array must
    /// have the exact same shape except along that axis, whose sizes simply add up.</summary>
    private static NdArrayData Concatenate(List<NdArrayData> arrays, int axis)
    {
        if (arrays.Count == 0)
            throw PyErr.ValueError("need at least one array to concatenate");
        int ndim = arrays[0].Ndim;
        axis = NormalizeAxis(axis, ndim);
        foreach (var arr in arrays)
        {
            if (arr.Ndim != ndim)
                throw PyErr.ValueError("all the input array dimensions must match exactly");
            if (arr.DType != arrays[0].DType)
                throw PyErr.TypeError("concatenate requires all input arrays to share the same dtype");
            for (int ax = 0; ax < ndim; ax++)
                if (ax != axis && arr.Shape[ax] != arrays[0].Shape[ax])
                    throw PyErr.ValueError(
                        "all the input array dimensions except for the concatenation axis must match exactly");
        }
        DType outDType = arrays[0].DType;
        int[] outShape = (int[])arrays[0].Shape.Clone();
        outShape[axis] = arrays.Sum(arr => arr.Shape[axis]);
        var outBuffer = MakeBuffer(outDType, SizeOf(outShape));
        int[] outStrides = NdArrayData.ComputeStrides(outShape);
        int axisOffset = 0;
        foreach (var arr in arrays)
        {
            ForEachBroadcastIndex(arr.Shape, index =>
            {
                var outIndex = (int[])index.Clone();
                outIndex[axis] += axisOffset;
                SetBufferElement(outBuffer, outDType, DotProduct(outIndex, outStrides),
                    GetElement(arr, DotProduct(index, arr.Strides)));
            });
            axisOffset += arr.Shape[axis];
        }
        return new NdArrayData(outDType, outBuffer, outShape);
    }

    /// <summary>Real numpy `stack`: joins same-shaped arrays along a *new* axis (unlike
    /// `concatenate`'s existing one) — built as `expand_dims` on every array followed by a
    /// `concatenate` along that same new axis, rather than a separate algorithm.</summary>
    private static NdArrayData Stack(List<NdArrayData> arrays, int axis)
    {
        if (arrays.Count == 0)
            throw PyErr.ValueError("need at least one array to stack");
        foreach (var arr in arrays)
            if (!arr.Shape.SequenceEqual(arrays[0].Shape))
                throw PyErr.ValueError("all input arrays must have the same shape");
        axis = NormalizeAxis(axis, arrays[0].Ndim + 1);
        return Concatenate(arrays.Select(arr => ExpandDims(arr, axis)).ToList(), axis);
    }

    /// <summary>Real numpy `vstack`: a real 1-D array is treated as a single row (promoted to 2-D
    /// first), then concatenated along axis 0 — matching real numpy's own actual behavior, not just
    /// "stack along axis 0" (which would be wrong for 1-D inputs).</summary>
    private static NdArrayData Vstack(List<NdArrayData> arrays)
        => Concatenate(arrays.Select(arr => arr.Ndim == 1 ? ExpandDims(arr, 0) : arr).ToList(), 0);

    /// <summary>Real numpy `hstack`: 1-D arrays concatenate along their only axis (axis 0); 2-D+
    /// arrays concatenate along axis 1 (the "horizontal" one) instead.</summary>
    private static NdArrayData Hstack(List<NdArrayData> arrays)
        => Concatenate(arrays, arrays.Count > 0 && arrays[0].Ndim == 1 ? 0 : 1);

    /// <summary>Real numpy `expand_dims`: inserts a real size-1 axis at `axis` (0..ndim, inclusive
    /// — it can legally be the new last axis). A real view (Phase 12.1): the synthetic axis gets
    /// stride 0 (its index is always 0, so the stride value never actually gets multiplied by
    /// anything else) — the same convention `None`/`np.newaxis` indexing already uses.</summary>
    private static NdArrayData ExpandDims(NdArrayData d, int axis)
    {
        int newNdim = d.Ndim + 1;
        int resolved = axis < 0 ? axis + newNdim : axis;
        if (resolved < 0 || resolved > d.Ndim)
            throw PyErr.ValueError($"axis {axis} is out of bounds for array of dimension {newNdim}");
        var newShape = new List<int>(d.Shape);
        newShape.Insert(resolved, 1);
        var newStrides = new List<int>(d.Strides);
        newStrides.Insert(resolved, 0);
        return new NdArrayData(d.DType, d.Buffer, newShape.ToArray(), newStrides.ToArray(), d.Offset, d.Base ?? d);
    }

    /// <summary>Real numpy `squeeze`: with an explicit `axis`, removes just that one size-1 axis
    /// (a real `ValueError` if its size isn't actually 1); with none, removes every size-1 axis at
    /// once. A real view (Phase 12.1) — the inverse of `expand_dims`: just drop the corresponding
    /// `Shape`/`Strides` entries together, no data movement.</summary>
    private static NdArrayData Squeeze(NdArrayData d, int? axis)
    {
        if (axis is int ax)
        {
            ax = NormalizeAxis(ax, d.Ndim);
            if (d.Shape[ax] != 1)
                throw PyErr.ValueError(
                    $"cannot select an axis to squeeze out which has size not equal to one");
            return new NdArrayData(
                d.DType, d.Buffer, d.Shape.Where((_, i) => i != ax).ToArray(),
                d.Strides.Where((_, i) => i != ax).ToArray(), d.Offset, d.Base ?? d);
        }
        var keptShape = new List<int>();
        var keptStrides = new List<int>();
        for (int i = 0; i < d.Ndim; i++)
        {
            if (d.Shape[i] == 1)
                continue;
            keptShape.Add(d.Shape[i]);
            keptStrides.Add(d.Strides[i]);
        }
        return new NdArrayData(d.DType, d.Buffer, keptShape.ToArray(), keptStrides.ToArray(), d.Offset, d.Base ?? d);
    }

    // ---------------------------------------------------------------- Phase 10: basic linear algebra

    /// <summary>Real numpy's own promotion rule for combining a 1-D operand into a matrix product:
    /// a 1-D `a` is treated as a `(1, n)` row, a 1-D `b` as an `(n, 1)` column, and whichever
    /// dimension got synthesized this way is dropped again from the result shape afterwards — so
    /// 1-D·1-D gives a real scalar (0-D), 1-D·2-D and 2-D·1-D each give a real 1-D result, and
    /// 2-D·2-D gives a real 2-D result. N-D "stacked/batched" matmul (real numpy's `matmul`/`@` for
    /// operands with more than 2 dimensions) is out of this v1 shim's scope (NUMPY_PLAN.md Phase
    /// 10.4, deliberately deferred — no reachable scenario in this repo needs it yet).</summary>
    private static NdArrayData MatMul(NdArrayData a, NdArrayData b)
    {
        if (a.Ndim is < 1 or > 2 || b.Ndim is < 1 or > 2)
            throw PyErr.ValueError(
                "matmul: only 1-D and 2-D operands are supported by this v1 shim (see NUMPY_PLAN.md Phase 10.4)");
        bool aWas1D = a.Ndim == 1;
        bool bWas1D = b.Ndim == 1;
        int m = aWas1D ? 1 : a.Shape[0];
        int k = aWas1D ? a.Shape[0] : a.Shape[1];
        int n = bWas1D ? 1 : b.Shape[1];
        if (k != b.Shape[0])
            throw PyErr.ValueError(
                $"matmul: input operand shapes {ShapeRepr(a.Shape)} and {ShapeRepr(b.Shape)} are not aligned");

        int aRowStride = aWas1D ? 0 : a.Strides[0];
        int aColStride = aWas1D ? a.Strides[0] : a.Strides[1];
        int bRowStride = bWas1D ? b.Strides[0] : b.Strides[0];
        int bColStride = bWas1D ? 0 : b.Strides[1];

        DType outDType = PromoteForArithmetic(a.DType, b.DType);
        var outBuf = MakeBuffer(outDType, m * n);
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int kk = 0; kk < k; kk++)
                    sum += AsComparableDouble(a, i * aRowStride + kk * aColStride)
                        * AsComparableDouble(b, kk * bRowStride + j * bColStride);
                SetBufferElement(outBuf, outDType, i * n + j, CoerceTo(outDType, sum));
            }
        int[] outShape = (aWas1D, bWas1D) switch
        {
            (true, true) => Array.Empty<int>(),
            (true, false) => new[] { n },
            (false, true) => new[] { m },
            (false, false) => new[] { m, n },
        };
        return new NdArrayData(outDType, outBuf, outShape);
    }

    private static NdArrayData MatMulOperand(object o)
        => o is PyInstance pi && pi.Class == NdArrayClass ? Data(pi) : ArrayFromPython(o);

    /// <summary>Real numpy: `a @ b`/`np.dot(a, b)` for two 1-D operands returns a real scalar, not a
    /// 0-D array (`np.array([1, 2]) @ np.array([3, 4])` is `11`, not `array(11)`) — unwrap a 0-D
    /// `MatMul` result the same way `ApplyUfunc`'s own scalar fast path does.</summary>
    private static object MatMulResult(NdArrayData result) => result.Ndim == 0 ? GetElement(result, 0) : Wrap(result);

    /// <summary>Real numpy `diagonal`/`trace`: `offset` selects a diagonal above (positive) or below
    /// (negative) the main one; length is however many elements fit before running off either edge.
    /// A real copy, not a view — real numpy itself only made `diagonal` a (read-only) view in a
    /// later version, and Phase 12.1's own checklist item only asked for "slices and `.T`", so this
    /// one deliberately stayed out of view scope.</summary>
    private static NdArrayData Diagonal(NdArrayData d, int offset)
    {
        if (d.Ndim != 2)
            throw PyErr.ValueError("diagonal/trace require a 2-D array (a documented v1 simplification)");
        int rows = d.Shape[0], cols = d.Shape[1];
        int startRow = offset >= 0 ? 0 : -offset;
        int startCol = offset >= 0 ? offset : 0;
        int len = Math.Max(0, Math.Min(rows - startRow, cols - startCol));
        var outBuf = MakeBuffer(d.DType, len);
        for (int i = 0; i < len; i++)
            SetBufferElement(outBuf, d.DType, i,
                GetElement(d, (startRow + i) * d.Strides[0] + (startCol + i) * d.Strides[1]));
        return new NdArrayData(d.DType, outBuf, new[] { len });
    }

    /// <summary>Real numpy's default `np.linalg.norm` (no `ord`/`axis`): the 2-norm for a 1-D vector
    /// and the Frobenius norm for a 2-D matrix are the exact same formula — `sqrt(sum(x_i^2))` over
    /// every element — so one flat implementation correctly covers both without special-casing
    /// `ndim`. `ord=`/`axis=` (other norm kinds, per-axis/per-row norms) are out of this v1 shim's
    /// scope.</summary>
    private static double Norm(NdArrayData d)
    {
        double sumSq = 0;
        ForEachBroadcastIndex(d.Shape, index =>
        {
            double v = AsComparableDouble(d, DotProduct(index, d.Strides));
            sumSq += v * v;
        });
        return Math.Sqrt(sumSq);
    }

    // ---------------------------------------------------------------- Phase 11: interop & conveniences

    /// <summary>Real numpy `tolist()`: nested Python lists down to the leaf elements, real Python
    /// scalars at the leaves (not numpy scalars — this shim never had a separate "numpy scalar" type
    /// to begin with, so `GetElement`'s own Python-visible boxed value already *is* the real thing).
    /// A 0-D array's `tolist()` is the bare scalar itself, not a 1-element list — matches real numpy.</summary>
    private static object ToPythonList(NdArrayData d) => BuildPythonList(d, 0, 0);

    private static object BuildPythonList(NdArrayData d, int dim, int baseOffset)
    {
        if (dim == d.Ndim)
            return GetElement(d, baseOffset);
        int n = d.Shape[dim];
        var items = new object[n];
        for (int i = 0; i < n; i++)
            items[i] = BuildPythonList(d, dim + 1, baseOffset + i * d.Strides[dim]);
        return new PyList(items);
    }

    private static NdArrayData RequireSize1(NdArrayData d)
    {
        if (d.Size != 1)
            throw PyErr.TypeError("only size-1 arrays can be converted to Python scalars");
        return d;
    }

    /// <summary>Truncates toward zero regardless of source dtype (real Python/numpy `int(3.7)` ==
    /// `3`, `int(-3.7)` == `-3`) — an `Int64`-dtype element is already a real `BigInteger` from
    /// `GetElement` and needs no conversion at all.</summary>
    private static object ToPyInt(object value) => value switch
    {
        BigInteger bi => bi,
        bool b => (BigInteger)(b ? 1 : 0),
        double d => (BigInteger)d,
        _ => throw new NotSupportedException($"cannot convert {PyOps.TypeName(value)} to int"),
    };

    /// <summary>Real .NET interop bridge (NUMPY_PLAN.md 11.5): a 1-D array only (documented v1
    /// scope, matching the plan's own literal "→ `double[]`" wording) — every element is coerced
    /// through `PyOps.AsDouble` regardless of source dtype, so an int64 array round-trips as a real
    /// `double[]` on the .NET side, same as `AsComparableDouble`'s own dtype-generic numeric
    /// reading elsewhere in this file.</summary>
    private static double[] ToClrDoubleArray(NdArrayData d)
    {
        if (d.Ndim != 1)
            throw PyErr.ValueError("to_clr() only supports 1-D arrays in this v1 shim");
        var result = new double[d.Size];
        int k = 0;
        ForEachBroadcastIndex(d.Shape, index => result[k++] = AsComparableDouble(d, DotProduct(index, d.Strides)));
        return result;
    }

    /// <summary>The other direction of the same bridge: a host `double[]`/`int[]`/`long[]`/`bool[]`
    /// injected via `PyEngine.SetVariable` arrives here as a `ClrObject` wrapping a real .NET array
    /// (see `ClrMarshal.ToPython`'s own default case) — normalized into a `PyList` of already-
    /// marshalled Python values up front so `ArrayFromPython`'s existing shape/dtype-inference
    /// machinery (built for nested `PyList`/`PyTuple`) handles the rest with no duplication.</summary>
    private static object NormalizeClrArrayLike(object value)
    {
        if (value is not ClrObject { Instance: Array arr })
            return value;
        if (arr.Rank != 1)
            throw PyErr.TypeError("np.array from a .NET array only supports 1-D arrays in this v1 shim");
        var items = new object[arr.Length];
        for (int i = 0; i < arr.Length; i++)
            items[i] = ClrMarshal.ToPython(arr.GetValue(i));
        return new PyList(items);
    }

    /// <summary>Real numpy `rand`/`randn`: no args → a plain Python scalar (real numpy: `np.random.
    /// rand()` is a float, not a 0-D array); one or more int args → a real `Float64` array of that
    /// shape, one independent `sample()` draw per element.</summary>
    private static object RandomArray(object[] a, Func<Random, double> sample)
    {
        if (a.Length == 0)
            return sample(_random);
        int[] shape = a.Select(x => (int)PyOps.AsBigInt(x, "shape")).ToArray();
        var buf = new double[SizeOf(shape)];
        for (int i = 0; i < buf.Length; i++)
            buf[i] = sample(_random);
        return Wrap(new NdArrayData(DType.Float64, buf, shape));
    }

    /// <summary>Box-Muller transform — a standard normal sample from two uniform ones, since C#'s
    /// `Random` has no built-in Gaussian sampler.</summary>
    private static double NextGaussian(Random rnd)
    {
        double u1 = 1.0 - rnd.NextDouble(); // (0, 1], never exactly 0 — avoids log(0)
        double u2 = rnd.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Real numpy `randint(low, high=None, size=None)`: with `high` omitted, samples from
    /// `[0, low)` (`low` is really the exclusive upper bound then); with both given, samples from
    /// `[low, high)`. `size=None` → a real scalar Python `int`; given → a real `Int64` array.</summary>
    private static object RandInt(object[] a, Dictionary<string, object>? kwargs)
    {
        object? highArg = a.Length > 1 ? a[1] : kwargs is not null && kwargs.TryGetValue("high", out var h) ? h : null;
        long low, high;
        if (highArg is null or PyNone)
        {
            low = 0;
            high = (long)PyOps.AsBigInt(a[0], "low");
        }
        else
        {
            low = (long)PyOps.AsBigInt(a[0], "low");
            high = (long)PyOps.AsBigInt(highArg, "high");
        }
        object? sizeArg = a.Length > 2 ? a[2] : kwargs is not null && kwargs.TryGetValue("size", out var s) ? s : null;
        if (sizeArg is null or PyNone)
            return (BigInteger)_random.NextInt64(low, high);
        int[] shape = ShapeArg(sizeArg);
        var buf = new long[SizeOf(shape)];
        for (int i = 0; i < buf.Length; i++)
            buf[i] = _random.NextInt64(low, high);
        return Wrap(new NdArrayData(DType.Int64, buf, shape));
    }

    /// <summary>Real numpy `choice(a, size=None, replace=True)`: `a` is either an int (sample from
    /// `arange(a)`) or a real 1-D array-like; `size=None` → a single scalar element; given → a real
    /// array of that shape, dtype matching the pool. `p=` (weighted sampling) is out of this v1
    /// shim's scope.</summary>
    private static object Choice(object[] a, Dictionary<string, object>? kwargs)
    {
        NdArrayData pool = a[0] switch
        {
            BigInteger poolLen => new NdArrayData(
                DType.Int64, Enumerable.Range(0, (int)poolLen).Select(i => (long)i).ToArray(), new[] { (int)poolLen }),
            PyInstance pi when pi.Class == NdArrayClass => Data(pi),
            _ => ArrayFromPython(a[0]),
        };
        if (pool.Ndim != 1)
            throw PyErr.ValueError("choice: a must be 1-D");
        int poolSize = pool.Size;

        object? replaceArg = a.Length > 2 ? a[2] : kwargs is not null && kwargs.TryGetValue("replace", out var r) ? r : null;
        bool replace = replaceArg is not bool rb || rb;

        // `pool` may itself be a view (Phase 12.1 — e.g. `np.random.choice(a[2:])`), so a logical
        // 1-D index must go through `pool.Strides[0]`, not be used as a raw buffer offset.
        object? sizeArg = a.Length > 1 ? a[1] : kwargs is not null && kwargs.TryGetValue("size", out var s) ? s : null;
        if (sizeArg is null or PyNone)
            return GetElement(pool, _random.Next(poolSize) * pool.Strides[0]);

        int[] shape = ShapeArg(sizeArg);
        int n = SizeOf(shape);
        if (!replace && n > poolSize)
            throw PyErr.ValueError("Cannot take a larger sample than population when 'replace=False'");
        var outBuf = MakeBuffer(pool.DType, n);
        if (replace)
        {
            for (int i = 0; i < n; i++)
                SetBufferElement(outBuf, pool.DType, i, GetElement(pool, _random.Next(poolSize) * pool.Strides[0]));
        }
        else
        {
            var indices = Enumerable.Range(0, poolSize).ToArray();
            for (int i = 0; i < n; i++)
            {
                int j = i + _random.Next(poolSize - i);
                (indices[i], indices[j]) = (indices[j], indices[i]);
                SetBufferElement(outBuf, pool.DType, i, GetElement(pool, indices[i] * pool.Strides[0]));
            }
        }
        return Wrap(new NdArrayData(pool.DType, outBuf, shape));
    }

    // ---------------------------------------------------------------- dtype-generic element access
    //
    // Phase 1-4 hardcoded `double[]` everywhere (the only dtype that existed). Phase 5 adds a real
    // `Bool` dtype (comparisons/masking), so every place that used to cast `d.Buffer` straight to
    // `double[]` now goes through these three dispatch points instead — one real switch per
    // concern, not scattered per-callsite casts.

    /// <summary>Reads one element, always as the real Python-visible type for its dtype: `double`
    /// for `Float64`, `bool` for `Bool`, and a real `BigInteger` for `Int64` — PySharp's own actual
    /// representation of a Python `int` (never a C# `long`), so `type(a[0]).__name__` on an int64
    /// array element correctly shows `int`, matching real numpy's own int64 scalars behaving like
    /// real Python ints. `offset` is relative to `d`'s own logical start (typically `DotProduct
    /// (index, d.Strides)`) — `d.Offset` (Phase 12.1: real views share another array's buffer at a
    /// nonzero starting position) is added here, once, so every caller can keep treating `d` as if
    /// it always owned a buffer starting at 0.</summary>
    private static object GetElement(NdArrayData d, int offset) => d.DType switch
    {
        DType.Float64 => ((double[])d.Buffer)[d.Offset + offset],
        DType.Bool => ((bool[])d.Buffer)[d.Offset + offset],
        DType.Int64 => (BigInteger)((long[])d.Buffer)[d.Offset + offset],
        _ => throw new NotSupportedException($"unsupported dtype {d.DType}"),
    };

    private static void SetElement(NdArrayData d, int offset, object value)
    {
        int i = d.Offset + offset;
        switch (d.DType)
        {
            case DType.Float64:
                ((double[])d.Buffer)[i] = PyOps.AsDouble(value);
                break;
            case DType.Bool:
                ((bool[])d.Buffer)[i] = value is bool b ? b
                    : throw PyErr.TypeError($"expected bool, got {PyOps.TypeName(value)}");
                break;
            case DType.Int64:
                ((long[])d.Buffer)[i] = value switch
                {
                    BigInteger bi => (long)bi,
                    double db => (long)db,
                    bool bv => bv ? 1L : 0L,
                    _ => throw PyErr.TypeError($"expected int, got {PyOps.TypeName(value)}"),
                };
                break;
            default:
                throw new NotSupportedException($"unsupported dtype {d.DType}");
        }
    }

    private static Array MakeBuffer(DType dtype, int size) => dtype switch
    {
        DType.Float64 => new double[size],
        DType.Bool => new bool[size],
        DType.Int64 => new long[size],
        _ => throw new NotSupportedException($"unsupported dtype {dtype}"),
    };

    /// <summary>Writes a value that's already `CoerceTo(dtype, ...)`'d — i.e. already the exact
    /// boxed type each buffer array expects (`double`/`bool`/`BigInteger`), no further conversion
    /// here beyond the unboxing cast.</summary>
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
            case DType.Int64:
                ((long[])buffer)[index] = (long)(BigInteger)value;
                break;
            default:
                throw new NotSupportedException($"unsupported dtype {dtype}");
        }
    }

    /// <summary>Whether `d`'s own (`Shape`, `Strides`) pair is real C-contiguous row-major layout —
    /// independent of `Offset` (a contiguous *view*, e.g. `a[2:]` on a 1-D array, still has
    /// `Offset != 0` but contiguous strides). Decides whether `reshape`/`ravel` can return a real
    /// view (Phase 12.1) or must fall back to `MaterializeContiguousBuffer` first.</summary>
    private static bool IsContiguous(NdArrayData d) => d.Strides.SequenceEqual(NdArrayData.ComputeStrides(d.Shape));

    /// <summary>Walks `d` in real C-order regardless of its own strides/offset (correctly even when
    /// `d` is itself a non-contiguous view — Phase 12.1) and returns a fresh, independent,
    /// C-contiguous buffer holding the exact same elements in the exact same visitation order. The
    /// one shared "materialize a real, disconnected-from-any-view copy" implementation behind both
    /// `.copy()` (`CopyOf`) and `.flatten()` (`Flatten`), and the fallback path for `reshape`/
    /// `ravel` when the source isn't already contiguous.</summary>
    private static Array MaterializeContiguousBuffer(NdArrayData d)
    {
        var buf = MakeBuffer(d.DType, d.Size);
        int k = 0;
        ForEachBroadcastIndex(d.Shape, index => SetBufferElement(buf, d.DType, k++, GetElement(d, DotProduct(index, d.Strides))));
        return buf;
    }

    /// <summary>Numeric value of an element regardless of dtype (`True`/`False` as `1.0`/`0.0`,
    /// matching `PyOps.AsDouble`'s own bool handling) — lets comparisons/arithmetic work between
    /// any dtype pair through one code path instead of one per combination. Arithmetic is always
    /// carried out in `double` internally even for `Int64` operands (a documented v1 simplification
    /// — real precision loss only shows up past `double`'s exact-integer range, ~2^53, which no
    /// reachable script comes near).</summary>
    private static double AsComparableDouble(NdArrayData d, int offset) => d.DType switch
    {
        DType.Float64 => (double)GetElement(d, offset),
        DType.Bool => (bool)GetElement(d, offset) ? 1.0 : 0.0,
        DType.Int64 => (double)(BigInteger)GetElement(d, offset),
        _ => throw new NotSupportedException($"unsupported dtype {d.DType}"),
    };

    /// <summary>The real numpy `int op int -> int`, `bool op int -> int`, `anything op float ->
    /// float` promotion rule for arithmetic (Phase 9.4) — `bool op bool` also promotes to `Int64`,
    /// matching real numpy (`np.array([True]) + np.array([True])` is a real int64 array `[2]`, not
    /// bool). Only used for `+ - * ** // %`; true division (`/`) always forces `Float64` regardless
    /// (real numpy: `int64 / int64` is real float64, never int64), handled by each `__truediv__`
    /// call site passing an explicit `forceDType` instead of calling this.</summary>
    private static DType PromoteForArithmetic(DType a, DType b)
    {
        if (a == DType.Float64 || b == DType.Float64)
            return DType.Float64;
        return DType.Int64;
    }

    /// <summary>Converts an already-Python-typed value (`bool`/`BigInteger`/`double` — whatever
    /// `GetElement` or a raw arithmetic result hands back) into the exact boxed type
    /// `SetBufferElement` expects for `dtype`. The one shared coercion point for `dtype=`
    /// construction, `astype`, `np.where`, and `concatenate`.</summary>
    private static object CoerceTo(DType dtype, object value) => dtype switch
    {
        DType.Float64 => value switch
        {
            double db => db,
            BigInteger bi => (double)bi,
            bool b => b ? 1.0 : 0.0,
            _ => throw PyErr.TypeError($"cannot convert to float64: {PyOps.TypeName(value)}"),
        },
        DType.Bool => value switch
        {
            bool b => b,
            BigInteger bi => bi != 0,
            double db => db != 0.0,
            _ => throw PyErr.TypeError($"cannot convert to bool: {PyOps.TypeName(value)}"),
        },
        DType.Int64 => value switch
        {
            BigInteger bi => bi,
            double db => (BigInteger)db,
            bool b => (BigInteger)(b ? 1 : 0),
            _ => throw PyErr.TypeError($"cannot convert to int64: {PyOps.TypeName(value)}"),
        },
        _ => throw new NotSupportedException($"unsupported dtype {dtype}"),
    };

    private static DType ParseDType(object arg) => arg switch
    {
        PyInstance pi when pi.Class == DTypeClass => DTypeFromName((string)pi.Dict["__name__"]),
        string s => DTypeFromName(s),
        _ => throw PyErr.TypeError($"data type not understood: {PyOps.TypeName(arg)}"),
    };

    private static DType DTypeFromName(string name) => name switch
    {
        "float64" => DType.Float64,
        "int64" => DType.Int64,
        "bool" or "bool_" => DType.Bool,
        _ => throw PyErr.TypeError($"data type '{name}' not understood"),
    };

    private static DType? DTypeArg(object[] a, Dictionary<string, object>? kwargs, int positionalIndex)
    {
        object? raw = a.Length > positionalIndex ? a[positionalIndex]
            : kwargs is not null && kwargs.TryGetValue("dtype", out var v) ? v : null;
        return raw is null or PyNone ? null : ParseDType(raw);
    }

    /// <summary>Converts every element to `dtype` via `CoerceTo`, producing a real independent
    /// buffer (a genuine cast, e.g. `int64 -> float64` truncation semantics live entirely in
    /// `CoerceTo`) — the one shared implementation behind both `dtype=` construction and
    /// `.astype()`.</summary>
    private static NdArrayData AsDType(NdArrayData d, DType dtype)
    {
        if (d.DType == dtype)
            return CopyOf(d);
        var buf = MakeBuffer(dtype, d.Size);
        int k = 0;
        ForEachBroadcastIndex(d.Shape, index =>
            SetBufferElement(buf, dtype, k++, CoerceTo(dtype, GetElement(d, DotProduct(index, d.Strides)))));
        return new NdArrayData(dtype, buf, (int[])d.Shape.Clone());
    }

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
        value = NormalizeClrArrayLike(value);
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

        // Phase 9.1 dtype inference: all-`bool` -> bool, all-`int` (and every value actually fits a
        // `long`, matching real numpy's own int64 storage) -> int64, else float64 — real numpy's own
        // `np.array([1, 2, 3]).dtype` is `int64`, not `float64`.
        DType dtype = flat.Count == 0 ? DType.Float64
            : flat.All(static v => v is bool) ? DType.Bool
            : flat.All(static v => v is BigInteger bi && bi >= long.MinValue && bi <= long.MaxValue) ? DType.Int64
            : DType.Float64;
        var buffer = MakeBuffer(dtype, flat.Count);
        for (int i = 0; i < flat.Count; i++)
            SetBufferElement(buffer, dtype, i, CoerceTo(dtype, flat[i] switch
            {
                bool b => b, BigInteger bi => bi, _ => PyOps.AsDouble(flat[i]),
            }));
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

    // ---------------------------------------------------------------- Phase 3/12.1: indexing/slicing

    /// <summary>Resolves a real numpy *basic* index (a single int/`None`, a `PySlice`, or a
    /// `PyTuple` mixing any of these — exactly what `Interp.EvalIndex` builds for `a[i]`/`a[1:3]`/
    /// `a[i, j]`/`a[1:3, i]`/`a[:, None]`; per-axis *fancy* indexing with a list/array of indices was
    /// never implemented by this shim, so every index that reaches here really is basic indexing)
    /// into a real view (Phase 12.1): one absolute buffer offset, plus one shape/stride pair per
    /// *kept* result axis. A `None`/`np.newaxis` entry inserts a synthetic size-1 stride-0 axis; a
    /// slice's `step` is folded directly into that axis's stride (real numpy: `a[::-1]` is a real
    /// negative-stride view, not a copy); a plain int index consumes and **reduces** that axis — no
    /// shape/stride entry survives into the result at all. The view shares `d`'s buffer and traces
    /// back to `d`'s own ultimate `Base` (or `d` itself, if `d` owns its buffer), so a chain of views
    /// (a slice of a transpose, say) always resolves to one real owner.</summary>
    private static NdArrayData ResolveIndexView(NdArrayData d, object index)
    {
        var items = index is PyTuple t ? t.Items : new[] { index };
        int explicitAxisCount = items.Count(static it => it is not PyNone);
        if (explicitAxisCount > d.Ndim)
            throw PyErr.IndexError(
                $"too many indices for array: array is {d.Ndim}-dimensional, but {explicitAxisCount} were indexed");

        int offset = 0;
        var shape = new List<int>();
        var strides = new List<int>();
        int srcAxis = 0;

        foreach (var item in items)
        {
            if (item is PyNone)
            {
                shape.Add(1);
                strides.Add(0);
                continue;
            }
            if (item is PySlice slice)
            {
                var (start, _, step, count) = slice.Indices(d.Shape[srcAxis]);
                offset += start * d.Strides[srcAxis];
                shape.Add(count);
                strides.Add(step * d.Strides[srcAxis]);
            }
            else
            {
                offset += ResolveIntIndex(item, d.Shape[srcAxis], srcAxis) * d.Strides[srcAxis];
            }
            srcAxis++;
        }
        while (srcAxis < d.Ndim)
        {
            shape.Add(d.Shape[srcAxis]);
            strides.Add(d.Strides[srcAxis]);
            srcAxis++;
        }

        return new NdArrayData(d.DType, d.Buffer, shape.ToArray(), strides.ToArray(), d.Offset + offset, d.Base ?? d);
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

    /// <summary>`a[index]`: a fully-reduced index (an explicit int on every real axis, no
    /// `None`/newaxis entries) returns a real Python scalar (`float`/`bool`/`int`, matching the
    /// array's own dtype); everything else returns a real *view* (Phase 12.1) sharing `d`'s buffer,
    /// not a copy — matching real numpy's own actual basic-indexing behavior. A real bool-array
    /// index (`a[mask]`) is boolean masking (Phase 5.5) — a genuinely different indexing mode (real
    /// numpy copies for it too, since a mask's `True` positions aren't expressible as a single
    /// offset+stride), handled entirely separately from `ResolveIndexView`.</summary>
    private static object GetItem(NdArrayData d, object index)
    {
        if (BoolMaskOf(index) is { } mask)
            return Wrap(GatherMask(d, mask));

        var view = ResolveIndexView(d, index);
        return view.Ndim == 0 ? GetElement(view, 0) : Wrap(view);
    }

    /// <summary>`a[index] = value`: a fully-reduced index assigns a single scalar element; any
    /// other index assigns either a broadcast scalar (`a[1:3] = 5.0`) or another array whose shape
    /// must exactly match the indexed region (`a[1:3] = other` — real per-element broadcasting
    /// beyond an exact shape match is Phase 4's job, not this one). Writes go through the same real
    /// view `GetItem` now returns (Phase 12.1) — `SetElement`/`ForEachBroadcastIndex` against the
    /// view mutate `d`'s own shared buffer directly, exactly like real numpy's own in-place basic-
    /// indexing assignment. `a[mask] = value` (Phase 5.6) is boolean-mask assignment, handled
    /// separately (masking was never expressible as a single view to begin with).</summary>
    private static void SetItem(NdArrayData d, object index, object value)
    {
        if (BoolMaskOf(index) is { } mask)
        {
            ScatterMask(d, mask, value);
            return;
        }

        var view = ResolveIndexView(d, index);
        if (view.Ndim == 0)
        {
            SetElement(view, 0, value);
            return;
        }

        if (value is PyInstance pi && pi.Class == NdArrayClass)
        {
            var src = Data(pi);
            if (!src.Shape.SequenceEqual(view.Shape))
                throw PyErr.ValueError(
                    $"could not broadcast input array from shape {ShapeRepr(src.Shape)} into shape {ShapeRepr(view.Shape)}");
            int i = 0;
            ForEachBroadcastIndex(view.Shape, idx => SetElement(view, DotProduct(idx, view.Strides), GetElement(src, i++)));
        }
        else
        {
            ForEachBroadcastIndex(view.Shape, idx => SetElement(view, DotProduct(idx, view.Strides), value));
        }
    }

    // ---------------------------------------------------------------- Phase 5.5/5.6: boolean masking

    /// <summary>Recognizes a real bool-dtype `ndarray` used AS the whole index (`a[mask]`), as
    /// opposed to an int/slice/tuple index — real numpy's boolean (fancy) indexing, a genuinely
    /// different mode from axis-by-axis indexing. Returns null for every other index shape (a plain
    /// int/slice/tuple falls through to the normal `ResolveIndexView` path).</summary>
    private static NdArrayData? BoolMaskOf(object index)
        => index is PyInstance pi && pi.Class == NdArrayClass && Data(pi).DType == DType.Bool ? Data(pi) : null;

    /// <summary>`a[mask]`: real numpy requires the mask's shape to exactly match `a`'s (v1 scope —
    /// no partial/broadcast mask), and returns a real 1-D array of every element whose mask
    /// position is `True`, visited in C-order. Walks `d` and `mask` each through their own
    /// `Strides`/`Offset` in lockstep (Phase 12.1: either can now be a view, not just a
    /// freshly-built contiguous array) rather than assuming either's buffer holds exactly its own
    /// elements starting at position 0.</summary>
    private static NdArrayData GatherMask(NdArrayData d, NdArrayData mask)
    {
        RequireMatchingMaskShape(d, mask);
        var selected = new List<object>();
        ForEachBroadcastIndex(d.Shape, index =>
        {
            if ((bool)GetElement(mask, DotProduct(index, mask.Strides)))
                selected.Add(GetElement(d, DotProduct(index, d.Strides)));
        });
        var buffer = MakeBuffer(d.DType, selected.Count);
        for (int i = 0; i < selected.Count; i++)
            SetBufferElement(buffer, d.DType, i, selected[i]);
        return new NdArrayData(d.DType, buffer, new[] { selected.Count });
    }

    /// <summary>`a[mask] = value`: assigns a broadcast scalar, or one value per `True` position
    /// (in C-order) from another 1-D array whose length must equal the number of `True`s. Same
    /// lockstep-via-own-strides walk as `GatherMask`, writing through `d`'s own real (possibly
    /// shared) buffer via `SetElement`.</summary>
    private static void ScatterMask(NdArrayData d, NdArrayData mask, object value)
    {
        RequireMatchingMaskShape(d, mask);
        int trueCount = 0;
        ForEachBroadcastIndex(d.Shape, index =>
        {
            if ((bool)GetElement(mask, DotProduct(index, mask.Strides)))
                trueCount++;
        });
        if (value is PyInstance pi && pi.Class == NdArrayClass)
        {
            var src = Data(pi);
            if (src.Size != trueCount)
                throw PyErr.ValueError(
                    $"NumPy boolean array indexing assignment cannot assign {src.Size} input values to the {trueCount} output values where the mask is true");
            int i = 0;
            ForEachBroadcastIndex(d.Shape, index =>
            {
                if ((bool)GetElement(mask, DotProduct(index, mask.Strides)))
                    SetElement(d, DotProduct(index, d.Strides), GetElement(src, i++));
            });
        }
        else
        {
            ForEachBroadcastIndex(d.Shape, index =>
            {
                if ((bool)GetElement(mask, DotProduct(index, mask.Strides)))
                    SetElement(d, DotProduct(index, d.Strides), value);
            });
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
        int flatOut = 0;
        ForEachBroadcastIndex(d.Shape, index =>
        {
            double v = AsComparableDouble(d, DotProduct(index, d.Strides));
            int key = LineKey(index, axis, excludedStrides);
            double acc = lineAcc.TryGetValue(key, out var prev) ? combine(prev, v) : v;
            lineAcc[key] = acc;
            // Written by C-order visitation position, not `d`'s own (possibly non-contiguous —
            // Phase 12.1) offset: `outBuf` is a fresh buffer laid out to match `d.Shape` exactly,
            // so its k-th visited element belongs at flat position k regardless of where `d` itself
            // physically stores that element.
            outBuf[flatOut++] = acc;
        });
        return new NdArrayData(DType.Float64, outBuf, (int[])d.Shape.Clone());
    }

    private static int? AxisArg(object[] a, Dictionary<string, object>? kwargs, int positionalIndex)
    {
        object? raw = a.Length > positionalIndex ? a[positionalIndex]
            : kwargs is not null && kwargs.TryGetValue("axis", out var v) ? v : null;
        return raw is null or PyNone ? null : (int)PyOps.AsBigInt(raw, "axis");
    }

    /// <summary>Real numpy promotion (Phase 9.4): output dtype is `PromoteForArithmetic(a.DType,
    /// b.DType)` unless `forceDType` overrides it (true division and the float-only ufuncs always
    /// force `Float64` regardless of their operands' dtype — see `ApplyUfunc`/`__truediv__`). The
    /// actual arithmetic is always carried out in `double` (see `AsComparableDouble`'s own note on
    /// why that's an acceptable v1 simplification), then coerced into the output dtype's real
    /// storage type via `CoerceTo`.</summary>
    private static NdArrayData ElementwiseBinary(
        NdArrayData a, NdArrayData b, Func<double, double, double> op, DType? forceDType = null)
    {
        // Phase 12.2 fast path: same-shape contiguous float64 operands (the overwhelmingly common
        // case — no broadcasting, no dtype promotion) skip `ForEachBroadcastIndex`'s per-element
        // closure call, `DotProduct`, and `AsComparableDouble`/`CoerceTo`'s per-element dtype
        // `switch`, operating on the raw `double[]` buffers directly instead. See this file's own
        // Phase 12.2 note in NUMPY_PLAN.md for the informal timing that justified adding this.
        if (a.DType == DType.Float64 && b.DType == DType.Float64 && forceDType is null or DType.Float64
            && a.Shape.SequenceEqual(b.Shape) && IsContiguous(a) && IsContiguous(b))
        {
            var fastA = (double[])a.Buffer;
            var fastB = (double[])b.Buffer;
            var fastOut = new double[a.Size];
            for (int i = 0; i < fastOut.Length; i++)
                fastOut[i] = op(fastA[a.Offset + i], fastB[b.Offset + i]);
            return new NdArrayData(DType.Float64, fastOut, (int[])a.Shape.Clone());
        }

        var (shape, stridesA, stridesB) = PrepareBroadcast(a, b);
        DType outDType = forceDType ?? PromoteForArithmetic(a.DType, b.DType);
        var outBuf = MakeBuffer(outDType, SizeOf(shape));
        int flat = 0;
        ForEachBroadcastIndex(shape, index =>
        {
            double result = op(
                AsComparableDouble(a, DotProduct(index, stridesA)), AsComparableDouble(b, DotProduct(index, stridesB)));
            SetBufferElement(outBuf, outDType, flat++, CoerceTo(outDType, result));
        });
        return new NdArrayData(outDType, outBuf, shape);
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

    /// <summary>The real numpy bitwise `&amp;`/`|`/`^` mechanism (Phase 9.5), replacing the old
    /// bool-only "logical" op (Phase 5.3): bitwise AND/OR/XOR of 0/1 values equal logical AND/OR/XOR,
    /// so one mechanism now covers both `bool &amp; bool -> bool` and real int64 bitwise ops (`int64
    /// &amp; int64 -> int64`, bool promotes to int64 when paired with an int64 operand). `Float64`
    /// has no bitwise operator, matching real numpy's own `TypeError` there.</summary>
    private static NdArrayData BitwiseBinary(NdArrayData a, NdArrayData b, Func<long, long, long> op)
    {
        RequireBitwiseDType(a);
        RequireBitwiseDType(b);
        var (shape, stridesA, stridesB) = PrepareBroadcast(a, b);
        DType outDType = a.DType == DType.Bool && b.DType == DType.Bool ? DType.Bool : DType.Int64;
        var outBuf = MakeBuffer(outDType, SizeOf(shape));
        int flat = 0;
        ForEachBroadcastIndex(shape, index =>
        {
            long result = op(
                AsComparableInt64(a, DotProduct(index, stridesA)), AsComparableInt64(b, DotProduct(index, stridesB)));
            SetBufferElement(outBuf, outDType, flat++, outDType == DType.Bool ? (result != 0) : (BigInteger)result);
        });
        return new NdArrayData(outDType, outBuf, shape);
    }

    private static void RequireBitwiseDType(NdArrayData d)
    {
        if (d.DType == DType.Float64)
            throw PyErr.TypeError(
                "ufunc 'bitwise_and'/'bitwise_or'/'bitwise_xor' not supported for the input types "
                + "(float64 has no bitwise operator)");
    }

    /// <summary>Integer value of an element for bitwise ops (`True`/`False` as `1`/`0`) — the
    /// bitwise counterpart to `AsComparableDouble`, kept separate since C#'s bitwise operators
    /// (`&amp; | ^ ~`) need a real integral type, not `double`.</summary>
    private static long AsComparableInt64(NdArrayData d, int offset) => d.DType switch
    {
        DType.Bool => (bool)GetElement(d, offset) ? 1L : 0L,
        DType.Int64 => (long)(BigInteger)GetElement(d, offset),
        _ => throw new NotSupportedException($"unsupported dtype {d.DType}"),
    };

    private static NdArrayData ElementwiseUnary(NdArrayData d, Func<double, double> op, DType? forceDType = null)
    {
        DType outDType = forceDType ?? d.DType;
        // Phase 12.2 fast path — same reasoning as `ElementwiseBinary`'s own.
        if (outDType == DType.Float64 && d.DType == DType.Float64 && IsContiguous(d))
        {
            var fastBuf = (double[])d.Buffer;
            var fastOut = new double[d.Size];
            for (int i = 0; i < fastOut.Length; i++)
                fastOut[i] = op(fastBuf[d.Offset + i]);
            return new NdArrayData(DType.Float64, fastOut, (int[])d.Shape.Clone());
        }

        var outBuf = MakeBuffer(outDType, d.Size);
        int k = 0;
        ForEachBroadcastIndex(d.Shape, index =>
            SetBufferElement(outBuf, outDType, k++, CoerceTo(outDType, op(AsComparableDouble(d, DotProduct(index, d.Strides))))));
        return new NdArrayData(outDType, outBuf, (int[])d.Shape.Clone());
    }

    private static object ElementwiseOp(object aObj, object bObj, Func<double, double, double> op, DType? forceDType = null)
        => Wrap(ElementwiseBinary(OperandData(aObj), OperandData(bObj), op, forceDType));

    private static object CompareOp(object aObj, object bObj, Func<double, double, bool> cmp)
        => Wrap(ElementwiseCompare(OperandData(aObj), OperandData(bObj), cmp));

    private static object BitwiseOp(object aObj, object bObj, Func<long, long, long> op)
        => Wrap(BitwiseBinary(BitwiseOperandData(aObj), BitwiseOperandData(bObj), op));

    /// <summary>Scalar coercion specific to bitwise ops: a real Python `bool`/`int` becomes a real
    /// 0-d `Bool`/`Int64` array (so `mask &amp; True` and `flags &amp; 0b101` both work), distinct
    /// from `OperandData`'s own coercion only in that it rejects `float` (no bitwise operator on
    /// `Float64`, enforced by `RequireBitwiseDType`).</summary>
    private static NdArrayData BitwiseOperandData(object o) => o switch
    {
        PyInstance pi when pi.Class == NdArrayClass => Data(pi),
        bool b => new NdArrayData(DType.Bool, new[] { b }, Array.Empty<int>()),
        BigInteger bi => new NdArrayData(DType.Int64, new[] { (long)bi }, Array.Empty<int>()),
        _ => throw PyErr.TypeError($"expected a bool/int array, bool, or int, got {PyOps.TypeName(o)}"),
    };

    /// <summary>Lets `ElementwiseBinary`/broadcasting treat a plain Python scalar (int/float/bool)
    /// exactly like a real 0-d array — `2 + arr` and `np.array(2.0) + arr` take the same code path,
    /// no special-casing needed. Preserves the scalar's own real Python type (Phase 9.4) rather than
    /// always forcing `Float64`, so promotion (`PromoteForArithmetic`) sees the scalar's true dtype —
    /// `arr_int64 + 2` promotes to int64, not float64, matching real numpy.</summary>
    private static NdArrayData OperandData(object o) => o switch
    {
        PyInstance pi when pi.Class == NdArrayClass => Data(pi),
        bool b => new NdArrayData(DType.Bool, new[] { b }, Array.Empty<int>()),
        BigInteger bi => new NdArrayData(DType.Int64, new[] { (long)bi }, Array.Empty<int>()),
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
        cls.Dict["T"] = MakeProperty(self => Wrap(Transpose(Data(self), null)));
        // Real numpy `.base`: `None` for an array that owns its own buffer, or the real underlying
        // array for a view (Phase 12.1) — the same "does this array own its data" question
        // `IsContiguous`/`Base` were added to answer everywhere else in this file, just exposed to
        // Python here.
        cls.Dict["base"] = MakeProperty(self => Data(self).Base is { } b ? Wrap(b) : PyNone.Instance);

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
        Add("astype", (_, a, _) => Wrap(AsDType(Data(a[0]), ParseDType(a[1]))));

        // Phase 11 — interop & conveniences.
        Add("tolist", (_, a, _) => ToPythonList(Data(a[0])));
        Add("to_clr", (_, a, _) => ClrMarshal.ToPython(ToClrDoubleArray(Data(a[0]))));

        // Real numpy: `float`/`int` on a multi-element array is a real `TypeError` ("only size-1
        // arrays can be converted to Python scalars"); `bool` on one is a real `ValueError` instead
        // ("truth value of an array... is ambiguous") — deliberately two different exception types,
        // matching real numpy's own actual behavior (not the NUMPY_PLAN.md checklist text's looser
        // "ValueError otherwise" wording, which this shim's own verify-against-real-numpy discipline
        // takes priority over).
        Add("__float__", (_, a, _) => PyOps.AsDouble(GetElement(RequireSize1(Data(a[0])), 0)));
        Add("__int__", (_, a, _) => ToPyInt(GetElement(RequireSize1(Data(a[0])), 0)));
        Add("__bool__", (_, a, _) =>
        {
            var d = Data(a[0]);
            if (d.Size != 1)
                throw PyErr.ValueError(
                    "The truth value of an array with more than one element is ambiguous. Use a.any() or a.all()");
            return AsComparableDouble(d, 0) != 0.0;
        });

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

        // Phase 8 — shape manipulation, as real ndarray methods (mirroring the module-level
        // functions registered in `Create()`, which real numpy also exposes both ways).
        Add("reshape", (_, a, _) => Wrap(Reshape(Data(a[0]), ReshapeShapeArg(a, 1))));
        Add("ravel", (_, a, _) => Wrap(Ravel(Data(a[0]))));
        Add("flatten", (_, a, _) => Wrap(Flatten(Data(a[0]))));
        Add("transpose", (_, a, _) => Wrap(Transpose(Data(a[0]), a.Length > 1 ? ReshapeShapeArg(a, 1) : null)));
        Add("squeeze", (_, a, kwargs) => Wrap(Squeeze(Data(a[0]), AxisArg(a, kwargs, 1))));

        Add("__getitem__", (_, a, _) => GetItem(Data(a[0]), a[1]));
        Add("__setitem__", (_, a, _) =>
        {
            SetItem(Data(a[0]), a[1], a[2]);
            return PyNone.Instance;
        });

        // Phase 10 — `@`/`__matmul__` was already wired into the interpreter's own operator table
        // (`Interp.BinDunders`) since Phase 4 but deliberately left unimplemented until now.
        Add("__matmul__", (_, a, _) => MatMulResult(MatMul(Data(a[0]), MatMulOperand(a[1]))));
        Add("__rmatmul__", (_, a, _) => MatMulResult(MatMul(MatMulOperand(a[1]), Data(a[0]))));

        // Phase 4 — elementwise ops & real broadcasting. `+= -= *= /=` need no dedicated
        // `__iadd__`/etc. here at all: `Interp.ExecAugAssign`
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
        // Real numpy: true division always produces a real float64 result regardless of the
        // operands' dtype (`int64 / int64` is float64, never int64) — forced here rather than left
        // to natural promotion.
        Add("__truediv__", (_, a, _) => ElementwiseOp(a[0], a[1], static (x, y) => x / y, DType.Float64));
        Add("__rtruediv__", (_, a, _) => ElementwiseOp(a[1], a[0], static (x, y) => x / y, DType.Float64));
        Add("__pow__", (_, a, _) => ElementwiseOp(a[0], a[1], Math.Pow));
        Add("__rpow__", (_, a, _) => ElementwiseOp(a[1], a[0], Math.Pow));
        // Phase 9.5 — integer floor division/modulo. Python/real-numpy semantics (sign follows the
        // divisor), not C#'s truncating `%` — e.g. `-7 % 3` is `2`, not `-1`.
        Add("__floordiv__", (_, a, _) => ElementwiseOp(a[0], a[1], static (x, y) => Math.Floor(x / y)));
        Add("__rfloordiv__", (_, a, _) => ElementwiseOp(a[1], a[0], static (x, y) => Math.Floor(x / y)));
        Add("__mod__", (_, a, _) => ElementwiseOp(a[0], a[1], static (x, y) => x - Math.Floor(x / y) * y));
        Add("__rmod__", (_, a, _) => ElementwiseOp(a[1], a[0], static (x, y) => x - Math.Floor(x / y) * y));

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

        Add("__and__", (_, a, _) => BitwiseOp(a[0], a[1], static (x, y) => x & y));
        Add("__rand__", (_, a, _) => BitwiseOp(a[1], a[0], static (x, y) => x & y));
        Add("__or__", (_, a, _) => BitwiseOp(a[0], a[1], static (x, y) => x | y));
        Add("__ror__", (_, a, _) => BitwiseOp(a[1], a[0], static (x, y) => x | y));
        Add("__xor__", (_, a, _) => BitwiseOp(a[0], a[1], static (x, y) => x ^ y));
        Add("__rxor__", (_, a, _) => BitwiseOp(a[1], a[0], static (x, y) => x ^ y));
        Add("__invert__", (_, a, _) =>
        {
            var d = Data(a[0]);
            if (d.DType == DType.Bool)
            {
                var outBuf = new bool[d.Size];
                int k = 0;
                ForEachBroadcastIndex(d.Shape, index => outBuf[k++] = !(bool)GetElement(d, DotProduct(index, d.Strides)));
                return Wrap(new NdArrayData(DType.Bool, outBuf, (int[])d.Shape.Clone()));
            }
            if (d.DType == DType.Int64)
            {
                var outBuf = new long[d.Size];
                int k = 0;
                ForEachBroadcastIndex(d.Shape, index =>
                    outBuf[k++] = ~(long)(BigInteger)GetElement(d, DotProduct(index, d.Strides)));
                return Wrap(new NdArrayData(DType.Int64, outBuf, (int[])d.Shape.Clone()));
            }
            throw PyErr.TypeError("ufunc 'invert' not supported for the input types (float64 has no bitwise operator)");
        });

        Add("any", (_, a, _) =>
        {
            var d = Data(a[0]);
            bool found = false;
            ForEachBroadcastIndex(d.Shape, index => found |= AsComparableDouble(d, DotProduct(index, d.Strides)) != 0.0);
            return found;
        });
        Add("all", (_, a, _) =>
        {
            var d = Data(a[0]);
            bool allTrue = true;
            ForEachBroadcastIndex(d.Shape, index => allTrue &= AsComparableDouble(d, DotProduct(index, d.Strides)) != 0.0);
            return allTrue;
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
        DType.Int64 => Int64DType,
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
        DType.Int64 => ((BigInteger)value).ToString(),
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
