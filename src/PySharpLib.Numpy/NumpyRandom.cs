// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary><c>numpy.random</c>. This first version draws from <see cref="System.Random"/>: seeding
/// makes the sequence reproducible run-to-run but does NOT reproduce numpy's actual values for a
/// seed (NOTEBOOKS_PLAN.md Phase 2 replaces it with a bit-compatible MT19937/PCG64).</summary>
internal static partial class NumpyRandom
{
    private static Random _rng = new();

    public static PyModule Create()
    {
        var m = new PyModule("numpy.random");
        void Def(string name, BuiltinFn fn) => m.Dict[name] = Native.Fn(name, fn);

        m.Dict["Generator"] = GeneratorClass;
        Def("default_rng", (_, a, kw) =>
        {
            var p = new Args("default_rng", null!, a, kw, "seed");
            if (p[0] is PyInstance { Native: NDSharp.Random.Generator } existing) return existing;
            return new PyInstance(GeneratorClass) { Native = NDSharp.Random.Generator.DefaultRng(Entropy(p[0])) };
        });

        Def("seed", (_, a, _) =>
        {
            _rng = a.Length > 0 && a[0] is not PyNone ? new Random(Conv.ToInt(a[0], "seed")) : new Random();
            return PyNone.Instance;
        });

        Def("rand", (_, a, _) => Draw(a.Select(x => Conv.ToInt(x, "shape")).ToArray(), static r => r.NextDouble()));
        Def("randn", (_, a, _) => Draw(a.Select(x => Conv.ToInt(x, "shape")).ToArray(), Gaussian));
        Def("random", (_, a, kw) =>
        {
            var p = new Args("random", null!, a, kw, "size");
            return Draw(p.Has(0) ? Conv.ToShape(p[0]!) : null, static r => r.NextDouble());
        });
        m.Dict["random_sample"] = m.Dict["random"];
        m.Dict["sample"] = m.Dict["random"];

        Def("randint", (i, a, kw) =>
        {
            var p = new Args("randint", i, a, kw, "low", "high", "size", "dtype");
            long low = 0, high;
            if (p.Has(1)) { low = (long)PyOps.AsBigInt(p[0]!, "low"); high = (long)PyOps.AsBigInt(p[1]!, "high"); }
            else high = (long)PyOps.AsBigInt(p.Required(0), "low");
            if (low >= high) throw PyErr.ValueError("low >= high");
            if (!p.Has(2)) return new BigInteger(_rng.NextInt64(low, high));
            // numpy's legacy RandomState.randint defaults to the C long: 32-bit on Windows.
            var defaultDt = OperatingSystem.IsWindows() ? DType.Int32 : DType.Int64;
            var shape = Conv.ToShape(p[2]!);
            var buf = new long[NDArray.SizeOf(shape)];
            for (int j = 0; j < buf.Length; j++) buf[j] = _rng.NextInt64(low, high);
            var r = NDArray.FromArray(buf, shape);
            return Conv.Wrap(r.AsType(p.DType(3) ?? defaultDt));
        });

        Def("uniform", (i, a, kw) =>
        {
            var p = new Args("uniform", i, a, kw, "low", "high", "size");
            double lo = p.Double(0, 0.0), hi = p.Double(1, 1.0);
            return Draw(p.Has(2) ? Conv.ToShape(p[2]!) : null, r => lo + (hi - lo) * r.NextDouble());
        });
        Def("normal", (i, a, kw) =>
        {
            var p = new Args("normal", i, a, kw, "loc", "scale", "size");
            double loc = p.Double(0, 0.0), scale = p.Double(1, 1.0);
            return Draw(p.Has(2) ? Conv.ToShape(p[2]!) : null, r => loc + scale * Gaussian(r));
        });

        Def("choice", (i, a, kw) =>
        {
            var p = new Args("choice", i, a, kw, "a", "size", "replace", "p");
            if (p.Has(3)) throw PyErr.NotImplementedError("choice(p=...) is not implemented");
            NDArray pool = p.Required(0) is BigInteger n ? np.Arange(0L, (long)n) : p.ND(0);
            if (pool.Ndim != 1) throw PyErr.ValueError("a must be 1-dimensional");
            bool replace = p.Bool(2, true);
            if (!p.Has(1)) return Conv.Result(pool.Get(_rng.Next(pool.Size)));
            var shape = Conv.ToShape(p[1]!);
            int count = NDArray.SizeOf(shape);
            if (!replace && count > pool.Size)
                throw PyErr.ValueError("Cannot take a larger sample than population when 'replace=False'");
            var idx = new long[count];
            if (replace)
                for (int j = 0; j < count; j++) idx[j] = _rng.Next(pool.Size);
            else
            {
                var perm = Enumerable.Range(0, pool.Size).Select(x => (long)x).ToArray();
                for (int j = 0; j < count; j++)
                {
                    int s = j + _rng.Next(pool.Size - j);
                    (perm[j], perm[s]) = (perm[s], perm[j]);
                    idx[j] = perm[j];
                }
            }
            return Conv.Wrap(pool.Get(NDArray.FromArray(idx, count)).Copy().Reshape(shape));
        });

        Def("shuffle", (_, a, _) =>
        {
            var x = Conv.ND(a[0]);
            var perm = Enumerable.Range(0, x.Shape[0]).ToArray();
            for (int j = perm.Length - 1; j > 0; j--)
            {
                int s = _rng.Next(j + 1);
                (perm[j], perm[s]) = (perm[s], perm[j]);
            }
            var shuffled = x.Get(NDArray.FromArray(perm.Select(q => (long)q).ToArray())).Copy();
            Assign.Copy(x, shuffled, Casting.Unsafe);
            return PyNone.Instance;
        });
        Def("permutation", (_, a, _) =>
        {
            var x = a[0] is BigInteger n ? np.Arange(0L, (long)n) : Conv.ND(a[0]).Copy();
            var perm = Enumerable.Range(0, x.Shape[0]).ToArray();
            for (int j = perm.Length - 1; j > 0; j--)
            {
                int s = _rng.Next(j + 1);
                (perm[j], perm[s]) = (perm[s], perm[j]);
            }
            return Conv.Wrap(x.Get(NDArray.FromArray(perm.Select(q => (long)q).ToArray())).Copy());
        });
        return m;
    }

    private static NDArray Reshape(this NDArray a, int[] shape) => np.Reshape(a, shape);

    private static object Draw(int[]? shape, Func<Random, double> sample)
    {
        if (shape is null || shape.Length == 0 && false) return sample(_rng);
        if (shape.Length == 0) return sample(_rng);
        var buf = new double[NDArray.SizeOf(shape)];
        for (int i = 0; i < buf.Length; i++) buf[i] = sample(_rng);
        return Conv.Wrap(NDArray.FromArray(buf, shape));
    }

    /// <summary>Box-Muller transform.</summary>
    private static double Gaussian(Random rnd)
    {
        double u1 = 1.0 - rnd.NextDouble();
        double u2 = rnd.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
