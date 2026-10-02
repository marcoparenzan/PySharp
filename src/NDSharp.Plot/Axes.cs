// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using SkiaSharp;

namespace NDSharp.Plot;

/// <summary>Where an axes sits in the figure's grid: rows/cols and the spanned cells (0-based, inclusive start, exclusive stop).</summary>
public sealed record GridCell(int Rows, int Cols, int RowStart, int RowStop, int ColStart, int ColStop)
{
    public static GridCell Single(int rows, int cols, int index) => new(rows, cols, index / cols, index / cols + 1, index % cols, index % cols + 1);
}

public sealed class Legend
{
    public string Location { get; set; } = "best";
    public double FontSize { get; set; } = 10;
    public string? Title { get; set; }
    public bool Frame { get; set; } = true;
    public int Columns { get; set; } = 1;
    public List<(Artist? Handle, string Label)> Entries { get; } = new();
}

/// <summary>One axes: data limits and scales, artists, ticks, labels, spines and an optional legend / colorbar.</summary>
public sealed class Axes
{
    public Figure Figure { get; }
    public List<Artist> Artists { get; } = new();
    public GridCell? Cell { get; set; }
    /// <summary>Explicit position (left, bottom, width, height in figure fractions) for add_axes.</summary>
    public (double L, double B, double W, double H)? Rect { get; set; }

    public string? Title { get; set; }
    public string? TitleLoc { get; set; }
    public double TitleSize { get; set; } = 12;
    public string? XLabel { get; set; }
    public string? YLabel { get; set; }
    public double LabelSize { get; set; } = 10;
    public string XScale { get; set; } = "linear";
    public string YScale { get; set; } = "linear";
    public (double Lo, double Hi)? XLimSet { get; set; }
    public (double Lo, double Hi)? YLimSet { get; set; }
    public bool XInverted { get; set; }
    public bool YInverted { get; set; }
    public double[]? XTicksSet { get; set; }
    public double[]? YTicksSet { get; set; }
    public string[]? XTickLabelsSet { get; set; }
    public string[]? YTickLabelsSet { get; set; }
    public double TickLabelSize { get; set; } = 10;
    public double XTickRotation { get; set; }
    public bool Grid { get; set; }
    public string GridLineStyle { get; set; } = "-";
    public double GridAlpha { get; set; } = 1;
    public SKColor GridColor { get; set; } = SKColor.Parse("#b0b0b0");
    public double GridWidth { get; set; } = 0.8;
    public bool AxisOff { get; set; }
    public bool XTicksVisible { get; set; } = true;
    public bool YTicksVisible { get; set; } = true;
    public bool XLabelsVisible { get; set; } = true;
    public bool YLabelsVisible { get; set; } = true;
    public Dictionary<string, bool> Spines { get; } = new() { ["left"] = true, ["right"] = true, ["top"] = true, ["bottom"] = true };
    public SKColor FaceColor { get; set; } = SKColors.White;
    /// <summary>null = automatic (images set "equal"); "equal"/"auto" or a number.</summary>
    public string? AspectMode { get; set; }
    public double AspectValue { get; set; } = 1;
    public bool AspectIsAuto { get; set; }
    /// <summary>For colorbar axes: width as a share of the (hidden) box aspect.</summary>
    public double? BoxAspect { get; set; }
    public (double X, double Y) Anchor { get; set; } = (0.5, 0.5);
    public Legend? Legend { get; set; }
    public Colorbar? Colorbar { get; set; }
    public Axes? ColorbarHost { get; set; }
    public List<Axes>? ColorbarHostGroup { get; set; }
    public double CbFraction { get; set; } = 0.15;
    public double CbPad { get; set; } = 0.05;
    public double CbShrink { get; set; } = 1;
    public bool Is3D { get; set; }
    public string? ZLabel { get; set; }
    public (double Lo, double Hi)? ZLimSet { get; set; }
    public bool ZInverted { get; set; }
    public (double Left, double Right) ViewZ()
    {
        if (ZLimSet is { } z) return z;
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (var a in Artists.OfType<Artist3D>())
            if (a.ZBounds is { } b) { lo = Math.Min(lo, b.Z0); hi = Math.Max(hi, b.Z1); }
        if (double.IsInfinity(lo)) return (0, 1);
        double m = 0.05 * (hi - lo);
        if (hi == lo) { m = lo == 0 ? 0.05 : 0.05 * Math.Abs(lo); }
        return ZInverted ? (hi + m, lo - m) : (lo - m, hi + m);
    }
    public (double Elev, double Azim) View { get; set; } = (30, -60);
    public int ColorCycleIndex { get; set; }
    public bool HasColorbarAxes => ColorbarHost is not null;
    /// <summary>Tick marks, labels and the y label go on the right edge (colorbar axes).</summary>
    public bool YOnRight { get; set; }

    /// <summary>Final box in figure pixels (top-left origin) after layout.</summary>
    public SKRect Box { get; internal set; }
    /// <summary>The slot before aspect shrinking, in figure pixels.</summary>
    public SKRect Slot { get; internal set; }

    public Axes(Figure fig) { Figure = fig; }

    public void Add(Artist a)
    {
        a.Parent = this;
        Artists.Add(a);
    }

    public SKColor NextColor() => SKColor.Parse(Colors.Cycle[ColorCycleIndex++ % Colors.Cycle.Length]);

    // ------------------------------------------------------------------------------------------ limits

    public (double Lo, double Hi) XLim => ViewX();
    public (double Lo, double Hi) YLim => ViewY();

    private (double, double) AutoLimits(bool x)
    {
        bool log = (x ? XScale : YScale) == "log";
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        double? stickLo = null, stickHi = null;
        foreach (var a in Artists)
        {
            if (!a.Visible || a.Bounds is not { } b) continue;
            double a0 = x ? b.X0 : b.Y0, a1 = x ? b.X1 : b.Y1;
            if (double.IsNaN(a0) || double.IsNaN(a1)) continue;
            if (log) { if (a1 <= 0) continue; if (a0 <= 0) a0 = a1 / 1e3; }
            lo = Math.Min(lo, a0); hi = Math.Max(hi, a1);
            var s = a.Sticky;
            double? s0 = x ? s.X0 : s.Y0, s1 = x ? s.X1 : s.Y1;
            if (s0 is { } v0) stickLo = stickLo is null ? v0 : Math.Min(stickLo.Value, v0);
            if (s1 is { } v1) stickHi = stickHi is null ? v1 : Math.Max(stickHi.Value, v1);
        }
        if (double.IsInfinity(lo)) return log ? (1, 10) : (0, 1);
        double result0, result1;
        if (!log && hi == lo)
        {
            // matplotlib's nonsingular expansion (expander 0.05) comes first, margins are applied on top
            if (lo == 0) { lo = -0.05; hi = 0.05; }
            else { double d0 = 0.05 * Math.Abs(lo), d1 = 0.05 * Math.Abs(hi); lo -= d0; hi += d1; }
        }
        if (log)
        {
            double l0 = Math.Log10(lo), l1 = Math.Log10(hi), m = 0.05 * (l1 - l0);
            if (l1 == l0) { result0 = lo / 10; result1 = hi * 10; }
            else { result0 = Math.Pow(10, l0 - m); result1 = Math.Pow(10, l1 + m); }
            if (stickLo is { } sl && lo == sl) result0 = lo;
            return (result0, result1);
        }
        double m1 = 0.05 * (hi - lo);
        result0 = lo - m1; result1 = hi + m1;
        // sticky edges keep the margin from crossing the edge the data touches
        if (stickLo is { } s0v && lo >= s0v - 1e-12 * Math.Max(1, Math.Abs(s0v)) && result0 < s0v) result0 = s0v;
        if (stickHi is { } s1v && hi <= s1v + 1e-12 * Math.Max(1, Math.Abs(s1v)) && result1 > s1v) result1 = s1v;
        if (result0 == result1)
        {
            // matplotlib's nonsingular expansion
            if (result0 == 0) { result0 = -0.05; result1 = 0.05; }
            else { double d = 0.05 * Math.Abs(result0); result0 -= d; result1 += d; }
        }
        return (result0, result1);
    }

    /// <summary>The view as displayed, left→right / bottom→top (so a reversed pair means an inverted axis).</summary>
    public (double Left, double Right) ViewX() => XLimSet ?? (XInverted ? Swap(AutoLimits(true)) : AutoLimits(true));
    public (double Bottom, double Top) ViewY() => YLimSet ?? (YInverted ? Swap(AutoLimits(false)) : AutoLimits(false));
    private static (double, double) Swap((double A, double B) l) => (l.B, l.A);

    // ------------------------------------------------------------------------------------------ transforms

    private static double Fwd(double v, string scale) => scale == "log" ? Math.Log10(v) : v;

    /// <summary>Pixel x for a data x (box edges at the view limits, honouring inversion and log scale).</summary>
    public double PxX(double x)
    {
        var (a, b) = ViewX();
        double fa = Fwd(a, XScale), fb = Fwd(b, XScale);
        return Box.Left + (Fwd(x, XScale) - fa) / (fb - fa) * Box.Width;
    }

    public double PxY(double y)
    {
        var (a, b) = ViewY();
        double fa = Fwd(a, YScale), fb = Fwd(b, YScale);
        return Box.Bottom - (Fwd(y, YScale) - fa) / (fb - fa) * Box.Height;
    }

    public (double X, double Y) ToPx(double x, double y, CoordSpace space) => space switch
    {
        CoordSpace.Data => (PxX(x), PxY(y)),
        CoordSpace.Axes => (Box.Left + x * Box.Width, Box.Bottom - y * Box.Height),
        CoordSpace.Figure => (x * Figure.WidthPx, Figure.HeightPx - y * Figure.HeightPx),
        _ => (x, y),
    };

    // ------------------------------------------------------------------------------------------ ticks

    public sealed record TickSet(double[] Positions, string[] Labels, string? Offset);

    public TickSet XTicks() => ComputeTicks(true);
    public TickSet YTicks() => ComputeTicks(false);

    private TickSet ComputeTicks(bool x)
    {
        var set = x ? XTicksSet : YTicksSet;
        var labels = x ? XTickLabelsSet : YTickLabelsSet;
        var (a, b) = x ? ViewX() : ViewY();
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        string scale = x ? XScale : YScale;
        if (set is not null)
        {
            var text = labels ?? Format(set, lo, hi, scale, out _);
            return new TickSet(set, text, null);
        }
        double[] locs;
        if (scale == "log") locs = Ticks_.LocateLog(lo, hi);
        else
        {
            double lengthPt = (x ? Box.Width : Box.Height) / (Figure.Dpi / 72);
            locs = Ticks_.Locate(lo, hi, Ticks_.TickSpace(lengthPt, TickLabelSize, x));
        }
        string[] lab = Format(locs, lo, hi, scale, out var off);
        return new TickSet(locs, lab, off);
    }

    private static string[] Format(double[] locs, double lo, double hi, string scale, out string? offset)
    {
        offset = null;
        if (scale == "log") return locs.Select(Ticks_.FormatLog).ToArray();
        var l = Ticks_.Format(locs, lo, hi);
        offset = l.OffsetText;
        return l.Text;
    }

    // ------------------------------------------------------------------------------------------ aspect / layout

    internal bool EqualAspect => AspectMode == "equal" || (AspectMode is null && Artists.OfType<ImageArtist>().Any() && !AspectIsAuto);
    internal double EffectiveAspect => AspectMode == "equal" || AspectMode is null ? 1 : AspectValue;

    /// <summary>Applies aspect shrinking (adjustable="box", anchor C) to the slot and stores the final box.</summary>
    internal void ApplyBox(SKRect slot)
    {
        Slot = slot;
        double w = slot.Width, h = slot.Height;
        double? ratio = null; // desired box height / width
        if (BoxAspect is { } ba) ratio = ba;
        else if (!AspectIsAuto && (AspectMode is not null || EqualAspect) && XScale == "linear" && YScale == "linear" && !Is3D)
        {
            var (x0, x1) = XLim; var (y0, y1) = YLim;
            double dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            if (dx > 0 && dy > 0) ratio = EffectiveAspect * dy / dx;
        }
        if (ratio is not { } r) { Box = slot; return; }
        double nw = w, nh = w * r;
        if (nh > h) { nh = h; nw = h / r; }
        double left = slot.Left + (w - nw) * Anchor.X;
        double top = slot.Top + (h - nh) * (1 - Anchor.Y);
        Box = new SKRect((float)left, (float)top, (float)(left + nw), (float)(top + nh));
    }

    // ------------------------------------------------------------------------------------------ extents (tight layout)

    private const double TickLenPt = 3.5, TickPadPt = 3.5, LabelPadPt = 4, TitlePadPt = 6;

    private double Px => Figure.Dpi / 72;

    /// <summary>The axes box plus its tick labels, axis labels and title, in figure pixels.</summary>
    public SKRect TightBox()
    {
        var r = Box;
        if (AxisOff || Is3D) return r;
        double dpi = Figure.Dpi, s = Px;
        float left = r.Left, right = r.Right, top = r.Top, bottom = r.Bottom;
        double xlabTop = r.Bottom;
        if (XTicksVisible || XLabelsVisible)
        {
            var t = XTicks();
            double maxH = 0;
            if (XLabelsVisible)
                for (int i = 0; i < t.Positions.Length; i++)
                {
                    double px = PxX(t.Positions[i]);
                    if (px < r.Left - 1e-6 || px > r.Right + 1e-6) continue;
                    var sz = TextMetrics.Measure(MathText.Convert(t.Labels[i]), TickLabelSize, dpi);
                    double rot = XTickRotation * Math.PI / 180;
                    double wRot = sz.Width * Math.Abs(Math.Cos(rot)) + sz.Height * Math.Abs(Math.Sin(rot));
                    double hRot = sz.Width * Math.Abs(Math.Sin(rot)) + sz.Height * Math.Abs(Math.Cos(rot));
                    maxH = Math.Max(maxH, hRot);
                    left = Math.Min(left, (float)(px - wRot / 2)); right = Math.Max(right, (float)(px + wRot / 2));
                }
            xlabTop = r.Bottom + (XTicksVisible ? TickLenPt * s : 0) + (XLabelsVisible ? TickPadPt * s + maxH : 0);
            bottom = Math.Max(bottom, (float)xlabTop);
        }
        if (XLabel is { Length: > 0 })
        {
            var sz = TextMetrics.Measure(MathText.Convert(XLabel), LabelSize, dpi);
            bottom = Math.Max(bottom, (float)(xlabTop + LabelPadPt * s + sz.Height));
            left = Math.Min(left, (float)(r.MidX - sz.Width / 2)); right = Math.Max(right, (float)(r.MidX + sz.Width / 2));
        }
        double ylabRight = r.Left;
        if (YTicksVisible || YLabelsVisible)
        {
            var t = YTicks();
            double maxW = 0;
            if (YLabelsVisible)
                for (int i = 0; i < t.Positions.Length; i++)
                {
                    double py = PxY(t.Positions[i]);
                    if (py < r.Top - 1e-6 || py > r.Bottom + 1e-6) continue;
                    var sz = TextMetrics.Measure(MathText.Convert(t.Labels[i]), TickLabelSize, dpi);
                    maxW = Math.Max(maxW, sz.Width);
                    top = Math.Min(top, (float)(py - sz.Height / 2)); bottom = Math.Max(bottom, (float)(py + sz.Height / 2));
                }
            if (YOnRight)
            {
                ylabRight = r.Right + (YTicksVisible ? TickLenPt * s : 0) + (YLabelsVisible ? TickPadPt * s + maxW : 0);
                right = Math.Max(right, (float)ylabRight);
            }
            else
            {
                ylabRight = r.Left - (YTicksVisible ? TickLenPt * s : 0) - (YLabelsVisible ? TickPadPt * s + maxW : 0);
                left = Math.Min(left, (float)ylabRight);
            }
        }
        if (YLabel is { Length: > 0 })
        {
            var sz = TextMetrics.Measure(MathText.Convert(YLabel), LabelSize, dpi);
            if (YOnRight) right = Math.Max(right, (float)(ylabRight + LabelPadPt * s + sz.Height));
            else left = Math.Min(left, (float)(ylabRight - LabelPadPt * s - sz.Height));
            top = Math.Min(top, (float)(r.MidY - sz.Width / 2)); bottom = Math.Max(bottom, (float)(r.MidY + sz.Width / 2));
        }
        if (Title is { Length: > 0 })
        {
            var sz = TextMetrics.Measure(MathText.Convert(Title), TitleSize, dpi);
            top = Math.Min(top, (float)(r.Top - TitlePadPt * s - sz.Ascent));
            double cx = TitleLoc == "left" ? r.Left + sz.Width / 2 : TitleLoc == "right" ? r.Right - sz.Width / 2 : r.MidX;
            left = Math.Min(left, (float)(cx - sz.Width / 2)); right = Math.Max(right, (float)(cx + sz.Width / 2));
        }
        foreach (var t in Artists.OfType<TextArtist>().Where(t => t.Visible && t.Space != CoordSpace.Data))
        {
            var e = t.Extent(this, dpi);
            left = Math.Min(left, e.Left); right = Math.Max(right, e.Right); top = Math.Min(top, e.Top); bottom = Math.Max(bottom, e.Bottom);
        }
        return new SKRect(left, top, right, bottom);
    }

    // ------------------------------------------------------------------------------------------ drawing

    private static class Ticks_
    {
        public static int TickSpace(double l, double f, bool h) => Ticks.TickSpace(l, f, h);
        public static double[] Locate(double a, double b, int n) => Ticks.Locate(a, b, n);
        public static double[] LocateLog(double a, double b) => Ticks.LocateLog(a, b);
        public static Ticks.Labels Format(double[] l, double a, double b) => Ticks.Format(l, a, b);
        public static string FormatLog(double v) => Ticks.FormatLog(v);
    }

    internal void Draw(SKCanvas canvas)
    {
        double s = Px, dpi = Figure.Dpi;
        var ctx = new DrawContext { Canvas = canvas, Axes = this, PtPx = s };
        var box = Box;
        if (Is3D)
        {
            Plot3D.DrawFrame(this, canvas, s, dpi);
            foreach (var a in Artists.Where(a => a.Visible).OrderBy(a => a.ZOrder).ToList()) a.Draw(ctx);
            if (Title is { Length: > 0 })
            {
                string tt = MathText.Convert(Title);
                var tsz = TextMetrics.Measure(tt, TitleSize, dpi);
                Renderer.DrawText(canvas, tt, box.MidX, box.Top - 0.01 * box.Height + tsz.Height / 2, TitleSize, dpi, SKColors.Black, 0, false, false);
            }
            DrawLegend(ctx);
            return;
        }
        if (!AxisOff)
        {
            using var bg = new SKPaint { Style = SKPaintStyle.Fill, Color = FaceColor };
            canvas.DrawRect(box, bg);
        }

        var xt = XTicks(); var yt = YTicks();
        if (Grid && !AxisOff)
        {
            using var gp = Artist.Stroke(Artist.WithAlpha(GridColor, GridAlpha), GridWidth * s, GridLineStyle, GridWidth, s);
            gp.StrokeCap = SKStrokeCap.Butt;
            canvas.Save(); canvas.ClipRect(box);
            foreach (var p in xt.Positions) { float x = (float)PxX(p); canvas.DrawLine(x, box.Top, x, box.Bottom, gp); }
            foreach (var p in yt.Positions) { float y = (float)PxY(p); canvas.DrawLine(box.Left, y, box.Right, y, gp); }
            canvas.Restore();
        }

        canvas.Save();
        canvas.ClipRect(box);
        foreach (var a in Artists.Where(a => a.Visible && !(a is TextArtist { Space: not CoordSpace.Data })).OrderBy(a => a.ZOrder).ToList()) a.Draw(ctx);
        canvas.Restore();

        // artists that may extend beyond the box (annotation text) are clipped in matplotlib too, except with annotation_clip off;
        // text with axes/figure coordinates is drawn unclipped above.
        if (AxisOff) { DrawLegend(ctx); return; }

        using var spine = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = (float)(0.8 * s), IsAntialias = false, StrokeCap = SKStrokeCap.Square };
        float l = Snap(box.Left), rr = Snap(box.Right), t = Snap(box.Top), b = Snap(box.Bottom);
        if (Spines["left"]) canvas.DrawLine(l, t, l, b, spine);
        if (Spines["right"]) canvas.DrawLine(rr, t, rr, b, spine);
        if (Spines["top"]) canvas.DrawLine(l, t, rr, t, spine);
        if (Spines["bottom"]) canvas.DrawLine(l, b, rr, b, spine);

        using var tick = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = (float)(0.8 * s), IsAntialias = false };
        double len = TickLenPt * s, pad = TickPadPt * s;
        double xBottomOfLabels = box.Bottom;
        double maxLabelH = 0;
        for (int i = 0; i < xt.Positions.Length; i++)
        {
            double px = PxX(xt.Positions[i]);
            if (px < box.Left - 1e-6 || px > box.Right + 1e-6) continue;
            if (XTicksVisible) canvas.DrawLine(Snap((float)px), box.Bottom, Snap((float)px), (float)(box.Bottom + len), tick);
            if (XLabelsVisible)
            {
                string text = MathText.Convert(xt.Labels[i]);
                var sz = TextMetrics.Measure(text, TickLabelSize, dpi);
                double rot = XTickRotation * Math.PI / 180;
                double hRot = sz.Width * Math.Abs(Math.Sin(rot)) + sz.Height * Math.Abs(Math.Cos(rot));
                maxLabelH = Math.Max(maxLabelH, hRot);
                double top0 = box.Bottom + (XTicksVisible ? len : 0) + pad;
                Renderer.DrawText(canvas, text, px, top0 + hRot / 2, TickLabelSize, dpi, SKColors.Black, XTickRotation, false, false);
            }
        }
        xBottomOfLabels = box.Bottom + (XTicksVisible ? len : 0) + (XLabelsVisible ? pad + maxLabelH : 0);
        double maxLabelW = 0;
        for (int i = 0; i < yt.Positions.Length; i++)
        {
            double py = PxY(yt.Positions[i]);
            if (py < box.Top - 1e-6 || py > box.Bottom + 1e-6) continue;
            if (YTicksVisible)
            {
                if (YOnRight) canvas.DrawLine(box.Right, Snap((float)py), (float)(box.Right + len), Snap((float)py), tick);
                else canvas.DrawLine((float)(box.Left - len), Snap((float)py), box.Left, Snap((float)py), tick);
            }
            if (YLabelsVisible)
            {
                string text = MathText.Convert(yt.Labels[i]);
                var sz = TextMetrics.Measure(text, TickLabelSize, dpi);
                maxLabelW = Math.Max(maxLabelW, sz.Width);
                double lx = YOnRight ? box.Right + (YTicksVisible ? len : 0) + pad + sz.Width / 2 : box.Left - (YTicksVisible ? len : 0) - pad - sz.Width / 2;
                Renderer.DrawText(canvas, text, lx, py, TickLabelSize, dpi, SKColors.Black, 0, false, false);
            }
        }
        double yLabelsLeft = YOnRight ? box.Right + (YTicksVisible ? len : 0) + (YLabelsVisible ? pad + maxLabelW : 0)
            : box.Left - (YTicksVisible ? len : 0) - (YLabelsVisible ? pad + maxLabelW : 0);
        if (xt.Offset is { } xo && XLabelsVisible)
            Renderer.DrawText(canvas, xo, box.Right - TextMetrics.Measure(xo, TickLabelSize, dpi).Width / 2, box.Bottom + len + pad + maxLabelH + TextMetrics.Measure(xo, TickLabelSize, dpi).Height / 2, TickLabelSize, dpi, SKColors.Black, 0, false, false);
        if (yt.Offset is { } yo && YLabelsVisible)
            Renderer.DrawText(canvas, yo, box.Left + TextMetrics.Measure(yo, TickLabelSize, dpi).Width / 2, box.Top - TextMetrics.Measure(yo, TickLabelSize, dpi).Height / 2 - 2 * s, TickLabelSize, dpi, SKColors.Black, 0, false, false);

        if (XLabel is { Length: > 0 })
        {
            string text = MathText.Convert(XLabel);
            var sz = TextMetrics.Measure(text, LabelSize, dpi);
            Renderer.DrawText(canvas, text, box.MidX, xBottomOfLabels + LabelPadPt * s + sz.Height / 2, LabelSize, dpi, SKColors.Black, 0, false, false);
        }
        if (YLabel is { Length: > 0 })
        {
            string text = MathText.Convert(YLabel);
            var sz = TextMetrics.Measure(text, LabelSize, dpi);
            double lx = YOnRight ? yLabelsLeft + LabelPadPt * s + sz.Height / 2 : yLabelsLeft - LabelPadPt * s - sz.Height / 2;
            Renderer.DrawText(canvas, text, lx, box.MidY, LabelSize, dpi, SKColors.Black, YOnRight ? 270 : 90, false, false);
        }
        if (Title is { Length: > 0 })
        {
            string text = MathText.Convert(Title);
            var sz = TextMetrics.Measure(text, TitleSize, dpi);
            double cx = TitleLoc == "left" ? box.Left + sz.Width / 2 : TitleLoc == "right" ? box.Right - sz.Width / 2 : box.MidX;
            double baseline = box.Top - TitlePadPt * s;
            Renderer.DrawText(canvas, text, cx, baseline - sz.Ascent + sz.Height / 2, TitleSize, dpi, SKColors.Black, 0, false, false);
        }
        foreach (var t2 in Artists.OfType<TextArtist>().Where(t2 => t2.Visible && t2.Space != CoordSpace.Data && false)) t2.Draw(ctx);
        DrawLegend(ctx);
    }

    /// <summary>Draws text artists placed in axes/figure coordinates, which are not clipped to the box.</summary>
    internal void DrawTopLevel(SKCanvas canvas)
    {
        var ctx = new DrawContext { Canvas = canvas, Axes = this, PtPx = Px };
        foreach (var t in Artists.OfType<TextArtist>().Where(t => t.Visible && t.Space != CoordSpace.Data)) t.Draw(ctx);
    }

    private static float Snap(float v) => (float)Math.Round(v - 0.5) + 0.5f;

    // ------------------------------------------------------------------------------------------ legend

    public Legend MakeLegend(string? loc = null)
    {
        var lg = Legend ?? new Legend();
        lg.Entries.Clear();
        foreach (var a in Artists)
            if (a.Label is { Length: > 0 } l && !l.StartsWith('_')) lg.Entries.Add((a, l));
        if (loc is not null) lg.Location = loc;
        Legend = lg;
        return lg;
    }

    private void DrawLegend(DrawContext ctx)
    {
        if (Legend is not { } lg || lg.Entries.Count == 0) return;
        double s = Px, dpi = Figure.Dpi, fs = lg.FontSize;
        double fsPx = fs * s;
        double borderPad = 0.4 * fsPx, labelSpacing = 0.5 * fsPx, handleLen = 2.0 * fsPx, handleH = 0.7 * fsPx, handleTextPad = 0.8 * fsPx, axesPad = 0.5 * fsPx;
        var sizes = lg.Entries.Select(e => TextMetrics.Measure(MathText.Convert(e.Label), fs, dpi)).ToList();
        int cols = Math.Max(1, lg.Columns);
        int rowsN = (lg.Entries.Count + cols - 1) / cols;
        // rows are as tall as their tallest text (or the handle)
        double rowH = Math.Max(sizes.Max(z => z.Height), handleH);
        double colTextW(int c) => sizes.Where((_, i) => i / rowsN == c).Select(z => z.Width).DefaultIfEmpty(0).Max();
        double contentW = Enumerable.Range(0, cols).Sum(c => handleLen + handleTextPad + colTextW(c)) + (cols - 1) * (2.0 * fsPx);
        double contentH = rowsN * rowH + (rowsN - 1) * labelSpacing;
        double titleH = 0, titleW = 0;
        if (lg.Title is { Length: > 0 }) { var tz = TextMetrics.Measure(lg.Title, fs, dpi); titleH = tz.Height + labelSpacing * 0.0 + 0; titleW = tz.Width; }
        double boxW = Math.Max(contentW, titleW) + 2 * borderPad, boxH = contentH + titleH + 2 * borderPad;
        var b = Box;
        SKRect Place(string loc) => loc switch
        {
            "upper left" => At(b.Left + axesPad, b.Top + axesPad),
            "upper center" => At(b.MidX - boxW / 2, b.Top + axesPad),
            "lower left" => At(b.Left + axesPad, b.Bottom - axesPad - boxH),
            "lower right" => At(b.Right - axesPad - boxW, b.Bottom - axesPad - boxH),
            "lower center" => At(b.MidX - boxW / 2, b.Bottom - axesPad - boxH),
            "center left" => At(b.Left + axesPad, b.MidY - boxH / 2),
            "center right" or "right" => At(b.Right - axesPad - boxW, b.MidY - boxH / 2),
            "center" => At(b.MidX - boxW / 2, b.MidY - boxH / 2),
            _ => At(b.Right - axesPad - boxW, b.Top + axesPad), // upper right
        };
        SKRect At(double x, double y) => new((float)x, (float)y, (float)(x + boxW), (float)(y + boxH));
        string chosen = lg.Location;
        if (chosen is "best" or "0")
        {
            string[] order = { "upper right", "upper left", "lower left", "lower right", "center left", "center right", "lower center", "upper center", "center" };
            var verts = Artists.Where(a => a.Visible).SelectMany(a => a.Vertices).Select(v => (PxX(v.X), PxY(v.Y))).Where(p => double.IsFinite(p.Item1) && double.IsFinite(p.Item2)).ToList();
            var lines = Artists.OfType<Line2D>().Where(l => l.Visible && !l.XAxesCoords && !l.YAxesCoords).ToList();
            int best = int.MaxValue;
            chosen = order[0];
            foreach (var o in order)
            {
                var r = Place(o);
                int cost = verts.Count(p => r.Contains((float)p.Item1, (float)p.Item2));
                // line segments crossing the candidate box count too
                foreach (var ln in lines)
                    for (int i = 0; i + 1 < ln.X.Length; i++)
                        if (SegmentHits(r, PxX(ln.X[i]), PxY(ln.Y[i]), PxX(ln.X[i + 1]), PxY(ln.Y[i + 1]))) cost++;
                // candidate must lie inside the axes
                if (cost < best) { best = cost; chosen = o; }
                if (best == 0) break;
            }
        }
        var rect = Place(chosen);
        if (lg.Frame)
        {
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 255, 255, 204), IsAntialias = true };
            using var edge = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColor.Parse("#cccccc"), StrokeWidth = (float)s, IsAntialias = true };
            float rad = (float)(0.2 * fsPx);
            ctx.Canvas.DrawRoundRect(rect, rad, rad, fill);
            ctx.Canvas.DrawRoundRect(rect, rad, rad, edge);
        }
        double y0 = rect.Top + borderPad;
        if (lg.Title is { Length: > 0 })
        {
            var tz = TextMetrics.Measure(lg.Title, fs, dpi);
            Renderer.DrawText(ctx.Canvas, lg.Title, rect.MidX, y0 + tz.Height / 2, fs, dpi, SKColors.Black, 0, false, false);
            y0 += titleH;
        }
        double x0 = rect.Left + borderPad;
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rowsN; r++)
            {
                int idx = c * rowsN + r;
                if (idx >= lg.Entries.Count) break;
                double ry = y0 + r * (rowH + labelSpacing);
                var handleBox = new SKRect((float)x0, (float)(ry + rowH / 2 - handleH / 2), (float)(x0 + handleLen), (float)(ry + rowH / 2 + handleH / 2));
                lg.Entries[idx].Handle?.DrawLegendHandle(ctx, handleBox);
                string label = MathText.Convert(lg.Entries[idx].Label);
                var sz = sizes[idx];
                Renderer.DrawText(ctx.Canvas, label, x0 + handleLen + handleTextPad + sz.Width / 2, ry + rowH / 2, fs, dpi, SKColors.Black, 0, false, false);
            }
            x0 += handleLen + handleTextPad + colTextW(c) + 2.0 * fsPx;
        }
    }

    private static bool SegmentHits(SKRect r, double x0, double y0, double x1, double y1)
    {
        if (!double.IsFinite(x0 + y0 + x1 + y1)) return false;
        double t0 = 0, t1 = 1, dx = x1 - x0, dy = y1 - y0;
        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
        return Clip(-dx, x0 - r.Left) && Clip(dx, r.Right - x0) && Clip(-dy, y0 - r.Top) && Clip(dy, r.Bottom - y0);
    }
}

/// <summary>A colorbar bound to a mappable (image or scatter); lives in its own axes.</summary>
public sealed class Colorbar
{
    public Artist Mappable { get; }
    public Axes Axes { get; }
    public string? Label { get; set; }
    public Colorbar(Artist m, Axes ax) { Mappable = m; Axes = ax; }

    public Colormap Cmap => ((IMappable)Mappable).Cmap;
    public (double lo, double hi) Range => ((IMappable)Mappable).Range;
}
