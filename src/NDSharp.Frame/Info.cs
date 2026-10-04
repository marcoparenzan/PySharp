// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Text;
using NDSharp;

namespace NDSharp.Frame;

/// <summary><c>DataFrame.info()</c> / <c>Series.info()</c> text and the pairwise statistics (<c>corr</c>, <c>cov</c>).</summary>
public static class Info
{
    private static string Fmt(object? o) => Formatter.ObjectStr(o);

    private static int ItemSize(Column c) => c.Kind switch
    {
        Kind.Bool or Kind.Category => 1,
        Kind.DateTime or Kind.Timedelta or Kind.Period => 8,
        Kind.Str or Kind.Object => 8,
        _ => c.Num!.Value switch
        {
            DType.Int8 or DType.UInt8 => 1, DType.Int16 or DType.UInt16 => 2, DType.Int32 or DType.UInt32 or DType.Float32 => 4, _ => 8,
        },
    };

    private static long IndexBytes(Index ix) => ix.IsRange ? 132 : (long)ix.Length * (ix.Labels.Kind is Kind.Bool ? 1 : 8);

    private static string SizeText(double bytes, bool plus)
    {
        string q = plus ? "+" : "";
        foreach (var unit in new[] { "bytes", "KB", "MB", "GB", "TB" })
        {
            if (bytes < 1024.0) return string.Format(CultureInfo.InvariantCulture, "{0:0.0}{1} {2}", bytes, q, unit);
            bytes /= 1024.0;
        }
        return string.Format(CultureInfo.InvariantCulture, "{0:0.0}{1} PB", bytes, q);
    }

    private static string IndexLine(Index ix)
    {
        if (ix.Length == 0) return (ix.IsRange ? "RangeIndex" : "Index") + ": 0 entries";
        string a = Fmt(ix.Labels[0]), b = Fmt(ix.Labels[ix.Length - 1]);
        return $"{(ix.IsRange ? "RangeIndex" : "Index")}: {ix.Length} entries, {a} to {b}";
    }

    private static string Put(string s, int width) => (s.Length > width ? s[..width] : s).PadRight(width);

    private static bool PlusFlag(IEnumerable<Column> cols, Index ix)
        => cols.Any(c => c.Kind == Kind.Object) || (!ix.IsRange && ix.Labels.Kind is Kind.Str or Kind.Object);

    public static string Describe(DataFrame d)
    {
        var sb = new StringBuilder();
        sb.Append("<class 'pandas.DataFrame'>\n");
        sb.Append(IndexLine(d.Index)).Append('\n');
        if (d.NCols == 0) { sb.Append("Empty DataFrame\n"); return sb.ToString().TrimEnd('\n') + "\n"; }
        sb.Append($"Data columns (total {d.NCols} columns):\n");
        var names = d.Columns.Items().Select(Fmt).ToList();
        var counts = d.Data.Select(c => Enumerable.Range(0, c.Length).Count(i => !c.IsNa(i)).ToString(CultureInfo.InvariantCulture)).ToList();
        var dtypes = d.Data.Select(c => c.DTypeName).ToList();
        int space = Math.Max(names.Max(n => n.Length), "Column".Length) + 2;
        int spaceNum = Math.Max(d.NCols.ToString().Length, " # ".Length) + 2;
        int spaceCount = Math.Max("Non-Null Count".Length, counts.Max(c => c.Length) + " non-null".Length) + 2;
        int spaceDtype = Math.Max("Dtype".Length, dtypes.Max(t => t.Length));
        sb.Append(Put(" # ", spaceNum)).Append(Put("Column", space)).Append(Put("Non-Null Count", spaceCount)).Append(Put("Dtype", spaceDtype)).Append('\n');
        sb.Append(Put(new string('-', 3), spaceNum)).Append(Put(new string('-', 6), space)).Append(Put(new string('-', 14), spaceCount)).Append(Put(new string('-', 5), spaceDtype)).Append('\n');
        for (int i = 0; i < d.NCols; i++)
            sb.Append(Put(" " + i, spaceNum)).Append(Put(names[i], space)).Append(Put(counts[i] + " non-null", spaceCount)).Append(Put(dtypes[i], spaceDtype)).Append('\n');
        sb.Append("dtypes: ").Append(string.Join(", ", dtypes.GroupBy(t => t).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}({g.Count()})"))).Append('\n');
        long mem = IndexBytes(d.Index) + d.Data.Sum(c => (long)c.Length * ItemSize(c) + (c.Nullable && c.Kind != Kind.Str ? c.Length : 0));
        sb.Append("memory usage: ").Append(SizeText(mem, PlusFlag(d.Data, d.Index))).Append('\n');
        return sb.ToString();
    }

    public static string Describe(Series s)
    {
        var sb = new StringBuilder();
        sb.Append("<class 'pandas.Series'>\n");
        sb.Append(IndexLine(s.Index)).Append('\n');
        sb.Append("Series name: ").Append(s.Name is null ? "None" : Fmt(s.Name)).Append('\n');
        string count = Enumerable.Range(0, s.Length).Count(i => !s.Values.IsNa(i)).ToString(CultureInfo.InvariantCulture);
        string dtype = s.DType;
        int spaceCount = Math.Max("Non-Null Count".Length, count.Length + " non-null".Length) + 2;
        int spaceDtype = Math.Max("Dtype".Length, dtype.Length);
        sb.Append(Put("Non-Null Count", spaceCount)).Append(Put("Dtype", spaceDtype)).Append('\n');
        sb.Append(Put(new string('-', 14), spaceCount)).Append(Put(new string('-', 5), spaceDtype)).Append('\n');
        sb.Append(Put(count + " non-null", spaceCount)).Append(Put(dtype, spaceDtype)).Append('\n');
        sb.Append($"dtypes: {dtype}(1)\n");
        long mem = IndexBytes(s.Index) + (long)s.Length * ItemSize(s.Values);
        sb.Append("memory usage: ").Append(SizeText(mem, PlusFlag(new[] { s.Values }, s.Index))).Append('\n');
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------ correlation

    private static double[] Dbl(Column c)
    {
        if (c.Kind == Kind.Float) return c.Doubles;
        if (c.Kind == Kind.Int) return c.Longs.Select(x => (double)x).ToArray();
        if (c.Kind == Kind.Bool) return c.Bools.Select(b => b ? 1.0 : 0.0).ToArray();
        throw new FrameException("could not convert string to float", "ValueError");
    }

    /// <summary>pandas' <c>nancorr</c> / <c>nancov</c> kernel (Welford-style running means) over the rows where both columns are present.</summary>
    public static double PairStat(Column x, Column y, bool corr, int minPeriods = 1, int ddof = 1)
    {
        var a = Dbl(x); var b = Dbl(y);
        long nobs = 0; double meanx = 0, meany = 0, ssqdmx = 0, ssqdmy = 0, covxy = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (double.IsNaN(a[i]) || double.IsNaN(b[i])) continue;
            double vx = a[i], vy = b[i];
            nobs++;
            double prevx = meanx, prevy = meany;
            meanx += 1.0 / nobs * (vx - meanx);
            meany += 1.0 / nobs * (vy - meany);
            ssqdmx += (vx - meanx) * (vx - prevx);
            ssqdmy += (vy - meany) * (vy - prevy);
            covxy += (vx - meanx) * (vy - prevy);
        }
        if (nobs < minPeriods) return double.NaN;
        if (!corr) return nobs - ddof > 0 ? covxy / (nobs - ddof) : double.NaN;
        double divisor = Math.Pow(ssqdmx * ssqdmy, 0.5);
        if (divisor == 0) return double.NaN;
        double val = covxy / divisor;
        return Math.Clamp(val, -1.0, 1.0);
    }

    /// <summary><c>Series.corr</c> goes through <c>np.corrcoef</c> on the complete pairs.</summary>
    public static double SeriesCorr(Column x, Column y, int minPeriods = 1)
    {
        var a = Dbl(x); var b = Dbl(y);
        var xs = new List<double>(); var ys = new List<double>();
        for (int i = 0; i < a.Length; i++) if (!double.IsNaN(a[i]) && !double.IsNaN(b[i])) { xs.Add(a[i]); ys.Add(b[i]); }
        if (xs.Count < minPeriods || xs.Count < 2) return double.NaN;
        // np.corrcoef: centred dot products (BLAS dot uses fused multiply-add) scaled by 1/(n-1), then divided by the standard deviations
        int n = xs.Count;
        double mx = xs.Count == 0 ? 0 : np.Sum(NDArray.FromArray(xs.ToArray())).GetDouble(0) / n;
        double my = np.Sum(NDArray.FromArray(ys.ToArray())).GetDouble(0) / n;
        double Dot(IList<double> u, double mu, IList<double> v, double mv)
        {
            double acc = 0;
            for (int i = 0; i < n; i++) acc = Math.FusedMultiplyAdd(u[i] - mu, v[i] - mv, acc);
            return acc;
        }
        double scale = 1.0 / (n - 1);
        double c00 = Dot(xs, mx, xs, mx) * scale, c01 = Dot(xs, mx, ys, my) * scale, c11 = Dot(ys, my, ys, my) * scale;
        double s0 = Math.Sqrt(c00), s1 = Math.Sqrt(c11);
        return Math.Clamp(c01 / s0 / s1, -1.0, 1.0);
    }

    public static double SeriesCov(Column x, Column y, int minPeriods = 1, int ddof = 1)
    {
        var a = Dbl(x); var b = Dbl(y);
        var xs = new List<double>(); var ys = new List<double>();
        for (int i = 0; i < a.Length; i++) if (!double.IsNaN(a[i]) && !double.IsNaN(b[i])) { xs.Add(a[i]); ys.Add(b[i]); }
        if (xs.Count < minPeriods || xs.Count < 2) return double.NaN;
        var m = np.Stack(new[] { NDArray.FromArray(xs.ToArray()), NDArray.FromArray(ys.ToArray()) }, 0);
        return np.Cov(m, null, true, false, ddof).GetDouble(1);
    }
}
