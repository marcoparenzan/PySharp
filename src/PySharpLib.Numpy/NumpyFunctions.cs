// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>The module-level numpy functions, as singletons shared by the module and by the
/// <c>ndarray</c> class (a method is the same builtin called with <c>self</c> as the first argument).</summary>
internal static class NumpyFunctions
{
    public static readonly Dictionary<string, PyBuiltinFunction> All = Build();

    /// <summary>Names that are also ndarray methods.</summary>
    public static readonly string[] MethodNames =
    {
        "sum", "prod", "min", "max", "mean", "std", "var", "argmin", "argmax", "cumsum", "cumprod", "any", "all",
        "clip", "round", "dot", "trace", "diagonal", "nonzero", "repeat", "ravel", "squeeze", "swapaxes", "ptp",
        "reshape", "transpose", "copy", "flatten", "astype", "tolist", "item", "fill", "take", "conj", "conjugate",
        "sort", "argsort", "view", "searchsorted", "cumsum", "compress", "tobytes", "tostring", "put",
    };

    private static Dictionary<string, PyBuiltinFunction> Build()
    {
        var all = new Dictionary<string, PyBuiltinFunction>();

        void Def(string name, BuiltinFn fn) => all[name] = Native.Fn(name, fn);
        void Alias(string alias, string name) => all[alias] = all[name];

        void Unary(string name, Func<NDArray, NDArray> f) => Def(name, (i, a, k) =>
        {
            var p = new Args(name, i, a, k, "x", "out");
            return p.Finish(1, f(p.ND(0)));
        });

        void Binary(string name, Func<NDArray, NDArray, NDArray> f) => Def(name, (i, a, k) =>
        {
            var p = new Args(name, i, a, k, "x1", "x2", "out");
            return p.Finish(2, f(p.ND(0), p.ND(1)));
        });

        // ---------------------------------------------------------------- arithmetic ufuncs
        Binary("add", np.Add);
        Binary("subtract", np.Subtract);
        Binary("multiply", np.Multiply);
        Binary("divide", np.Divide);
        Alias("true_divide", "divide");
        Binary("floor_divide", np.FloorDivide);
        Binary("mod", np.Mod);
        Alias("remainder", "mod");
        Binary("fmod", np.Fmod);
        Binary("power", np.Power);
        Binary("maximum", np.Maximum);
        Binary("minimum", np.Minimum);
        Binary("arctan2", np.Arctan2);
        Binary("hypot", np.Hypot);

        Binary("equal", np.Equal);
        Binary("not_equal", np.NotEqual);
        Binary("less", np.Less);
        Binary("less_equal", np.LessEqual);
        Binary("greater", np.Greater);
        Binary("greater_equal", np.GreaterEqual);

        Binary("bitwise_and", np.BitwiseAnd);
        Binary("bitwise_or", np.BitwiseOr);
        Binary("bitwise_xor", np.BitwiseXor);
        Binary("left_shift", np.LeftShift);
        Binary("right_shift", np.RightShift);
        Binary("logical_and", np.LogicalAnd);
        Binary("logical_or", np.LogicalOr);
        Binary("logical_xor", np.LogicalXor);
        Unary("logical_not", np.LogicalNot);
        Unary("invert", np.Invert);
        Alias("bitwise_not", "invert");

        Unary("negative", np.Negative);
        Unary("positive", np.Positive);
        Unary("absolute", np.Abs);
        Alias("abs", "absolute");
        Alias("fabs", "absolute");
        Unary("sign", np.Sign);
        Unary("square", np.Square);
        Unary("sqrt", np.Sqrt);
        Unary("cbrt", np.Cbrt);
        Unary("exp", np.Exp);
        Unary("exp2", np.Exp2);
        Unary("expm1", np.Expm1);
        Unary("log", np.Log);
        Unary("log2", np.Log2);
        Unary("log10", np.Log10);
        Unary("log1p", np.Log1p);
        Unary("sin", np.Sin);
        Unary("cos", np.Cos);
        Unary("tan", np.Tan);
        Unary("arcsin", np.Arcsin);
        Unary("arccos", np.Arccos);
        Unary("arctan", np.Arctan);
        Unary("sinh", np.Sinh);
        Unary("cosh", np.Cosh);
        Unary("tanh", np.Tanh);
        Unary("arcsinh", np.Arcsinh);
        Unary("arccosh", np.Arccosh);
        Unary("arctanh", np.Arctanh);
        Unary("reciprocal", np.Reciprocal);
        Unary("rint", np.Rint);
        Unary("floor", np.Floor);
        Unary("ceil", np.Ceil);
        Unary("trunc", np.Trunc);
        Unary("degrees", np.Degrees);
        Unary("radians", np.Radians);
        Alias("rad2deg", "degrees");
        Alias("deg2rad", "radians");
        Unary("isnan", np.IsNan);
        Unary("isinf", np.IsInf);
        Unary("isfinite", np.IsFinite);
        Unary("signbit", np.SignBit);

        Def("round", (i, a, k) =>
        {
            var p = new Args("round", i, a, k, "a", "decimals", "out");
            return p.Finish(2, np.Round(p.ND(0), p.Int(1, 0)));
        });
        Alias("around", "round");

        Def("clip", (i, a, k) =>
        {
            var p = new Args("clip", i, a, k, "a", "a_min", "a_max", "out", "min", "max");
            var lo = p.NDOpt(1) ?? p.NDOpt(4);
            var hi = p.NDOpt(2) ?? p.NDOpt(5);
            return p.Finish(3, np.Clip(p.ND(0), lo, hi));
        });

        Def("where", (i, a, k) =>
        {
            var p = new Args("where", i, a, k, "condition", "x", "y");
            if (!p.Has(1) && !p.Has(2))
                return new PyTuple(np.NonZero(p.ND(0)).Select(x => (object)Conv.Wrap(x)).ToArray());
            if (p.Has(1) != p.Has(2)) throw PyErr.ValueError("either both or neither of x and y should be given");
            return Conv.Result(np.Where(p.ND(0), p.ND(1), p.ND(2)));
        });

        Def("nonzero", (i, a, k) =>
        {
            var p = new Args("nonzero", i, a, k, "a");
            return new PyTuple(np.NonZero(p.ND(0)).Select(x => (object)Conv.Wrap(x)).ToArray());
        });

        // ---------------------------------------------------------------- reductions
        Def("sum", (i, a, k) =>
        {
            var p = new Args("sum", i, a, k, "a", "axis", "dtype", "out", "keepdims");
            return p.Finish(3, np.Sum(p.ND(0), p.Axes(1), p.Bool(4, false), p.DType(2)));
        });
        Def("prod", (i, a, k) =>
        {
            var p = new Args("prod", i, a, k, "a", "axis", "dtype", "out", "keepdims");
            return p.Finish(3, np.Prod(p.ND(0), p.Axes(1), p.Bool(4, false), p.DType(2)));
        });
        Alias("product", "prod");
        Def("max", (i, a, k) =>
        {
            var p = new Args("max", i, a, k, "a", "axis", "out", "keepdims");
            return p.Finish(2, np.Max(p.ND(0), p.Axes(1), p.Bool(3, false)));
        });
        Alias("amax", "max");
        Def("min", (i, a, k) =>
        {
            var p = new Args("min", i, a, k, "a", "axis", "out", "keepdims");
            return p.Finish(2, np.Min(p.ND(0), p.Axes(1), p.Bool(3, false)));
        });
        Alias("amin", "min");
        Def("ptp", (i, a, k) =>
        {
            var p = new Args("ptp", i, a, k, "a", "axis", "out", "keepdims");
            return p.Finish(2, np.Ptp(p.ND(0), p.Axes(1), p.Bool(3, false)));
        });
        Def("mean", (i, a, k) =>
        {
            var p = new Args("mean", i, a, k, "a", "axis", "dtype", "out", "keepdims");
            return p.Finish(3, np.Mean(p.ND(0), p.Axes(1), p.Bool(4, false), p.DType(2)));
        });
        Def("std", (i, a, k) =>
        {
            var p = new Args("std", i, a, k, "a", "axis", "dtype", "out", "ddof", "keepdims");
            return p.Finish(3, np.Std(p.ND(0), p.Axes(1), p.Bool(5, false), p.Int(4, 0)));
        });
        Def("var", (i, a, k) =>
        {
            var p = new Args("var", i, a, k, "a", "axis", "dtype", "out", "ddof", "keepdims");
            return p.Finish(3, np.Var(p.ND(0), p.Axes(1), p.Bool(5, false), p.Int(4, 0)));
        });
        Def("argmax", (i, a, k) =>
        {
            var p = new Args("argmax", i, a, k, "a", "axis", "out", "keepdims");
            return p.Finish(2, np.ArgMax(p.ND(0), p.Axis(1), p.Bool(3, false)));
        });
        Def("argmin", (i, a, k) =>
        {
            var p = new Args("argmin", i, a, k, "a", "axis", "out", "keepdims");
            return p.Finish(2, np.ArgMin(p.ND(0), p.Axis(1), p.Bool(3, false)));
        });
        Def("cumsum", (i, a, k) =>
        {
            var p = new Args("cumsum", i, a, k, "a", "axis", "dtype", "out");
            return p.Finish(3, np.CumSum(p.ND(0), p.Axis(1), p.DType(2)));
        });
        Def("cumprod", (i, a, k) =>
        {
            var p = new Args("cumprod", i, a, k, "a", "axis", "dtype", "out");
            return p.Finish(3, np.CumProd(p.ND(0), p.Axis(1), p.DType(2)));
        });
        Def("any", (i, a, k) =>
        {
            var p = new Args("any", i, a, k, "a", "axis", "out", "keepdims");
            return p.Finish(2, np.Any(p.ND(0), p.Axes(1), p.Bool(3, false)));
        });
        Def("all", (i, a, k) =>
        {
            var p = new Args("all", i, a, k, "a", "axis", "out", "keepdims");
            return p.Finish(2, np.All(p.ND(0), p.Axes(1), p.Bool(3, false)));
        });
        Def("count_nonzero", (i, a, k) =>
        {
            var p = new Args("count_nonzero", i, a, k, "a", "axis", "keepdims");
            return Conv.Result(np.CountNonzero(p.ND(0), p.Axes(1), p.Bool(2, false)));
        });

        // ---------------------------------------------------------------- creation
        Def("array", (i, a, k) =>
        {
            var p = new Args("array", i, a, k, "object", "dtype", "copy", "order", "subok", "ndmin", "like");
            return Conv.Wrap(MakeArray(p, copyDefault: true));
        });
        Def("asarray", (i, a, k) =>
        {
            var p = new Args("asarray", i, a, k, "a", "dtype", "order", "like", "copy");
            var nd = Conv.TryUnwrap(p.Required(0));
            var dt = p.DType(1);
            NDArray r = nd is not null ? (dt is DType d && d != nd.DType ? nd.AsType(d) : nd) : MakeArrayFrom(p.Required(0), dt);
            return Conv.Wrap(r);
        });
        Alias("asanyarray", "asarray");
        Def("ascontiguousarray", (i, a, k) =>
        {
            var p = new Args("ascontiguousarray", i, a, k, "a", "dtype");
            var nd = Conv.TryUnwrap(p.Required(0)) ?? MakeArrayFrom(p.Required(0), p.DType(1));
            if (p.DType(1) is DType d && d != nd.DType) nd = nd.AsType(d);
            return Conv.Wrap(nd.IsContiguous && nd.Offset == 0 ? nd : nd.Copy());
        });
        Def("copy", (i, a, k) =>
        {
            var p = new Args("copy", i, a, k, "a", "order", "subok");
            return Conv.Wrap(p.ND(0).Copy());
        });

        Def("zeros", (i, a, k) =>
        {
            var p = new Args("zeros", i, a, k, "shape", "dtype", "order", "like");
            return Conv.Wrap(np.Zeros(Conv.ToShape(p.Required(0)), p.DType(1) ?? DType.Float64));
        });
        Def("ones", (i, a, k) =>
        {
            var p = new Args("ones", i, a, k, "shape", "dtype", "order", "like");
            return Conv.Wrap(np.Ones(Conv.ToShape(p.Required(0)), p.DType(1) ?? DType.Float64));
        });
        Def("empty", (i, a, k) =>
        {
            var p = new Args("empty", i, a, k, "shape", "dtype", "order", "like");
            return Conv.Wrap(np.Empty(Conv.ToShape(p.Required(0)), p.DType(1) ?? DType.Float64));
        });
        Def("full", (i, a, k) =>
        {
            var p = new Args("full", i, a, k, "shape", "fill_value", "dtype", "order", "like");
            var fill = Conv.NDStrong(p.Required(1));
            var dt = p.DType(2) ?? fill.DType;
            var r = np.Zeros(Conv.ToShape(p.Required(0)), dt);
            if (r.Size > 0) Assign.Copy(r, fill, Casting.Unsafe);
            return Conv.Wrap(r);
        });
        Def("zeros_like", (i, a, k) =>
        {
            var p = new Args("zeros_like", i, a, k, "a", "dtype", "order", "subok", "shape");
            return Conv.Wrap(np.Zeros(p.Has(4) ? Conv.ToShape(p[4]!) : p.ND(0).Shape, p.DType(1) ?? p.ND(0).DType));
        });
        Def("ones_like", (i, a, k) =>
        {
            var p = new Args("ones_like", i, a, k, "a", "dtype", "order", "subok", "shape");
            return Conv.Wrap(np.Ones(p.Has(4) ? Conv.ToShape(p[4]!) : p.ND(0).Shape, p.DType(1) ?? p.ND(0).DType));
        });
        Def("empty_like", (i, a, k) =>
        {
            var p = new Args("empty_like", i, a, k, "prototype", "dtype", "order", "subok", "shape");
            return Conv.Wrap(np.Empty(p.Has(4) ? Conv.ToShape(p[4]!) : p.ND(0).Shape, p.DType(1) ?? p.ND(0).DType));
        });
        Def("full_like", (i, a, k) =>
        {
            var p = new Args("full_like", i, a, k, "a", "fill_value", "dtype", "order", "subok", "shape");
            var src = p.ND(0);
            var fill = Conv.NDStrong(p.Required(1));
            var r = np.Zeros(p.Has(5) ? Conv.ToShape(p[5]!) : src.Shape, p.DType(2) ?? src.DType);
            if (r.Size > 0) Assign.Copy(r, fill, Casting.Unsafe);
            return Conv.Wrap(r);
        });
        Def("eye", (i, a, k) =>
        {
            var p = new Args("eye", i, a, k, "N", "M", "k", "dtype", "order");
            return Conv.Wrap(np.Eye(p.Int(0, 0), p.IntOrNull(1), p.Int(2, 0), p.DType(3) ?? DType.Float64));
        });
        Def("identity", (i, a, k) =>
        {
            var p = new Args("identity", i, a, k, "n", "dtype");
            return Conv.Wrap(np.Identity(p.Int(0, 0), p.DType(1) ?? DType.Float64));
        });
        Def("arange", (i, a, k) =>
        {
            var p = new Args("arange", i, a, k, "start", "stop", "step", "dtype");
            object start = p.Has(1) ? p[0]! : BigInteger.Zero;
            object stop = p.Has(1) ? p[1]! : p.Required(0);
            object step = p.Has(2) ? p[2]! : BigInteger.One;
            static bool IsInt(object o) => o is BigInteger or bool || (Conv.TryUnwrap(o) is { } nd && nd.DType.IsInteger());
            static double D(object o) => PyOps.AsDouble(Conv.TryUnwrap(o) is { Ndim: 0 } nd ? Conv.Scalarize(nd) : o);
            bool allInt = IsInt(start) && IsInt(stop) && IsInt(step);
            var dt = p.DType(3);
            if (allInt && dt is null)
                return Conv.Wrap(np.Arange((long)D(start), (long)D(stop), (long)D(step)));
            return Conv.Wrap(np.Arange(D(start), D(stop), D(step), dt, allInt));
        });
        Def("linspace", (i, a, k) =>
        {
            var p = new Args("linspace", i, a, k, "start", "stop", "num", "endpoint", "retstep", "dtype", "axis");
            int num = p.Int(2, 50);
            bool endpoint = p.Bool(3, true);
            var r = np.Linspace(p.Double(0, 0), p.Double(1, 0), num, endpoint, p.DType(5));
            if (p.Bool(4, false))
            {
                int div = endpoint ? num - 1 : num;
                double step = div > 0 ? (p.Double(1, 0) - p.Double(0, 0)) / div : double.NaN;
                return new PyTuple(new object[] { Conv.Wrap(r), step });
            }
            return Conv.Wrap(r);
        });
        Def("meshgrid", (i, a, k) =>
        {
            string indexing = "xy";
            if (k is not null)
            {
                foreach (var (key, v) in k)
                {
                    if (key == "indexing") indexing = (string)v;
                    else if (key is "sparse" or "copy") { if (key == "sparse" && PyOps.Truthy(i, v)) throw PyErr.NotImplementedError("meshgrid(sparse=True) is not implemented"); }
                    else throw PyErr.TypeError($"meshgrid() got an unexpected keyword argument '{key}'");
                }
            }
            var xi = a.Select(x => np.Ravel(Conv.ND(x))).ToArray();
            return new PyTuple(np.Meshgrid(xi, indexing == "xy").Select(x => (object)Conv.Wrap(x)).ToArray());
        });
        Def("diag", (i, a, k) =>
        {
            var p = new Args("diag", i, a, k, "v", "k");
            return Conv.Wrap(np.Diag(p.ND(0), p.Int(1, 0)));
        });
        Def("diagonal", (i, a, k) =>
        {
            var p = new Args("diagonal", i, a, k, "a", "offset", "axis1", "axis2");
            return Conv.Wrap(np.Diagonal(p.ND(0), p.Int(1, 0)));
        });
        Def("trace", (i, a, k) =>
        {
            var p = new Args("trace", i, a, k, "a", "offset", "axis1", "axis2", "dtype", "out");
            return Conv.Result(np.Trace(p.ND(0), p.Int(1, 0)));
        });
        Def("fill_diagonal", (i, a, k) =>
        {
            var p = new Args("fill_diagonal", i, a, k, "a", "val", "wrap");
            np.FillDiagonal(p.ND(0), p.ND(1));
            return PyNone.Instance;
        });

        // ---------------------------------------------------------------- shape
        Def("reshape", (i, a, k) =>
        {
            if (k is not null && k.Keys.Any(x => x is not ("order" or "newshape" or "shape" or "copy")))
                throw PyErr.TypeError("reshape() got an unexpected keyword argument");
            var shapeArgs = a.Skip(1).ToList();
            if (shapeArgs.Count == 0 && k is not null && (k.TryGetValue("newshape", out var ns) || k.TryGetValue("shape", out ns))) shapeArgs.Add(ns);
            return Conv.Wrap(np.Reshape(Conv.ND(a[0]), ShapeFromArgs(shapeArgs)));
        });
        Def("ravel", (i, a, k) => Conv.Wrap(np.Ravel(Conv.ND(a[0]))));
        Def("flatten", (i, a, k) => Conv.Wrap(np.Flatten(Conv.ND(a[0]))));
        Def("transpose", (i, a, k) =>
        {
            var rest = a.Skip(1).ToList();
            if (rest.Count == 0 && k is not null && k.TryGetValue("axes", out var ax)) rest.Add(ax);
            int[]? axes = rest.Count == 0 || (rest.Count == 1 && rest[0] is PyNone) ? null : ShapeFromArgs(rest);
            return Conv.Wrap(np.Transpose(Conv.ND(a[0]), axes));
        });
        Def("swapaxes", (i, a, k) =>
        {
            var p = new Args("swapaxes", i, a, k, "a", "axis1", "axis2");
            return Conv.Wrap(np.SwapAxes(p.ND(0), p.Int(1, 0), p.Int(2, 0)));
        });
        Def("moveaxis", (i, a, k) =>
        {
            var p = new Args("moveaxis", i, a, k, "a", "source", "destination");
            return Conv.Wrap(np.MoveAxis(p.ND(0), p.Int(1, 0), p.Int(2, 0)));
        });
        Def("expand_dims", (i, a, k) =>
        {
            var p = new Args("expand_dims", i, a, k, "a", "axis");
            var arr = p.ND(0);
            var axes = p.Axes(1) ?? throw PyErr.TypeError("expand_dims() missing 'axis'");
            int nd = arr.Ndim + axes.Length;
            foreach (var ax in axes.Select(x => x < 0 ? x + nd : x).OrderBy(x => x))
                arr = np.ExpandDims(arr, ax);
            return Conv.Wrap(arr);
        });
        Def("squeeze", (i, a, k) =>
        {
            var p = new Args("squeeze", i, a, k, "a", "axis");
            return Conv.Wrap(np.Squeeze(p.ND(0), p.Axis(1)));
        });
        Def("broadcast_to", (i, a, k) =>
        {
            var p = new Args("broadcast_to", i, a, k, "array", "shape", "subok");
            return Conv.Wrap(np.BroadcastTo(p.ND(0), Conv.ToShape(p.Required(1))));
        });
        Def("atleast_1d", (i, a, k) => Conv.Wrap(np.AtLeast1d(Conv.ND(a[0]))));
        Def("atleast_2d", (i, a, k) => Conv.Wrap(np.AtLeast2d(Conv.ND(a[0]))));

        List<NDArray> Seq(Interp interp, object o) => PyOps.Iterate(interp, o).Select(Conv.ND).ToList();
        Def("concatenate", (i, a, k) =>
        {
            var p = new Args("concatenate", i, a, k, "arrays", "axis", "out", "dtype");
            var arrays = Seq(i, p.Required(0));
            if (p.Has(1) is false && p[1] is PyNone)
                return Conv.Wrap(np.Concatenate(arrays.Select(np.Ravel).ToList(), 0));
            return p.Finish(2, np.Concatenate(arrays, p.Int(1, 0)));
        });
        Def("stack", (i, a, k) =>
        {
            var p = new Args("stack", i, a, k, "arrays", "axis", "out");
            return p.Finish(2, np.Stack(Seq(i, p.Required(0)), p.Int(1, 0)));
        });
        Def("vstack", (i, a, k) => Conv.Wrap(np.VStack(Seq(i, a[0]))));
        Def("hstack", (i, a, k) => Conv.Wrap(np.HStack(Seq(i, a[0]))));
        Def("dstack", (i, a, k) => Conv.Wrap(np.DStack(Seq(i, a[0]))));
        Def("flip", (i, a, k) =>
        {
            var p = new Args("flip", i, a, k, "m", "axis");
            return Conv.Wrap(np.Flip(p.ND(0), p.Axes(1)));
        });
        Def("fliplr", (i, a, k) => Conv.Wrap(np.Fliplr(Conv.ND(a[0]))));
        Def("flipud", (i, a, k) => Conv.Wrap(np.Flipud(Conv.ND(a[0]))));
        Def("roll", (i, a, k) =>
        {
            var p = new Args("roll", i, a, k, "a", "shift", "axis");
            var shifts = p.Axes(1)!;
            var axes = p.Axes(2);
            var arr = p.ND(0);
            if (axes is null) return Conv.Wrap(np.Roll(arr, shifts[0]));
            if (axes.Length != shifts.Length) throw PyErr.ValueError("'shift' and 'axis' should be scalars or 1D sequences");
            for (int j = 0; j < axes.Length; j++) arr = np.Roll(arr, shifts[j], axes[j]);
            return Conv.Wrap(arr);
        });
        Def("tile", (i, a, k) =>
        {
            var p = new Args("tile", i, a, k, "A", "reps");
            return Conv.Wrap(np.Tile(p.ND(0), Conv.ToShape(p.Required(1))));
        });
        Def("repeat", (i, a, k) =>
        {
            var p = new Args("repeat", i, a, k, "a", "repeats", "axis");
            return Conv.Wrap(np.Repeat(p.ND(0), p.Int(1, 1), p.Axis(2)));
        });
        Def("pad", (i, a, k) =>
        {
            var p = new Args("pad", i, a, k, "array", "pad_width", "mode", "constant_values");
            string mode = p.Has(2) ? (string)p[2]! : "constant";
            if (mode != "constant") throw PyErr.NotImplementedError($"np.pad mode '{mode}' is not implemented");
            var widths = ParsePadWidth(p.Required(1));
            object? cv = null;
            if (p.Has(3)) cv = Conv.Scalarize(Conv.NDStrong(p[3]!)) is var v ? v : null;
            return Conv.Wrap(np.PadConstant(p.ND(0), widths, cv is null ? null : NativeValue(cv)));
        });

        // ---------------------------------------------------------------- products & linalg
        Def("dot", (i, a, k) =>
        {
            var p = new Args("dot", i, a, k, "a", "b", "out");
            return p.Finish(2, np.Dot(p.ND(0), p.ND(1)));
        });
        Def("matmul", (i, a, k) =>
        {
            var p = new Args("matmul", i, a, k, "x1", "x2", "out");
            return p.Finish(2, np.MatMul(p.ND(0), p.ND(1)));
        });
        Def("outer", (i, a, k) =>
        {
            var p = new Args("outer", i, a, k, "a", "b", "out");
            return p.Finish(2, np.Outer(p.ND(0), p.ND(1)));
        });
        Def("inner", (i, a, k) =>
        {
            var p = new Args("inner", i, a, k, "a", "b");
            return Conv.Result(np.Inner(p.ND(0), p.ND(1)));
        });

        // ---------------------------------------------------------------- introspection
        Def("shape", (i, a, k) => Shape(Conv.ND(a[0])));
        Def("ndim", (i, a, k) => new BigInteger(Conv.ND(a[0]).Ndim));
        Def("size", (i, a, k) => new BigInteger(Conv.ND(a[0]).Size));
        Def("isscalar", (i, a, k) => a[0] is bool or BigInteger or double or string || a[0] is PyInstance { Native: ScalarBox });

        Def("isclose", (i, a, k) =>
        {
            var p = new Args("isclose", i, a, k, "a", "b", "rtol", "atol", "equal_nan");
            return Conv.Result(IsClose(p.ND(0), p.ND(1), p.Double(2, 1e-5), p.Double(3, 1e-8), p.Bool(4, false)));
        });
        Def("allclose", (i, a, k) =>
        {
            var p = new Args("allclose", i, a, k, "a", "b", "rtol", "atol", "equal_nan");
            var r = IsClose(p.ND(0), p.ND(1), p.Double(2, 1e-5), p.Double(3, 1e-8), p.Bool(4, false));
            return (bool)np.All(r).GetAt(0);
        });
        Def("array_equal", (i, a, k) =>
        {
            var p = new Args("array_equal", i, a, k, "a1", "a2", "equal_nan");
            var x = p.ND(0);
            var y = p.ND(1);
            if (!x.Shape.SequenceEqual(y.Shape)) return false;
            var eq = np.Equal(x, y);
            if (p.Bool(2, false))
                eq = np.LogicalOr(eq, np.LogicalAnd(np.IsNan(x), np.IsNan(y)));
            return (bool)np.All(eq).GetAt(0);
        });

        Def("norm", (i, a, k) =>
        {
            var p = new Args("norm", i, a, k, "x", "ord", "axis", "keepdims");
            return Conv.Result(Norm(p));
        });

        Def("set_printoptions", (i, a, k) =>
        {
            var p = new Args("set_printoptions", i, a, k, "precision", "threshold", "edgeitems", "linewidth", "suppress", "nanstr", "infstr");
            var o = PrintOptions.Default;
            if (p.Has(0)) o.Precision = p.Int(0, 8);
            if (p.Has(1)) o.Threshold = p.Int(1, 1000);
            if (p.Has(2)) o.EdgeItems = p.Int(2, 3);
            if (p.Has(3)) o.LineWidth = p.Int(3, 75);
            if (p.Has(4)) o.Suppress = p.Bool(4, false);
            if (p.Has(5)) o.NanStr = (string)p[5]!;
            if (p.Has(6)) o.InfStr = (string)p[6]!;
            return PyNone.Instance;
        });
        Def("get_printoptions", (i, a, k) =>
        {
            var o = PrintOptions.Default;
            var d = new PyDict();
            d["precision"] = new BigInteger(o.Precision);
            d["threshold"] = new BigInteger(o.Threshold);
            d["edgeitems"] = new BigInteger(o.EdgeItems);
            d["linewidth"] = new BigInteger(o.LineWidth);
            d["suppress"] = o.Suppress;
            return d;
        });
        Def("array2string", (i, a, k) => ArrayFormat.Str(Conv.ND(a[0])));
        Def("array_str", (i, a, k) => ArrayFormat.Str(Conv.ND(a[0])));
        Def("array_repr", (i, a, k) => ArrayFormat.Repr(Conv.ND(a[0])));

        // ndarray-only methods that live in the same table so the class can pick them up
        Def("astype", (i, a, k) =>
        {
            var p = new Args("astype", i, a, k, "self", "dtype", "order", "casting", "subok", "copy");
            var nd = p.ND(0);
            var dt = p.DType(1) ?? throw PyErr.TypeError("astype() missing required argument 'dtype'");
            return Conv.Wrap(dt == nd.DType && !p.Bool(5, true) ? nd : nd.AsType(dt));
        });
        Def("tolist", (i, a, k) => ToList(Conv.ND(a[0])));
        Def("item", (i, a, k) =>
        {
            var nd = Conv.ND(a[0]);
            if (a.Length == 1) return Conv.BoxedToPython(nd.ToScalar());
            var idx = a.Length == 2 && a[1] is PyTuple t ? t.Items : a.Skip(1).ToArray();
            if (idx.Length == 1)
            {
                int flat = Conv.ToInt(idx[0], "index");
                var r = np.Ravel(nd);
                return Conv.BoxedToPython(r.GetAt((flat < 0 ? flat + r.Size : flat) * r.Strides[0]));
            }
            return Conv.BoxedToPython(nd[idx.Select(x => Conv.ToInt(x, "index")).ToArray()]);
        });
        Def("fill", (i, a, k) =>
        {
            Assign.Copy(Conv.ND(a[0]), Conv.ND(a[1]), Casting.Unsafe);
            return PyNone.Instance;
        });
        Def("conj", (i, a, k) => Conv.Wrap(Conv.ND(a[0]).Copy()));
        Alias("conjugate", "conj");
        Def("tobytes", (i, a, k) =>
        {
            var nd = Conv.ND(a[0]).Copy();
            var bytes = new byte[nd.Size * nd.DType.ItemSize()];
            Buffer.BlockCopy(nd.Buffer, 0, bytes, 0, bytes.Length);
            return new PyBytes(bytes);
        });
        Alias("tostring", "tobytes");

        return all;
    }

    // ================================================================ helpers

    private static NDArray MakeArray(Args p, bool copyDefault)
    {
        var src = p.Required(0);
        var dt = p.DType(1);
        bool copy = p.Bool(2, copyDefault);
        int ndmin = p.Int(5, 0);
        NDArray r;
        if (Conv.TryUnwrap(src) is { } nd && src is PyInstance { Native: NDArray })
        {
            r = dt is DType d && d != nd.DType ? nd.AsType(d) : copy ? nd.Copy() : nd;
        }
        else r = MakeArrayFrom(src, dt);
        while (r.Ndim < ndmin) r = np.ExpandDims(r, 0);
        return r;
    }

    /// <summary>numpy <c>array(x, dtype)</c> for non-ndarray input.</summary>
    private static NDArray MakeArrayFrom(object src, DType? dt)
    {
        NDArray r = src switch
        {
            PyList or PyTuple => Conv.FromSequence(src, dt),
            _ => Conv.NDStrong(src),
        };
        return dt is DType d && d != r.DType ? r.AsType(d) : r;
    }

    private static int[] ShapeFromArgs(List<object> args)
    {
        if (args.Count == 1 && args[0] is PyTuple or PyList) return Conv.ToShape(args[0]);
        if (args.Count == 1 && Conv.IsNdArray(args[0])) return Conv.ToShape(args[0]);
        return args.Select(x => Conv.ToInt(x, "shape")).ToArray();
    }

    internal static PyTuple Shape(NDArray a) => new(a.Shape.Select(d => (object)new BigInteger(d)).ToArray());

    private static (int, int)[] ParsePadWidth(object o)
    {
        if (o is BigInteger b) return new[] { ((int)b, (int)b) };
        var items = o switch { PyTuple t => t.Items, PyList l => l.Items.ToArray(), _ => throw PyErr.TypeError("pad_width must be int or sequence") };
        if (items.Length == 2 && items.All(x => x is BigInteger))
            return new[] { ((int)(BigInteger)items[0], (int)(BigInteger)items[1]) };
        if (items.Length == 1 && items[0] is BigInteger one) return new[] { ((int)one, (int)one) };
        return items.Select(x =>
        {
            var pair = x switch { PyTuple t => t.Items, PyList l => l.Items.ToArray(), BigInteger => new[] { x, x }, _ => throw PyErr.TypeError("pad_width must be int or sequence") };
            return ((int)(BigInteger)pair[0], (int)(BigInteger)pair[pair.Length > 1 ? 1 : 0]);
        }).ToArray();
    }

    private static object NativeValue(object py) => py switch
    {
        bool b => b,
        BigInteger bi => (long)bi,
        double d => d,
        _ => py is PyInstance { Native: ScalarBox sb } ? sb.Array.GetAt(0) : throw PyErr.TypeError("invalid constant value"),
    };

    internal static NDArray IsClose(NDArray a, NDArray b, double rtol, double atol, bool equalNan)
    {
        var af = a.DType.IsFloat() ? a : a.AsType(DType.Float64);
        var bf = b.DType.IsFloat() ? b : b.AsType(DType.Float64);
        var finite = np.LogicalAnd(np.IsFinite(af), np.IsFinite(bf));
        // |a - b| <= atol + rtol * |b|
        var diff = np.Abs(np.Subtract(af, bf));
        var tol = np.Add(NDArray.WeakScalar(atol), np.Multiply(NDArray.WeakScalar(rtol), np.Abs(bf)));
        var close = np.LessEqual(diff, tol);
        var result = np.Where(finite, close, np.Equal(af, bf));
        if (equalNan)
            result = np.LogicalOr(result, np.LogicalAnd(np.IsNan(af), np.IsNan(bf)));
        return result;
    }

    internal static NDArray Norm(Args p)
    {
        var x = p.ND(0);
        var axes = p.Axes(2);
        bool keep = p.Bool(3, false);
        if (!p.Has(1)) return np.Norm(x, axes, keep);
        var ordObj = p[1]!;
        if (ordObj is string s)
        {
            if (s == "fro") return np.Norm(x, axes, keep);
            throw PyErr.NotImplementedError($"norm(ord='{s}') is not implemented");
        }
        double ord = PyOps.AsDouble(ordObj is PyInstance { Native: ScalarBox } ? ordObj : ordObj);
        bool matrix = (axes is null && x.Ndim == 2) || (axes is not null && axes.Length == 2);
        if (!matrix) return np.Norm(x, ord, axes, keep);
        var ax = axes ?? new[] { 0, 1 };
        var ab = np.Abs(x.DType.IsFloat() ? x : x.AsType(DType.Float64));
        int a0 = np.NormalizeAxis(ax[0], x.Ndim), a1 = np.NormalizeAxis(ax[1], x.Ndim);
        if (ord == 1 || ord == -1)
        {
            var colSums = np.Sum(ab, new[] { a0 }, true);
            var r = ord == 1 ? np.Max(colSums, new[] { a1 }, true) : np.Min(colSums, new[] { a1 }, true);
            return keep ? r : np.Squeeze(np.Squeeze(r, a0), a1 > a0 ? a1 - 1 : a1);
        }
        if (double.IsInfinity(ord))
        {
            var rowSums = np.Sum(ab, new[] { a1 }, true);
            var r = ord > 0 ? np.Max(rowSums, new[] { a0 }, true) : np.Min(rowSums, new[] { a0 }, true);
            return keep ? r : np.Squeeze(np.Squeeze(r, a0), a1 > a0 ? a1 - 1 : a1);
        }
        throw PyErr.NotImplementedError("matrix norm with this ord requires an SVD (not implemented yet)");
    }

    internal static object ToList(NDArray d)
    {
        if (d.Ndim == 0) return Conv.BoxedToPython(d.GetAt(0));
        var items = new object[d.Shape[0]];
        for (int i = 0; i < items.Length; i++)
            items[i] = ToList(d.Get(i));
        return new PyList(items);
    }
}
