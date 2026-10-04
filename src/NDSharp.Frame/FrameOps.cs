// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;

namespace NDSharp.Frame;

/// <summary>Missing data, ordering, uniqueness and reshaping-in-place operations on columns.</summary>
public static class FrameOps
{
    // ------------------------------------------------------------------------------------------ missing data

    public static bool[] NaMask(Column c) { var r = new bool[c.Length]; for (int i = 0; i < r.Length; i++) r[i] = c.IsNa(i); return r; }

    public static Column FillNa(Column c, object? value)
    {
        var pos = Enumerable.Range(0, c.Length).Where(c.IsNa).ToArray();
        if (pos.Length == 0) return c;
        return c.WithValues(pos, new object?[] { value });
    }

    public static Column FillDirectional(Column c, bool forward, int? limit = null)
    {
        int n = c.Length;
        var src = new int[n];
        int last = -1, run = 0;
        for (int k = 0; k < n; k++)
        {
            int i = forward ? k : n - 1 - k;
            if (!c.IsNa(i)) { last = i; run = 0; src[i] = i; }
            else
            {
                run++;
                src[i] = last >= 0 && (limit is null || run <= limit) ? last : -1;
            }
        }
        var take = new int[n];
        for (int i = 0; i < n; i++) take[i] = src[i] >= 0 ? src[i] : i;
        return c.Take(take);
    }

    /// <summary>Rows kept by <c>dropna</c>: <paramref name="how"/> "any"/"all" over <paramref name="cols"/> (or at least <paramref name="thresh"/> non-NA values).</summary>
    public static int[] DropNaRows(IReadOnlyList<Column> cols, int nRows, string how, int? thresh)
    {
        var keep = new List<int>();
        for (int i = 0; i < nRows; i++)
        {
            int na = cols.Count(c => c.IsNa(i));
            int ok = cols.Count - na;
            bool drop = thresh is int t ? ok < t : how == "all" ? na == cols.Count && cols.Count > 0 : na > 0;
            if (!drop) keep.Add(i);
        }
        return keep.ToArray();
    }

    // ------------------------------------------------------------------------------------------ where / replace / clip / round

    public static Column Where(Column c, IReadOnlyList<bool> cond, object? other, bool keepWhenTrue = true)
    {
        var pos = new List<int>();
        for (int i = 0; i < c.Length; i++) if (cond[i] != keepWhenTrue) pos.Add(i);
        if (pos.Count == 0) return c;
        return c.WithValues(pos, new object?[] { other ?? double.NaN });
    }

    public static Column Replace(Column c, IReadOnlyList<(object? from, object? to)> pairs)
    {
        var cells = c.ToObjects();
        bool changed = false;
        for (int i = 0; i < cells.Length; i++)
        {
            var key = Column.Key(cells[i]);
            foreach (var (f, t) in pairs)
                if (Equals(Column.Key(f), key) && (f is null || !(f is string) == !(cells[i] is string))) { cells[i] = t; changed = true; break; }
        }
        if (!changed) return c;
        var r = Column.Infer(cells);
        return c.Kind is Kind.Int && r.Kind == Kind.Int && c.Num != DType.Int64 ? Column.FromLongs(r.Longs, c.Num!.Value) : r;
    }

    public static Column Clip(Column c, double? lo, double? hi)
    {
        if (c.Kind == Kind.Int && (lo is null || lo == Math.Floor(lo.Value)) && (hi is null || hi == Math.Floor(hi.Value)))
            return Column.FromLongs(c.Longs.Select(x => lo is double l && x < l ? (long)l : hi is double h && x > h ? (long)h : x).ToArray(), c.Num!.Value);
        if (!Reduce.IsNumeric(c) || c.Kind == Kind.Bool) throw new FrameException("clip needs a numeric column", "TypeError");
        var v = c.Kind == Kind.Int ? c.Longs.Select(x => (double)x).ToArray() : c.Doubles;
        return Column.FromDoubles(v.Select(x => double.IsNaN(x) ? x : lo is double l && x < l ? l : hi is double h && x > h ? h : x).ToArray(), c.Kind == Kind.Float ? c.Num!.Value : DType.Float64);
    }

    public static Column Round(Column c, int decimals)
    {
        if (c.Kind == Kind.Int && decimals < 0)
        {
            double f = Math.Pow(10, -decimals);
            return Column.FromLongs(c.Longs.Select(x => (long)(Math.Round(x / f, MidpointRounding.ToEven) * f)).ToArray(), c.Num!.Value);
        }
        if (c.Kind != Kind.Float) return c;
        var r = np.Round(NDArray.FromArray((double[])c.Doubles.Clone()), decimals);
        return Column.FromDoubles(r.ToArray<double>(), c.Num!.Value);
    }

    // ------------------------------------------------------------------------------------------ ordering

    /// <summary>A stable ordering of rows by several key columns (NaN placed last or first, like <c>na_position</c>).</summary>
    public static int[] SortPositions(IReadOnlyList<Column> keys, IReadOnlyList<bool> ascending, bool naLast = true)
    {
        int n = keys[0].Length;
        var idx = Enumerable.Range(0, n).ToArray();
        int Cmp(int a, int b)
        {
            for (int k = 0; k < keys.Count; k++)
            {
                var c = keys[k];
                bool na = c.IsNa(a), nb = c.IsNa(b);
                if (na || nb)
                {
                    if (na && nb) continue;
                    return na ? (naLast ? 1 : -1) : (naLast ? -1 : 1);
                }
                int r = c.Kind switch
                {
                    Kind.Int => c.LongAt(a).CompareTo(c.LongAt(b)),
                    Kind.Float => c.DoubleAt(a).CompareTo(c.DoubleAt(b)),
                    Kind.Bool => c.BoolAt(a).CompareTo(c.BoolAt(b)),
                    Kind.Str => string.CompareOrdinal(c.StrAt(a), c.StrAt(b)),
                    _ => Ops.CompareLabels(c[a], c[b]),
                };
                if (r != 0) return ascending[k] ? r : -r;
            }
            return 0;
        }
        // LINQ OrderBy is stable
        return idx.OrderBy(i => i, Comparer<int>.Create(Cmp)).ToArray();
    }

    // ------------------------------------------------------------------------------------------ uniqueness

    public static int[] UniquePositions(Column c)
    {
        var seen = new HashSet<object>();
        var r = new List<int>();
        for (int i = 0; i < c.Length; i++)
            if (seen.Add(Column.Key(c[i]) ?? Column.NaNKey)) r.Add(i);
        return r.ToArray();
    }

    /// <summary>Row keys combining several columns (for duplicated / drop_duplicates / groupby).</summary>
    public static string RowKey(IReadOnlyList<Column> cols, int i)
        => string.Join("\u0001", cols.Select(c => { var k = Column.Key(c[i]); return k is null ? "N" : (k.GetType().Name + ":" + k); }));

    public static bool[] Duplicated(IReadOnlyList<Column> cols, int n, string keep)
    {
        var r = new bool[n];
        var seen = new Dictionary<string, int>();
        if (keep == "last")
        {
            for (int i = n - 1; i >= 0; i--) { var k = RowKey(cols, i); if (seen.ContainsKey(k)) r[i] = true; else seen[k] = i; }
            return r;
        }
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < n; i++) { var k = RowKey(cols, i); counts[k] = counts.GetValueOrDefault(k) + 1; }
        for (int i = 0; i < n; i++)
        {
            var k = RowKey(cols, i);
            if (keep == "first") { if (seen.ContainsKey(k)) r[i] = true; else seen[k] = i; }
            else r[i] = counts[k] > 1; // keep=False marks every duplicate
        }
        return r;
    }

    /// <summary>Distinct values (NaN dropped unless asked) with their counts, most frequent first; ties keep first appearance.</summary>
    public static (int[] firstPos, long[] counts) ValueCounts(Column c, bool dropNa = true, bool sort = true, bool ascending = false)
    {
        var order = new List<object>();
        var first = new Dictionary<object, int>();
        var counts = new Dictionary<object, long>();
        for (int i = 0; i < c.Length; i++)
        {
            if (dropNa && c.IsNa(i)) continue;
            var k = Column.Key(c[i]) ?? Column.NaNKey;
            if (!counts.ContainsKey(k)) { counts[k] = 0; first[k] = i; order.Add(k); }
            counts[k]++;
        }
        IEnumerable<object> keys = order;
        if (sort) keys = ascending ? order.OrderBy(k => counts[k]) : order.OrderByDescending(k => counts[k]);
        var ks = keys.ToArray();
        return (ks.Select(k => first[k]).ToArray(), ks.Select(k => counts[k]).ToArray());
    }

    public static bool[] IsIn(Column c, IEnumerable<object?> values)
    {
        var set = new HashSet<object>(values.Select(v => Column.Key(v) ?? Column.NaNKey));
        var r = new bool[c.Length];
        for (int i = 0; i < r.Length; i++) r[i] = set.Contains(Column.Key(c[i]) ?? Column.NaNKey);
        return r;
    }

    // ------------------------------------------------------------------------------------------ shifting

    public static Column Shift(Column c, int periods, object? fill = null)
    {
        int n = c.Length;
        bool hasFill = fill is not null && !(fill is double d && double.IsNaN(d));
        var take = new int[n];
        var missing = new List<int>();
        for (int i = 0; i < n; i++) { int s = i - periods; if (s >= 0 && s < n) take[i] = s; else { take[i] = hasFill ? 0 : -1; missing.Add(i); } }
        if (n == 0) return c;
        var r = c.Take(take);
        if (hasFill && missing.Count > 0) r = r.WithValues(missing, new[] { fill });
        return r;
    }

    public static Column Diff(Column c, int periods)
    {
        if (!Reduce.IsNumeric(c)) throw new FrameException("unsupported operand type(s) for -", "TypeError");
        var f = c.Kind == Kind.Bool ? Column.FromLongs(c.Bools.Select(b => b ? 1L : 0L).ToArray()) : c;
        var prev = Shift(f, periods);
        return Ops.Binary(BinOp.Sub, f.Kind == Kind.Int ? Column.FromDoubles(f.Longs.Select(x => (double)x).ToArray()) : f, prev);
    }

    // ------------------------------------------------------------------------------------------ describe

    public static string PercentileLabel(double q)
    {
        string t = (q * 100).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        return t + "%";
    }

    /// <summary>The <c>describe()</c> summary of a Series: numeric (count mean std min percentiles max) or categorical (count unique top freq).</summary>
    public static Series Describe(Series s, IReadOnlyList<double>? percentiles = null)
    {
        percentiles ??= new[] { 0.25, 0.5, 0.75 };
        var c = s.Values;
        if (c.Kind is Kind.Int or Kind.Float)
        {
            var labels = new List<object?> { "count", "mean", "std", "min" };
            var vals = new List<double>
            {
                (long)Reduce.Scalar("count", c)!,
                Convert.ToDouble(Reduce.Scalar("mean", c)),
                Convert.ToDouble(Reduce.Scalar("std", c)),
                Convert.ToDouble(Reduce.Scalar("min", c)),
            };
            foreach (var q in percentiles) { labels.Add(PercentileLabel(q)); vals.Add(Reduce.Quantile(c, q)); }
            labels.Add("max"); vals.Add(Convert.ToDouble(Reduce.Scalar("max", c)));
            return new Series(Column.FromDoubles(vals.ToArray()), new Index(Column.Infer(labels)), s.Name);
        }
        var (firstPos, counts) = ValueCounts(c);
        var cells = new object?[]
        {
            (long)Reduce.Scalar("count", c)!, (long)firstPos.Length,
            firstPos.Length > 0 ? c[firstPos[0]] : (object)double.NaN, counts.Length > 0 ? (object)counts[0] : double.NaN,
        };
        return new Series(Column.FromObjects(cells), new Index(Column.Infer(new object?[] { "count", "unique", "top", "freq" })), s.Name);
    }
}
