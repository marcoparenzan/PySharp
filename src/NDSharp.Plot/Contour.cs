// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using SkiaSharp;

namespace NDSharp.Plot;

/// <summary>Anything a colorbar can describe: a colormap and the value range it spans.</summary>
public interface IMappable
{
    Colormap Cmap { get; }
    (double lo, double hi) Range { get; }
}

/// <summary>contour / contourf over a rectilinear grid: marching squares for lines, per-pixel band lookup for filled regions.</summary>
public sealed class ContourArtist : Artist, IMappable
{
    public double[] Xs { get; set; } = Array.Empty<double>();
    public double[] Ys { get; set; } = Array.Empty<double>();
    /// <summary>Row-major values, Ys.Length rows × Xs.Length columns.</summary>
    public double[] Z { get; set; } = Array.Empty<double>();
    public double[] Levels { get; set; } = Array.Empty<double>();
    public bool Filled { get; set; }
    public Colormap Cmap { get; set; } = Colormap.Get("viridis");
    public SKColor[]? Colors { get; set; }
    public double LineWidth { get; set; } = 1.5;
    public string? LineStyle { get; set; }

    public (double lo, double hi) Range => (Levels.Min(), Levels.Max());

    public ContourArtist() { ZOrder = 1; }

    public override DataBounds? Bounds => new(Xs.Min(), Xs.Max(), Ys.Min(), Ys.Max());
    public override (double? X0, double? X1, double? Y0, double? Y1) Sticky => (Xs.Min(), Xs.Max(), Ys.Min(), Ys.Max());

    private SKColor LevelColor(int i, int n)
    {
        if (Colors is { Length: > 0 } c) return c[i % c.Length];
        var (lo, hi) = Range;
        return Cmap.At(hi == lo ? 0 : (Levels[i] - lo) / (hi - lo));
    }

    public static double[] AutoLevels(double zmin, double zmax, int n)
    {
        var ticks = Ticks.Locate(zmin, zmax, n + 1, fineSteps: true);
        return ticks;
    }

    public override void Draw(DrawContext c)
    {
        if (Xs.Length < 2 || Ys.Length < 2) return;
        if (Filled) DrawFilled(c); else DrawLines(c);
    }

    private double At(int r, int col) => Z[r * Xs.Length + col];

    private void DrawLines(DrawContext c)
    {
        var ax = c.Axes;
        bool mono = Colors is { Length: 1 };
        double zlo = Z.Where(double.IsFinite).Min(), zhi = Z.Where(double.IsFinite).Max();
        for (int li = 0; li < Levels.Length; li++)
        {
            double lv = Levels[li];
            if (lv <= zlo || lv >= zhi) continue;
            using var path = new SKPath();
            for (int r = 0; r + 1 < Ys.Length; r++)
                for (int col = 0; col + 1 < Xs.Length; col++)
                {
                    double v00 = At(r, col), v10 = At(r, col + 1), v11 = At(r + 1, col + 1), v01 = At(r + 1, col);
                    if (!double.IsFinite(v00 + v10 + v11 + v01)) continue;
                    var pts = new List<(double, double)>();
                    void Edge(double va, double vb, double xa, double ya, double xb, double yb)
                    {
                        if ((va < lv) == (vb < lv)) return;
                        double t = (lv - va) / (vb - va);
                        pts.Add((xa + t * (xb - xa), ya + t * (yb - ya)));
                    }
                    double x0 = Xs[col], x1 = Xs[col + 1], y0 = Ys[r], y1 = Ys[r + 1];
                    Edge(v00, v10, x0, y0, x1, y0);
                    Edge(v10, v11, x1, y0, x1, y1);
                    Edge(v01, v11, x0, y1, x1, y1);
                    Edge(v00, v01, x0, y0, x0, y1);
                    if (pts.Count == 2) { Seg(path, ax, pts[0], pts[1]); }
                    else if (pts.Count == 4) { Seg(path, ax, pts[0], pts[1]); Seg(path, ax, pts[2], pts[3]); }
                }
            string style = LineStyle ?? (mono && lv < 0 ? "--" : "-");
            using var paint = Stroke(WithAlpha(LevelColor(li, Levels.Length), Alpha), LineWidth * c.PtPx, style, LineWidth, c.PtPx);
            c.Canvas.DrawPath(path, paint);
        }
    }

    private static void Seg(SKPath p, Axes ax, (double x, double y) a, (double x, double y) b)
    {
        p.MoveTo((float)ax.PxX(a.x), (float)ax.PxY(a.y));
        p.LineTo((float)ax.PxX(b.x), (float)ax.PxY(b.y));
    }

    private double Sample(double x, double y)
    {
        int col = Math.Clamp(Array.BinarySearch(Xs, x) is var i && i >= 0 ? i : ~i - 1, 0, Xs.Length - 2);
        int r = Math.Clamp(Array.BinarySearch(Ys, y) is var j && j >= 0 ? j : ~j - 1, 0, Ys.Length - 2);
        double tx = (x - Xs[col]) / (Xs[col + 1] - Xs[col]), ty = (y - Ys[r]) / (Ys[r + 1] - Ys[r]);
        return At(r, col) * (1 - tx) * (1 - ty) + At(r, col + 1) * tx * (1 - ty) + At(r + 1, col) * (1 - tx) * ty + At(r + 1, col + 1) * tx * ty;
    }

    private void DrawFilled(DrawContext c)
    {
        var ax = c.Axes;
        var box = ax.Box;
        var (vx0, vx1) = ax.ViewX(); var (vy0, vy1) = ax.ViewY();
        int w = Math.Max(1, (int)Math.Ceiling(box.Width)), h = Math.Max(1, (int)Math.Ceiling(box.Height));
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var px = new byte[w * h * 4];
        double xmin = Xs.Min(), xmax = Xs.Max(), ymin = Ys.Min(), ymax = Ys.Max();
        for (int py = 0; py < h; py++)
            for (int pxl = 0; pxl < w; pxl++)
            {
                double x = vx0 + (pxl + 0.5) / box.Width * (vx1 - vx0), y = vy0 + (1 - (py + 0.5) / box.Height) * (vy1 - vy0);
                if (x < xmin || x > xmax || y < ymin || y > ymax) continue;
                double z = Sample(x, y);
                int band = -1;
                for (int k = 0; k + 1 < Levels.Length; k++) if (z >= Levels[k] && z <= Levels[k + 1]) { band = k; if (z < Levels[k + 1]) break; }
                if (band < 0) continue;
                // matplotlib colours band k by the midpoint of its bounds, normalised over the full level range
                double mid = (Levels[band] + Levels[band + 1]) / 2, lo = Levels[0], hi = Levels[^1];
                var col = Colors is { Length: > 0 } cs ? cs[band % cs.Length] : Cmap.At(hi == lo ? 0 : (mid - lo) / (hi - lo));
                int o = 4 * (py * w + pxl);
                px[o] = col.Red; px[o + 1] = col.Green; px[o + 2] = col.Blue; px[o + 3] = (byte)(255 * Alpha);
            }
        System.Runtime.InteropServices.Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
        using var img = SKImage.FromBitmap(bmp);
        using var paint = new SKPaint { IsAntialias = false };
        c.Canvas.DrawImage(img, new SKRect(0, 0, w, h), new SKRect(box.Left, box.Top, box.Left + w, box.Top + h), new SKSamplingOptions(SKFilterMode.Nearest), paint);
    }
}
