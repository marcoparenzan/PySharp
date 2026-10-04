// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;

namespace NDSharp.Frame;

/// <summary>Column reductions with pandas' <c>nanops</c> semantics (NaN skipped by default). Float sums go through NDSharp so the pairwise summation
/// of numpy — and therefore its last-digit results — is reproduced.</summary>
public static class Reduce
{
    public static bool IsNumeric(Column c) => c.Kind is Kind.Bool or Kind.Int or Kind.Float;

    private static double PairwiseSum(double[] v) => v.Length == 0 ? 0.0 : np.Sum(NDArray.FromArray(v)).GetDouble(0);

    /// <summary>The column as doubles (NaN for missing) — numeric kinds, or objects that hold numbers.</summary>
    private static double[] AsDoubles(Column c, string op)
    {
        switch (c.Kind)
        {
            case Kind.Float: return c.Doubles;
            case Kind.Int: return c.Longs.Select(x => (double)x).ToArray();
            case Kind.Bool: return c.Bools.Select(x => x ? 1.0 : 0.0).ToArray();
            case Kind.Object:
                try { return c.Objects.Select(o => o is null ? double.NaN : Column.ToDouble(o)).ToArray(); }
                catch (InvalidCastException) { break; }
        }
        throw new FrameException($"Cannot perform reduction '{op}' with {(c.Kind == Kind.Str ? "string" : "non-numeric")} dtype", "TypeError");
    }

    private static double[] Valid(double[] v) => v.Where(x => !double.IsNaN(x)).ToArray();

    public static object? Scalar(string name, Column c, bool skipna = true, int ddof = 1, int minCount = 0)
    {
        if (c.Kind is Kind.DateTime or Kind.Timedelta)
        {
            bool isDt = c.Kind == Kind.DateTime;
            object Wrap(long ticks) => isDt ? new Ts(ticks, c.Unit) : new Td(ticks, c.Unit);
            var valid = c.Ticks.Where(t => t != DateTimeCore.NaT).ToArray();
            switch (name)
            {
                case "count": return (long)valid.Length;
                case "nunique": return (long)valid.Distinct().Count();
                case "min": case "max":
                    if (valid.Length < c.Length && !skipna || valid.Length == 0) return Wrap(DateTimeCore.NaT);
                    return Wrap(name == "min" ? valid.Min() : valid.Max());
                case "mean": case "median": case "sum" when !isDt: case "std" when !isDt:
                {
                    if (valid.Length < c.Length && !skipna || valid.Length == 0) return Wrap(DateTimeCore.NaT);
                    if (name == "sum") return Wrap(valid.Aggregate(0L, (x, y) => x + y));
                    if (name == "mean") return Wrap((long)((System.Numerics.BigInteger)valid.Aggregate(System.Numerics.BigInteger.Zero, (x, y) => x + y) / valid.Length));
                    if (name == "median") { var sorted = valid.OrderBy(x => x).ToArray(); return Wrap(sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2); }
                    var asDouble = Column.FromDoubles(valid.Select(x => (double)x).ToArray());
                    return new Td((long)Convert.ToDouble(Scalar("std", asDouble, true, ddof)), c.Unit);
                }
                default: throw new FrameException($"'{(isDt ? "DatetimeArray" : "TimedeltaArray")}' with dtype {c.DTypeName} does not support reduction '{name}'", "TypeError");
            }
        }
        if (c.Kind == Kind.Category)
        {
            switch (name)
            {
                case "count": case "nunique": return Scalar(name, c.Decategorized(), skipna, ddof, minCount);
                case "min": case "max":
                {
                    if (!c.Ordered) throw new FrameException($"Categorical is not ordered for operation {name}\nyou can use .as_ordered() to change the Categorical to an ordered one\n", "TypeError");
                    var codes = c.Codes.Where(x => x >= 0).ToArray();
                    if (codes.Length < c.Length && !skipna) return double.NaN;
                    return codes.Length == 0 ? double.NaN : c.Categories[name == "min" ? codes.Min() : codes.Max()];
                }
                default: throw new FrameException($"Categorical cannot perform the reduction '{name}'", "TypeError");
            }
        }
        int n = c.Length;
        switch (name)
        {
            case "count": { int k = 0; for (int i = 0; i < n; i++) if (!c.IsNa(i)) k++; return (long)k; }
            case "nunique": return (long)Enumerable.Range(0, n).Where(i => !c.IsNa(i)).Select(i => Column.Key(c[i])!).Distinct().Count();
            case "any": case "all":
            {
                bool any = false, all = true;
                for (int i = 0; i < n; i++)
                {
                    if (c.IsNa(i)) { if (!skipna) { any = true; } continue; }
                    bool t = c.Kind switch { Kind.Bool => c.BoolAt(i), Kind.Int => c.LongAt(i) != 0, Kind.Float => c.DoubleAt(i) != 0, Kind.Str => c.StrAt(i)!.Length > 0, _ => c[i] is not (null or false or 0L or 0.0) };
                    any |= t; all &= t;
                }
                return name == "any" ? any : all;
            }
            case "min": case "max": return Extreme(c, name == "max", skipna);
            case "sum":
                switch (c.Kind)
                {
                    case Kind.Int: return n < minCount ? (object)double.NaN : c.Longs.Aggregate(0L, (a, b) => unchecked(a + b));
                    case Kind.Bool: return (long)c.Bools.Count(b => b);
                    case Kind.Str:
                    {
                        if (!skipna && Enumerable.Range(0, n).Any(c.IsNa)) return double.NaN;
                        return string.Concat(c.Strings.Where(s => s is not null));
                    }
                    default:
                    {
                        var v = AsDoubles(c, "sum");
                        int valid = v.Count(x => !double.IsNaN(x));
                        if (!skipna) return PairwiseSum(v);
                        if (valid < minCount) return double.NaN;
                        return PairwiseSum(v.Select(x => double.IsNaN(x) ? 0.0 : x).ToArray());
                    }
                }
            case "prod":
            {
                if (c.Kind == Kind.Int) return c.Longs.Aggregate(1L, (a, b) => unchecked(a * b));
                var v = AsDoubles(c, "prod");
                double p = 1.0;
                foreach (var x in v) { if (double.IsNaN(x)) { if (skipna) continue; return double.NaN; } p *= x; }
                return p;
            }
            case "mean": case "median": case "std": case "var":
            {
                var all = AsDoubles(c, name);
                if (!skipna && all.Any(double.IsNaN)) return double.NaN;
                var v = Valid(all);
                int cnt = v.Length;
                if (cnt == 0) return double.NaN;
                if (name == "mean") return PairwiseSum(v) / cnt;
                if (name == "median") return np.Median(NDArray.FromArray(v)).GetDouble(0);
                if (cnt - ddof <= 0) return double.NaN;
                double avg = PairwiseSum(v) / cnt;
                double var = PairwiseSum(v.Select(x => (avg - x) * (avg - x)).ToArray()) / (cnt - ddof);
                return name == "var" ? var : Math.Sqrt(var);
            }
            default: throw new FrameException($"unknown reduction '{name}'");
        }
    }

    private static object? Extreme(Column c, bool max, bool skipna)
    {
        int n = c.Length;
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (c.IsNa(i)) { if (!skipna) return c.Kind == Kind.Str ? double.NaN : double.NaN; continue; }
            if (best < 0) { best = i; continue; }
            int cmp = CompareCells(c, i, best);
            if (max ? cmp > 0 : cmp < 0) best = i;
        }
        if (best < 0) return double.NaN;
        return c[best];
    }

    private static int CompareCells(Column c, int i, int j) => c.Kind switch
    {
        Kind.DateTime or Kind.Timedelta => c.Ticks[i].CompareTo(c.Ticks[j]),
        Kind.Category => c.Codes[i].CompareTo(c.Codes[j]),
        Kind.Int => c.LongAt(i).CompareTo(c.LongAt(j)),
        Kind.Float => c.DoubleAt(i).CompareTo(c.DoubleAt(j)),
        Kind.Bool => c.BoolAt(i).CompareTo(c.BoolAt(j)),
        Kind.Str => string.CompareOrdinal(c.StrAt(i), c.StrAt(j)),
        _ => Ops.CompareLabels(c[i], c[j]),
    };

    /// <summary>Position of the first minimum / maximum, or -1 when there is none.</summary>
    public static int ArgExtreme(Column c, bool max, bool skipna = true)
    {
        int best = -1;
        for (int i = 0; i < c.Length; i++)
        {
            if (c.IsNa(i)) { if (!skipna) return -1; continue; }
            if (best < 0 || (max ? CompareCells(c, i, best) > 0 : CompareCells(c, i, best) < 0)) best = i;
        }
        return best;
    }

    /// <summary>Linear-interpolated quantile of datetime/timedelta ticks, as the same kind of value.</summary>
    public static object QuantileTime(Column c, double q)
    {
        var valid = c.Ticks.Where(t => t != DateTimeCore.NaT).OrderBy(t => t).Select(t => (double)t).ToArray();
        long result = DateTimeCore.NaT;
        if (valid.Length > 0)
        {
            double pos = (valid.Length - 1) * q;
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            result = (long)(valid[lo] + (valid[hi] - valid[lo]) * (pos - lo));
        }
        return c.Kind == Kind.DateTime ? new Ts(result, c.Unit) : new Td(result, c.Unit);
    }

    public static double Quantile(Column c, double q, bool skipna = true)
    {
        if (q < 0 || q > 1) throw new FrameException("percentiles should all be in the interval [0, 1]");
        var all = AsDoubles(c, "quantile");
        if (!skipna && all.Any(double.IsNaN)) return double.NaN;
        var v = Valid(all);
        if (v.Length == 0) return double.NaN;
        return np.Percentile(NDArray.FromArray(v), NDArray.Scalar(q), null, false, true).GetDouble(0);
    }

    public static Column Cumulative(string name, Column c, bool skipna = true)
    {
        int n = c.Length;
        if (c.Kind is Kind.DateTime or Kind.Timedelta)
        {
            bool ok = name is "cummax" or "cummin" || c.Kind == Kind.Timedelta && name == "cumsum";
            if (!ok) throw new FrameException($"Cannot perform reduction '{name}' with non-numeric dtype", "TypeError");
            var tk = new long[n];
            long acc = 0; bool have = false, dead = false;
            for (int i = 0; i < n; i++)
            {
                long tv = c.Ticks[i];
                if (tv == DateTimeCore.NaT) { tk[i] = DateTimeCore.NaT; if (!skipna) dead = true; continue; }
                if (dead) { tk[i] = DateTimeCore.NaT; continue; }
                acc = !have ? tv : name switch { "cumsum" => acc + tv, "cummax" => Math.Max(acc, tv), _ => Math.Min(acc, tv) };
                have = true; tk[i] = acc;
            }
            return c.Kind == Kind.DateTime ? Column.FromDateTime(tk, c.Unit) : Column.FromTimedelta(tk, c.Unit);
        }
        if (c.Kind is Kind.Int or Kind.Bool && name is "cumsum" or "cumprod" or "cummax" or "cummin")
        {
            var src = c.Kind == Kind.Bool ? c.Bools.Select(b => b ? 1L : 0L).ToArray() : c.Longs;
            var r = new long[n];
            long acc = 0;
            for (int i = 0; i < n; i++)
            {
                acc = i == 0 ? src[0] : name switch { "cumsum" => acc + src[i], "cumprod" => acc * src[i], "cummax" => Math.Max(acc, src[i]), _ => Math.Min(acc, src[i]) };
                r[i] = acc;
            }
            return Column.FromLongs(r, c.Kind == Kind.Int ? c.Num!.Value : DType.Int64);
        }
        var v = AsDoubles(c, name);
        var res = new double[n];
        double a = 0; bool started = false, poisoned = false;
        for (int i = 0; i < n; i++)
        {
            if (poisoned) { res[i] = double.NaN; continue; }
            if (double.IsNaN(v[i])) { res[i] = double.NaN; if (!skipna) poisoned = true; continue; }
            if (!started) { a = v[i]; started = true; }
            else a = name switch { "cumsum" => a + v[i], "cumprod" => a * v[i], "cummax" => Math.Max(a, v[i]), _ => Math.Min(a, v[i]) };
            res[i] = a;
        }
        return Column.FromDoubles(res, c.Num ?? DType.Float64);
    }
}
