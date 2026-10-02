// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using SkiaSharp;

namespace NDSharp.Plot;

/// <summary>An artist living in 3-D data space (mplot3d). Its x/y extent feeds the 2-D autoscale machinery, z is separate.</summary>
public abstract class Artist3D : Artist
{
    public abstract (double Z0, double Z1)? ZBounds { get; }
}

public sealed class Line3D : Artist3D
{
    public double[] X { get; set; }
    public double[] Y { get; set; }
    public double[] Z { get; set; }
    public SKColor Color { get; set; }
    public double LineWidth { get; set; } = 1.5;
    public string LineStyle { get; set; } = "-";
    public string? Marker { get; set; }
    public double MarkerSize { get; set; } = 6;

    public Line3D(double[] x, double[] y, double[] z, SKColor c) { X = x; Y = y; Z = z; Color = c; ZOrder = 2; }

    public override DataBounds? Bounds => new(X.Min(), X.Max(), Y.Min(), Y.Max());
    public override (double, double)? ZBounds => (Z.Min(), Z.Max());

    public override void Draw(DrawContext c)
    {
        var ax = c.Axes;
        if (LineStyle != "None" && X.Length > 1)
        {
            using var path = new SKPath();
            for (int i = 0; i < X.Length; i++)
            {
                var (px, py, _) = Plot3D.Project(ax, X[i], Y[i], Z[i]);
                if (i == 0) path.MoveTo((float)px, (float)py); else path.LineTo((float)px, (float)py);
            }
            using var paint = Stroke(WithAlpha(Color, Alpha), LineWidth * c.PtPx, LineStyle, LineWidth, c.PtPx);
            c.Canvas.DrawPath(path, paint);
        }
        if (Marker is not null)
            for (int i = 0; i < X.Length; i++)
            {
                var (px, py, _) = Plot3D.Project(ax, X[i], Y[i], Z[i]);
                Markers.Draw(c.Canvas, Marker, px, py, MarkerSize * c.PtPx, WithAlpha(Color, Alpha), WithAlpha(Color, Alpha), c.PtPx);
            }
    }

    public override void DrawLegendHandle(DrawContext c, SKRect box)
    {
        using var paint = Stroke(WithAlpha(Color, Alpha), LineWidth * c.PtPx, LineStyle, LineWidth, c.PtPx);
        c.Canvas.DrawLine(box.Left, box.MidY, box.Right, box.MidY, paint);
    }
}

public sealed class Scatter3D : Artist3D, IMappable
{
    public double[]? Values { get; set; }
    public Colormap Cmap { get; set; } = Colormap.Get("viridis");
    public double? VMin { get; set; }
    public double? VMax { get; set; }
    public (double lo, double hi) Range
    {
        get
        {
            var fin = (Values ?? Array.Empty<double>()).Where(double.IsFinite).ToArray();
            return (VMin ?? (fin.Length > 0 ? fin.Min() : 0), VMax ?? (fin.Length > 0 ? fin.Max() : 1));
        }
    }
    private SKColor ColorAt(int i)
    {
        if (Values is null) return Colors[i % Colors.Length];
        var (lo, hi) = Range;
        return Cmap.At(hi == lo ? 0 : (Values[i] - lo) / (hi - lo));
    }

    public double[] X { get; set; } = Array.Empty<double>();
    public double[] Y { get; set; } = Array.Empty<double>();
    public double[] Z { get; set; } = Array.Empty<double>();
    public double[] Sizes { get; set; } = { 20 };
    public SKColor[] Colors { get; set; } = { SKColor.Parse("#1f77b4") };
    public string Marker { get; set; } = "o";

    public Scatter3D() { ZOrder = 2; }

    public override DataBounds? Bounds => new(X.Min(), X.Max(), Y.Min(), Y.Max());
    public override (double, double)? ZBounds => (Z.Min(), Z.Max());

    public override void Draw(DrawContext c)
    {
        var order = Enumerable.Range(0, X.Length).Select(i => (i, d: Plot3D.Project(c.Axes, X[i], Y[i], Z[i]))).OrderByDescending(t => t.d.depth);
        foreach (var (i, d) in order)
        {
            var col = WithAlpha(ColorAt(i), Alpha);
            Markers.Draw(c.Canvas, Marker, d.x, d.y, Math.Sqrt(Sizes[i % Sizes.Length]) * c.PtPx, col, col, 1.5 * c.PtPx);
        }
    }

    public override void DrawLegendHandle(DrawContext c, SKRect box)
        => Markers.Draw(c.Canvas, Marker, box.MidX, box.MidY, Math.Sqrt(Sizes[0]) * c.PtPx, WithAlpha(ColorAt(0), Alpha), WithAlpha(ColorAt(0), Alpha), 1.5 * c.PtPx);
}

public sealed class Poly3D : Artist3D
{
    public List<double[][]> Polygons { get; } = new(); // each polygon: array of (x,y,z)
    public SKColor FaceColor { get; set; } = SKColor.Parse("#1f77b4");
    public SKColor? EdgeColor { get; set; }
    public double LineWidth { get; set; } = 1;

    public Poly3D() { ZOrder = 1; }

    private IEnumerable<double[]> Pts => Polygons.SelectMany(p => p);
    public override DataBounds? Bounds => Pts.Any() ? new(Pts.Min(p => p[0]), Pts.Max(p => p[0]), Pts.Min(p => p[1]), Pts.Max(p => p[1])) : null;
    public override (double, double)? ZBounds => Pts.Any() ? (Pts.Min(p => p[2]), Pts.Max(p => p[2])) : null;

    public override void Draw(DrawContext c)
    {
        foreach (var poly in Polygons.OrderByDescending(p => p.Average(v => Plot3D.Project(c.Axes, v[0], v[1], v[2]).depth)))
        {
            using var path = new SKPath();
            for (int i = 0; i < poly.Length; i++)
            {
                var (px, py, _) = Plot3D.Project(c.Axes, poly[i][0], poly[i][1], poly[i][2]);
                if (i == 0) path.MoveTo((float)px, (float)py); else path.LineTo((float)px, (float)py);
            }
            path.Close();
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = WithAlpha(FaceColor, Alpha), IsAntialias = true };
            c.Canvas.DrawPath(path, fill);
            if (EdgeColor is { } e)
            {
                using var stroke = Stroke(WithAlpha(e, Alpha), LineWidth * c.PtPx, "-", LineWidth, c.PtPx);
                c.Canvas.DrawPath(path, stroke);
            }
        }
    }
}

public sealed class Text3D : Artist3D
{
    public string Text { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double FontSize { get; set; } = 10;
    public SKColor Color { get; set; } = SKColors.Black;
    public string HAlign { get; set; } = "left";
    public string VAlign { get; set; } = "baseline";

    public Text3D(string t, double x, double y, double z) { Text = t; X = x; Y = y; Z = z; ZOrder = 3; }

    public override DataBounds? Bounds => null;
    public override (double, double)? ZBounds => null;

    public override void Draw(DrawContext c)
    {
        var (px, py, _) = Plot3D.Project(c.Axes, X, Y, Z);
        string text = MathText.Convert(Text);
        var sz = TextMetrics.Measure(text, FontSize, c.Dpi);
        double cx = HAlign switch { "center" => px, "right" => px - sz.Width / 2, _ => px + sz.Width / 2 };
        double cy = VAlign switch { "center" => py, "top" => py + sz.Height / 2, "bottom" => py - sz.Height / 2, _ => py - sz.Ascent + sz.Height / 2 };
        Renderer.DrawText(c.Canvas, text, cx, cy, FontSize, c.Dpi, WithAlpha(Color, Alpha), 0, false, false);
    }
}

/// <summary>mplot3d's camera: the same world/view/perspective matrices matplotlib builds (box aspect 4:4:3, eye distance 10, focal length 1),
/// then a uniform fit of the projected unit box into the axes rectangle.</summary>
public static class Plot3D
{
    private const double AspectScale = 1.8294640721620434;
    private static readonly double[] Box = { 4, 4, 3 };

    public static (double x0, double x1) Lim(Axes ax, int i) => i switch
    {
        0 => ax.ViewX(),
        1 => ax.ViewY(),
        _ => ax.ViewZ(),
    };

    private static double[,] Matrix(Axes ax, out double[] eye)
    {
        double n = Math.Sqrt(Box.Sum(b => b * b));
        var asp = Box.Select(b => b / n * AspectScale).ToArray();
        var R = asp.Select(v => v * 0.5).ToArray();
        double el = ax.View.Elev * Math.PI / 180, az = ax.View.Azim * Math.PI / 180;
        var ps = new[] { Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el) };
        eye = new[] { R[0] + 10 * ps[0], R[1] + 10 * ps[1], R[2] + 10 * ps[2] };
        var w = Norm(new[] { eye[0] - R[0], eye[1] - R[1], eye[2] - R[2] });
        var u = Norm(Cross(new[] { 0.0, 0, 1 }, w));
        var v = Cross(w, u);
        var view = new double[,]
        {
            { u[0], u[1], u[2], -Dot(u, eye) },
            { v[0], v[1], v[2], -Dot(v, eye) },
            { w[0], w[1], w[2], -Dot(w, eye) },
            { 0, 0, 0, 1 },
        };
        var proj = new double[,] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 0, -10 }, { 0, 0, -1, 0 } };
        var world = new double[4, 4];
        for (int i = 0; i < 3; i++)
        {
            var (lo, hi) = Lim(ax, i);
            world[i, i] = asp[i] / (hi - lo);
            world[i, 3] = -lo * asp[i] / (hi - lo);
        }
        world[3, 3] = 1;
        return Mul(Mul(proj, view), world);
    }

    private static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[4, 4];
        for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) for (int k = 0; k < 4; k++) r[i, j] += a[i, k] * b[k, j];
        return r;
    }

    private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Norm(double[] a) { double l = Math.Sqrt(Dot(a, a)); return new[] { a[0] / l, a[1] / l, a[2] / l }; }

    // projected unit-box extent, cached per call through the axes
    private static (double cx, double cy, double scale) Fit(Axes ax)
    {
        var m = Matrix(ax, out _);
        double minx = double.MaxValue, maxx = double.MinValue, miny = double.MaxValue, maxy = double.MinValue;
        for (int k = 0; k < 8; k++)
        {
            double[] p = new double[3];
            for (int i = 0; i < 3; i++) { var (lo, hi) = Lim(ax, i); p[i] = (k >> i & 1) == 0 ? lo : hi; }
            var (x, y, _) = Apply(m, p[0], p[1], p[2]);
            minx = Math.Min(minx, x); maxx = Math.Max(maxx, x); miny = Math.Min(miny, y); maxy = Math.Max(maxy, y);
        }
        var box = ax.Box;
        // matplotlib keeps the projected box inside a square view (±~0.8 of the half side) — fit the same way
        double scale = 0.86 * Math.Min(box.Width / (maxx - minx), box.Height / (maxy - miny));
        return ((minx + maxx) / 2, (miny + maxy) / 2, scale);
    }

    private static (double x, double y, double depth) Apply(double[,] m, double x, double y, double z)
    {
        double[] v = { x, y, z, 1 };
        var o = new double[4];
        for (int i = 0; i < 4; i++) o[i] = m[i, 0] * v[0] + m[i, 1] * v[1] + m[i, 2] * v[2] + m[i, 3];
        return (o[0] / o[3], o[1] / o[3], o[2] / o[3]);
    }

    public static (double x, double y, double depth) Project(Axes ax, double x, double y, double z)
    {
        var m = Matrix(ax, out _);
        var (px, py, d) = Apply(m, x, y, z);
        var (cx, cy, s) = Fit(ax);
        var box = ax.Box;
        return (box.MidX + (px - cx) * s, box.MidY - (py - cy) * s, d);
    }

    // ---------------------------------------------------------------------------------------------- the axes frame

    public static void DrawFrame(Axes ax, SKCanvas canvas, double ptPx, double dpi)
    {
        var lim = Enumerable.Range(0, 3).Select(i => Lim(ax, i)).ToArray();
        double el = ax.View.Elev * Math.PI / 180, az = ax.View.Azim * Math.PI / 180;
        var ps = new[] { Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el) };
        // back panes sit on the side away from the viewer
        double[] paneAt = Enumerable.Range(0, 3).Select(i => ps[i] > 0 ? Math.Min(lim[i].x0, lim[i].x1) : Math.Max(lim[i].x0, lim[i].x1)).ToArray();
        double[] front = Enumerable.Range(0, 3).Select(i => ps[i] > 0 ? Math.Max(lim[i].x0, lim[i].x1) : Math.Min(lim[i].x0, lim[i].x1)).ToArray();
        var ticks = Enumerable.Range(0, 3).Select(i => TicksFor(ax, i)).ToArray();

        SKPoint P(double x, double y, double z) { var (px, py, _) = Project(ax, x, y, z); return new SKPoint((float)px, (float)py); }
        void Pane(int axis)
        {
            int a = (axis + 1) % 3, b = (axis + 2) % 3;
            var c = new double[3];
            c[axis] = paneAt[axis];
            var quad = new SKPath();
            double[][] corners = { new[] { lim[a].x0, lim[b].x0 }, new[] { lim[a].x1, lim[b].x0 }, new[] { lim[a].x1, lim[b].x1 }, new[] { lim[a].x0, lim[b].x1 } };
            for (int k = 0; k < 4; k++)
            {
                c[a] = corners[k][0]; c[b] = corners[k][1];
                var pt = P(c[0], c[1], c[2]);
                if (k == 0) quad.MoveTo(pt); else quad.LineTo(pt);
            }
            quad.Close();
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(242, 242, 242, 128), IsAntialias = true };
            using var edge = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(230, 230, 230, 255), StrokeWidth = (float)(0.8 * ptPx), IsAntialias = true };
            canvas.DrawPath(quad, fill);
            canvas.DrawPath(quad, edge);
            // grid lines on the pane
            using var grid = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(176, 176, 176), StrokeWidth = (float)(0.8 * ptPx), IsAntialias = true };
            foreach (var t in ticks[a]) { var s0 = new double[3]; var s1 = new double[3]; s0[axis] = s1[axis] = paneAt[axis]; s0[a] = s1[a] = t; s0[b] = lim[b].x0; s1[b] = lim[b].x1; canvas.DrawLine(P(s0[0], s0[1], s0[2]), P(s1[0], s1[1], s1[2]), grid); }
            foreach (var t in ticks[b]) { var s0 = new double[3]; var s1 = new double[3]; s0[axis] = s1[axis] = paneAt[axis]; s0[b] = s1[b] = t; s0[a] = lim[a].x0; s1[a] = lim[a].x1; canvas.DrawLine(P(s0[0], s0[1], s0[2]), P(s1[0], s1[1], s1[2]), grid); }
        }
        for (int i = 0; i < 3; i++) Pane(i);

        // axis lines on the edges nearest the viewer (x, y along the bottom, z on the left-most vertical edge)
        // x axis: the edge parallel to x that sits lowest on screen; y axis likewise
        double bestXY = double.MinValue, xyv = 0, xzv = 0;
        foreach (var yv in new[] { lim[1].x0, lim[1].x1 }) foreach (var zv in new[] { lim[2].x0, lim[2].x1 })
        { var pt = P((lim[0].x0 + lim[0].x1) / 2, yv, zv); if (pt.Y > bestXY) { bestXY = pt.Y; xyv = yv; xzv = zv; } }
        double bestYY = double.MinValue, yxv = 0, yzv = 0;
        foreach (var xv in new[] { lim[0].x0, lim[0].x1 }) foreach (var zv in new[] { lim[2].x0, lim[2].x1 })
        { var pt = P(xv, (lim[1].x0 + lim[1].x1) / 2, zv); if (pt.Y > bestYY) { bestYY = pt.Y; yxv = xv; yzv = zv; } }
        double zEdge = xzv;
        front[1] = xyv; front[0] = yxv;
        var xl = new[] { lim[0].x0, xyv, xzv }; var xr = new[] { lim[0].x1, xyv, xzv };
        var yl = new[] { yxv, lim[1].x0, yzv }; var yr = new[] { yxv, lim[1].x1, yzv };
        // choose the vertical edge whose projection is left-most
        double bx = 0, by = 0; double bestX = double.MaxValue;
        foreach (var xv in new[] { lim[0].x0, lim[0].x1 }) foreach (var yv in new[] { lim[1].x0, lim[1].x1 })
        { var pt = P(xv, yv, lim[2].x0); if (pt.X < bestX) { bestX = pt.X; bx = xv; by = yv; } }
        using var line = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = (float)(0.8 * ptPx), IsAntialias = true };
        using var tickPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = (float)(0.8 * ptPx), IsAntialias = true };
        canvas.DrawLine(P(xl[0], xl[1], xl[2]), P(xr[0], xr[1], xr[2]), line);
        canvas.DrawLine(P(yl[0], yl[1], yl[2]), P(yr[0], yr[1], yr[2]), line);
        canvas.DrawLine(P(bx, by, lim[2].x0), P(bx, by, lim[2].x1), line);

        double fs = ax.TickLabelSize;
        void TickLabels(int axis, Func<double, double[]> at, Func<double, double[]> outward, string?[] labels, double[] pos)
        {
            for (int k = 0; k < pos.Length; k++)
            {
                var p0 = at(pos[k]); var p1 = outward(pos[k]);
                var a = P(p0[0], p0[1], p0[2]); var b = P(p1[0], p1[1], p1[2]);
                // tick mark: a short step away from the box, in screen space
                double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                double ux = dx / len, uy = dy / len;
                var tip = new SKPoint((float)(a.X + ux * 4 * ptPx), (float)(a.Y + uy * 4 * ptPx));
                canvas.DrawLine(a, tip, tickPaint);
                string text = labels[k] ?? "";
                var sz = TextMetrics.Measure(text, fs, dpi);
                double pad = 5.5 * ptPx;
                double cx = tip.X + ux * (pad + Math.Abs(ux) * sz.Width / 2), cy = tip.Y + uy * (pad + Math.Abs(uy) * sz.Height / 2);
                Renderer.DrawText(canvas, text, cx, cy, fs, dpi, SKColors.Black, 0, false, false);
            }
        }
        string[] Lab(int i) { var l = Ticks.Format(ticks[i], Math.Min(lim[i].x0, lim[i].x1), Math.Max(lim[i].x0, lim[i].x1)); return l.Text; }
        var outY = front[1] == Math.Min(lim[1].x0, lim[1].x1) ? -1.0 : 1.0;
        double ext(int i) => Math.Abs(lim[i].x1 - lim[i].x0) * 0.1;
        TickLabels(0, t => new[] { t, front[1], zEdge }, t => new[] { t, front[1] + outY * ext(1), zEdge + (ps[2] > 0 ? -1 : 1) * ext(2) * 0.0 }, Lab(0), ticks[0]);
        var outX = front[0] == Math.Min(lim[0].x0, lim[0].x1) ? -1.0 : 1.0;
        TickLabels(1, t => new[] { front[0], t, zEdge }, t => new[] { front[0] + outX * ext(0), t, zEdge }, Lab(1), ticks[1]);
        double zox = bx == Math.Min(lim[0].x0, lim[0].x1) ? -1.0 : 1.0, zoy = by == Math.Min(lim[1].x0, lim[1].x1) ? -1.0 : 1.0;
        TickLabels(2, t => new[] { bx, by, t }, t => new[] { bx + zox * ext(0), by + zoy * ext(1), t }, Lab(2), ticks[2]);

        // axis labels
        void AxisLabel(string? text, SKPoint from, SKPoint to, double rotation)
        {
            if (string.IsNullOrEmpty(text)) return;
            string t = MathText.Convert(text);
            var sz = TextMetrics.Measure(t, ax.LabelSize, dpi);
            double mx = (from.X + to.X) / 2, my = (from.Y + to.Y) / 2;
            double dx = to.X - from.X, dy = to.Y - from.Y;
            // push the label away from the box centre
            double ox = mx - ax.Box.MidX, oy = my - ax.Box.MidY, ol = Math.Sqrt(ox * ox + oy * oy);
            double off = 36 * ptPx;
            Renderer.DrawText(canvas, t, mx + (ol > 0 ? ox / ol * off : 0), my + (ol > 0 ? oy / ol * off * 0.8 : 0), ax.LabelSize, dpi, SKColors.Black, rotation, false, false);
        }
        AxisLabel(ax.XLabel, P(xl[0], xl[1], xl[2]), P(xr[0], xr[1], xr[2]), 0);
        AxisLabel(ax.YLabel, P(yl[0], yl[1], yl[2]), P(yr[0], yr[1], yr[2]), 0);
        var zl = P(bx, by, lim[2].x0); var zh = P(bx, by, lim[2].x1);
        if (!string.IsNullOrEmpty(ax.ZLabel))
        {
            string t = MathText.Convert(ax.ZLabel);
            Renderer.DrawText(canvas, t, zl.X - 40 * ptPx, (zl.Y + zh.Y) / 2, ax.LabelSize, dpi, SKColors.Black, 90, false, false);
        }
    }

    public static double[] TicksFor(Axes ax, int i)
    {
        var (a, b) = Lim(ax, i);
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        return Ticks.Locate(lo, hi, 6).Where(t => t >= lo - 1e-9 * (hi - lo) && t <= hi + 1e-9 * (hi - lo)).ToArray();
    }
}
