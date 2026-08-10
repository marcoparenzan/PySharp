// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>random: a real (not stubbed) module-level PRNG on top of .NET's System.Random — real
/// randomness for random()/uniform()/randint()/randrange()/choice()/choices()/shuffle()/sample()/
/// getrandbits()/seed(), not bit-exact with CPython's own Mersenne Twister sequence for a given
/// seed (nothing in scope needs reproducible-across-interpreters sequences, only real randomness).
/// Found via urllib3's own `util/retry.py` (`random.random()`/`random.uniform(0, jitter)` for retry
/// backoff jitter), reachable from `import requests`. See HTTP_PLAN.md.</summary>
public static class RandomModule
{
    public static PyModule Create()
    {
        var m = new PyModule("random");
        var d = m.Dict;
        var rng = new RngState();

        d["seed"] = new PyBuiltinFunction("seed", (_, a, _) =>
        {
            rng.Rng = a.Length > 0 && a[0] is not PyNone
                ? new Random(a[0] switch
                {
                    BigInteger bi => unchecked((int)(bi % int.MaxValue)),
                    double dd => dd.GetHashCode(),
                    string s => s.GetHashCode(),
                    _ => a[0].GetHashCode(),
                })
                : new Random();
            return PyNone.Instance;
        });

        d["random"] = new PyBuiltinFunction("random", (_, _, _) => rng.Rng.NextDouble());

        d["uniform"] = new PyBuiltinFunction("uniform", (_, a, _) =>
        {
            double lo = PyOps.AsDouble(a[0]);
            double hi = PyOps.AsDouble(a[1]);
            return lo + (hi - lo) * rng.Rng.NextDouble();
        });

        d["randint"] = new PyBuiltinFunction("randint", (_, a, _) =>
        {
            long lo = (long)PyOps.AsBigInt(a[0], "a");
            long hi = (long)PyOps.AsBigInt(a[1], "b");
            if (hi < lo)
                throw PyErr.ValueError($"empty range for randint({lo}, {hi})");
            return (BigInteger)(lo + (long)(rng.Rng.NextDouble() * (hi - lo + 1)));
        });

        d["randrange"] = new PyBuiltinFunction("randrange", (_, a, _) =>
        {
            long start, stop, step;
            if (a.Length == 1)
            {
                start = 0;
                stop = (long)PyOps.AsBigInt(a[0], "stop");
                step = 1;
            }
            else
            {
                start = (long)PyOps.AsBigInt(a[0], "start");
                stop = (long)PyOps.AsBigInt(a[1], "stop");
                step = a.Length > 2 ? (long)PyOps.AsBigInt(a[2], "step") : 1;
            }
            long width = stop - start;
            long n = step > 0 ? (width + step - 1) / step : throw PyErr.ValueError("zero step for randrange()");
            if (n <= 0)
                throw PyErr.ValueError($"empty range for randrange({start}, {stop}, {step})");
            long k = (long)(rng.Rng.NextDouble() * n);
            return (BigInteger)(start + k * step);
        });

        d["choice"] = new PyBuiltinFunction("choice", (interp, a, _) =>
        {
            var items = PyOps.Iterate(interp, a[0]).ToList();
            if (items.Count == 0)
                throw PyErr.IndexError("Cannot choose from an empty sequence");
            return items[rng.Rng.Next(items.Count)];
        });

        d["choices"] = new PyBuiltinFunction("choices", (interp, a, kwargs) =>
        {
            var population = PyOps.Iterate(interp, a[0]).ToList();
            int k = kwargs is not null && kwargs.TryGetValue("k", out var kv) ? (int)PyOps.AsBigInt(kv, "k")
                : a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "k") : 1;
            var result = new List<object>(k);
            for (int i = 0; i < k; i++)
                result.Add(population[rng.Rng.Next(population.Count)]);
            return new PyList(result);
        });

        d["shuffle"] = new PyBuiltinFunction("shuffle", (_, a, _) =>
        {
            var list = (PyList)a[0];
            for (int i = list.Items.Count - 1; i > 0; i--)
            {
                int j = rng.Rng.Next(i + 1);
                (list.Items[i], list.Items[j]) = (list.Items[j], list.Items[i]);
            }
            return PyNone.Instance;
        });

        d["sample"] = new PyBuiltinFunction("sample", (interp, a, _) =>
        {
            var population = PyOps.Iterate(interp, a[0]).ToList();
            int k = (int)PyOps.AsBigInt(a[1], "k");
            if (k > population.Count)
                throw PyErr.ValueError("Sample larger than population or is negative");
            var pool = new List<object>(population);
            var result = new List<object>(k);
            for (int i = 0; i < k; i++)
            {
                int j = rng.Rng.Next(pool.Count);
                result.Add(pool[j]);
                pool.RemoveAt(j);
            }
            return new PyList(result);
        });

        d["getrandbits"] = new PyBuiltinFunction("getrandbits", (_, a, _) =>
        {
            int k = (int)PyOps.AsBigInt(a[0], "k");
            var bytes = new byte[(k + 7) / 8 + 1];
            rng.Rng.NextBytes(bytes[..^1]);
            var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
            var mask = (BigInteger.One << k) - 1;
            return value & mask;
        });

        return m;
    }

    private sealed class RngState
    {
        public Random Rng { get; set; } = new();
    }
}
