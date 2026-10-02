// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using SkiaSharp;

namespace NDSharp.Plot;

public readonly record struct DataBounds(double X0, double X1, double Y0, double Y1)
{
    public DataBounds Union(DataBounds o) => new(Math.Min(X0, o.X0), Math.Max(X1, o.X1), Math.Min(Y0, o.Y0), Math.Max(Y1, o.Y1));
}

/// <summary>Everything an artist needs to draw itself: the canvas, the owning axes (data → pixel) and the pt → px factor.</summary>
public sealed class DrawContext
{
    public required SKCanvas Canvas { get; init; }
    public required Axes Axes { get; init; }
    /// <summary>Pixels per typographic point (dpi / 72).</summary>
    public required double PtPx { get; init; }
    public double Dpi => PtPx * 72;
}

public abstract class Artist
{
    public string? Label { get; set; }
    public double Alpha { get; set; } = 1;
    public int ZOrder { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public Axes? Parent { get; internal set; }

    /// <summary>Data-space extent used for autoscaling (null: does not take part).</summary>
    public virtual DataBounds? Bounds => null;
    /// <summary>Edges the margins must not push past (images, bar bases).</summary>
    public virtual (double? X0, double? X1, double? Y0, double? Y1) Sticky => (null, null, null, null);
    /// <summary>Data points for "best" legend placement.</summary>
    public virtual IEnumerable<(double X, double Y)> Vertices => Enumerable.Empty<(double, double)>();
    public abstract void Draw(DrawContext c);
    /// <summary>Draws the legend handle into the box (pixels).</summary>
    public virtual void DrawLegendHandle(DrawContext c, SKRect box) { }

    internal static SKColor WithAlpha(SKColor c, double alpha) => alpha >= 1 ? c : c.WithAlpha((byte)Math.Round(c.Alpha * Math.Clamp(alpha, 0, 1)));

    internal static SKPaint Stroke(SKColor color, double widthPx, string style, double lwPt, double ptPx)
    {
        var p = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = (float)widthPx, IsAntialias = true, StrokeCap = style == "-" ? SKStrokeCap.Square : SKStrokeCap.Butt, StrokeJoin = SKStrokeJoin.Round };
        float[]? dash = style switch
        {
            "--" => new[] { 3.7f, 1.6f },
            ":" => new[] { 1f, 1.65f },
            "-." => new[] { 6.4f, 1.6f, 1f, 1.6f },
            _ => null,
        };
        if (dash is not null)
        {
            var d = dash.Select(v => (float)(v * lwPt * ptPx)).ToArray();
            p.PathEffect = SKPathEffect.CreateDash(d, 0);
            p.StrokeCap = style == ":" ? SKStrokeCap.Round : SKStrokeCap.Butt;
        }
        return p;
    }
}

// ----------------------------------------------------------------------------------------------- lines

public sealed class Line2D : Artist
{
    public double[] X { get; set; }
    public double[] Y { get; set; }
    public SKColor Color { get; set; }
    public double LineWidth { get; set; } = 1.5;
    public string LineStyle { get; set; } = "-";
    public string? Marker { get; set; }
    public double MarkerSize { get; set; } = 6;
    public SKColor? MarkerFace { get; set; }
    public SKColor? MarkerEdge { get; set; }
    public double MarkerEdgeWidth { get; set; } = 1;
    /// <summary>When true the coordinate is in axes fraction (0..1) instead of data units (axhline / axvline).</summary>
    public bool XAxesCoords { get; set; }
    public bool YAxesCoords { get; set; }

    public Line2D(double[] x, double[] y, SKColor color) { X = x; Y = y; Color = color; ZOrder = 2; }

    public override DataBounds? Bounds
    {
        get
        {
            double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
            for (int i = 0; i < X.Length; i++)
            {
                if (!XAxesCoords && double.IsFinite(X[i])) { x0 = Math.Min(x0, X[i]); x1 = Math.Max(x1, X[i]); }
                if (!YAxesCoords && double.IsFinite(Y[i])) { y0 = Math.Min(y0, Y[i]); y1 = Math.Max(y1, Y[i]); }
            }
            if (XAxesCoords) { x0 = double.NaN; x1 = double.NaN; }
            if (YAxesCoords) { y0 = double.NaN; y1 = double.NaN; }
            return new DataBounds(x0, x1, y0, y1);
        }
    }

    public override IEnumerable<(double X, double Y)> Vertices
        => XAxesCoords || YAxesCoords ? Enumerable.Empty<(double, double)>() : X.Zip(Y);

    private (double x, double y) Map(Axes ax, int i)
    {
        double x = XAxesCoords ? ax.Box.Left + X[i] * ax.Box.Width : ax.PxX(X[i]);
        double y = YAxesCoords ? ax.Box.Bottom - Y[i] * ax.Box.Height : ax.PxY(Y[i]);
        return (x, y);
    }

    public override void Draw(DrawContext c)
    {
        var ax = c.Axes;
        if (LineStyle != "None" && LineStyle != "" && X.Length > 1)
        {
            using var path = new SKPath();
            bool pen = false;
            for (int i = 0; i < X.Length; i++)
            {
                var (px, py) = Map(ax, i);
                if (!double.IsFinite(px) || !double.IsFinite(py)) { pen = false; continue; }
                if (!pen) { path.MoveTo((float)px, (float)py); pen = true; } else path.LineTo((float)px, (float)py);
            }
            using var paint = Stroke(WithAlpha(Color, Alpha), LineWidth * c.PtPx, LineStyle, LineWidth, c.PtPx);
            c.Canvas.DrawPath(path, paint);
        }
        if (Marker is not null and not "" and not "None")
            for (int i = 0; i < X.Length; i++)
            {
                var (px, py) = Map(ax, i);
                if (double.IsFinite(px) && double.IsFinite(py))
                    Markers.Draw(c.Canvas, Marker, px, py, MarkerSize * c.PtPx, WithAlpha(MarkerFace ?? Color, Alpha), WithAlpha(MarkerEdge ?? Color, Alpha), MarkerEdgeWidth * c.PtPx);
            }
    }

    public override void DrawLegendHandle(DrawContext c, SKRect box)
    {
        float cy = box.MidY;
        if (LineStyle != "None" && LineStyle != "")
        {
            using var paint = Stroke(WithAlpha(Color, Alpha), LineWidth * c.PtPx, LineStyle, LineWidth, c.PtPx);
            c.Canvas.DrawLine(box.Left, cy, box.Right, cy, paint);
        }
        if (Marker is not null and not "" and not "None")
        {
            // matplotlib draws two markers for line+marker handles ("numpoints" is 1 by default → centred marker)
            Markers.Draw(c.Canvas, Marker, box.MidX, cy, MarkerSize * c.PtPx, WithAlpha(MarkerFace ?? Color, Alpha), WithAlpha(MarkerEdge ?? Color, Alpha), MarkerEdgeWidth * c.PtPx);
        }
    }
}

public static class Markers
{
    /// <summary>Draws a matplotlib marker of the given size (px, = diameter for 'o') centred at (x, y).</summary>
    public static void Draw(SKCanvas canvas, string m, double x, double y, double size, SKColor face, SKColor edge, double edgeWidthPx)
    {
        float r = (float)(size / 2);
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = face, IsAntialias = true };
        using var line = new SKPaint { Style = SKPaintStyle.Stroke, Color = edge, StrokeWidth = (float)edgeWidthPx, IsAntialias = true, StrokeJoin = SKStrokeJoin.Miter };
        float fx = (float)x, fy = (float)y;
        void Poly(params (float, float)[] pts)
        {
            using var p = new SKPath();
            for (int i = 0; i < pts.Length; i++)
            {
                var (px, py) = (fx + pts[i].Item1 * r, fy + pts[i].Item2 * r);
                if (i == 0) p.MoveTo(px, py); else p.LineTo(px, py);
            }
            p.Close();
            canvas.DrawPath(p, fill);
            canvas.DrawPath(p, line);
        }
        switch (m)
        {
            case "o": canvas.DrawCircle(fx, fy, r, fill); canvas.DrawCircle(fx, fy, r, line); break;
            case ".": canvas.DrawCircle(fx, fy, r * 0.5f, fill); canvas.DrawCircle(fx, fy, r * 0.5f, line); break;
            case ",": canvas.DrawRect(fx - 0.5f, fy - 0.5f, 1, 1, fill); break;
            case "s": Poly((-1, -1), (1, -1), (1, 1), (-1, 1)); break;
            case "^": Poly((0, -1), (1, 1), (-1, 1)); break;
            case "v": Poly((0, 1), (1, -1), (-1, -1)); break;
            case "<": Poly((-1, 0), (1, -1), (1, 1)); break;
            case ">": Poly((1, 0), (-1, -1), (-1, 1)); break;
            case "d": Poly((0, -1), (0.6f, 0), (0, 1), (-0.6f, 0)); break;
            case "D": Poly((0, -1.41f), (1.41f, 0), (0, 1.41f), (-1.41f, 0)); break;
            case "p":
            {
                var pts = Enumerable.Range(0, 5).Select(i => ((float)Math.Sin(2 * Math.PI * i / 5), -(float)Math.Cos(2 * Math.PI * i / 5))).ToArray();
                Poly(pts); break;
            }
            case "h":
            {
                var pts = Enumerable.Range(0, 6).Select(i => ((float)Math.Sin(2 * Math.PI * i / 6), -(float)Math.Cos(2 * Math.PI * i / 6))).ToArray();
                Poly(pts); break;
            }
            case "*":
            {
                var pts = new (float, float)[10];
                for (int i = 0; i < 10; i++)
                {
                    float rad = i % 2 == 0 ? 1f : 0.382f;
                    pts[i] = (rad * (float)Math.Sin(Math.PI * i / 5), -rad * (float)Math.Cos(Math.PI * i / 5));
                }
                Poly(pts); break;
            }
            case "x":
            case "+":
            case "|":
            case "_":
            {
                using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = edge, StrokeWidth = (float)edgeWidthPx, IsAntialias = true, StrokeCap = SKStrokeCap.Butt };
                if (m == "x") { canvas.DrawLine(fx - r, fy - r, fx + r, fy + r, stroke); canvas.DrawLine(fx - r, fy + r, fx + r, fy - r, stroke); }
                if (m == "+") { canvas.DrawLine(fx - r, fy, fx + r, fy, stroke); canvas.DrawLine(fx, fy - r, fx, fy + r, stroke); }
                if (m == "|") canvas.DrawLine(fx, fy - r, fx, fy + r, stroke);
                if (m == "_") canvas.DrawLine(fx - r, fy, fx + r, fy, stroke);
                break;
            }
            default: throw new NDValueException($"{m} is not a valid marker style");
        }
    }
}

// ----------------------------------------------------------------------------------------------- scatter

public sealed class Scatter : Artist, IMappable
{
    public double[] X { get; set; }
    public double[] Y { get; set; }
    /// <summary>Marker areas in points²; one value is broadcast.</summary>
    public double[] Sizes { get; set; } = { 36 };
    public SKColor[]? Colors { get; set; }
    public double[]? Values { get; set; }
    public Colormap Cmap { get; set; } = Colormap.Get("viridis");
    public double? VMin { get; set; }
    public double? VMax { get; set; }
    public string Marker { get; set; } = "o";
    public SKColor? EdgeColor { get; set; }
    public double LineWidth { get; set; } = 1.5;

    public Scatter(double[] x, double[] y) { X = x; Y = y; ZOrder = 1; }

    public (double lo, double hi) Range
    {
        get
        {
            var fin = (Values ?? Array.Empty<double>()).Where(double.IsFinite).ToArray();
            double lo = VMin ?? (fin.Length > 0 ? fin.Min() : 0), hi = VMax ?? (fin.Length > 0 ? fin.Max() : 1);
            return (lo, hi);
        }
    }

    public SKColor FaceColor(int i)
    {
        if (Values is not null)
        {
            var (lo, hi) = Range;
            return Cmap.At(hi == lo ? 0 : (Values[i] - lo) / (hi - lo));
        }
        var cs = Colors ?? new[] { SKColor.Parse("#1f77b4") };
        return cs[i % cs.Length];
    }

    public override DataBounds? Bounds
    {
        get
        {
            var fx = X.Where(double.IsFinite).ToArray(); var fy = Y.Where(double.IsFinite).ToArray();
            return fx.Length == 0 || fy.Length == 0 ? null : new DataBounds(fx.Min(), fx.Max(), fy.Min(), fy.Max());
        }
    }
    public override IEnumerable<(double X, double Y)> Vertices => X.Zip(Y);

    public override void Draw(DrawContext c)
    {
        var ax = c.Axes;
        for (int i = 0; i < X.Length; i++)
        {
            double px = ax.PxX(X[i]), py = ax.PxY(Y[i]);
            if (!double.IsFinite(px) || !double.IsFinite(py)) continue;
            double size = Math.Sqrt(Sizes[i % Sizes.Length]) * c.PtPx;
            var face = WithAlpha(FaceColor(i), Alpha);
            var edge = WithAlpha(EdgeColor ?? FaceColor(i), Alpha);
            Markers.Draw(c.Canvas, Marker, px, py, size, face, edge, LineWidth * c.PtPx);
        }
    }

    public override void DrawLegendHandle(DrawContext c, SKRect box)
        => Markers.Draw(c.Canvas, Marker, box.MidX, box.MidY, Math.Sqrt(Sizes[0]) * c.PtPx, WithAlpha(FaceColor(0), Alpha), WithAlpha(EdgeColor ?? FaceColor(0), Alpha), LineWidth * c.PtPx);
}

// ----------------------------------------------------------------------------------------------- patches

public abstract class Patch : Artist
{
    public SKColor? FaceColor { get; set; }
    public SKColor? EdgeColor { get; set; }
    public double LineWidth { get; set; } = 1;
    public string LineStyle { get; set; } = "-";
    public bool Fill { get; set; } = true;
    public string? Hatch { get; set; }
    /// <summary>True for fill_between polygons, which matplotlib keeps in ax.collections rather than ax.patches.</summary>
    public bool InCollections { get; set; }

    protected Patch() { ZOrder = 1; }

    protected abstract SKPath BuildPath(Axes ax);
    protected virtual IEnumerable<(double X, double Y)> DataPoints() => Enumerable.Empty<(double, double)>();
    public override IEnumerable<(double X, double Y)> Vertices => DataPoints();

    public override DataBounds? Bounds
    {
        get
        {
            var pts = DataPoints().Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToArray();
            return pts.Length == 0 ? null : new DataBounds(pts.Min(p => p.X), pts.Max(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.Y));
        }
    }

    // matplotlib: a filled patch with no explicit edge has none; an unfilled one is edged black; unspecified fill is C0.
    private SKColor? Face => Fill ? FaceColor ?? SKColor.Parse("#1f77b4") : null;
    private SKColor? Edge => EdgeColor ?? (Fill ? null : SKColors.Black);

    public override void Draw(DrawContext c)
    {
        using var path = BuildPath(c.Axes);
        if (Face is { } f && f.Alpha > 0)
        {
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = WithAlpha(f, Alpha), IsAntialias = true };
            c.Canvas.DrawPath(path, fill);
        }
        if (Edge is { } e && e.Alpha > 0 && LineWidth > 0)
        {
            using var stroke = Stroke(WithAlpha(e, Alpha), LineWidth * c.PtPx, LineStyle, LineWidth, c.PtPx);
            stroke.StrokeCap = SKStrokeCap.Butt;
            stroke.StrokeJoin = SKStrokeJoin.Miter;
            c.Canvas.DrawPath(path, stroke);
        }
    }

    public override void DrawLegendHandle(DrawContext c, SKRect box)
    {
        // a handle is a 2 × 0.7 font-size rectangle
        if (Face is { } f)
        {
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = WithAlpha(f, Alpha), IsAntialias = true };
            c.Canvas.DrawRect(box, fill);
        }
        if (Edge is { } e)
        {
            using var stroke = Stroke(WithAlpha(e, Alpha), LineWidth * c.PtPx, LineStyle, LineWidth, c.PtPx);
            c.Canvas.DrawRect(box, stroke);
        }
    }
}

public sealed class RectPatch : Patch
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    /// <summary>Degrees about (X, Y); only meaningful for linear axes.</summary>
    public double Angle { get; set; }
    /// <summary>True for bars: the base edge is sticky at zero.</summary>
    public bool StickyBase { get; set; }
    public bool Horizontal { get; set; }

    public RectPatch(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }

    protected override IEnumerable<(double X, double Y)> DataPoints()
    {
        yield return (X, Y);
        yield return (X + Width, Y + Height);
    }

    public override (double? X0, double? X1, double? Y0, double? Y1) Sticky
        => !StickyBase ? (null, null, null, null) : Horizontal ? (0, 0, null, null) : (null, null, 0, 0);

    protected override SKPath BuildPath(Axes ax)
    {
        var p = new SKPath();
        float x0 = (float)ax.PxX(X), x1 = (float)ax.PxX(X + Width), y0 = (float)ax.PxY(Y), y1 = (float)ax.PxY(Y + Height);
        p.AddRect(new SKRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1)));
        if (Angle != 0)
            p.Transform(SKMatrix.CreateRotationDegrees((float)-Angle, (float)ax.PxX(X), (float)ax.PxY(Y)));
        return p;
    }
}

public sealed class EllipsePatch : Patch
{
    public double CX { get; set; }
    public double CY { get; set; }
    public double RX { get; set; }
    public double RY { get; set; }

    public EllipsePatch(double cx, double cy, double rx, double ry) { CX = cx; CY = cy; RX = rx; RY = ry; }

    protected override IEnumerable<(double X, double Y)> DataPoints()
    {
        yield return (CX - RX, CY - RY);
        yield return (CX + RX, CY + RY);
    }

    protected override SKPath BuildPath(Axes ax)
    {
        var p = new SKPath();
        float x0 = (float)ax.PxX(CX - RX), x1 = (float)ax.PxX(CX + RX), y0 = (float)ax.PxY(CY - RY), y1 = (float)ax.PxY(CY + RY);
        p.AddOval(new SKRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1)));
        return p;
    }
}

public sealed class PolygonPatch : Patch
{
    public double[] X { get; set; }
    public double[] Y { get; set; }
    public bool Closed { get; set; } = true;

    public PolygonPatch(double[] x, double[] y) { X = x; Y = y; }

    protected override IEnumerable<(double X, double Y)> DataPoints() => X.Zip(Y);

    protected override SKPath BuildPath(Axes ax)
    {
        var p = new SKPath();
        for (int i = 0; i < X.Length; i++)
        {
            float px = (float)ax.PxX(X[i]), py = (float)ax.PxY(Y[i]);
            if (i == 0) p.MoveTo(px, py); else p.LineTo(px, py);
        }
        if (Closed) p.Close();
        return p;
    }
}

/// <summary>ax.arrow(): matplotlib's FancyArrow polygon, built in data coordinates.</summary>
public static class FancyArrow
{
    public static PolygonPatch Create(double x, double y, double dx, double dy, double width, double headWidth, double headLength, bool lengthIncludesHead, double overhang = 0)
    {
        double distance = double.Hypot(dx, dy);
        double length = lengthIncludesHead ? distance : distance + headLength;
        if (length == 0) return new PolygonPatch(Array.Empty<double>(), Array.Empty<double>());
        double hw = headWidth, hl = headLength, hs = overhang, lw = width;
        // half arrow with the tip at the origin, pointing along +x, tail at -length
        var left = new (double, double)[] { (0, 0), (-hl, -hw / 2), (-hl * (1 - hs), -lw / 2), (-length, -lw / 2), (-length, 0) };
        var pts = new List<(double, double)>();
        for (int i = 0; i < left.Length - 1; i++) pts.Add(left[i]);
        for (int i = left.Length - 2; i >= 0; i--) pts.Add((left[i].Item1, -left[i].Item2));
        double cx = distance == 0 ? 1 : dx / distance, sx = distance == 0 ? 0 : dy / distance;
        // tip lands at start + length * unit
        double tx = x + cx * length, ty = y + sx * length;
        var xs = pts.Select(p => p.Item1 * cx - p.Item2 * sx + tx).ToArray();
        var ys = pts.Select(p => p.Item1 * sx + p.Item2 * cx + ty).ToArray();
        return new PolygonPatch(xs, ys);
    }
}

// ----------------------------------------------------------------------------------------------- text

public sealed class TextArtist : Artist
{
    public string Text { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Anchor space of (X, Y): data, axes fraction, or figure fraction.</summary>
    public CoordSpace Space { get; set; } = CoordSpace.Data;
    public double FontSize { get; set; } = 10;
    public SKColor Color { get; set; } = SKColors.Black;
    public string HAlign { get; set; } = "left";
    public string VAlign { get; set; } = "baseline";
    public double Rotation { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public double? Linespacing { get; set; }
    public (SKColor face, SKColor edge, double pad)? BBox { get; set; }

    // annotate(): arrow from the text to a data point
    /// <summary>annotate(textcoords="offset points"): the text sits this many points from (X, Y).</summary>
    public (double X, double Y)? OffsetPoints { get; set; }
    public (double X, double Y)? ArrowTo { get; set; }
    public CoordSpace ArrowSpace { get; set; } = CoordSpace.Data;
    public ArrowProps? Arrow { get; set; }

    public TextArtist(string text, double x, double y) { Text = text; X = x; Y = y; ZOrder = 3; }

    public override DataBounds? Bounds => null;

    public (double x, double y) Anchor(Axes ax)
    {
        var (px, py) = ax.ToPx(X, Y, Space);
        if (OffsetPoints is { } o) { double s = ax.Figure.Dpi / 72; px += o.X * s; py -= o.Y * s; }
        return (px, py);
    }

    /// <summary>Pixel rectangle of the text including rotation.</summary>
    public SKRect Extent(Axes ax, double dpi)
    {
        var (ax0, ay0) = Anchor(ax);
        return Layout(MathText.Convert(Text), ax0, ay0, dpi, out _, out _);
    }

    private SKRect Layout(string text, double ax0, double ay0, double dpi, out double cx, out double cy)
    {
        var sz = TextMetrics.Measure(text, FontSize, dpi, Bold, Italic);
        double w = sz.Width, h = sz.Height;
        double th = Rotation * Math.PI / 180, c = Math.Abs(Math.Cos(th)), s = Math.Abs(Math.Sin(th));
        double bw = w * c + h * s, bh = w * s + h * c;
        double left = HAlign switch { "center" => ax0 - bw / 2, "right" => ax0 - bw, _ => ax0 };
        double top = VAlign switch
        {
            "center" or "center_baseline" => ay0 - bh / 2,
            "top" => ay0,
            "bottom" => ay0 - bh,
            _ => ay0 - (bh - (Rotation == 0 ? sz.Descent : 0)), // baseline: bottom of the box sits a descent below the anchor
        };
        cx = left + bw / 2; cy = top + bh / 2;
        return new SKRect((float)left, (float)top, (float)(left + bw), (float)(top + bh));
    }

    public override void Draw(DrawContext c)
    {
        var ax = c.Axes;
        string text = MathText.Convert(Text);
        var (x0, y0) = Anchor(ax);
        var r = Layout(text, x0, y0, c.Dpi, out double cx, out double cy);
        if (Arrow is not null && ArrowTo is { } to) DrawArrow(c, r, to);
        if (BBox is { } bb)
        {
            float pad = (float)(bb.pad * FontSize * c.PtPx);
            var box = new SKRect(r.Left - pad, r.Top - pad, r.Right + pad, r.Bottom + pad);
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = bb.face, IsAntialias = true };
            using var edge = new SKPaint { Style = SKPaintStyle.Stroke, Color = bb.edge, StrokeWidth = (float)c.PtPx, IsAntialias = true };
            c.Canvas.DrawRoundRect(box, 0.25f * (float)(FontSize * c.PtPx), 0.25f * (float)(FontSize * c.PtPx), fill);
            c.Canvas.DrawRoundRect(box, 0.25f * (float)(FontSize * c.PtPx), 0.25f * (float)(FontSize * c.PtPx), edge);
        }
        Renderer.DrawText(c.Canvas, text, cx, cy, FontSize, c.Dpi, WithAlpha(Color, Alpha), Rotation, Bold, Italic);
    }

    private void DrawArrow(DrawContext c, SKRect textBox, (double X, double Y) to)
    {
        var ap = Arrow!;
        var (tx, ty) = c.Axes.ToPx(to.X, to.Y, ArrowSpace);
        // the arrow leaves from the text box centre, clipped at its edge (matplotlib: relpos (0.5, 0.5) + shrinkA 2pt)
        double sx = textBox.MidX, sy = textBox.MidY;
        double dx = tx - sx, dy = ty - sy, len = double.Hypot(dx, dy);
        if (len < 1e-9) return;
        double ux = dx / len, uy = dy / len;
        // clip the start to the text rectangle
        double tmin = double.PositiveInfinity;
        if (Math.Abs(ux) > 1e-12) tmin = Math.Min(tmin, (textBox.Width / 2) / Math.Abs(ux));
        if (Math.Abs(uy) > 1e-12) tmin = Math.Min(tmin, (textBox.Height / 2) / Math.Abs(uy));
        double shrinkA = ap.ShrinkA * c.PtPx, shrinkB = ap.ShrinkB * c.PtPx;
        double startOff = tmin + shrinkA;
        double x1 = sx + ux * startOff, y1 = sy + uy * startOff;
        double x2 = tx - ux * shrinkB, y2 = ty - uy * shrinkB;
        var color = WithAlpha(ap.Color ?? SKColors.Black, Alpha);
        double lw = ap.LineWidth * c.PtPx;
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = (float)lw, IsAntialias = true };
        string style = ap.Style;
        double headLen = 0.4 * FontSize * c.PtPx * ap.MutationScale / 10;
        double headWid = 0.2 * FontSize * c.PtPx * ap.MutationScale / 10;
        if (style is "-" or "")
        {
            c.Canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, paint);
            return;
        }
        if (style is "->" or "-[")
        {
            c.Canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, paint);
            using var head = new SKPath();
            double nx = -uy, ny = ux;
            head.MoveTo((float)(x2 - ux * headLen + nx * headWid), (float)(y2 - uy * headLen + ny * headWid));
            head.LineTo((float)x2, (float)y2);
            head.LineTo((float)(x2 - ux * headLen - nx * headWid), (float)(y2 - uy * headLen - ny * headWid));
            c.Canvas.DrawPath(head, paint);
            return;
        }
        // "-|>", "simple", "fancy", "wedge": filled head
        c.Canvas.DrawLine((float)x1, (float)y1, (float)(x2 - ux * headLen * 0.9), (float)(y2 - uy * headLen * 0.9), paint);
        using var tri = new SKPath();
        double hx = -uy, hy = ux;
        tri.MoveTo((float)x2, (float)y2);
        tri.LineTo((float)(x2 - ux * headLen + hx * headWid), (float)(y2 - uy * headLen + hy * headWid));
        tri.LineTo((float)(x2 - ux * headLen - hx * headWid), (float)(y2 - uy * headLen - hy * headWid));
        tri.Close();
        using var fill = new SKPaint { Style = SKPaintStyle.StrokeAndFill, Color = color, StrokeWidth = (float)lw, IsAntialias = true, StrokeJoin = SKStrokeJoin.Miter };
        c.Canvas.DrawPath(tri, fill);
    }
}

public enum CoordSpace { Data, Axes, Figure, Pixels }

public sealed class ArrowProps
{
    public string Style { get; set; } = "-";
    public SKColor? Color { get; set; }
    public double LineWidth { get; set; } = 1;
    public double ShrinkA { get; set; } = 2;
    public double ShrinkB { get; set; } = 2;
    public double MutationScale { get; set; } = 10;
}

// ----------------------------------------------------------------------------------------------- images

public sealed class ImageArtist : Artist, IMappable
{
    /// <summary>Scalar data (rows × cols), mapped through <see cref="Cmap"/>; null when <see cref="Rgba"/> is set.</summary>
    public double[]? Values { get; set; }
    /// <summary>Direct colours, 4 bytes per pixel (rows × cols × RGBA).</summary>
    public byte[]? Rgba { get; set; }
    public int Rows { get; set; }
    public int Cols { get; set; }
    public Colormap Cmap { get; set; } = Colormap.Get("viridis");
    public double? VMin { get; set; }
    public double? VMax { get; set; }
    public string Origin { get; set; } = "upper";
    /// <summary>(left, right, bottom, top); null = pixel-centred default.</summary>
    public (double L, double R, double B, double T)? ExtentValue { get; set; }
    public string Interpolation { get; set; } = "antialiased";

    public ImageArtist() { ZOrder = 0; }

    public (double lo, double hi) Range
    {
        get
        {
            var fin = (Values ?? Array.Empty<double>()).Where(double.IsFinite).ToArray();
            return (VMin ?? (fin.Length > 0 ? fin.Min() : 0), VMax ?? (fin.Length > 0 ? fin.Max() : 1));
        }
    }

    public (double L, double R, double B, double T) Extent
        => ExtentValue ?? (Origin == "lower" ? (-0.5, Cols - 0.5, -0.5, Rows - 0.5) : (-0.5, Cols - 0.5, Rows - 0.5, -0.5));

    public override DataBounds? Bounds
    {
        get { var e = Extent; return new DataBounds(Math.Min(e.L, e.R), Math.Max(e.L, e.R), Math.Min(e.B, e.T), Math.Max(e.B, e.T)); }
    }
    public override (double? X0, double? X1, double? Y0, double? Y1) Sticky
    {
        get { var b = Bounds!.Value; return (b.X0, b.X1, b.Y0, b.Y1); }
    }

    private SKBitmap ToBitmap()
    {
        var bmp = new SKBitmap(new SKImageInfo(Cols, Rows, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var px = new byte[Rows * Cols * 4];
        if (Rgba is not null) Array.Copy(Rgba, px, px.Length);
        else
        {
            var (lo, hi) = Range;
            for (int i = 0; i < Rows * Cols; i++)
            {
                double t = hi == lo ? 0 : (Values![i] - lo) / (hi - lo);
                var col = double.IsNaN(Values![i]) ? SKColors.Transparent : Cmap.At(t);
                px[4 * i] = col.Red; px[4 * i + 1] = col.Green; px[4 * i + 2] = col.Blue; px[4 * i + 3] = col.Alpha;
            }
        }
        if (Alpha < 1) for (int i = 0; i < Rows * Cols; i++) px[4 * i + 3] = (byte)Math.Round(px[4 * i + 3] * Alpha);
        System.Runtime.InteropServices.Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
        return bmp;
    }

    public override void Draw(DrawContext c)
    {
        var ax = c.Axes;
        var e = Extent;
        // origin "upper": row 0 sits at the extent's top value, origin "lower": at its bottom value
        double yRow0 = Origin == "lower" ? e.B : e.T, yRowN = Origin == "lower" ? e.T : e.B;
        double xa = ax.PxX(e.L), xb = ax.PxX(e.R), ya = ax.PxY(yRow0), yb = ax.PxY(yRowN);
        double sx = (xb - xa) / Math.Max(Cols, 1), sy = (yb - ya) / Math.Max(Rows, 1);
        using var bmp = ToBitmap();
        using var img = SKImage.FromBitmap(bmp);
        double scale = Math.Min(Math.Abs(sx), Math.Abs(sy));
        var sampling = Interpolation switch
        {
            "nearest" or "none" => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
            "bilinear" => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
            "antialiased" => scale > 3 ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
                : scale >= 1 ? new SKSamplingOptions(new SKCubicResampler(1 / 3f, 1 / 3f))
                : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            _ => new SKSamplingOptions(new SKCubicResampler(1 / 3f, 1 / 3f)),
        };
        c.Canvas.Save();
        c.Canvas.Concat(new SKMatrix((float)sx, 0, (float)xa, 0, (float)sy, (float)ya, 0, 0, 1));
        using var paint = new SKPaint { IsAntialias = false };
        c.Canvas.DrawImage(img, new SKRect(0, 0, Cols, Rows), sampling, paint);
        c.Canvas.Restore();
    }
}
