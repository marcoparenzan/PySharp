// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;

namespace NDSharp.Frame;

/// <summary><c>pd.cut</c> and <c>pd.qcut</c>: value → interval binning, with pandas' end-point adjustment and label rounding rules.</summary>
public static class Binning
{
    private static double Around(double x, int decimals)
    {
        double f = Math.Pow(10, decimals);
        return Math.Round(x * f, MidpointRounding.ToEven) / f;
    }

    /// <summary>pandas' <c>_round_frac</c>: round to <paramref name="precision"/> significant fractional digits (more digits for numbers below 1).</summary>
    private static double RoundFrac(double x, int precision)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || x == 0) return x;
        double whole = Math.Truncate(x), frac = x - whole;
        int digits = whole == 0 ? -(int)Math.Floor(Math.Log10(Math.Abs(frac))) - 1 + precision : precision;
        return Around(x, digits);
    }

    private static int InferPrecision(int basePrecision, double[] bins)
    {
        for (int p = basePrecision; p < 20; p++)
        {
            var levels = bins.Select(b => RoundFrac(b, p)).Distinct().Count();
            if (levels == bins.Length) return p;
        }
        return basePrecision;
    }

    private static double[] Linspace(double start, double stop, int num)
    {
        var y = new double[num];
        if (num == 1) { y[0] = start; return y; }
        double step = (stop - start) / (num - 1);
        for (int i = 0; i < num; i++) y[i] = i * step + start;
        y[num - 1] = stop;
        return y;
    }

    private static string IndexText(double[] bins, bool ints)
        => Formatter.IndexRepr(new Index(ints ? Column.FromLongs(bins.Select(b => (long)b).ToArray()) : Column.FromDoubles(bins)));

    private static int SearchSorted(double[] a, double v, bool left)
    {
        int lo = 0, hi = a.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (left ? a[mid] < v : a[mid] <= v) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    public static (Column result, double[] bins) Cut(double[] x, double[]? explicitBins, bool binsAreInt, int? nbins, bool right, IReadOnlyList<object?>? labels, bool labelsFalse,
        int precision, bool includeLowest, bool dropDuplicates, bool ordered)
    {
        double[] bins;
        if (nbins is int nb)
        {
            if (nb < 1) throw new FrameException("`bins` should be a positive integer.");
            var valid = x.Where(v => !double.IsNaN(v)).ToArray();
            if (valid.Length == 0) throw new FrameException("Cannot cut empty array");
            double mn = valid.Min(), mx = valid.Max();
            if (double.IsInfinity(mn) || double.IsInfinity(mx)) throw new FrameException("cannot specify integer `bins` when input data contains infinity");
            if (mn == mx)
            {
                mn -= mn != 0 ? 0.001 * Math.Abs(mn) : 0.001;
                mx += mx != 0 ? 0.001 * Math.Abs(mx) : 0.001;
                bins = Linspace(mn, mx, nb + 1);
            }
            else
            {
                bins = Linspace(mn, mx, nb + 1);
                double adj = (mx - mn) * 0.001;
                if (right) bins[0] -= adj; else bins[^1] += adj;
            }
            binsAreInt = false;
        }
        else
        {
            bins = (double[])explicitBins!.Clone();
            if (bins.Length < 2) throw new FrameException("bins must have at least two edges");
            for (int i = 1; i < bins.Length; i++) if (bins[i] < bins[i - 1]) throw new FrameException("bins must increase monotonically.");
        }
        return ToCuts(x, bins, binsAreInt, right, labels, labelsFalse, precision, includeLowest, dropDuplicates, ordered);
    }

    public static (Column result, double[] bins) QCut(double[] x, double[] quantiles, IReadOnlyList<object?>? labels, bool labelsFalse, int precision, bool dropDuplicates)
    {
        var col = Column.FromDoubles(x);
        var bins = quantiles.Select(q => Reduce.Quantile(col, q)).ToArray();
        return ToCuts(x, bins, false, true, labels, labelsFalse, precision, true, dropDuplicates, true);
    }

    private static (Column, double[]) ToCuts(double[] x, double[] bins, bool binsAreInt, bool right, IReadOnlyList<object?>? labels, bool labelsFalse, int precision,
        bool includeLowest, bool dropDuplicates, bool ordered)
    {
        if (bins.Distinct().Count() != bins.Length)
        {
            if (!dropDuplicates)
                throw new FrameException($"Bin edges must be unique: {IndexText(bins, binsAreInt)}.\nYou can drop duplicate edges by setting the 'duplicates' kwarg");
            bins = bins.Distinct().ToArray();
        }
        int n = x.Length;
        var ids = new int[n];
        for (int i = 0; i < n; i++)
        {
            ids[i] = double.IsNaN(x[i]) ? 0 : SearchSorted(bins, x[i], right);
            if (includeLowest && !double.IsNaN(x[i]) && x[i] == bins[0]) ids[i] = 1;
        }
        var na = new bool[n];
        for (int i = 0; i < n; i++) na[i] = double.IsNaN(x[i]) || ids[i] == bins.Length || ids[i] == 0;
        if (labelsFalse)
        {
            if (na.Any(v => v)) return (Column.FromDoubles(Enumerable.Range(0, n).Select(i => na[i] ? double.NaN : ids[i] - 1).ToArray()), bins);
            return (Column.FromLongs(ids.Select(v => (long)(v - 1)).ToArray()), bins);
        }
        var codes = Enumerable.Range(0, n).Select(i => na[i] ? -1 : ids[i] - 1).ToArray();
        Column cats;
        if (labels is not null)
        {
            if (labels.Count != bins.Length - 1) throw new FrameException("Bin labels must be one fewer than the number of bin edges");
            cats = Column.Infer(labels);
            return (Column.FromCodes(codes, cats, ordered), bins);
        }
        // interval labels: edges rounded for display, the lowest edge nudged when the first bin includes it
        int prec = InferPrecision(precision, bins);
        var breaks = bins.Select(b => RoundFrac(b, prec)).ToArray();
        if (right && includeLowest) breaks[0] -= Math.Pow(10, -prec);
        string closed = right ? "right" : "left";
        bool ints = binsAreInt && breaks.All(b => b == Math.Floor(b));
        var intervals = new object?[bins.Length - 1];
        for (int i = 0; i < intervals.Length; i++)
            intervals[i] = ints ? new IntervalValue((long)breaks[i], (long)breaks[i + 1], closed) : new IntervalValue(breaks[i], breaks[i + 1], closed);
        cats = Column.FromObjects(intervals);
        return (Column.FromCodes(codes, cats, ordered), bins);
    }
}
