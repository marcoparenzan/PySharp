// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Runtime.InteropServices;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using TorchSharp;
using static TorchSharp.torch;
using Tensor = TorchSharp.torch.Tensor;

namespace PySharpLib.Torch;

internal delegate object RawFn(Interp interp, object[] a, Dictionary<string, object>? kw);

/// <summary>One torch operation, callable both as <c>torch.name(x, ...)</c> and <c>x.name(...)</c>.</summary>
internal sealed class OpDef
{
    public string Name { get; }
    public RawFn Fn { get; }
    public OpDef(string name, RawFn fn) { Name = name; Fn = fn; }
}

internal static partial class Ops
{
    public static readonly Dictionary<string, OpDef> Table = new();

    /// <summary>libtorch/TorchSharp failures surface as Python's RuntimeError, like real torch.</summary>
    public static PyBuiltinFunction Fn(string name, RawFn fn)
        => new("torch." + name, (interp, a, kw) =>
        {
            TC.Interp = interp;
            try
            {
                var r = fn(interp, a, kw);
                if (!name.StartsWith("__")) GradFn.Tag(r, name);
                return r;
            }
            catch (PyRaise) { throw; }
            catch (NDSharp.NDException ex) { throw PyErr.ValueError(ex.Message); }
            catch (ExternalException ex) { throw PyErr.RuntimeError(Clean(ex.Message)); }
            catch (InvalidOperationException ex) { throw PyErr.RuntimeError(Clean(ex.Message)); }
            catch (ArgumentException ex) { throw PyErr.RuntimeError(Clean(ex.Message)); }
            catch (IndexOutOfRangeException ex) { throw PyErr.IndexError(ex.Message); }
        });

    private static string Clean(string m) => m.Split('\n')[0];

    private static void Def(string name, string[] ps, Func<Args, object> fn)
        => Table[name] = new OpDef(name, (i, a, k) => fn(new Args(name, i, a, k, ps)));

    private static void Raw(string name, RawFn fn) => Table[name] = new OpDef(name, fn);

    private static object R(Tensor t) => TC.Wrap(t);
    private static object Tup(params Tensor[] ts) => new PyTuple(ts.Select(t => (object)TC.Wrap(t)).ToArray());

    private static Tensor X(this Args p, int i) => TC.ToTensor(p.Required(i));
    private static Tensor? XOpt(this Args p, int i) => p.Has(i) ? TC.ToTensor(p[i]!) : null;
    private static long[]? Dims(this Args p, int i) => p.Has(i) ? TC.ToLongs(p[i]!) : null;
    private static long L(this Args p, int i, long dflt) => p.Has(i) ? TC.ToLong(p[i]!) : dflt;
    private static ScalarType? DT(this Args p, int i) => p.Has(i) ? TC.ToDType(p[i]) : null;

    private static Tensor Other(Tensor like, Args p, int i) => TC.Operand(like, p.Required(i));

    /// <summary>Named result like torch.return_types.max (values, indices) — indexable, iterable and with attributes.</summary>
    private static readonly Dictionary<string, PyClass> NamedClasses = new();

    public static object Named(string typeName, string[] fields, params object[] values)
    {
        if (!NamedClasses.TryGetValue(typeName, out var cls))
        {
            cls = new PyClass(typeName, new List<PyClass>());
            cls.Dict["__module__"] = "torch.return_types";
            cls.Dict["__len__"] = new PyBuiltinFunction("__len__", (_, a, _) => new BigInteger(fields.Length));
            cls.Dict["__iter__"] = new PyBuiltinFunction("__iter__", (_, a, _) => new PyIterator(((object[])((PyInstance)a[0]).Native!).AsEnumerable().GetEnumerator()));
            cls.Dict["__getitem__"] = new PyBuiltinFunction("__getitem__", (_, a, _) =>
            {
                var v = (object[])((PyInstance)a[0]).Native!;
                int i = (int)TC.ToLong(a[1]);
                return v[i < 0 ? i + v.Length : i];
            });
            cls.Dict["__repr__"] = new PyBuiltinFunction("__repr__", (i, a, _) =>
            {
                var v = (object[])((PyInstance)a[0]).Native!;
                return $"torch.return_types.{typeName}(\n" + string.Join(",\n", fields.Select((f, k) => $"{f}={PyOps.Repr(i, v[k])}")) + ")";
            });
            for (int k = 0; k < fields.Length; k++)
            {
                int idx = k;
                cls.Dict[fields[k]] = new PyProperty { Getter = new PyBuiltinFunction(fields[k], (_, a, _) => ((object[])((PyInstance)a[0]).Native!)[idx]) };
            }
            NamedClasses[typeName] = cls;
        }
        return new PyInstance(cls) { Native = values };
    }

    private static object Pair(string name, (Tensor a, Tensor b) t) => Named(name, new[] { "values", "indices" }, R(t.a), R(t.b));

    // ------------------------------------------------------------------------------------------------ registration

    static Ops()
    {
        Unary();
        Binary();
        Reductions();
        Shapes();
        Linalg();
        Misc();
        InPlace();
    }

    private static void Unary()
    {
        void U(string name, Func<Tensor, Tensor> f) => Def(name, new[] { "input" }, p => R(f(p.X(0))));
        U("sin", t => t.sin()); U("cos", t => t.cos()); U("tan", t => t.tan()); U("tanh", t => t.tanh());
        U("sinh", t => t.sinh()); U("cosh", t => t.cosh()); U("asin", t => t.asin()); U("acos", t => t.acos()); U("atan", t => t.atan());
        U("asinh", t => t.asinh()); U("acosh", t => t.acosh()); U("atanh", t => t.atanh());
        U("sigmoid", t => t.sigmoid()); U("relu", t => t.relu()); U("exp", t => t.exp()); U("log", t => t.log());
        U("log2", t => t.log2()); U("log10", t => t.log10()); U("log1p", t => t.log1p()); U("expm1", t => t.expm1());
        U("sqrt", t => t.sqrt()); U("rsqrt", t => t.rsqrt()); U("abs", t => t.abs()); U("absolute", t => t.abs()); U("neg", t => t.neg());
        U("negative", t => t.neg()); U("floor", t => t.floor()); U("ceil", t => t.ceil()); U("trunc", t => t.trunc()); U("fix", t => t.trunc());
        U("frac", t => t.frac()); U("sign", t => t.sign()); U("square", t => t.square()); U("erf", t => t.erf()); U("erfc", t => t.erfc());
        U("reciprocal", t => t.reciprocal()); U("isnan", t => t.isnan()); U("isinf", t => t.isinf()); U("isfinite", t => t.isfinite());
        U("logical_not", t => t.logical_not()); U("bitwise_not", t => t.bitwise_not()); U("detach", t => t.detach());
        U("clone", t => t.clone()); U("contiguous", t => t.contiguous()); U("t", t => t.t()); U("lgamma", t => t.lgamma());
        U("digamma", t => t.digamma()); U("sgn", t => t.sign()); U("positive", t => t);
        Def("round", new[] { "input", "decimals" }, p => R(p.Has(1) ? (p.X(0) * Math.Pow(10, p.L(1, 0))).round() / Math.Pow(10, p.L(1, 0)) : p.X(0).round()));
        Def("clamp", new[] { "input", "min", "max" }, p => Clamp(p.X(0), p.Has(1) ? p[1] : null, p.Has(2) ? p[2] : null));
        Table["clip"] = Table["clamp"];
        Def("clamp_min", new[] { "input", "min" }, p => Clamp(p.X(0), p[1], null));
        Def("clamp_max", new[] { "input", "max" }, p => Clamp(p.X(0), null, p[1]));
        Def("nan_to_num", new[] { "input", "nan", "posinf", "neginf" }, p => R(p.X(0).nan_to_num(p.Double(1, 0.0), p.Has(2) ? p.Double(2, 0) : null, p.Has(3) ? p.Double(3, 0) : null)));
        Def("softmax", new[] { "input", "dim", "dtype" }, p => R(torch.special.softmax(p.X(0), p.L(1, -1), p.DT(2))));
        Def("log_softmax", new[] { "input", "dim", "dtype" }, p => R(torch.special.log_softmax(p.X(0), p.L(1, -1), p.DT(2))));
        Def("leaky_relu", new[] { "input", "negative_slope" }, p => R(torch.nn.functional.leaky_relu(p.X(0), p.Double(1, 0.01))));
        Def("pow", new[] { "input", "exponent" }, p =>
        {
            if (TC.IsPyNumber(p.Required(0))) return R(torch.pow(TC.Operand(p.X(1), p.Required(0)), p.X(1)));
            var x = p.X(0);
            return R(x.pow(Other(x, p, 1)));
        });
    }

    private static object Clamp(Tensor x, object? lo, object? hi)
    {
        if (lo is null && hi is null) throw PyErr.RuntimeError("torch.clamp: At least one of 'min' or 'max' must not be None");
        Tensor r = x;
        if (lo is not null) r = lo is PyInstance { Native: TBox } ? torch.maximum(r, TC.Unwrap(lo)) : torch.maximum(r, TC.ScalarLike(x, lo).to(r.dtype == ScalarType.Bool ? ScalarType.Int64 : TC.IsFloat(r.dtype) ? r.dtype : (lo is double ? TC.DefaultFloat : r.dtype)));
        if (hi is not null) r = hi is PyInstance { Native: TBox } ? torch.minimum(r, TC.Unwrap(hi)) : torch.minimum(r, TC.ScalarLike(x, hi).to(r.dtype));
        // clamp with NaN input must stay NaN: maximum/minimum propagate NaN already
        return R(r);
    }

    private static void Binary()
    {
        void B(string name, Func<Tensor, Tensor, Tensor> f)
            => Def(name, new[] { "input", "other" }, p =>
            {
                var a = p.Required(0); var b = p.Required(1);
                if (TC.IsPyNumber(a) && !TC.IsPyNumber(b)) { var y = TC.ToTensor(b); return R(f(TC.Operand(y, a), y)); }
                var x = TC.ToTensor(a);
                return R(f(x, TC.Operand(x, b)));
            });
        B("mul", (a, b) => a.mul(b)); B("multiply", (a, b) => a.mul(b));
        B("div", (a, b) => a.div(b)); B("divide", (a, b) => a.div(b)); B("true_divide", (a, b) => a.div(b));
        B("floor_divide", (a, b) => a.div(b, RoundingMode.floor));
        B("remainder", (a, b) => a.remainder(b)); B("fmod", (a, b) => a.fmod(b));
        B("eq", (a, b) => a.eq(b)); B("ne", (a, b) => a.ne(b)); B("lt", (a, b) => a.lt(b)); B("le", (a, b) => a.le(b));
        B("gt", (a, b) => a.gt(b)); B("ge", (a, b) => a.ge(b));
        B("maximum", (a, b) => torch.maximum(a, b)); B("minimum", (a, b) => torch.minimum(a, b));
        B("atan2", (a, b) => torch.atan2(a, b)); B("arctan2", (a, b) => torch.atan2(a, b));
        B("logical_and", (a, b) => a.logical_and(b)); B("logical_or", (a, b) => a.logical_or(b)); B("logical_xor", (a, b) => a.logical_xor(b));
        B("bitwise_and", (a, b) => a.bitwise_and(b)); B("bitwise_or", (a, b) => a.bitwise_or(b)); B("bitwise_xor", (a, b) => a.bitwise_xor(b));
        Def("add", new[] { "input", "other", "alpha" }, p =>
        {
            var a = p.Required(0); var b = p.Required(1);
            Tensor x, y;
            if (TC.IsPyNumber(a) && !TC.IsPyNumber(b)) { y = TC.ToTensor(b); x = TC.Operand(y, a); } else { x = TC.ToTensor(a); y = TC.Operand(x, b); }
            if (p.Has(2)) y = y.mul(TC.ScalarLike(y, p[2]!));
            return R(x.add(y));
        });
        Def("sub", new[] { "input", "other", "alpha" }, p =>
        {
            var a = p.Required(0); var b = p.Required(1);
            Tensor x, y;
            if (TC.IsPyNumber(a) && !TC.IsPyNumber(b)) { y = TC.ToTensor(b); x = TC.Operand(y, a); } else { x = TC.ToTensor(a); y = TC.Operand(x, b); }
            if (p.Has(2)) y = y.mul(TC.ScalarLike(y, p[2]!));
            return R(x.sub(y));
        });
        Table["subtract"] = Table["sub"];
        Def("rsub", new[] { "input", "other" }, p => { var x = p.X(0); return R(Other(x, p, 1).sub(x)); });
        Def("lerp", new[] { "input", "end", "weight" }, p =>
        {
            var x = p.X(0); var e = Other(x, p, 1);
            return R(x.lerp(e, Other(x, p, 2)));
        });
        Def("addcmul", new[] { "input", "tensor1", "tensor2", "value" }, p => R(p.X(0).addcmul(p.X(1), p.X(2), p.Has(3) ? TC.ToDouble(p[3]!) : 1.0)));
        Def("addcdiv", new[] { "input", "tensor1", "tensor2", "value" }, p => R(p.X(0).addcdiv(p.X(1), p.X(2), p.Has(3) ? TC.ToDouble(p[3]!) : 1.0)));
        Def("where", new[] { "condition", "input", "other" }, p =>
        {
            var c = p.X(0);
            if (!p.Has(1)) return Tup(c.nonzero().unbind(1).ToArray());
            var a = p.Required(1); var b = p.Required(2);
            Tensor x, y;
            if (TC.IsPyNumber(a) && TC.IsPyNumber(b)) { x = TC.ToTensor(a); y = TC.ToTensor(b); if (a is BigInteger && b is double) x = x.to(TC.DefaultFloat); else if (a is double && b is BigInteger) y = y.to(TC.DefaultFloat); if (x.dtype == ScalarType.Float64 && y.dtype == ScalarType.Float64) { x = x.to(TC.DefaultFloat); y = y.to(TC.DefaultFloat); } }
            else if (TC.IsPyNumber(a)) { y = TC.ToTensor(b); x = TC.Operand(y, a); }
            else { x = TC.ToTensor(a); y = TC.Operand(x, b); }
            return R(torch.where(c, x, y));
        });
        Def("masked_fill", new[] { "input", "mask", "value" }, p => { var x = p.X(0); var v = p.Required(2); if (TC.IsTensor(v)) v = TC.Item(TC.Unwrap(v)); return R(x.masked_fill(p.X(1), TC.IsFloat(x.dtype) ? (Scalar)TC.ToDouble(v) : (Scalar)(v is double dd ? (long)dd : TC.ToLong(v)))); });
        Def("equal", new[] { "input", "other" }, p => p.X(0).Equals(p.X(1)) || (p.X(0).shape.SequenceEqual(p.X(1).shape) && p.X(0).eq(p.X(1)).all().item<bool>()));
        Def("allclose", new[] { "input", "other", "rtol", "atol", "equal_nan" }, p => p.X(0).allclose(p.X(1), p.Double(2, 1e-5), p.Double(3, 1e-8), p.Bool(4, false)));
        Def("isclose", new[] { "input", "other", "rtol", "atol", "equal_nan" }, p => R(p.X(0).isclose(p.X(1), p.Double(2, 1e-5), p.Double(3, 1e-8), p.Bool(4, false))));
    }

    private static void Reductions()
    {
        Def("sum", new[] { "input", "dim", "keepdim", "dtype" }, p =>
        {
            var x = p.X(0);
            if (x.dtype is ScalarType.Bool or ScalarType.Byte or ScalarType.Int8 or ScalarType.Int16 or ScalarType.Int32 && !p.Has(3)) x = x.to(ScalarType.Int64);
            return R(p.Has(1) ? x.sum(p.Dims(1)!, p.Bool(2, false), p.DT(3)) : (p.Has(3) ? x.sum(p.DT(3)) : x.sum()));
        });
        Def("mean", new[] { "input", "dim", "keepdim", "dtype" }, p =>
        {
            var x = p.X(0);
            if (!TC.IsFloat(x.dtype) && !p.Has(3)) throw PyErr.RuntimeError($"mean(): could not infer output dtype. Input dtype must be either a floating point or complex dtype. Got: {TC.DTypeName(x.dtype).Replace("int64", "Long")}");
            return R(p.Has(1) ? x.mean(p.Dims(1)!, p.Bool(2, false), p.DT(3)) : (p.Has(3) ? x.to(p.DT(3)!.Value).mean() : x.mean()));
        });
        Def("prod", new[] { "input", "dim", "keepdim", "dtype" }, p => R(p.Has(1) ? p.X(0).prod(p.L(1, 0), p.Bool(2, false), p.DT(3)) : p.X(0).prod()));
        void StdVar(string name, bool isStd)
            => Raw(name, (i, a, kw) =>
            {
                var p = new Args(name, i, a, kw, "input", "dim", "unbiased", "keepdim", "correction");
                var x = p.X(0);
                object? dimArg = p.Has(1) ? p[1] : null; bool unbiased = true;
                if (dimArg is bool ub) { unbiased = ub; dimArg = null; }
                if (p.Has(2)) unbiased = p.Bool(2, true);
                if (p.Has(4)) unbiased = TC.ToDouble(p[4]!) != 0;
                if (dimArg is null) return R(isStd ? x.std(unbiased) : x.@var(unbiased));
                var dims = TC.ToLongs(dimArg);
                return R(isStd ? x.std(dims, unbiased, p.Bool(3, false)) : x.@var(dims, unbiased, p.Bool(3, false)));
            });
        StdVar("std", true); StdVar("var", false);
        Def("max", new[] { "input", "dim", "keepdim" }, p => MinMax(p, true));
        Def("min", new[] { "input", "dim", "keepdim" }, p => MinMax(p, false));
        Def("amax", new[] { "input", "dim", "keepdim" }, p => AmaxAmin(p, true));
        Def("amin", new[] { "input", "dim", "keepdim" }, p => AmaxAmin(p, false));
        Def("argmax", new[] { "input", "dim", "keepdim" }, p => R(p.Has(1) ? p.X(0).argmax(p.L(1, 0), p.Bool(2, false)) : p.X(0).argmax()));
        Def("argmin", new[] { "input", "dim", "keepdim" }, p => R(p.Has(1) ? p.X(0).argmin(p.L(1, 0), p.Bool(2, false)) : p.X(0).argmin()));
        Def("all", new[] { "input", "dim", "keepdim" }, p => R(p.Has(1) ? p.X(0).all(p.L(1, 0), p.Bool(2, false)) : p.X(0).all()));
        Def("any", new[] { "input", "dim", "keepdim" }, p => R(p.Has(1) ? p.X(0).any(p.L(1, 0), p.Bool(2, false)) : p.X(0).any()));
        Def("cumsum", new[] { "input", "dim", "dtype" }, p => R(p.X(0).cumsum(p.L(1, 0), p.DT(2))));
        Def("cumprod", new[] { "input", "dim", "dtype" }, p => R(p.X(0).cumprod(p.L(1, 0), p.DT(2))));
        Def("logsumexp", new[] { "input", "dim", "keepdim" }, p => R(torch.logsumexp(p.X(0), p.L(1, 0), p.Bool(2, false))));
        Def("median", new[] { "input", "dim", "keepdim" }, p => p.Has(1) ? Median(p.X(0), p.L(1, 0), p.Bool(2, false)) : R(p.X(0).median()));
        Def("norm", new[] { "input", "p", "dim", "keepdim" }, p =>
        {
            var x = p.X(0);
            double pp = p.Has(1) ? (p[1] is string ps ? (ps == "fro" ? 2.0 : double.NaN) : TC.ToDouble(p[1]!)) : 2.0;
            if (!p.Has(2)) return R(x.norm((float)pp));
            var dims = p.Dims(2)!;
            return R(torch.linalg.vector_norm(x, pp, dims, p.Bool(3, false)));
        });
        Def("topk", new[] { "input", "k", "dim", "largest", "sorted" }, p =>
        {
            var (v, i) = p.X(0).topk((int)p.L(1, 1), (int)p.L(2, -1), p.Bool(3, true), p.Bool(4, true));
            return Named("topk", new[] { "values", "indices" }, R(v), R(i));
        });
        Def("sort", new[] { "input", "dim", "descending", "stable" }, p =>
        {
            var (v, i) = p.X(0).sort(p.L(1, -1), p.Bool(2, false), p.Bool(3, false));
            return Named("sort", new[] { "values", "indices" }, R(v), R(i));
        });
        Def("argsort", new[] { "input", "dim", "descending", "stable" }, p => R(p.X(0).argsort(p.L(1, -1), p.Bool(2, false))));
        Def("count_nonzero", new[] { "input", "dim" }, p => R(p.Has(1) ? p.X(0).count_nonzero(p.Dims(1)!) : p.X(0).ne(0).sum()));
        Def("nonzero", new[] { "input", "as_tuple" }, p => p.Bool(1, false) ? Tup(p.X(0).nonzero().unbind(1).ToArray()) : R(p.X(0).nonzero()));
        Def("unique", new[] { "input", "sorted", "return_inverse", "return_counts", "dim" }, p =>
        {
            var (v, inv, cnt) = p.X(0).unique(p.Bool(1, true), p.Bool(2, false), p.Bool(3, false), p.Has(4) ? (int?)p.L(4, 0) : null);
            bool ri = p.Bool(2, false), rc = p.Bool(3, false);
            if (!ri && !rc) return R(v);
            var items = new List<Tensor> { v };
            if (ri) items.Add(inv); if (rc) items.Add(cnt);
            return Tup(items.ToArray());
        });
    }

    private static object AmaxAmin(Args p, bool isMax)
    {
        var x = p.X(0);
        var dims = p.Has(1) ? p.Dims(1)!.Select(d => d < 0 ? d + x.Dimensions : d).ToArray() : Enumerable.Range(0, (int)x.Dimensions).Select(v => (long)v).ToArray();
        var r = isMax ? x.amax(dims) : x.amin(dims);
        if (p.Bool(2, false)) foreach (var d in dims.OrderBy(v => v)) r = r.unsqueeze(d);
        return R(r);
    }

    private static object Median(Tensor x, long dim, bool keep)
    {
        if (dim < 0) dim += x.Dimensions;
        var (v, i) = x.sort(dim);
        long k = (x.shape[dim] - 1) / 2;
        var values = v.select(dim, k); var idx = i.select(dim, k);
        if (keep) { values = values.unsqueeze(dim); idx = idx.unsqueeze(dim); }
        return Named("median", new[] { "values", "indices" }, R(values), R(idx));
    }

    private static object MinMax(Args p, bool isMax)
    {
        var x = p.X(0);
        string n = isMax ? "max" : "min";
        if (!p.Has(1)) return R(isMax ? x.max() : x.min());
        if (p[1] is PyInstance { Native: TBox ob }) return R(isMax ? torch.maximum(x, ob.T) : torch.minimum(x, ob.T));
        long dim = p.L(1, 0); bool keep = p.Bool(2, false);
        var r = isMax ? x.max(dim, keep) : x.min(dim, keep);
        return Pair(n, r);
    }

    private static Tensor Reshape(Tensor x, object[] a, int from, bool view)
    {
        var shape = TC.ShapeArgs(a, from);
        return view ? x.view(shape) : x.reshape(shape);
    }

    private static void Shapes()
    {
        Raw("view", (i, a, kw) =>
        {
            if (a.Length == 1 && kw is not null && kw.TryGetValue("size", out var sz)) return R(TC.Unwrap(a[0]).view(TC.ToLongs(sz)));
            return R(Reshape(TC.Unwrap(a[0]), a, 1, true));
        });
        Raw("reshape", (i, a, kw) =>
        {
            if (a.Length == 1 && kw is not null && kw.TryGetValue("shape", out var sz)) return R(TC.Unwrap(a[0]).reshape(TC.ToLongs(sz)));
            return R(Reshape(TC.Unwrap(a[0]), a, 1, false));
        });
        Raw("view_as", (i, a, kw) => R(TC.Unwrap(a[0]).view(TC.Unwrap(a[1]).shape)));
        Raw("reshape_as", (i, a, kw) => R(TC.Unwrap(a[0]).reshape(TC.Unwrap(a[1]).shape)));
        Raw("permute", (i, a, kw) =>
        {
            var dims = a.Length == 1 && kw is not null && kw.TryGetValue("dims", out var d) ? TC.ToLongs(d) : TC.ShapeArgs(a, 1);
            return R(TC.Unwrap(a[0]).permute(dims));
        });
        Raw("expand", (i, a, kw) => R(TC.Unwrap(a[0]).expand(TC.ShapeArgs(a, 1))));
        Raw("repeat", (i, a, kw) => R(TC.Unwrap(a[0]).repeat(TC.ShapeArgs(a, 1))));
        Raw("tile", (i, a, kw) => R(TC.Unwrap(a[0]).tile(TC.ShapeArgs(a, 1))));
        Raw("flip", (i, a, kw) => R(TC.Unwrap(a[0]).flip(TC.ShapeArgs(a, 1))));
        Raw("expand_as", (i, a, kw) => R(TC.Unwrap(a[0]).expand(TC.Unwrap(a[1]).shape)));
        Def("flatten", new[] { "input", "start_dim", "end_dim" }, p => R(p.X(0).flatten(p.L(1, 0), p.L(2, -1))));
        Def("ravel", new[] { "input" }, p => R(p.X(0).reshape(-1)));
        Def("unflatten", new[] { "input", "dim", "sizes" }, p => R(p.X(0).unflatten(p.L(1, 0), p.Dims(2)!)));
        Def("squeeze", new[] { "input", "dim" }, p => R(p.Has(1) ? p.X(0).squeeze(p.L(1, 0)) : p.X(0).squeeze()));
        Def("unsqueeze", new[] { "input", "dim" }, p => R(p.X(0).unsqueeze(p.L(1, 0))));
        Def("transpose", new[] { "input", "dim0", "dim1" }, p => R(p.X(0).transpose(p.L(1, 0), p.L(2, 1))));
        Def("swapaxes", new[] { "input", "axis0", "axis1" }, p => R(p.X(0).transpose(p.L(1, 0), p.L(2, 1))));
        Def("movedim", new[] { "input", "source", "destination" }, p => R(p.X(0).movedim(p.Dims(1)!, p.Dims(2)!)));
        Def("narrow", new[] { "input", "dim", "start", "length" }, p => R(p.X(0).narrow(p.L(1, 0), p.L(2, 0), p.L(3, 0))));
        Def("select", new[] { "input", "dim", "index" }, p => R(p.X(0).select(p.L(1, 0), p.L(2, 0))));
        Def("index_select", new[] { "input", "dim", "index" }, p => R(p.X(0).index_select(p.L(1, 0), p.X(2))));
        Def("gather", new[] { "input", "dim", "index" }, p => R(p.X(0).gather(p.L(1, 0), p.X(2))));
        Def("scatter", new[] { "input", "dim", "index", "src" }, p => { var x = p.X(0); return R(x.scatter(p.L(1, 0), p.X(2), p.Required(3) is PyInstance ? p.X(3) : TC.ScalarLike(x, p[3]!).expand(p.X(2).shape))); });
        Def("scatter_add", new[] { "input", "dim", "index", "src" }, p => R(p.X(0).scatter_add(p.L(1, 0), p.X(2), p.X(3))));
        Def("unfold", new[] { "input", "dimension", "size", "step" }, p => R(p.X(0).unfold(p.L(1, 0), p.L(2, 1), p.L(3, 1))));
        Def("tril", new[] { "input", "diagonal" }, p => R(p.X(0).tril(p.L(1, 0))));
        Def("triu", new[] { "input", "diagonal" }, p => R(p.X(0).triu(p.L(1, 0))));
        Def("diag", new[] { "input", "diagonal" }, p => R(p.X(0).diag(p.L(1, 0))));
        Def("roll", new[] { "input", "shifts", "dims" }, p => R(p.Has(2) ? p.X(0).roll(p.Dims(1)!, p.Dims(2)!) : p.X(0).roll(p.L(1, 0))));
        Def("chunk", new[] { "input", "chunks", "dim" }, p => Tup(p.X(0).chunk(p.L(1, 1), p.L(2, 0))));
        Def("split", new[] { "input", "split_size_or_sections", "dim" }, p =>
        {
            var x = p.X(0); long dim = p.L(2, 0);
            if (p[1] is PyList or PyTuple) return Tup(x.split(TC.ToLongs(p[1]!), dim));
            return Tup(x.split(p.L(1, 1), dim));
        });
        Def("unbind", new[] { "input", "dim" }, p => Tup(p.X(0).unbind(p.L(1, 0)).ToArray()));
        Raw("cat", (i, a, kw) =>
        {
            var p = new Args("cat", i, a, kw, "tensors", "dim");
            var ts = M(p.Required(0));
            return R(torch.cat(ts, p.L(1, 0)));
        });
        Table["concat"] = Table["cat"]; Table["concatenate"] = Table["cat"];
        Raw("stack", (i, a, kw) =>
        {
            var p = new Args("stack", i, a, kw, "tensors", "dim");
            return R(torch.stack(M(p.Required(0)), p.L(1, 0)));
        });
        Raw("hstack", (i, a, kw) => R(torch.hstack(M(a[0]))));
        Raw("vstack", (i, a, kw) => R(torch.vstack(M(a[0]))));
        Raw("meshgrid", (i, a, kw) =>
        {
            var ts = a.Length == 1 && a[0] is PyList or PyTuple ? M(a[0]) : a.Select(v => TC.ToTensor(v)).ToArray();
            string indexing = kw is not null && kw.TryGetValue("indexing", out var ix) ? (string)ix : "ij";
            return Tup(torch.meshgrid(ts, indexing));
        });
        Raw("broadcast_to", (i, a, kw) => R(TC.Unwrap(a[0]).broadcast_to(TC.ToLongs(a[1]))));
    }

    private static Tensor[] M(object o) => o switch
    {
        PyList l => l.Items.Select(v => TC.ToTensor(v)).ToArray(),
        PyTuple t => t.Items.Select(v => TC.ToTensor(v)).ToArray(),
        _ => throw PyErr.TypeError("expected a sequence of tensors"),
    };

    private static void Linalg()
    {
        Def("matmul", new[] { "input", "other" }, p => R(p.X(0).matmul(p.X(1))));
        Def("mm", new[] { "input", "mat2" }, p => R(p.X(0).mm(p.X(1))));
        Def("bmm", new[] { "input", "mat2" }, p => R(p.X(0).bmm(p.X(1))));
        Def("mv", new[] { "input", "vec" }, p => R(p.X(0).mv(p.X(1))));
        Def("dot", new[] { "input", "other" }, p => R(p.X(0).dot(p.X(1))));
        Def("outer", new[] { "input", "vec2" }, p => R(p.X(0).outer(p.X(1))));
        Def("cross", new[] { "input", "other", "dim" }, p => R(torch.linalg.cross(p.X(0), p.X(1), p.L(2, -1))));
        Def("inverse", new[] { "input" }, p => R(torch.linalg.inv(p.X(0))));
        Def("det", new[] { "input" }, p => R(torch.linalg.det(p.X(0))));
        Def("trace", new[] { "input" }, p => R(p.X(0).trace()));
        Def("cdist", new[] { "x1", "x2", "p" }, p => R(torch.cdist(p.X(0), p.X(1), p.Double(2, 2.0))));
        Raw("einsum", (i, a, kw) =>
        {
            var ops = a.Length == 2 && a[1] is PyList or PyTuple ? M(a[1]) : a.Skip(1).Select(v => TC.ToTensor(v)).ToArray();
            return R(torch.einsum((string)a[0], ops));
        });
        Raw("tensordot", (i, a, kw) => throw PyErr.NotImplementedError("tensordot is not supported"));
    }

    private static void Misc()
    {
        // x.new_zeros / new_ones / new_empty / new_full / new_tensor: a fresh tensor with x's dtype unless overridden
        ScalarType? KwDType(Dictionary<string, object>? kw) => kw is not null && kw.TryGetValue("dtype", out var d) ? TC.ToDType(d) : null;
        Raw("new_zeros", (i, a, kw) => R(torch.zeros(TC.ShapeArgs(a, 1), KwDType(kw) ?? TC.Unwrap(a[0]).dtype)));
        Raw("new_ones", (i, a, kw) => R(torch.ones(TC.ShapeArgs(a, 1), KwDType(kw) ?? TC.Unwrap(a[0]).dtype)));
        Raw("new_empty", (i, a, kw) => R(torch.empty(TC.ShapeArgs(a, 1), KwDType(kw) ?? TC.Unwrap(a[0]).dtype)));
        Raw("new_full", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]);
            var dt = KwDType(kw) ?? x.dtype;
            return R(torch.full(TC.ToLongs(a[1]), TC.ToDouble(a[2]), dt));
        });
        Raw("new_tensor", (i, a, kw) => R(TC.ToTensor(a[1], KwDType(kw) ?? TC.Unwrap(a[0]).dtype)));
        Def("numel", new[] { "input" }, p => new BigInteger(p.X(0).numel()));
        Def("nelement", new[] { "input" }, p => new BigInteger(p.X(0).numel()));
        Def("dim", new[] { "input" }, p => new BigInteger(p.X(0).Dimensions));
        Def("ndimension", new[] { "input" }, p => new BigInteger(p.X(0).Dimensions));
        Def("is_floating_point", new[] { "input" }, p => TC.IsFloat(p.X(0).dtype));
        Def("is_tensor", new[] { "obj" }, p => TC.IsTensor(p.Required(0)));
        Def("item", new[] { "input" }, p => TC.Item(p.X(0)));
        Def("tolist", new[] { "input" }, p => TC.ToList(p.X(0)));
        Def("numpy", new[] { "input", "force" }, p => Conv.Wrap(TC.ToNd(p.X(0))));
        Def("cpu", new[] { "input" }, p => p.Required(0));
        Def("cuda", new[] { "input" }, p => throw PyErr.RuntimeError("Torch not compiled with CUDA enabled"));
        Def("float", new[] { "input" }, p => R(p.X(0).to(ScalarType.Float32)));
        Def("double", new[] { "input" }, p => R(p.X(0).to(ScalarType.Float64)));
        Def("half", new[] { "input" }, p => R(p.X(0).to(ScalarType.Float16)));
        Def("bfloat16", new[] { "input" }, p => R(p.X(0).to(ScalarType.BFloat16)));
        Def("long", new[] { "input" }, p => R(p.X(0).to(ScalarType.Int64)));
        Def("int", new[] { "input" }, p => R(p.X(0).to(ScalarType.Int32)));
        Def("short", new[] { "input" }, p => R(p.X(0).to(ScalarType.Int16)));
        Def("char", new[] { "input" }, p => R(p.X(0).to(ScalarType.Int8)));
        Def("byte", new[] { "input" }, p => R(p.X(0).to(ScalarType.Byte)));
        Def("bool", new[] { "input" }, p => R(p.X(0).to(ScalarType.Bool)));
        Raw("to", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]);
            ScalarType? dt = null; bool copy = false;
            foreach (var o in a.Skip(1))
            {
                if (o is PyInstance { Native: ScalarType s }) dt = s;
                else if (TC.TryUnwrap(o) is { } other) dt = other.dtype;
            }
            if (kw is not null)
            {
                if (kw.TryGetValue("dtype", out var d)) dt = TC.ToDType(d);
                if (kw.TryGetValue("copy", out var c)) copy = PyOps.Truthy(i, c);
                if (kw.TryGetValue("device", out var dev) && dev is string ds && ds.StartsWith("cuda")) throw PyErr.RuntimeError("Torch not compiled with CUDA enabled");
            }
            foreach (var o in a.Skip(1)) if (o is string s2 && s2.StartsWith("cuda")) throw PyErr.RuntimeError("Torch not compiled with CUDA enabled");
            if (dt is { } t && t != x.dtype) return R(x.to(t));
            return copy ? R(x.clone()) : a[0];
        });
        Raw("type", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]);
            if (a.Length == 1) return "torch." + x.dtype switch
            {
                ScalarType.Float32 => "FloatTensor", ScalarType.Float64 => "DoubleTensor", ScalarType.Int64 => "LongTensor",
                ScalarType.Int32 => "IntTensor", ScalarType.Bool => "BoolTensor", ScalarType.Byte => "ByteTensor", ScalarType.Float16 => "HalfTensor",
                _ => "Tensor",
            };
            return R(x.to(TC.ToDType(a[1]) ?? x.dtype));
        });
        Def("type_as", new[] { "input", "other" }, p => R(p.X(0).to(p.X(1).dtype)));
        Def("size", new[] { "input", "dim" }, p => p.Has(1) ? new BigInteger(p.X(0).shape[(int)((p.L(1, 0) + p.X(0).Dimensions) % Math.Max(p.X(0).Dimensions, 1))]) : TC.SizeObj(p.X(0).shape));
        Def("stride", new[] { "input", "dim" }, p => p.Has(1) ? new BigInteger(p.X(0).stride((int)p.L(1, 0))) : new PyTuple(p.X(0).stride().Select(v => (object)new BigInteger(v)).ToArray()));
        Def("is_contiguous", new[] { "input" }, p => p.X(0).is_contiguous());
        Def("data_ptr", new[] { "input" }, p => new BigInteger((long)p.X(0).Handle));
        Def("backward", new[] { "input", "gradient", "retain_graph", "create_graph", "inputs" }, p =>
        {
            var x = p.X(0);
            if (x.numel() != 1 && !p.Has(1)) throw PyErr.RuntimeError("grad can be implicitly created only for scalar outputs");
            if (!x.requires_grad) throw PyErr.RuntimeError("element 0 of tensors does not require grad and does not have a grad_fn");
            if (p.Has(1)) x.backward(new List<Tensor> { p.X(1) }, p.Bool(2, false), p.Bool(3, false));
            else x.backward(retain_graph: p.Bool(2, false), create_graph: p.Bool(3, false));
            return PyNone.Instance;
        });
        Def("requires_grad_", new[] { "input", "requires_grad" }, p => { p.X(0).requires_grad_(p.Bool(1, true)); return p.Required(0); });
        Def("detach_", new[] { "input" }, p => { p.X(0).detach_(); return p.Required(0); });
        Def("retain_grad", new[] { "input" }, p => { p.X(0).retain_grad(); return PyNone.Instance; });
        Def("softmax_", new[] { "input", "dim" }, p => R(p.X(0).softmax(p.L(1, -1))));
        Def("multinomial", new[] { "input", "num_samples", "replacement", "generator" }, p => R(p.X(0).multinomial(p.L(1, 1), p.Bool(2, false), p.Has(3) ? Gen(p[3]!) : null)));
        Def("bernoulli", new[] { "input", "p", "generator" }, p => R(p.Has(2) ? p.X(0).bernoulli(Gen(p[2]!)) : p.X(0).bernoulli()));
        Def("one_hot", new[] { "input", "num_classes" }, p => R(torch.nn.functional.one_hot(p.X(0), p.L(1, -1))));
    }

    public static torch.Generator Gen(object o) => o is PyInstance { Native: torch.Generator g } ? g : throw PyErr.TypeError("expected a torch.Generator");

    private static void InPlace()
    {
        // each x.op_(...) computes op out of place and copies the result back into x (under the caller's grad mode),
        // so the leaf-requires-grad check and autograd versioning stay libtorch's.
        foreach (var name in new[] { "add", "sub", "mul", "div", "pow", "clamp", "clip", "floor", "ceil", "round", "abs", "neg", "sqrt", "exp", "log", "sin", "cos", "tanh",
                     "sigmoid", "relu", "masked_fill", "addcmul", "addcdiv", "lerp", "floor_divide", "remainder", "fmod", "square", "reciprocal", "sign", "trunc", "clamp_min", "clamp_max",
                     "scatter", "scatter_add", "index_select_unused", "nan_to_num", "maximum_unused" })
        {
            if (!Table.TryGetValue(name, out var op)) continue;
            Table[name + "_"] = new OpDef(name + "_", (i, a, kw) =>
            {
                var x = TC.Unwrap(a[0]);
                var r = TC.Unwrap(op.Fn(i, a, kw));
                x.copy_(r.dtype == x.dtype ? r : r.to(x.dtype));
                return a[0];
            });
        }
        Def("zero_", new[] { "input" }, p => { p.X(0).zero_(); return p.Required(0); });
        Def("fill_", new[] { "input", "value" }, p => { var x = p.X(0); x.fill_(TC.IsFloat(x.dtype) ? (Scalar)TC.ToDouble(p[1]!) : (Scalar)TC.ToLong(p[1] is double d ? (object)new BigInteger(d) : p[1]!)); return p.Required(0); });
        Def("copy_", new[] { "input", "src" }, p => { var x = p.X(0); var s = TC.Operand(x, p.Required(1)); x.copy_(s.dtype == x.dtype ? s : s.to(x.dtype)); return p.Required(0); });
        Def("uniform_", new[] { "input", "a", "b", "generator" }, p => { p.X(0).uniform_(p.Double(1, 0.0), p.Double(2, 1.0), p.Has(3) ? Gen(p[3]!) : null); return p.Required(0); });
        Def("normal_", new[] { "input", "mean", "std", "generator" }, p => { p.X(0).normal_(p.Double(1, 0.0), p.Double(2, 1.0), p.Has(3) ? Gen(p[3]!) : null); return p.Required(0); });
        Def("bernoulli_", new[] { "input", "p", "generator" }, p => { p.X(0).bernoulli_(p.Double(1, 0.5), p.Has(2) ? Gen(p[2]!) : null); return p.Required(0); });
        Def("random_", new[] { "input", "from", "to", "generator" }, p =>
        {
            var x = p.X(0);
            if (p.Has(2)) x.random_(p.L(1, 0), p.L(2, 0));
            else if (p.Has(1)) x.random_(0, p.L(1, 1));
            else if (x.dtype == ScalarType.Int64)
            {
                // random_() on int64 is random64() % 2^63. randint(long.MinValue, long.MaxValue) yields random64() reinterpreted
                // (range 2^64-1), so masking away the sign bit gives exactly the same low 63 bits.
                var r = torch.randint(long.MinValue, long.MaxValue, x.shape, ScalarType.Int64, null, false, p.Has(3) ? Gen(p[3]!) : null);
                x.copy_(r.bitwise_and(torch.tensor(long.MaxValue)));
            }
            else x.random_(0, p.L(1, 1) <= 1 ? 16777216 : p.L(1, 1));
            return p.Required(0);
        });
        Def("index_put_", new[] { "input", "indices", "values", "accumulate" }, p =>
        {
            var x = p.X(0);
            var idx = M(p.Required(1));
            x.index_put_(TC.Operand(x, p.Required(2)), idx, p.Bool(3, false));
            return p.Required(0);
        });
    }
}
