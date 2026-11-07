// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using MathemaSharp;
using MathemaSharp.Core;
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
        // expm1 and log1p are exact for small arguments (the old Math.Exp(x) - 1 and Math.Log(1 + x) lost every digit below 1e-16), and
        // report out-of-range arguments as CPython does.
        F1("expm1", x =>
        {
            var r = SpecialFunctions.ExpM1(x);
            if (double.IsInfinity(r) && double.IsFinite(x)) throw PyErr.OverflowError("math range error");
            return r;
        });
        F1("radians", x => x * Math.PI / 180);
        F1("degrees", x => x * 180 / Math.PI);
        // The error and gamma functions are MathemaSharp.Core.SpecialFunctions, accurate to a few ulp against libm
        // (the old local Erf was Abramowitz-Stegun 7.1.26, about 1e-7).
        F1("erf", SpecialFunctions.Erf);
        F1("erfc", SpecialFunctions.Erfc);
        F1("gamma", x =>
        {
            var r = SpecialFunctions.Gamma(x);
            if (double.IsNaN(r) && !double.IsNaN(x)) throw PyErr.ValueError("math domain error");   // a pole, or -inf
            if (double.IsInfinity(r) && double.IsFinite(x)) throw PyErr.OverflowError("math range error");
            return r;
        });
        F1("lgamma", x =>
        {
            if (double.IsFinite(x) && x <= 0 && Math.Floor(x) == x) throw PyErr.ValueError("math domain error");   // a pole
            var r = SpecialFunctions.LogAbsGamma(x);
            if (double.IsPositiveInfinity(r) && double.IsFinite(x)) throw PyErr.OverflowError("math range error");
            return r;
        });

        d["log"] = new PyBuiltinFunction("log", (_, a, _) =>
        {
            double x = PyOps.AsDouble(a[0]);
            if (x <= 0)
                throw PyErr.ValueError("math domain error");
            return a.Length > 1 ? Math.Log(x) / Math.Log(PyOps.AsDouble(a[1])) : Math.Log(x);
        });
        d["log2"] = new PyBuiltinFunction("log2", (_, a, _) => Math.Log2(PyOps.AsDouble(a[0])));
        d["log10"] = new PyBuiltinFunction("log10", (_, a, _) => Math.Log10(PyOps.AsDouble(a[0])));
        d["log1p"] = new PyBuiltinFunction("log1p", (_, a, _) =>
        {
            double x = PyOps.AsDouble(a[0]);
            if (x <= -1) throw PyErr.ValueError("math domain error");
            return SpecialFunctions.LogP1(x);
        });
        d["pow"] = new PyBuiltinFunction("pow", (_, a, _) =>
            Math.Pow(PyOps.AsDouble(a[0]), PyOps.AsDouble(a[1])));
        d["atan2"] = new PyBuiltinFunction("atan2", (_, a, _) =>
            Math.Atan2(PyOps.AsDouble(a[0]), PyOps.AsDouble(a[1])));
        // hypot of any number of coordinates, by folding MathemaSharp's two-argument hypot: no overflow or underflow in the squares
        // (the old square-root-of-the-sum-of-squares gave inf for hypot(1e200, 1e200)).
        d["hypot"] = new PyBuiltinFunction("hypot", (_, a, _) =>
        {
            double r = 0;
            foreach (var x in a) r = GenericMath.Hypot(r, PyOps.AsDouble(x));
            return r;
        });
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
}
