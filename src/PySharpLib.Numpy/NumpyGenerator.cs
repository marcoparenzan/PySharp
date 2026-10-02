// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Random;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

internal static partial class NumpyRandom
{
    public static readonly PyClass GeneratorClass = BuildGeneratorClass();

    private static NDSharp.Random.Generator Gen(object self) => (NDSharp.Random.Generator)((PyInstance)self).Native!;

    /// <summary>numpy seed forms: None, an int, or a sequence/array of ints.</summary>
    internal static IReadOnlyList<BigInteger>? Entropy(object? seed)
    {
        switch (seed)
        {
            case null or PyNone: return null;
            case BigInteger b: return new[] { b };
            case bool bo: return new[] { bo ? BigInteger.One : BigInteger.Zero };
            case PyList l: return l.Items.Select(x => PyOps.AsBigInt(x, "seed")).ToList();
            case PyTuple t: return t.Items.Select(x => PyOps.AsBigInt(x, "seed")).ToList();
            default:
                if (Conv.TryUnwrap(seed) is { } nd)
                    return nd.ToArray<long>().Select(x => (BigInteger)x).ToList();
                throw PyErr.TypeError("SeedSequence expects int or sequence of ints for entropy not " + PyOps.TypeName(seed));
        }
    }

    private static bool IsArrayParam(object? o) => o is PyList or PyTuple || (o is not null && Conv.TryUnwrap(o) is { Ndim: > 0 });

    private static int[]? SizeArg(object? o) => o is null or PyNone ? null : Conv.ToShape(o);

    private static object Out(NDArray a, bool scalar) => scalar ? Conv.Scalarize(a) : Conv.Wrap(a);

    private static PyClass BuildGeneratorClass()
    {
        var cls = new PyClass("Generator", new List<PyClass>());
        void Def(string name, BuiltinFn fn) => cls.Dict[name] = Native.Fn($"Generator.{name}", fn);

        Def("random", (i, a, kw) =>
        {
            var p = new Args("random", i, a.Skip(1).ToArray(), kw, "size", "dtype", "out");
            var size = SizeArg(p[0]);
            var r = Gen(a[0]).Random(size);
            if (p.DType(1) is DType dt && dt != DType.Float64) r = r.AsType(dt);
            return Out(r, size is null);
        });
        Def("uniform", (i, a, kw) =>
        {
            var p = new Args("uniform", i, a.Skip(1).ToArray(), kw, "low", "high", "size");
            var size = SizeArg(p[2]);
            if (IsArrayParam(p[0]) || IsArrayParam(p[1]))
                return Out(Gen(a[0]).Uniform(p.Has(0) ? p.ND(0) : NDArray.Scalar(0.0), p.Has(1) ? p.ND(1) : NDArray.Scalar(1.0), size), false);
            return Out(Gen(a[0]).Uniform(p.Double(0, 0.0), p.Double(1, 1.0), size), size is null);
        });
        Def("normal", (i, a, kw) =>
        {
            var p = new Args("normal", i, a.Skip(1).ToArray(), kw, "loc", "scale", "size");
            var size = SizeArg(p[2]);
            if (IsArrayParam(p[0]) || IsArrayParam(p[1]))
                return Out(Gen(a[0]).Normal(p.Has(0) ? p.ND(0) : NDArray.Scalar(0.0), p.Has(1) ? p.ND(1) : NDArray.Scalar(1.0), size), false);
            return Out(Gen(a[0]).Normal(p.Double(0, 0.0), p.Double(1, 1.0), size), size is null);
        });
        Def("standard_normal", (i, a, kw) =>
        {
            var p = new Args("standard_normal", i, a.Skip(1).ToArray(), kw, "size", "dtype", "out");
            var size = SizeArg(p[0]);
            var r = Gen(a[0]).StandardNormal(size);
            if (p.DType(1) is DType dt && dt != DType.Float64) r = r.AsType(dt);
            return Out(r, size is null);
        });
        Def("integers", (i, a, kw) =>
        {
            var p = new Args("integers", i, a.Skip(1).ToArray(), kw, "low", "high", "size", "dtype", "endpoint");
            long low = 0, high;
            if (p.Has(1)) { low = (long)PyOps.AsBigInt(p.Required(0), "low"); high = (long)PyOps.AsBigInt(p[1]!, "high"); }
            else high = (long)PyOps.AsBigInt(p.Required(0), "high");
            var size = SizeArg(p[2]);
            var r = Gen(a[0]).Integers(low, high, size, p.DType(3) ?? DType.Int64, p.Bool(4, false));
            return Out(r, size is null);
        });
        Def("choice", (i, a, kw) =>
        {
            var p = new Args("choice", i, a.Skip(1).ToArray(), kw, "a", "size", "replace", "p", "axis", "shuffle");
            if (p.Has(3)) throw PyErr.NotImplementedError("Generator.choice(p=...) is not implemented yet");
            var g = Gen(a[0]);
            NDArray? pool = p.Required(0) is BigInteger ? null : p.ND(0);
            long popSize = pool is null ? (long)(BigInteger)p.Required(0) : pool.Shape[0];
            var size = SizeArg(p[1]);
            int count = size is null ? 1 : NDArray.SizeOf(size);
            var idx = g.ChooseIndices(popSize, count, p.Bool(2, true), p.Bool(5, true));
            var idxArr = NDArray.FromArray(idx, size ?? new[] { count });
            NDArray result;
            if (pool is null) result = idxArr;
            else result = pool.Get(idxArr).Copy();
            if (size is null)
                return Conv.Result(pool is null ? idxArr.Get(0) : pool.Get((int)idx[0]));
            return Conv.Wrap(result);
        });
        Def("multivariate_normal", (i, a, kw) =>
        {
            var p = new Args("multivariate_normal", i, a.Skip(1).ToArray(), kw, "mean", "cov", "size", "check_valid", "tol", "method");
            if (p.Has(5) && (string)p[5]! != "svd") throw PyErr.NotImplementedError("multivariate_normal supports method='svd' only");
            return Conv.Wrap(Gen(a[0]).MultivariateNormal(p.ND(0), p.ND(1), SizeArg(p[2])));
        });
        Def("permutation", (i, a, kw) =>
        {
            var p = new Args("permutation", i, a.Skip(1).ToArray(), kw, "x", "axis");
            var g = Gen(a[0]);
            if (p.Required(0) is BigInteger n) return Conv.Wrap(g.Permutation((long)n));
            return Conv.Wrap(g.Permutation(p.ND(0)));
        });
        Def("shuffle", (i, a, kw) =>
        {
            var p = new Args("shuffle", i, a.Skip(1).ToArray(), kw, "x", "axis");
            Gen(a[0]).Shuffle(p.ND(0));
            return PyNone.Instance;
        });
        Def("__repr__", (_, a, _) => "Generator(PCG64)");
        return cls;
    }
}
