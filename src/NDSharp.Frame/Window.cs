// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;

namespace NDSharp.Frame;

/// <summary>Rolling / expanding window reductions, ported from pandas' <c>_libs/window/aggregations</c> (Kahan-compensated running sums, online variance),
/// so results equal pandas' bit for bit.</summary>
public static class Window
{
    /// <summary>Window bounds [start, end) of every row for a fixed-size (rolling) or growing (expanding) window.</summary>
    public static (int[] start, int[] end) Bounds(int n, int window, bool center, bool expanding)
    {
        var start = new int[n]; var end = new int[n];
        int offset = center ? (window - 1) / 2 : 0;
        for (int i = 0; i < n; i++)
        {
            int e = i + 1 + offset;
            int s = expanding ? 0 : e - window;
            end[i] = Math.Clamp(e, 0, n);
            start[i] = Math.Clamp(s, 0, n);
            if (expanding) end[i] = Math.Clamp(i + 1, 0, n);
        }
        return (start, end);
    }

    private static double[] Doubles(Column c)
    {
        return c.Kind switch
        {
            Kind.Float => c.Doubles,
            Kind.Int => c.Longs.Select(x => (double)x).ToArray(),
            Kind.Bool => c.Bools.Select(b => b ? 1.0 : 0.0).ToArray(),
            _ => throw new FrameException("rolling needs numeric data", "TypeError"),
        };
    }

    public static Column Apply(string name, Column col, int window, int minPeriods, bool center, bool expanding, int ddof = 1)
    {
        var (start, end) = Bounds(col.Length, window, center, expanding);
        return ApplyBounds(name, col, start, end, minPeriods, ddof);
    }

    /// <summary>Window bounds of a time-offset window (<c>rolling('3D')</c>): rows whose timestamp lies within <paramref name="windowTicks"/> of the current one; the index must be increasing.</summary>
    public static (int[] start, int[] end) TimeBounds(long[] ticks, long windowTicks, string closed = "right")
    {
        int n = ticks.Length;
        for (int i = 1; i < n; i++) if (ticks[i] < ticks[i - 1]) throw new FrameException("index must be monotonic");
        var start = new int[n]; var end = new int[n];
        bool leftInclusive = closed is "both" or "left", rightInclusive = closed is "right" or "both";
        int s = 0, e = 0;
        for (int i = 0; i < n; i++)
        {
            long t = ticks[i];
            while (e < n && (rightInclusive ? ticks[e] <= t : ticks[e] < t)) e++;
            if (e <= i && rightInclusive) e = i + 1;
            while (s < n && (leftInclusive ? ticks[s] < t - windowTicks : ticks[s] <= t - windowTicks)) s++;
            start[i] = Math.Min(s, e); end[i] = e;
        }
        return (start, end);
    }

    public static Column ApplyBounds(string name, Column col, int[] start, int[] end, int minPeriods, int ddof = 1)
    {
        var v = Doubles(col);
        int n = v.Length;
        var res = new double[n];
        switch (name)
        {
            case "sum": RollSum(v, start, end, minPeriods, res); break;
            case "mean": RollMean(v, start, end, minPeriods, res); break;
            case "var": case "std":
                RollVar(v, start, end, minPeriods, ddof, res);
                if (name == "std") for (int i = 0; i < n; i++) res[i] = double.IsNaN(res[i]) ? res[i] : Math.Sqrt(res[i]);
                break;
            case "count":
                for (int i = 0; i < n; i++)
                {
                    int nobs = 0;
                    for (int j = start[i]; j < end[i]; j++) if (!double.IsNaN(v[j])) nobs++;
                    res[i] = nobs >= minPeriods ? nobs : double.NaN;
                }
                break;
            case "min": case "max":
                for (int i = 0; i < n; i++)
                {
                    int nobs = 0; double best = double.NaN;
                    for (int j = start[i]; j < end[i]; j++)
                    {
                        if (double.IsNaN(v[j])) continue;
                        nobs++;
                        if (double.IsNaN(best) || (name == "min" ? v[j] < best : v[j] > best)) best = v[j];
                    }
                    res[i] = nobs >= minPeriods ? best : double.NaN;
                }
                break;
            case "median":
                for (int i = 0; i < n; i++)
                {
                    var w = new List<double>();
                    for (int j = start[i]; j < end[i]; j++) if (!double.IsNaN(v[j])) w.Add(v[j]);
                    if (w.Count < minPeriods || w.Count == 0) { res[i] = double.NaN; continue; }
                    w.Sort();
                    res[i] = w.Count % 2 == 1 ? w[w.Count / 2] : (w[w.Count / 2 - 1] + w[w.Count / 2]) / 2.0;
                }
                break;
            default: throw new FrameException($"unknown window function '{name}'");
        }
        return Column.FromDoubles(res);
    }

    private static bool NewSetup(int i, int[] start, int[] end) => i == 0 || start[i] >= end[i - 1];

    private static void RollSum(double[] v, int[] start, int[] end, int minp, double[] output)
    {
        double sumX = 0, compAdd = 0, compRem = 0, prev = 0; int nobs = 0, same = 0;
        for (int i = 0; i < v.Length; i++)
        {
            int s = start[i], e = end[i];
            if (NewSetup(i, start, end))
            {
                sumX = 0; compAdd = 0; compRem = 0; nobs = 0; same = 0; prev = double.NaN;
                for (int j = s; j < e; j++) AddSum(v[j], ref nobs, ref sumX, ref compAdd, ref same, ref prev);
            }
            else
            {
                for (int j = start[i - 1]; j < s; j++) RemoveSum(v[j], ref nobs, ref sumX, ref compRem);
                for (int j = end[i - 1]; j < e; j++) AddSum(v[j], ref nobs, ref sumX, ref compAdd, ref same, ref prev);
            }
            output[i] = nobs == 0 && minp == 0 ? 0 : nobs >= minp ? (same >= nobs ? prev * nobs : sumX) : double.NaN;
        }
    }

    private static void AddSum(double val, ref int nobs, ref double sumX, ref double comp, ref int same, ref double prev)
    {
        if (double.IsNaN(val)) return;
        nobs++;
        double y = val - comp, t = sumX + y;
        comp = t - sumX - y; sumX = t;
        if (val == prev) same++; else same = 1;
        prev = val;
    }

    private static void RemoveSum(double val, ref int nobs, ref double sumX, ref double comp)
    {
        if (double.IsNaN(val)) return;
        nobs--;
        double y = -val - comp, t = sumX + y;
        comp = t - sumX - y; sumX = t;
    }

    private static void RollMean(double[] v, int[] start, int[] end, int minp, double[] output)
    {
        double sumX = 0, compAdd = 0, compRem = 0, prev = 0; int nobs = 0, neg = 0, same = 0;
        for (int i = 0; i < v.Length; i++)
        {
            int s = start[i], e = end[i];
            if (NewSetup(i, start, end))
            {
                sumX = 0; compAdd = 0; compRem = 0; nobs = 0; neg = 0; same = 0; prev = double.NaN;
                for (int j = s; j < e; j++) AddMean(v[j], ref nobs, ref sumX, ref neg, ref compAdd, ref same, ref prev);
            }
            else
            {
                for (int j = start[i - 1]; j < s; j++) RemoveMean(v[j], ref nobs, ref sumX, ref neg, ref compRem);
                for (int j = end[i - 1]; j < e; j++) AddMean(v[j], ref nobs, ref sumX, ref neg, ref compAdd, ref same, ref prev);
            }
            if (nobs >= minp && nobs > 0)
            {
                double r = sumX / nobs;
                if (same >= nobs) r = prev;
                else if (neg == 0 && r < 0) r = 0;
                else if (neg == nobs && r > 0) r = 0;
                output[i] = r;
            }
            else output[i] = double.NaN;
        }
    }

    private static void AddMean(double val, ref int nobs, ref double sumX, ref int neg, ref double comp, ref int same, ref double prev)
    {
        if (double.IsNaN(val)) return;
        nobs++;
        double y = val - comp, t = sumX + y;
        comp = t - sumX - y; sumX = t;
        if (double.IsNegative(val)) neg++;
        if (val == prev) same++; else same = 1;
        prev = val;
    }

    private static void RemoveMean(double val, ref int nobs, ref double sumX, ref int neg, ref double comp)
    {
        if (double.IsNaN(val)) return;
        nobs--;
        double y = -val - comp, t = sumX + y;
        comp = t - sumX - y; sumX = t;
        if (double.IsNegative(val)) neg--;
    }

    private static void RollVar(double[] v, int[] start, int[] end, int minp, int ddof, double[] output)
    {
        double mean = 0, ssqdm = 0, comp = 0, prev = 0; int nobs = 0, same = 0;
        double compRem = 0;
        for (int i = 0; i < v.Length; i++)
        {
            int s = start[i], e = end[i];
            if (NewSetup(i, start, end))
            {
                mean = 0; ssqdm = 0; comp = 0; compRem = 0; nobs = 0; same = 0; prev = double.NaN;
                for (int j = s; j < e; j++) AddVar(v[j], ref nobs, ref mean, ref ssqdm, ref comp, ref same, ref prev);
            }
            else
            {
                for (int j = start[i - 1]; j < s; j++) RemoveVar(v[j], ref nobs, ref mean, ref ssqdm, ref compRem);
                for (int j = end[i - 1]; j < e; j++) AddVar(v[j], ref nobs, ref mean, ref ssqdm, ref comp, ref same, ref prev);
            }
            if (nobs >= minp && nobs > ddof)
            {
                double r;
                if (nobs == 1) r = 0;
                else
                {
                    r = ssqdm / (nobs - ddof);
                    if (r < 0) r = 0;
                    if (same >= nobs) r = 0;
                }
                output[i] = r;
            }
            else output[i] = double.NaN;
        }
    }

    private static void AddVar(double val, ref int nobs, ref double mean, ref double ssqdm, ref double comp, ref int same, ref double prev)
    {
        if (double.IsNaN(val)) return;
        if (val == prev) same++; else same = 1;
        prev = val;
        nobs++;
        double prevMean = mean - comp;
        double y = val - comp, t = y - mean;
        comp = t + mean - y;
        double delta = t;
        mean = nobs > 0 ? mean + delta / nobs : 0;
        ssqdm += (val - prevMean) * (val - mean);
    }

    private static void RemoveVar(double val, ref int nobs, ref double mean, ref double ssqdm, ref double comp)
    {
        if (double.IsNaN(val)) return;
        nobs--;
        if (nobs > 0)
        {
            double prevMean = mean - comp;
            double y = val - comp, t = y - mean;
            comp = t + mean - y;
            double delta = t;
            mean -= delta / nobs;
            ssqdm -= (val - prevMean) * (val - mean);
        }
        else { mean = 0; ssqdm = 0; }
    }

    // ------------------------------------------------------------------------------------------ exponentially weighted

    /// <summary>pandas' centre of mass from exactly one of com / span / halflife / alpha, then alpha = 1 / (1 + com) (same operation order, so alpha is bit-identical).</summary>
    public static double Alpha(double? com, double? span, double? halflife, double? alpha)
    {
        int given = (com is null ? 0 : 1) + (span is null ? 0 : 1) + (halflife is null ? 0 : 1) + (alpha is null ? 0 : 1);
        if (given != 1) throw new FrameException("Must pass one of com, span, halflife, or alpha");
        if (alpha is double a)
        {
            if (a <= 0 || a > 1) throw new FrameException("alpha must satisfy: 0 < alpha <= 1");
            return a;
        }
        double c;
        if (com is double cm) { if (cm < 0) throw new FrameException("com must satisfy: com >= 0"); c = cm; }
        else if (span is double sp) { if (sp < 1) throw new FrameException("span must satisfy: span >= 1"); c = (sp - 1) / 2.0; }
        else
        {
            double h = halflife!.Value;
            if (h <= 0) throw new FrameException("halflife must satisfy: halflife > 0");
            double decay = 1 - Math.Exp(Math.Log(0.5) / h);
            c = 1 / decay - 1;
        }
        return 1.0 / (1.0 + c);
    }

    public static Column Ewm(string name, Column col, double alpha, int minPeriods, bool adjust, bool ignoreNa, bool bias = false)
    {
        var v = Doubles(col);
        int n = v.Length;
        int minp = Math.Max(minPeriods, 1);
        double oldWtFactor = 1.0 - alpha, newWt = adjust ? 1.0 : alpha;
        var output = new double[n];
        if (n == 0) return Column.FromDoubles(output);
        if (name == "mean")
        {
            double weighted = v[0];
            bool isObs = !double.IsNaN(weighted);
            int nobs = isObs ? 1 : 0;
            output[0] = nobs >= minp ? weighted : double.NaN;
            double oldWt = 1.0;
            for (int i = 1; i < n; i++)
            {
                double cur = v[i];
                isObs = !double.IsNaN(cur);
                if (isObs) nobs++;
                if (!double.IsNaN(weighted))
                {
                    if (isObs || !ignoreNa)
                    {
                        oldWt *= oldWtFactor;
                        if (isObs)
                        {
                            if (weighted != cur)
                            {
                                weighted = oldWt * weighted + newWt * cur;
                                weighted /= oldWt + newWt;
                            }
                            if (adjust) oldWt += newWt; else oldWt = 1.0;
                        }
                    }
                }
                else if (isObs) weighted = cur;
                output[i] = nobs >= minp ? weighted : double.NaN;
            }
            return Column.FromDoubles(output);
        }
        // var / std: exponentially weighted covariance of the series with itself
        {
            double meanX = v[0], meanY = v[0];
            bool isObs = !double.IsNaN(meanX);
            int nobs = isObs ? 1 : 0;
            if (!isObs) { meanX = double.NaN; meanY = double.NaN; }
            output[0] = nobs >= minp ? (bias ? 0.0 : double.NaN) : double.NaN;
            double cov = 0, sumWt = 1, sumWt2 = 1, oldWt = 1;
            for (int i = 1; i < n; i++)
            {
                double curX = v[i], curY = v[i];
                isObs = !double.IsNaN(curX);
                if (isObs) nobs++;
                if (!double.IsNaN(meanX))
                {
                    if (isObs || !ignoreNa)
                    {
                        sumWt *= oldWtFactor;
                        sumWt2 *= oldWtFactor * oldWtFactor;
                        oldWt *= oldWtFactor;
                        if (isObs)
                        {
                            double oldMeanX = meanX, oldMeanY = meanY;
                            if (meanX != curX) meanX = (oldWt * oldMeanX + newWt * curX) / (oldWt + newWt);
                            if (meanY != curY) meanY = (oldWt * oldMeanY + newWt * curY) / (oldWt + newWt);
                            cov = (oldWt * (cov + (oldMeanX - meanX) * (oldMeanY - meanY)) + newWt * ((curX - meanX) * (curY - meanY))) / (oldWt + newWt);
                            sumWt += newWt;
                            sumWt2 += newWt * newWt;
                            oldWt += newWt;
                            if (!adjust) { sumWt /= oldWt; sumWt2 /= oldWt * oldWt; oldWt = 1.0; }
                        }
                    }
                }
                else if (isObs) { meanX = curX; meanY = curY; }
                if (nobs >= minp)
                {
                    if (!bias)
                    {
                        double numerator = sumWt * sumWt, denominator = numerator - sumWt2;
                        output[i] = denominator > 0 ? numerator / denominator * cov : double.NaN;
                    }
                    else output[i] = cov;
                }
                else output[i] = double.NaN;
            }
            if (name == "std") for (int i = 0; i < n; i++) output[i] = double.IsNaN(output[i]) ? output[i] : Math.Sqrt(output[i]);
            return Column.FromDoubles(output);
        }
    }

    // ------------------------------------------------------------------------------------------ rank

    /// <summary>Ranks of the values (NaN stays NaN). Methods: average, min, max, first, dense.</summary>
    public static Column Rank(Column c, string method, bool ascending, bool pct, string naOption = "keep")
    {
        int n = c.Length;
        var order = FrameOps.SortPositions(new[] { c }, new[] { ascending }, true);
        var valid = order.Where(i => !c.IsNa(i)).ToArray();
        var res = new double[n];
        for (int i = 0; i < n; i++) res[i] = double.NaN;
        int pos = 0;
        double dense = 0;
        while (pos < valid.Length)
        {
            int end = pos + 1;
            while (end < valid.Length && Same(c, valid[pos], valid[end])) end++;
            dense++;
            for (int k = pos; k < end; k++)
            {
                res[valid[k]] = method switch
                {
                    "average" => (pos + 1 + end) / 2.0,
                    "min" => pos + 1,
                    "max" => end,
                    "first" => k + 1,
                    "dense" => dense,
                    _ => throw new FrameException($"{method} is not a valid function for rank"),
                };
            }
            pos = end;
        }
        if (naOption is "top" or "bottom")
        {
            var nas = Enumerable.Range(0, n).Where(c.IsNa).ToArray();
            if (naOption == "top") { for (int i = 0; i < n; i++) if (!double.IsNaN(res[i])) res[i] += nas.Length; for (int k = 0; k < nas.Length; k++) res[nas[k]] = method == "average" ? (1 + nas.Length) / 2.0 : method == "first" ? k + 1 : 1; }
            else for (int k = 0; k < nas.Length; k++) res[nas[k]] = method == "average" ? valid.Length + (1 + nas.Length) / 2.0 : method == "first" ? valid.Length + k + 1 : valid.Length + 1;
        }
        if (pct)
        {
            double denom = method == "dense" ? dense : res.Count(x => !double.IsNaN(x));
            for (int i = 0; i < n; i++) if (!double.IsNaN(res[i])) res[i] /= denom;
        }
        return c.Nullable ? Column.MakeNullable(Column.FromDoubles(res), res.Select(double.IsNaN).ToArray()) : Column.FromDoubles(res);
    }

    private static bool Same(Column c, int a, int b) => c.Kind switch
    {
        Kind.Category => c.Codes[a] == c.Codes[b],
        Kind.Int => c.LongAt(a) == c.LongAt(b),
        Kind.Float => c.DoubleAt(a) == c.DoubleAt(b),
        Kind.Bool => c.BoolAt(a) == c.BoolAt(b),
        Kind.Str => c.StrAt(a) == c.StrAt(b),
        _ => Ops.CompareLabels(c[a], c[b]) == 0,
    };
}
