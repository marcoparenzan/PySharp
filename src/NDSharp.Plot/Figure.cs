// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using SkiaSharp;

namespace NDSharp.Plot;

public sealed class SubplotParams
{
    public double Left { get; set; } = 0.125;
    public double Right { get; set; } = 0.9;
    public double Bottom { get; set; } = 0.11;
    public double Top { get; set; } = 0.88;
    public double WSpace { get; set; } = 0.2;
    public double HSpace { get; set; } = 0.2;
}

/// <summary>Draws text centred on a point; the box is the text's measured extent, rotated about its centre.</summary>
public static class Renderer
{
    public static void DrawText(SKCanvas canvas, string text, double cx, double cy, double sizePt, double dpi, SKColor color, double rotation, bool bold, bool italic)
    {
        if (text.Length == 0) return;
        using var font = new SKFont(Fonts.Face(bold, italic), (float)(sizePt * dpi / 72)) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var sz = TextMetrics.Measure(text, sizePt, dpi, bold, italic);
        var lines = text.Split('\n');
        canvas.Save();
        canvas.Translate((float)cx, (float)cy);
        if (rotation != 0) canvas.RotateDegrees((float)-rotation);
        double top = -sz.Height / 2;
        double step = lines.Length > 1 ? 1.2 * (TextMetrics.Measure("lp", sizePt, dpi).Height) : 0;
        for (int i = 0; i < lines.Length; i++)
        {
            float w = font.MeasureText(lines[i]);
            canvas.DrawText(lines[i], -w / 2, (float)(top + sz.Ascent - (lines.Length - 1 - 0) * 0 + i * step), SKTextAlign.Left, font, paint);
        }
        canvas.Restore();
    }
}

public sealed class Figure
{
    public double WidthIn { get; set; } = 6.4;
    public double HeightIn { get; set; } = 4.8;
    public double Dpi { get; set; } = 100;
    public List<Axes> Axes { get; } = new();
    public SubplotParams Params { get; } = new();
    private SubplotParams? _tightParams;
    /// <summary>The parameters in force: the tight_layout result when it is on, otherwise the user-set ones.</summary>
    public SubplotParams Effective => Tight && _tightParams is not null ? _tightParams : Params;
    public string? SuptitleText { get; set; }
    public double SuptitleSize { get; set; } = 12;
    public bool Tight { get; set; }
    public double TightPad { get; set; } = 1.08;
    public double? TightWPad { get; set; }
    public double? TightHPad { get; set; }
    public SKColor FaceColor { get; set; } = SKColors.White;
    public int Number { get; set; }

    public double WidthPx => WidthIn * Dpi;
    public double HeightPx => HeightIn * Dpi;

    public Axes AddAxes(GridCell? cell, (double, double, double, double)? rect = null)
    {
        var ax = new Axes(this) { Cell = cell, Rect = rect };
        Axes.Add(ax);
        return ax;
    }

    // ------------------------------------------------------------------------------------------ layout

    private SKRect CellSlot(GridCell c)
    {
        var p = Effective;
        double totW = p.Right - p.Left, totH = p.Top - p.Bottom;
        double cellW = totW / (c.Cols + p.WSpace * (c.Cols - 1)), sepW = p.WSpace * cellW;
        double cellH = totH / (c.Rows + p.HSpace * (c.Rows - 1)), sepH = p.HSpace * cellH;
        double l = p.Left + c.ColStart * (cellW + sepW);
        double r = p.Left + (c.ColStop - 1) * (cellW + sepW) + cellW;
        double t = p.Top - c.RowStart * (cellH + sepH);
        double b = p.Top - (c.RowStop - 1) * (cellH + sepH) - cellH;
        return new SKRect((float)(l * WidthPx), (float)((1 - t) * HeightPx), (float)(r * WidthPx), (float)((1 - b) * HeightPx));
    }

    private void PlaceAll()
    {
        foreach (var ax in Axes)
        {
            if (ax.ColorbarHost is not null) continue; // placed together with its host
            if (ax.ColorbarHostGroup is not null && ax.ColorbarHostGroup.Contains(ax)) continue;
            SKRect slot = ax.Cell is { } c ? CellSlot(c)
                : ax.Rect is { } r ? new SKRect((float)(r.L * WidthPx), (float)((1 - r.B - r.H) * HeightPx), (float)((r.L + r.W) * WidthPx), (float)((1 - r.B) * HeightPx))
                : CellSlot(new GridCell(1, 1, 0, 1, 0, 1));
            if (Axes.FirstOrDefault(a => a.ColorbarHost == ax && a.ColorbarHostGroup is null) is { } cax)
            {
                double fraction = cax.CbFraction, pad = cax.CbPad;
                double w = slot.Width;
                var hostSlot = new SKRect(slot.Left, slot.Top, (float)(slot.Left + (1 - fraction - pad) * w), slot.Bottom);
                var caxSlot = new SKRect((float)(slot.Left + (1 - fraction) * w), slot.Top, slot.Right, slot.Bottom);
                ax.ApplyBox(hostSlot);
                cax.ApplyBox(caxSlot);
            }
            else ax.ApplyBox(slot);
        }
        // a colorbar shared by several axes: the hosts give up a strip on the right (matplotlib's make_axes)
        foreach (var cax in Axes.Where(a => a.ColorbarHostGroup is not null))
        {
            var hosts = cax.ColorbarHostGroup!;
            foreach (var h in hosts) h.ApplyBox(h.Cell is { } c ? CellSlot(c) : h.Slot);
            var union = hosts.Select(h => h.Slot).Aggregate(SKRect.Union);
            double f = cax.CbFraction, p = cax.CbPad, k = 1 - f - p;
            foreach (var h in hosts)
            {
                var s = h.Slot;
                h.ApplyBox(new SKRect((float)(union.Left + (s.Left - union.Left) * k), s.Top, (float)(union.Left + (s.Right - union.Left) * k), s.Bottom));
            }
            double uw = union.Width, uh = union.Height, shrink = cax.CbShrink;
            double top = union.Top + uh * (1 - shrink) / 2;
            cax.ApplyBox(new SKRect((float)(union.Left + (1 - f) * uw), (float)top, union.Right, (float)(top + uh * shrink)));
        }
    }

    /// <summary>Port of matplotlib's tight_layout: sets the subplot parameters so labels fit, then places the axes.</summary>
    private void TightAdjust()
    {
        _tightParams = null;
        PlaceAll();
        double fs = 10, padPx = TightPad * fs / 72 * Dpi;
        double wPad = (TightWPad ?? TightPad) * fs / 72 * Dpi, hPad = (TightHPad ?? TightPad) * fs / 72 * Dpi;
        var hosts = Axes.Where(a => a.Cell is not null && a.ColorbarHost is null).ToList();
        if (hosts.Count == 0) return;
        int rows = hosts[0].Cell!.Rows, cols = hosts[0].Cell!.Cols;
        var hs = new double[rows, cols + 1];
        var vs = new double[rows + 1, cols];
        foreach (var grp in hosts.GroupBy(a => a.Cell!))
        {
            var c = grp.Key;
            var members = grp.SelectMany(a => new[] { a }.Concat(Axes.Where(x => x.ColorbarHost == a))).Where(a => !a.AxisOff || true).ToList();
            var tight = members.Select(a => a.TightBox()).Aggregate(SKRect.Union);
            var slot = CellSlot(c);
            double l = slot.Left - tight.Left, r = tight.Right - slot.Right, t = slot.Top - tight.Top, b = tight.Bottom - slot.Bottom;
            for (int row = c.RowStart; row < c.RowStop; row++) { hs[row, c.ColStart] += l; hs[row, c.ColStop] += r; }
            for (int col = c.ColStart; col < c.ColStop; col++) { vs[c.RowStart, col] += t; vs[c.RowStop, col] += b; }
        }
        double W = WidthPx, H = HeightPx;
        double Max(Func<IEnumerable<double>> f) => f().DefaultIfEmpty(0).Max();
        double mLeft = Math.Max(Max(() => Enumerable.Range(0, rows).Select(i => hs[i, 0])), 0) + padPx;
        double mRight = Math.Max(Max(() => Enumerable.Range(0, rows).Select(i => hs[i, cols])), 0) + padPx;
        double mTop = Math.Max(Max(() => Enumerable.Range(0, cols).Select(j => vs[0, j])), 0) + padPx;
        double mBottom = Math.Max(Max(() => Enumerable.Range(0, cols).Select(j => vs[rows, j])), 0) + padPx;
        if (SuptitleText is { Length: > 0 })
        {
            var sz = TextMetrics.Measure(MathText.Convert(SuptitleText), SuptitleSize, Dpi);
            mTop += sz.Height + padPx;
        }
        if (mLeft + mRight >= W || mTop + mBottom >= H) return;
        var p = new SubplotParams { WSpace = Params.WSpace, HSpace = Params.HSpace };
        _tightParams = p;
        p.Left = mLeft / W; p.Right = 1 - mRight / W; p.Bottom = mBottom / H; p.Top = 1 - mTop / H;
        if (cols > 1)
        {
            double hspace = Max(() => Enumerable.Range(0, rows).SelectMany(i => Enumerable.Range(1, cols - 1).Select(j => hs[i, j]))) + wPad;
            double wAxes = ((p.Right - p.Left) * W - hspace * (cols - 1)) / cols;
            if (wAxes > 0) p.WSpace = hspace / wAxes;
        }
        if (rows > 1)
        {
            double vspace = Max(() => Enumerable.Range(1, rows - 1).SelectMany(i => Enumerable.Range(0, cols).Select(j => vs[i, j]))) + hPad;
            double hAxes = ((p.Top - p.Bottom) * H - vspace * (rows - 1)) / rows;
            if (hAxes > 0) p.HSpace = vspace / hAxes;
        }
        PlaceAll();
    }

    public void Layout()
    {
        if (Tight) TightAdjust(); else PlaceAll();
    }

    // ------------------------------------------------------------------------------------------ render

    public byte[] RenderPng(double? dpi = null, bool tightBbox = false, bool transparent = false)
    {
        double saved = Dpi;
        if (dpi is { } d) Dpi = d;
        try
        {
            Layout();
            int w = (int)Math.Round(WidthPx), h = (int)Math.Round(HeightPx);
            float offX = 0, offY = 0;
            if (tightBbox)
            {
                // savefig(bbox_inches="tight"): crop to the artists' extent plus 0.1 inch
                var box = Axes.Where(a => !a.AxisOff || a.Artists.Count > 0).Select(a => a.TightBox()).DefaultIfEmpty(new SKRect(0, 0, w, h)).Aggregate(SKRect.Union);
                if (SuptitleText is { Length: > 0 })
                {
                    var sz = TextMetrics.Measure(MathText.Convert(SuptitleText), SuptitleSize, Dpi);
                    box = SKRect.Union(box, new SKRect((float)(WidthPx / 2 - sz.Width / 2), (float)(HeightPx * 0.02), (float)(WidthPx / 2 + sz.Width / 2), (float)(HeightPx * 0.02 + sz.Height)));
                }
                float pad = (float)(0.1 * Dpi);
                offX = box.Left - pad; offY = box.Top - pad;
                w = (int)Math.Ceiling(box.Width + 2 * pad); h = (int)Math.Ceiling(box.Height + 2 * pad);
            }
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            var canvas = surface.Canvas;
            canvas.Clear(transparent ? SKColors.Transparent : FaceColor);
            canvas.Translate(-offX, -offY);
            foreach (var ax in Axes) ax.Draw(canvas);
            if (SuptitleText is { Length: > 0 } st)
            {
                string text = MathText.Convert(st);
                var sz = TextMetrics.Measure(text, SuptitleSize, Dpi);
                Renderer.DrawText(canvas, text, WidthPx / 2, HeightPx * 0.02 + sz.Height / 2, SuptitleSize, Dpi, SKColors.Black, 0, false, false);
            }
            foreach (var ax in Axes) ax.DrawTopLevel(canvas);
            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally { Dpi = saved; }
    }
}
