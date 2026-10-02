// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;

namespace NDSharp.Plot;

/// <summary>Tick placement and labelling: ports of matplotlib's <c>MaxNLocator</c> (what AutoLocator uses) and
/// <c>ScalarFormatter</c>, so ticks land where matplotlib puts them for the same axis length and range.
/// The "offset" (e.g. +1.234e3 on the axis corner) is not implemented.</summary>
public static class Ticks
{
    private static readonly double[] Steps = { 0.1, 0.2, 0.25, 0.5, 1, 2, 2.5, 5, 10, 20 };
    /// <summary>MaxNLocator's own default steps (contour levels use these; AutoLocator passes the coarser set above).</summary>
    private static readonly double[] FineSteps = { 0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.5, 0.6, 0.8, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10, 15 };

    /// <summary>Number of ticks that fit: matplotlib's <c>get_tick_space</c> (x: 3 font sizes per tick, y: 2), clipped to [1, 9].</summary>
    public static int TickSpace(double lengthPoints, double fontSizePt, bool horizontal)
        => (int)Math.Clamp(Math.Floor(lengthPoints / (fontSizePt * (horizontal ? 3 : 2))), 1, 9);

    /// <summary>The AutoLocator tick positions (including ones just outside the view) for [vmin, vmax].</summary>
    public static double[] Locate(double vmin, double vmax, int nbins, bool fineSteps = false)
    {
        if (vmin > vmax) (vmin, vmax) = (vmax, vmin);
        if (vmin == vmax) { vmin -= 0.05 * Math.Max(Math.Abs(vmin), 1); vmax += 0.05 * Math.Max(Math.Abs(vmax), 1); }
        nbins = Math.Max(nbins, 1);

        double dv = vmax - vmin, mean = (vmax + vmin) / 2;
        double offset = Math.Abs(mean) / dv >= 100 ? Math.CopySign(Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(mean)))), mean) : 0;
        double scale = Math.Pow(10, Math.Floor(Math.Log10(dv / nbins)));
        double lo = vmin - offset, hi = vmax - offset;
        double rawStep = (hi - lo) / nbins;
        var steps = (fineSteps ? FineSteps : Steps).Select(s => s * scale).ToArray();
        int istep = Array.FindIndex(steps, s => s >= rawStep);
        if (istep < 0) istep = steps.Length - 1;

        double[] ticks = Array.Empty<double>();
        for (int si = istep; si >= 0; si--)
        {
            double step = steps[si];
            double bestMin = Math.Floor(lo / step) * step;
            // _Edge_integer: smallest/largest multiples of step reaching the view edges
            double dl = (lo - bestMin) / step, dh = (hi - bestMin) / step;
            long low = (long)Math.Floor(dl); if (Math.Abs(dl - low - 1) <= 1e-10) low++;
            long high = (long)Math.Floor(dh); if (Math.Abs(dh - high) > 1e-10) high++;
            var list = new List<double>();
            for (long k = low; k <= high; k++) list.Add(k * step + bestMin);
            ticks = list.ToArray();
            if (ticks.Count(t => t >= lo - 1e-12 * step && t <= hi + 1e-12 * step) >= 2) break;
        }
        return ticks.Select(t => t + offset).ToArray();
    }

    /// <summary>Decade ticks for a log axis (LogLocator, numticks 'auto' subs [1]).</summary>
    public static double[] LocateLog(double vmin, double vmax)
    {
        if (vmin <= 0) vmin = 1e-300;
        int lo = (int)Math.Floor(Math.Log10(vmin)), hi = (int)Math.Ceiling(Math.Log10(vmax));
        int stride = Math.Max(1, (int)Math.Ceiling((hi - lo + 1) / 9.0));
        var list = new List<double>();
        for (int e = lo; e <= hi; e += stride) list.Add(Math.Pow(10, e));
        return list.ToArray();
    }

    public sealed record Labels(string[] Text, string? OffsetText);

    private const string Minus = "−";

    /// <summary>Labels for the tick locations, in matplotlib's ScalarFormatter style (shared decimals, scientific scale outside 1e-5..1e6).</summary>
    public static Labels Format(IReadOnlyList<double> locs, double vmin, double vmax)
    {
        if (locs.Count == 0) return new Labels(Array.Empty<string>(), null);
        double lo = Math.Min(vmin, vmax), hi = Math.Max(vmin, vmax);
        var inView = locs.Where(l => l >= lo && l <= hi).ToArray();
        if (inView.Length == 0) inView = locs.ToArray();
        double val = inView.Max(Math.Abs);
        int oom = val > 0 ? (int)Math.Floor(Math.Log10(val)) : 0;
        if (!(oom < -5 || oom > 6)) oom = 0;
        double div = Math.Pow(10, oom);
        var scaled = locs.Select(l => l / div).ToArray();

        double range = scaled.Max() - scaled.Min();
        if (range == 0) range = scaled.Max(Math.Abs);
        if (range == 0) range = 1;
        int rOom = (int)Math.Floor(Math.Log10(range));
        int sig = Math.Max(0, 3 - rOom);
        double thresh = 1e-3 * Math.Pow(10, rOom);
        while (sig >= 0)
        {
            int s = Math.Min(sig, 15);
            if (scaled.Max(l => Math.Abs(l - Math.Round(l, s, MidpointRounding.ToEven))) < thresh) sig--;
            else break;
        }
        sig++;
        var text = scaled.Select(v =>
        {
            if (Math.Abs(v) < 1e-8) v = 0;
            string s = v.ToString("F" + sig, CultureInfo.InvariantCulture);
            return s.StartsWith('-') ? Minus + s[1..] : s;
        }).ToArray();
        return new Labels(text, oom != 0 ? "×10" + Superscript(oom) : null);
    }

    public static string FormatLog(double v)
    {
        int e = (int)Math.Round(Math.Log10(v));
        if (Math.Abs(Math.Log10(v) - e) < 1e-9) return "$\\mathdefault{10^{" + e + "}}$";
        return v.ToString("G4", CultureInfo.InvariantCulture);
    }

    public static string Superscript(int n)
    {
        const string digits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
        return string.Concat(n.ToString(CultureInfo.InvariantCulture).Select(c => c == '-' ? '⁻' : digits[c - '0']));
    }
}
