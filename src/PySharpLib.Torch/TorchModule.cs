// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using PySharpLib.Importing;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using TorchSharp;
using static TorchSharp.torch;
using Tensor = TorchSharp.torch.Tensor;

namespace PySharpLib.Torch;

/// <summary>Opt-in registration of <c>torch</c> (tensors, autograd, nn, optim, utils.data) over TorchSharp/libtorch.</summary>
public static class TorchRegistration
{
    public static void Register(Importer importer)
    {
        importer.RegisterBuiltin("torch._C", _ => TorchModule.Create());
        importer.RegisterBuiltin("torch._F", _ => TorchFunctional.Create());
        importer.RegisterBuiltin("torch.nn.init", _ => TorchFunctional.CreateInit());
        importer.RegisterBuiltin("torch._utils", _ => TorchSerialization.UtilsModule());
        // every embedded py/<module>.py (or <module>.pkg.py for a package's __init__) is a Python-source module
        var names = PySource.ModuleNames().ToList();
        foreach (var (name, pkgFlag) in names)
        {
            bool isPackage = pkgFlag || names.Any(n => n.Item1.StartsWith(name + ".", StringComparison.Ordinal));
            importer.RegisterSourceModule(name, PySource.Load(name), isPackage);
        }
    }
}

internal static class PySource
{
    private const string Prefix = "PySharpLib.Torch.py.";

    /// <summary>(module name, declared as a package with a .pkg.py file) for every embedded Python source.</summary>
    public static IEnumerable<(string, bool)> ModuleNames()
    {
        foreach (var r in typeof(PySource).Assembly.GetManifestResourceNames())
        {
            if (!r.StartsWith(Prefix, StringComparison.Ordinal) || !r.EndsWith(".py", StringComparison.Ordinal)) continue;
            var n = r[Prefix.Length..^3];
            bool pkg = n.EndsWith(".pkg", StringComparison.Ordinal);
            yield return (pkg ? n[..^4] : n, pkg);
        }
    }

    public static string Load(string module)
    {
        string res = "PySharpLib.Torch.py." + module + ".py";
        using var s = (typeof(PySource).Assembly.GetManifestResourceStream(res)
            ?? typeof(PySource).Assembly.GetManifestResourceStream("PySharpLib.Torch.py." + module + ".pkg.py"))
            ?? throw new InvalidOperationException("missing embedded Python source " + res);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}

internal static class TorchModule
{
    private static PyModule? _m;

    private static PyClass NewClass(string name, string module = "torch")
    {
        var c = new PyClass(name, new List<PyClass>());
        c.Dict["__module__"] = module;
        return c;
    }

    private static T Self<T>(object o) => o is PyInstance { Native: T t } ? t : throw PyErr.TypeError($"expected a {typeof(T).Name}");

    public static PyModule Create()
    {
        if (_m is not null) return _m;
        var m = new PyModule("torch._C");
        var dtypeCls = NewClass("dtype");
        dtypeCls.Dict["__repr__"] = new PyBuiltinFunction("__repr__", (_, a, _) => "torch." + TC.DTypeName(Self<ScalarType>(a[0])));
        dtypeCls.Dict["__str__"] = dtypeCls.Dict["__repr__"];
        TC.InitDTypes(dtypeCls);
        m.Dict["dtype"] = dtypeCls;
        foreach (var (name, t) in TC.AllDTypes) m.Dict[name] = TC.DTypeObj(t);
        m.Dict["float"] = TC.DTypeObj(ScalarType.Float32); m.Dict["double"] = TC.DTypeObj(ScalarType.Float64);
        m.Dict["half"] = TC.DTypeObj(ScalarType.Float16); m.Dict["long"] = TC.DTypeObj(ScalarType.Int64);
        m.Dict["int"] = TC.DTypeObj(ScalarType.Int32); m.Dict["short"] = TC.DTypeObj(ScalarType.Int16);
        m.Dict["cfloat"] = TC.DTypeObj(ScalarType.ComplexFloat32); m.Dict["cdouble"] = TC.DTypeObj(ScalarType.ComplexFloat64);

        var tensorCls = BuildTensorClass();
        TC.TensorClass = tensorCls;
        m.Dict["Tensor"] = tensorCls;
        m.Dict["Size"] = TSize.Class;
        var paramCls = new PyClass("Parameter", new List<PyClass> { tensorCls });
        paramCls.Dict["__module__"] = "torch.nn.parameter";
        paramCls.Dict["__new__"] = Ops.Fn("Parameter.__new__", (i, a, kw) =>
        {
            var p = new Args("Parameter", i, a.Skip(1).ToArray(), kw, "data", "requires_grad");
            var data = p.Has(0) ? TC.ToTensor(p[0]!) : torch.empty(0);
            return TC.WrapParameter(data.detach().requires_grad_(p.Bool(1, true)));
        });
        paramCls.Dict["__init__"] = new PyBuiltinFunction("__init__", (_, _, _) => PyNone.Instance);
        paramCls.Dict["__repr__"] = new PyBuiltinFunction("__repr__", (_, a, _) => "Parameter containing:\n" + TorchFormat.Repr(TC.Unwrap(a[0])));
        paramCls.Dict["__str__"] = paramCls.Dict["__repr__"];
        TC.ParameterClass = paramCls;
        m.Dict["_Parameter"] = paramCls;

        InstallFunctions(m);
        InstallCreation(m);
        InstallAutograd(m);
        InstallMisc(m);
        _m = m;
        return m;
    }

    // ------------------------------------------------------------------------------------------ Tensor class

    private static PyClass BuildTensorClass()
    {
        var c = NewClass("Tensor");
        c.Dict["__module__"] = "torch";
        void Add(string name, RawFn fn) => c.Dict[name] = Ops.Fn(name, fn);
        foreach (var (name, op) in Ops.Table) Add(name, op.Fn);

        void Bin(string dunder, string? rdunder, string op)
        {
            var f = Ops.Table[op].Fn;
            Add(dunder, (i, a, kw) => { if (!IsOperandOk(a[1])) return PyNotImplemented.Instance; var r = f(i, new[] { a[0], a[1] }, null); GradFn.Tag(r, op); return r; });
            if (rdunder is not null) Add(rdunder, (i, a, kw) => { if (!IsOperandOk(a[1])) return PyNotImplemented.Instance; var r = f(i, new[] { a[1], a[0] }, null); GradFn.Tag(r, op); return r; });
        }
        Bin("__add__", "__radd__", "add"); Bin("__sub__", "__rsub__", "sub"); Bin("__mul__", "__rmul__", "mul");
        Bin("__truediv__", "__rtruediv__", "div"); Bin("__floordiv__", "__rfloordiv__", "floor_divide"); Bin("__mod__", "__rmod__", "remainder");
        Bin("__pow__", "__rpow__", "pow"); Bin("__matmul__", "__rmatmul__", "matmul");
        Bin("__and__", "__rand__", "bitwise_and"); Bin("__or__", "__ror__", "bitwise_or"); Bin("__xor__", "__rxor__", "bitwise_xor");
        Bin("__eq__", null, "eq"); Bin("__ne__", null, "ne"); Bin("__lt__", null, "lt"); Bin("__le__", null, "le"); Bin("__gt__", null, "gt"); Bin("__ge__", null, "ge");
        void InPl(string dunder, string op) { var f = Ops.Table[op].Fn; Add(dunder, (i, a, kw) => f(i, new[] { a[0], a[1] }, null)); }
        InPl("__iadd__", "add_"); InPl("__isub__", "sub_"); InPl("__imul__", "mul_"); InPl("__itruediv__", "div_"); InPl("__ipow__", "pow_");
        InPl("__ifloordiv__", "floor_divide_"); InPl("__imod__", "remainder_");
        Add("__neg__", (i, a, kw) => TC.Wrap(TC.Unwrap(a[0]).neg()));
        Add("__pos__", (i, a, kw) => a[0]);
        Add("__abs__", (i, a, kw) => TC.Wrap(TC.Unwrap(a[0]).abs()));
        Add("__invert__", (i, a, kw) => { var x = TC.Unwrap(a[0]); return TC.Wrap(x.dtype == ScalarType.Bool ? x.logical_not() : x.bitwise_not()); });
        Add("__hash__", (i, a, kw) => new BigInteger(RuntimeHelpers.GetHashCode(((PyInstance)a[0]).Native!)));
        Add("__bool__", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]);
            if (x.numel() == 0) throw PyErr.RuntimeError("Boolean value of Tensor with no values is ambiguous");
            if (x.numel() != 1) throw PyErr.RuntimeError("Boolean value of Tensor with more than one value is ambiguous");
            return x.to(ScalarType.Float64).item<double>() != 0;
        });
        Add("__float__", (i, a, kw) => TC.ToDouble(a[0]));
        Add("__int__", (i, a, kw) => (BigInteger)Math.Truncate(TC.ToDouble(a[0])));
        Add("__index__", (i, a, kw) =>
        {
            if (!((TBox)((PyInstance)a[0]).Native!).TryAsBigInt(out var v)) throw PyErr.TypeError("only integer tensors of a single element can be converted to an index");
            return v;
        });
        Add("__len__", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]);
            if (x.Dimensions == 0) throw PyErr.TypeError("len() of a 0-d tensor");
            return new BigInteger(x.shape[0]);
        });
        Add("__iter__", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]);
            if (x.Dimensions == 0) throw PyErr.TypeError("iteration over a 0-d tensor");
            return new PyIterator(x.unbind(0).Select(t => (object)TC.Wrap(t)).GetEnumerator());
        });
        Add("__contains__", (i, a, kw) => TC.Unwrap(a[0]).eq(TC.Operand(TC.Unwrap(a[0]), a[1])).any().item<bool>());
        Add("__getitem__", (i, a, kw) =>
        {
            var r = TC.Wrap(TC.Unwrap(a[0]).index(TC.ParseIndex(a[1])));
            var parts = a[1] is PyTuple pt ? pt.Items : new[] { a[1] };
            var last = parts.LastOrDefault(o => o is not PyEllipsis) ?? PyNone.Instance;
            GradFn.Tag(r, last switch { BigInteger or bool => "select", PySlice => "slice", PyNone => "unsqueeze", _ => "index" });
            return r;
        });
        Add("__setitem__", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]);
            var idx = TC.ParseIndex(a[1]);
            var v = TC.Operand(x, a[2]);
            x.index_put_(v.dtype == x.dtype ? v : v.to(x.dtype), idx);
            return PyNone.Instance;
        });
        Add("__repr__", (i, a, kw) => TorchFormat.Repr(TC.Unwrap(a[0])));
        Add("__str__", (i, a, kw) => TorchFormat.Repr(TC.Unwrap(a[0])));
        Add("__format__", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]); string spec = a.Length > 1 ? (string)a[1] : "";
            if (spec.Length == 0 || x.numel() != 1 || x.Dimensions != 0) return TorchFormat.Repr(x);
            return i.FormatValue(TC.Item(x), spec);
        });
        Add("__array__", (i, a, kw) => Conv.Wrap(TC.ToNd(TC.Unwrap(a[0]))));
        Add("__deepcopy__", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]);
            var copy = x.detach().clone().requires_grad_(x.requires_grad);
            return ((PyInstance)a[0]).Class == TC.ParameterClass ? TC.WrapParameter(copy) : TC.Wrap(copy);
        });

        void Prop(string name, Func<Tensor, object> get, Action<Tensor, object>? set = null)
            => c.Dict[name] = new PyProperty
            {
                Getter = Ops.Fn(name, (i, a, kw) => get(TC.Unwrap(a[0]))),
                Setter = set is null ? null : Ops.Fn(name, (i, a, kw) => { set(TC.Unwrap(a[0]), a[1]); return PyNone.Instance; }),
            };
        Prop("shape", t => TC.SizeObj(t.shape));
        Prop("dtype", t => TC.DTypeObj(t.dtype));
        Prop("ndim", t => new BigInteger(t.Dimensions));
        Prop("device", t => DeviceObj());
        Prop("is_cuda", t => false);
        Prop("is_sparse", t => false);
        Prop("is_leaf", t => t.is_leaf);
        Prop("layout", t => "torch.strided");
        Prop("requires_grad", t => t.requires_grad, (t, v) => t.requires_grad_(PyOps.Truthy(TC.Interp!, v)));
        Prop("grad", t => t.grad is { } g && !g.IsInvalid ? TC.Wrap(g) : PyNone.Instance, (t, v) => { t.grad = v is PyNone ? null : TC.Unwrap(v); });
        Prop("grad_fn", t => t.requires_grad && !t.is_leaf ? GradFnObj(t) : PyNone.Instance);
        Prop("T", t => TC.Wrap(t.Dimensions <= 2 ? t.t() : t.permute(Enumerable.Range(0, (int)t.Dimensions).Reverse().Select(v => (long)v).ToArray())));
        Prop("mT", t => TC.Wrap(t.transpose(-2, -1)));
        Prop("H", t => TC.Wrap(t.t()));
        Prop("itemsize", t => new BigInteger(t.element_size()));
        Prop("nbytes", t => new BigInteger(t.element_size() * t.numel()));
        Prop("data", t => TC.Wrap(t.detach()), (t, v) => t.set_(TC.ToTensor(v)));
        Prop("real", t => TC.Wrap(t));
        return c;
    }

    private static bool IsOperandOk(object o)
        => o is bool or BigInteger or double or PyList or PyTuple || o is PyInstance { Native: IPyNumberLike } || TC.IsTensor(o) || (o is PyInstance { Native: INdArrayConvertible }) || Conv.IsNdArray(o);

    private static PyClass? _gradFnCls;
    private static object GradFnObj(Tensor t)
    {
        _gradFnCls ??= NewClass("Node");
        _gradFnCls.Dict["__repr__"] = new PyBuiltinFunction("__repr__", (_, a, _) => "<" + (string)((PyInstance)a[0]).Dict["_name"] + ">");
        _gradFnCls.Dict["name"] = new PyBuiltinFunction("name", (_, a, _) => (string)((PyInstance)a[0]).Dict["_name"]);
        var inst = new PyInstance(_gradFnCls);
        inst.Dict["_name"] = GradFn.NameOf(t);
        return inst;
    }

    private static PyClass? _deviceCls;
    private static PyInstance? _cpu;
    public static PyInstance DeviceObj()
    {
        if (_cpu is not null) return _cpu;
        _deviceCls = NewClass("device");
        _deviceCls.Dict["__repr__"] = new PyBuiltinFunction("__repr__", (_, a, _) => "device(type='cpu')");
        _deviceCls.Dict["__str__"] = new PyBuiltinFunction("__str__", (_, a, _) => "cpu");
        _deviceCls.Dict["__eq__"] = new PyBuiltinFunction("__eq__", (_, a, _) => a[1] is PyInstance { Class: var c } && c == _deviceCls || (a[1] is string s && s.StartsWith("cpu")));
        _deviceCls.Dict["__hash__"] = new PyBuiltinFunction("__hash__", (_, a, _) => BigInteger.Zero);
        _deviceCls.Dict["__new__"] = new PyBuiltinFunction("__new__", (_, a, _) =>
        {
            if (a.Length > 1 && a[1] is string s && s.StartsWith("cuda")) throw PyErr.RuntimeError("Torch not compiled with CUDA enabled");
            return _cpu!;
        });
        _deviceCls.Dict["__init__"] = new PyBuiltinFunction("__init__", (_, _, _) => PyNone.Instance);
        _cpu = new PyInstance(_deviceCls);
        _cpu.Dict["type"] = "cpu";
        _cpu.Dict["index"] = PyNone.Instance;
        return _cpu;
    }

    // ------------------------------------------------------------------------------------------ functions

    private static void InstallFunctions(PyModule m)
    {
        foreach (var (name, op) in Ops.Table) if (!m.Dict.ContainsKey(name)) m.Dict[name] = Ops.Fn(name, op.Fn);
        m.Dict["device"] = _deviceCls is null ? (DeviceObj().Class) : _deviceCls;
    }

    private sealed record Opts(ScalarType? DType, bool RequiresGrad, torch.Generator? Gen);

    private static Opts GetOpts(Dictionary<string, object>? kw)
    {
        ScalarType? dt = null; bool rg = false; torch.Generator? g = null;
        if (kw is not null)
        {
            if (kw.TryGetValue("dtype", out var d)) dt = TC.ToDType(d);
            if (kw.TryGetValue("requires_grad", out var r)) rg = r is true;
            if (kw.TryGetValue("generator", out var gg) && gg is not PyNone) g = Ops.Gen(gg);
            if (kw.TryGetValue("device", out var dev) && dev is string ds && ds.StartsWith("cuda")) throw PyErr.RuntimeError("Torch not compiled with CUDA enabled");
        }
        return new Opts(dt, rg, g);
    }

    private static long[] SizeFrom(object[] a, Dictionary<string, object>? kw, int from = 0)
    {
        if (a.Length > from) return TC.ShapeArgs(a, from);
        if (kw is not null && kw.TryGetValue("size", out var s)) return TC.ToLongs(s);
        return Array.Empty<long>();
    }

    private static Tensor Finish(Tensor t, Opts o) => o.RequiresGrad ? t.requires_grad_(true) : t;

    private static void InstallCreation(PyModule m)
    {
        void Def(string name, RawFn fn) => m.Dict[name] = Ops.Fn(name, fn);

        Def("zeros", (i, a, kw) => { var o = GetOpts(kw); return TC.Wrap(Finish(torch.zeros(SizeFrom(a, kw), o.DType), o)); });
        Def("ones", (i, a, kw) => { var o = GetOpts(kw); return TC.Wrap(Finish(torch.ones(SizeFrom(a, kw), o.DType), o)); });
        Def("empty", (i, a, kw) => { var o = GetOpts(kw); return TC.Wrap(Finish(torch.empty(SizeFrom(a, kw), o.DType), o)); });
        Def("rand", (i, a, kw) => { var o = GetOpts(kw); return TC.Wrap(Finish(torch.rand(SizeFrom(a, kw), o.DType, null, false, o.Gen), o)); });
        Def("randn", (i, a, kw) => { var o = GetOpts(kw); return TC.Wrap(Finish(torch.randn(SizeFrom(a, kw), o.DType, null, false, o.Gen), o)); });
        Def("full", (i, a, kw) =>
        {
            var p = new Args("full", i, a, kw, "size", "fill_value", "dtype", "device", "requires_grad", "generator", "layout", "pin_memory");
            var o = GetOpts(kw);
            var fv = p.Required(1);
            var dt = o.DType ?? (fv is double ? TC.DefaultFloat : fv is bool ? ScalarType.Bool : ScalarType.Int64);
            return TC.Wrap(Finish(torch.full(TC.ToLongs(p.Required(0)), fv is double d ? d : fv is bool b ? (b ? 1 : 0) : (double)(BigInteger)fv, dt), o));
        });
        void Like(string name, Func<Tensor, ScalarType, Tensor> f)
            => Def(name, (i, a, kw) => { var x = TC.Unwrap(a[0]); var o = GetOpts(kw); return TC.Wrap(Finish(f(x, o.DType ?? x.dtype), o)); });
        Like("zeros_like", (x, d) => torch.zeros(x.shape, d));
        Like("ones_like", (x, d) => torch.ones(x.shape, d));
        Like("empty_like", (x, d) => torch.empty(x.shape, d));
        Like("rand_like", (x, d) => torch.rand(x.shape, d));
        Like("randn_like", (x, d) => torch.randn(x.shape, d));
        Def("full_like", (i, a, kw) =>
        {
            var x = TC.Unwrap(a[0]); var o = GetOpts(kw);
            var fv = a.Length > 1 ? a[1] : kw!["fill_value"];
            return TC.Wrap(Finish(torch.full(x.shape, TC.ToDouble(fv), o.DType ?? x.dtype), o));
        });
        Def("arange", (i, a, kw) =>
        {
            var p = new Args("arange", i, a, kw, "start", "end", "step", "dtype", "device", "requires_grad", "layout", "pin_memory");
            var o = GetOpts(kw);
            object first = p.Required(0);
            object? second = p.Has(1) ? p[1] : null, third = p.Has(2) ? p[2] : null;
            object start = second is null ? new BigInteger(0) : first, end = second ?? first, step = third ?? new BigInteger(1);
            bool anyFloat = start is double || end is double || step is double
                || new[] { start, end, step }.Any(v => v is PyInstance { Native: TBox b } && TC.IsFloat(b.T.dtype));
            var dt = o.DType ?? (anyFloat ? TC.DefaultFloat : ScalarType.Int64);
            Scalar S(object v) => anyFloat ? (Scalar)TC.ToDouble(v) : (Scalar)TC.ToLong(v);
            return TC.Wrap(Finish(torch.arange(S(start), S(end), S(step), dt), o));
        });
        Def("linspace", (i, a, kw) =>
        {
            var p = new Args("linspace", i, a, kw, "start", "end", "steps", "dtype", "device", "requires_grad", "layout", "pin_memory");
            var o = GetOpts(kw);
            return TC.Wrap(Finish(torch.linspace(TC.ToDouble(p.Required(0)), TC.ToDouble(p.Required(1)), TC.ToLong(p.Required(2)), o.DType ?? TC.DefaultFloat), o));
        });
        Def("eye", (i, a, kw) =>
        {
            var p = new Args("eye", i, a, kw, "n", "m", "dtype", "device", "requires_grad", "layout", "pin_memory");
            var o = GetOpts(kw);
            long n = TC.ToLong(p.Required(0));
            return TC.Wrap(Finish(torch.eye(n, p.Has(1) ? TC.ToLong(p[1]!) : n, o.DType ?? TC.DefaultFloat), o));
        });
        Def("randint", (i, a, kw) =>
        {
            var o = GetOpts(kw);
            long low = 0, high; long[] size;
            var pos = a.ToList();
            if (kw is not null && kw.TryGetValue("size", out var sz)) size = TC.ToLongs(sz);
            else { size = TC.ToLongs(pos[^1]); pos.RemoveAt(pos.Count - 1); }
            if (kw is not null && kw.TryGetValue("high", out var hg)) { high = TC.ToLong(hg); if (pos.Count > 0) low = TC.ToLong(pos[0]); else if (kw.TryGetValue("low", out var lw)) low = TC.ToLong(lw); }
            else if (pos.Count == 2) { low = TC.ToLong(pos[0]); high = TC.ToLong(pos[1]); }
            else high = TC.ToLong(pos[0]);
            return TC.Wrap(Finish(torch.randint(low, high, size, o.DType ?? ScalarType.Int64, null, false, o.Gen), o));
        });
        Def("randperm", (i, a, kw) => { var o = GetOpts(kw); return TC.Wrap(torch.randperm(TC.ToLong(a[0]), o.DType ?? ScalarType.Int64, null, false, o.Gen)); });
        Def("normal", (i, a, kw) =>
        {
            var p = new Args("normal", i, a, kw, "mean", "std", "size", "generator", "dtype");
            var o = GetOpts(kw);
            if (TC.IsTensor(p.Required(0)) || TC.IsTensor(p[1]))
            {
                var mean = TC.ToTensor(p.Required(0)); var std = p.Has(1) ? TC.Operand(mean, p[1]!) : torch.ones_like(mean);
                if (mean.numel() == 1 && std.numel() > 1) mean = mean.expand(std.shape);
                return TC.Wrap(torch.normal(mean, std, o.Gen));
            }
            return TC.Wrap(torch.randn(TC.ToLongs(p.Required(2)), o.DType, null, false, o.Gen) * p.Double(1, 1.0) + p.Double(0, 0.0));
        });
        Def("tensor", (i, a, kw) =>
        {
            var p = new Args("tensor", i, a, kw, "data", "dtype", "device", "requires_grad", "pin_memory");
            var o = GetOpts(kw);
            var t = TC.ToTensor(p.Required(0), o.DType);
            // a tensor given as data is copied (torch.tensor never aliases)
            if (TC.IsTensor(p[0])) t = t.detach().clone();
            return TC.Wrap(Finish(t, new Opts(null, p.Bool(3, false), null)));
        });
        Def("as_tensor", (i, a, kw) => TC.Wrap(TC.ToTensor(a[0], GetOpts(kw).DType)));
        Def("asarray", (i, a, kw) => TC.Wrap(TC.ToTensor(a[0], GetOpts(kw).DType)));
        Def("from_numpy", (i, a, kw) => TC.Wrap(TC.ToTensor(a[0])));
        Def("scalar_tensor", (i, a, kw) => TC.Wrap(TC.ToTensor(a[0], GetOpts(kw).DType ?? TC.DefaultFloat)));
        Def("manual_seed", (i, a, kw) => { var g = torch.manual_seed(TC.ToLong(a[0])); return _defaultGen ??= new PyInstance(_genCls!) { Native = g }; });
        Def("seed", (i, a, kw) => new BigInteger(0));
        Def("initial_seed", (i, a, kw) => new BigInteger(0));
        Def("get_default_dtype", (i, a, kw) => TC.DTypeObj(torch.get_default_dtype()));
        Def("set_default_dtype", (i, a, kw) => { torch.set_default_dtype(TC.ToDType(a[0])!.Value); return PyNone.Instance; });
        Def("is_tensor", (i, a, kw) => TC.IsTensor(a[0]));
        Def("is_floating_point", (i, a, kw) => TC.IsFloat(TC.Unwrap(a[0]).dtype));
        Def("result_type", (i, a, kw) => TC.DTypeObj(torch.result_type(TC.ToTensor(a[0]), TC.Operand(TC.ToTensor(a[0]), a[1]))));
        Def("set_printoptions", (i, a, kw) => { TorchFormat.SetOptions(kw); return PyNone.Instance; });
        Def("numel", (i, a, kw) => new BigInteger(TC.Unwrap(a[0]).numel()));

        var genCls = NewClass("Generator");
        genCls.Dict["__new__"] = Ops.Fn("Generator.__new__", (i, a, kw) => new PyInstance(genCls) { Native = new torch.Generator(5489) });
        genCls.Dict["__init__"] = new PyBuiltinFunction("__init__", (_, _, _) => PyNone.Instance);
        genCls.Dict["manual_seed"] = Ops.Fn("manual_seed", (i, a, kw) =>
        {
            var self = (PyInstance)a[0];
            self.Native = new torch.Generator((ulong)TC.ToLong(a[1]));
            return self;
        });
        genCls.Dict["initial_seed"] = Ops.Fn("initial_seed", (i, a, kw) => new BigInteger(0));
        m.Dict["Generator"] = genCls;
        _genCls = genCls;

        var finfo = NewClass("finfo");
        finfo.Dict["__new__"] = Ops.Fn("finfo.__new__", (i, a, kw) =>
        {
            var dt = a.Length > 1 ? TC.ToDType(a[1])!.Value : TC.DefaultFloat;
            var inst = new PyInstance(finfo);
            (double eps, double max, double tiny) = dt switch
            {
                ScalarType.Float64 => (2.220446049250313e-16, double.MaxValue, 2.2250738585072014e-308),
                ScalarType.Float16 => (0.0009765625, 65504.0, 6.103515625e-05),
                ScalarType.BFloat16 => (0.0078125, 3.3895313892515355e+38, 1.1754943508222875e-38),
                _ => (1.1920928955078125e-07, 3.4028234663852886e+38, 1.1754943508222875e-38),
            };
            inst.Dict["eps"] = eps; inst.Dict["max"] = max; inst.Dict["min"] = -max; inst.Dict["tiny"] = tiny; inst.Dict["smallest_normal"] = tiny;
            inst.Dict["bits"] = new BigInteger(dt switch { ScalarType.Float64 => 64, ScalarType.Float32 => 32, _ => 16 });
            inst.Dict["dtype"] = TC.DTypeName(dt);
            return inst;
        });
        finfo.Dict["__init__"] = new PyBuiltinFunction("__init__", (_, _, _) => PyNone.Instance);
        m.Dict["finfo"] = finfo;
        var iinfo = NewClass("iinfo");
        iinfo.Dict["__new__"] = Ops.Fn("iinfo.__new__", (i, a, kw) =>
        {
            var dt = TC.ToDType(a[1])!.Value;
            var inst = new PyInstance(iinfo);
            (long min, long max) = dt switch { ScalarType.Int8 => (-128L, 127L), ScalarType.Byte => (0L, 255L), ScalarType.Int16 => (-32768L, 32767L), ScalarType.Int32 => ((long)int.MinValue, (long)int.MaxValue), _ => (long.MinValue, long.MaxValue) };
            inst.Dict["min"] = new BigInteger(min); inst.Dict["max"] = new BigInteger(max);
            return inst;
        });
        iinfo.Dict["__init__"] = new PyBuiltinFunction("__init__", (_, _, _) => PyNone.Instance);
        m.Dict["iinfo"] = iinfo;
    }

    private static PyClass? _genCls;
    private static PyInstance? _defaultGen;

    // ------------------------------------------------------------------------------------------ autograd modes

    private static IDisposable GradMode(bool enabled) => torch.set_grad_enabled(enabled);

    private static void InstallAutograd(PyModule m)
    {
        PyClass Mode(string name, Func<object[], bool> enabled)
        {
            var cls = NewClass(name, "torch.autograd.grad_mode");
            cls.Dict["__new__"] = Ops.Fn(name + ".__new__", (i, a, kw) => new PyInstance(cls) { Native = new Stack<IDisposable>(), });
            cls.Dict["__init__"] = Ops.Fn(name + ".__init__", (i, a, kw) =>
            {
                var self = (PyInstance)a[0];
                self.Dict["_enabled"] = enabled(a.Skip(1).ToArray());
                return PyNone.Instance;
            });
            cls.Dict["__enter__"] = Ops.Fn("__enter__", (i, a, kw) =>
            {
                var self = (PyInstance)a[0];
                ((Stack<IDisposable>)self.Native!).Push(GradMode((bool)self.Dict["_enabled"]));
                return PyNone.Instance;
            });
            cls.Dict["__exit__"] = Ops.Fn("__exit__", (i, a, kw) =>
            {
                ((Stack<IDisposable>)((PyInstance)a[0]).Native!).Pop().Dispose();
                return false;
            });
            // usable as a decorator: @torch.no_grad() / @torch.no_grad
            cls.Dict["__call__"] = Ops.Fn("__call__", (i, a, kw) =>
            {
                var self = (PyInstance)a[0]; var fn = a[1];
                bool en = (bool)self.Dict["_enabled"];
                return new PyBuiltinFunction("wrapped", (ii, aa, kk) =>
                {
                    using var _ = GradMode(en);
                    return ii.Call(fn, aa, kk);
                });
            });
            m.Dict[name] = cls;
            return cls;
        }
        Mode("no_grad", _ => false);
        Mode("enable_grad", _ => true);
        Mode("set_grad_enabled", a => a.Length == 0 || PyOps.Truthy(TC.Interp!, a[0]));
        Mode("inference_mode", a => !(a.Length == 0 || PyOps.Truthy(TC.Interp!, a[0])));
        m.Dict["is_grad_enabled"] = Ops.Fn("is_grad_enabled", (i, a, kw) => torch.is_grad_enabled());
        m.Dict["is_inference_mode_enabled"] = Ops.Fn("is_inference_mode_enabled", (i, a, kw) => false);

        m.Dict["_autograd_grad"] = Ops.Fn("grad", (i, a, kw) =>
        {
            var p = new Args("grad", i, a, kw, "outputs", "inputs", "grad_outputs", "retain_graph", "create_graph", "allow_unused");
            var outs = p.Required(0) is PyList or PyTuple ? ((IEnumerable<object>)(p[0] is PyList l ? l.Items : ((PyTuple)p[0]!).Items)).Select(TC.Unwrap).ToArray() : new[] { TC.Unwrap(p[0]!) };
            var ins = p.Required(1) is PyList or PyTuple ? ((IEnumerable<object>)(p[1] is PyList l2 ? l2.Items : ((PyTuple)p[1]!).Items)).Select(TC.Unwrap).ToArray() : new[] { TC.Unwrap(p[1]!) };
            var gos = p.Has(2) ? (p[2] is PyList or PyTuple ? ((IEnumerable<object>)(p[2] is PyList l3 ? l3.Items : ((PyTuple)p[2]!).Items)).Select(TC.Unwrap).ToArray() : new[] { TC.Unwrap(p[2]!) }) : null;
            var res = torch.autograd.grad(outs, ins, gos, p.Has(3) ? p.Bool(3, false) : p.Bool(4, false), p.Bool(4, false), p.Bool(5, false));
            return new PyTuple(res.Select(t => t is null || t.IsInvalid ? (object)PyNone.Instance : TC.Wrap(t)).ToArray());
        });
    }

    private static void InstallMisc(PyModule m)
    {
        foreach (var (name, cls) in TorchSerialization.StorageClasses) m.Dict[name] = cls;
        m.Dict["_load_zip"] = Ops.Fn("_load_zip", (i, a, kw) => TorchSerialization.LoadZip(i, (string)a[0]));
        m.Dict["_load_legacy"] = Ops.Fn("_load_legacy", (i, a, kw) => TorchSerialization.LoadLegacy(i, (string)a[0]));
        TorchVisionOps.Install(m);
        m.Dict["_get_tracing_state"] = Ops.Fn("_get_tracing_state", (i, a, kw) => PyNone.Instance);
        m.Dict["_set_size_class"] = Ops.Fn("_set_size_class", (i, a, kw) => { TC.SizeClass = a[0]; TC.Interp = i; return PyNone.Instance; });
        m.Dict["__version__"] = "2.10.0+pysharp (TorchSharp/libtorch)";
        m.Dict["strided"] = "torch.strided";
        m.Dict["pi"] = Math.PI; m.Dict["e"] = Math.E; m.Dict["inf"] = double.PositiveInfinity; m.Dict["nan"] = double.NaN;
        m.Dict["_cuda_is_available"] = Ops.Fn("is_available", (i, a, kw) => false);
        m.Dict["_get_num_threads"] = Ops.Fn("get_num_threads", (i, a, kw) => new BigInteger(torch.get_num_threads()));
        m.Dict["_set_num_threads"] = Ops.Fn("set_num_threads", (i, a, kw) => { torch.set_num_threads((int)TC.ToLong(a[0])); return PyNone.Instance; });
        m.Dict["_save_tensor"] = Ops.Fn("_save_tensor", (i, a, kw) => throw PyErr.NotImplementedError("torch.save is not supported yet"));
    }
}
