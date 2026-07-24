// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

public static class MathModule
{
    public static PyModule Create()
    {
        var m = new PyModule("math");
        var d = m.Dict;

        d["pi"] = Math.PI;
        d["e"] = Math.E;
        d["tau"] = Math.Tau;
        d["inf"] = double.PositiveInfinity;
        d["nan"] = double.NaN;

        void F1(string name, Func<double, double> fn) =>
            d[name] = new PyBuiltinFunction(name, (_, a, _) => fn(PyOps.AsDouble(a[0])));

        F1("sqrt", x => x < 0 ? throw PyErr.ValueError("math domain error") : Math.Sqrt(x));
        F1("sin", Math.Sin);
        F1("cos", Math.Cos);
        F1("tan", Math.Tan);
        F1("asin", Math.Asin);
        F1("acos", Math.Acos);
        F1("atan", Math.Atan);
        F1("sinh", Math.Sinh);
        F1("cosh", Math.Cosh);
        F1("tanh", Math.Tanh);
        F1("exp", Math.Exp);
        F1("expm1", x => Math.Exp(x) - 1);
        F1("radians", x => x * Math.PI / 180);
        F1("degrees", x => x * 180 / Math.PI);
        F1("erf", Erf);
        F1("gamma", Gamma);
        F1("lgamma", x => Math.Log(Math.Abs(Gamma(x))));

        d["log"] = new PyBuiltinFunction("log", (_, a, _) =>
        {
            double x = PyOps.AsDouble(a[0]);
            if (x <= 0)
                throw PyErr.ValueError("math domain error");
            return a.Length > 1 ? Math.Log(x) / Math.Log(PyOps.AsDouble(a[1])) : Math.Log(x);
        });
        d["log2"] = new PyBuiltinFunction("log2", (_, a, _) => Math.Log2(PyOps.AsDouble(a[0])));
        d["log10"] = new PyBuiltinFunction("log10", (_, a, _) => Math.Log10(PyOps.AsDouble(a[0])));
        d["log1p"] = new PyBuiltinFunction("log1p", (_, a, _) => Math.Log(1 + PyOps.AsDouble(a[0])));
        d["pow"] = new PyBuiltinFunction("pow", (_, a, _) =>
            Math.Pow(PyOps.AsDouble(a[0]), PyOps.AsDouble(a[1])));
        d["atan2"] = new PyBuiltinFunction("atan2", (_, a, _) =>
            Math.Atan2(PyOps.AsDouble(a[0]), PyOps.AsDouble(a[1])));
        d["hypot"] = new PyBuiltinFunction("hypot", (_, a, _) =>
            Math.Sqrt(a.Sum(x => { double v = PyOps.AsDouble(x); return v * v; })));
        d["copysign"] = new PyBuiltinFunction("copysign", (_, a, _) =>
            Math.CopySign(PyOps.AsDouble(a[0]), PyOps.AsDouble(a[1])));
        // fmod: remainder with the dividend's sign (as in C), which is the semantics of % on double in .NET
        d["fmod"] = new PyBuiltinFunction("fmod", (_, a, _) =>
            PyOps.AsDouble(a[0]) % PyOps.AsDouble(a[1]));
        d["nextafter"] = new PyBuiltinFunction("nextafter", (_, a, _) =>
        {
            double x = PyOps.AsDouble(a[0]), y = PyOps.AsDouble(a[1]);
            return y > x ? Math.BitIncrement(x) : y < x ? Math.BitDecrement(x) : y;
        });

        d["ceil"] = new PyBuiltinFunction("ceil", (_, a, _) =>
            (object)new BigInteger(Math.Ceiling(PyOps.AsDouble(a[0]))));
        d["floor"] = new PyBuiltinFunction("floor", (_, a, _) =>
            (object)new BigInteger(Math.Floor(PyOps.AsDouble(a[0]))));
        d["trunc"] = new PyBuiltinFunction("trunc", (_, a, _) =>
            (object)new BigInteger(Math.Truncate(PyOps.AsDouble(a[0]))));

        d["isnan"] = new PyBuiltinFunction("isnan", (_, a, _) => double.IsNaN(PyOps.AsDouble(a[0])));
        d["isinf"] = new PyBuiltinFunction("isinf", (_, a, _) => double.IsInfinity(PyOps.AsDouble(a[0])));
        d["isfinite"] = new PyBuiltinFunction("isfinite", (_, a, _) => double.IsFinite(PyOps.AsDouble(a[0])));

        d["fabs"] = new PyBuiltinFunction("fabs", (_, a, _) => Math.Abs(PyOps.AsDouble(a[0])));

        d["gcd"] = new PyBuiltinFunction("gcd", (_, a, _) =>
        {
            BigInteger g = 0;
            foreach (var x in a)
                g = BigInteger.GreatestCommonDivisor(g, PyOps.AsBigInt(x, "gcd"));
            return g;
        });
        d["lcm"] = new PyBuiltinFunction("lcm", (_, a, _) =>
        {
            BigInteger l = 1;
            foreach (var x in a)
            {
                var v = BigInteger.Abs(PyOps.AsBigInt(x, "lcm"));
                if (v.IsZero) return BigInteger.Zero;
                l = l / BigInteger.GreatestCommonDivisor(l, v) * v;
            }
            return l;
        });
        d["factorial"] = new PyBuiltinFunction("factorial", (_, a, _) =>
        {
            var n = PyOps.AsBigInt(a[0], "factorial");
            if (n < 0)
                throw PyErr.ValueError("factorial() not defined for negative values");
            BigInteger r = 1;
            for (BigInteger i = 2; i <= n; i++)
                r *= i;
            return r;
        });

        d["isclose"] = new PyBuiltinFunction("isclose", (_, a, kwargs) =>
        {
            double x = PyOps.AsDouble(a[0]), y = PyOps.AsDouble(a[1]);
            double relTol = kwargs is not null && kwargs.TryGetValue("rel_tol", out var rt) ? PyOps.AsDouble(rt) : 1e-9;
            double absTol = kwargs is not null && kwargs.TryGetValue("abs_tol", out var at) ? PyOps.AsDouble(at) : 0.0;
            if (x == y) return true;
            if (double.IsInfinity(x) || double.IsInfinity(y)) return false;
            return Math.Abs(x - y) <= Math.Max(relTol * Math.Max(Math.Abs(x), Math.Abs(y)), absTol);
        });

        d["frexp"] = new PyBuiltinFunction("frexp", (_, a, _) =>
        {
            double x = PyOps.AsDouble(a[0]);
            if (x == 0 || double.IsNaN(x) || double.IsInfinity(x))
                return new PyTuple(new object[] { x, BigInteger.Zero });
            int exp = (int)Math.Ceiling(Math.Log2(Math.Abs(x)));
            double mant = x / Math.Pow(2, exp);
            while (Math.Abs(mant) >= 1.0) { mant /= 2; exp++; }
            while (Math.Abs(mant) < 0.5) { mant *= 2; exp--; }
            return new PyTuple(new object[] { mant, new BigInteger(exp) });
        });
        d["ldexp"] = new PyBuiltinFunction("ldexp", (_, a, _) =>
            PyOps.AsDouble(a[0]) * Math.Pow(2, (double)PyOps.AsBigInt(a[1], "exp")));
        d["modf"] = new PyBuiltinFunction("modf", (_, a, _) =>
        {
            double x = PyOps.AsDouble(a[0]);
            double ip = Math.Truncate(x);
            return new PyTuple(new object[] { x - ip, ip });
        });

        return m;
    }

    private static double Erf(double x)
    {
        // Abramowitz & Stegun 7.1.26
        double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t
                          + 0.254829592) * t * Math.Exp(-x * x);
        return Math.Sign(x) * y;
    }

    private static double Gamma(double x)
    {
        // Lanczos approximation
        double[] g =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        };
        if (x < 0.5)
            return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1 - x));
        x -= 1;
        double a = g[0];
        double t = x + 7.5;
        for (int i = 1; i < g.Length; i++)
            a += g[i] / (x + i);
        return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * a;
    }
}
