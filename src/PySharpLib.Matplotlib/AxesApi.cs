// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Plot;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using SkiaSharp;
using PlotAxes = NDSharp.Plot.Axes;

namespace PySharpLib.Matplotlib;

internal delegate object AxFn(PlotAxes ax, Interp interp, object[] a, Kw kw);

/// <summary>The Axes methods. The same table backs <c>ax.method(...)</c> and <c>plt.method(...)</c> (which targets the current axes).</summary>
internal static class AxesApi
{
    public static readonly Dictionary<string, AxFn> Table = Build();

    private static object Arg(object[] a, int i, string name = "argument")
        => i < a.Length ? a[i] : throw PyErr.TypeError($"missing required argument '{name}'");

    private static object? Opt(object[] a, int i) => i < a.Length && a[i] is not PyNone ? a[i] : null;

    private static PyList L(IEnumerable<object> items) => new(items);

    private static object W(object native) => Classes.W(native);

    /// <summary>x values: numbers, or categories (strings) that become tick labels 0..n-1.</summary>
    private static double[] XValues(PlotAxes ax, object o, bool xAxis = true)
    {
        if (M.IsStringSeq(o))
        {
            var labels = M.StrList(o);
            var pos = Enumerable.Range(0, labels.Length).Select(i => (double)i).ToArray();
            if (xAxis) { ax.XTicksSet = pos; ax.XTickLabelsSet = labels; } else { ax.YTicksSet = pos; ax.YTickLabelsSet = labels; }
            return pos;
        }
        return M.Dbl(o);
    }

    private static double[] Broadcast(double[] v, int n) => v.Length == n ? v : v.Length == 1 ? Enumerable.Repeat(v[0], n).ToArray() : throw PyErr.ValueError($"shape mismatch: objects cannot be broadcast to a single shape ({v.Length} vs {n})");

    private static Line2D MakeLine(PlotAxes ax, double[] x, double[] y, SKColor? fmtColor, string? fmtLine, string? fmtMarker, Kw kw)
    {
        var color = kw.Get("color", "c") is { } co ? M.Color(co) : fmtColor ?? ax.NextColor();
        if (kw.Get("color", "c") is not null && fmtColor is null) { } // explicit colour does not advance the cycle
        var line = new Line2D(x, y, color)
        {
            LineWidth = kw.Dbl(1.5, "linewidth", "lw"),
            Alpha = kw.Dbl(1, "alpha"),
            ZOrder = kw.Int(2, "zorder"),
        };
        string? ls = kw.Str("linestyle", "ls") is { } k ? M.NormalizeLineStyle(k) : fmtLine;
        string? marker = kw.Str("marker") ?? fmtMarker;
        line.Marker = marker is "None" or "none" or " " ? null : marker;
        line.LineStyle = ls ?? (line.Marker is not null && fmtMarker is not null ? "None" : "-");
        line.MarkerSize = kw.Dbl(6, "markersize", "ms");
        if (kw.Get("markerfacecolor", "mfc") is { } mf) line.MarkerFace = M.Color(mf);
        if (kw.Get("markeredgecolor", "mec") is { } me) line.MarkerEdge = M.Color(me);
        line.MarkerEdgeWidth = kw.Dbl(1, "markeredgewidth", "mew");
        if (kw.Str("label") is { } lab) line.Label = lab;
        return line;
    }

    private static Dictionary<string, AxFn> Build()
    {
        var t = new Dictionary<string, AxFn>();

        // ------------------------------------------------------------------ plot & friends

        t["plot"] = (ax, i, a, kw) =>
        {
            if (ax.Is3D) return Plot3DLine(ax, a, kw);
            var lines = new List<Line2D>();
            int p = 0;
            while (p < a.Length)
            {
                object first = a[p++];
                object? second = null; string? fmt = null;
                if (p < a.Length && a[p] is string fs0 && first is not string) { fmt = fs0; p++; }
                else if (p < a.Length && a[p] is not string) { second = a[p++]; if (p < a.Length && a[p] is string fs1) { fmt = fs1; p++; } }
                double[] x, y; int cols = 1; int rows;
                double[] xs;
                NDArray? ynd = null;
                if (second is null)
                {
                    ynd = M.Nd(first);
                    rows = ynd.Ndim == 0 ? 1 : ynd.Shape[0];
                    cols = ynd.Ndim == 2 ? ynd.Shape[1] : 1;
                    xs = Enumerable.Range(0, rows).Select(v => (double)v).ToArray();
                }
                else
                {
                    xs = XValues(ax, first);
                    ynd = M.Nd(second);
                    rows = ynd.Ndim == 0 ? 1 : ynd.Shape[0];
                    cols = ynd.Ndim == 2 ? ynd.Shape[1] : 1;
                }
                y = ynd.ToArray<double>();
                var (fc, fl, fm) = fmt is null ? (null, null, null) : M.ParseFmt(fmt);
                bool x2d = second is not null && Conv.TryND(first, out var xnd0) && xnd0.Ndim == 2;
                double[] xAll = x2d ? M.Dbl(first) : xs;
                for (int c = 0; c < cols; c++)
                {
                    var yc = cols == 1 ? y : Enumerable.Range(0, rows).Select(r => y[r * cols + c]).ToArray();
                    var xc = x2d ? Enumerable.Range(0, rows).Select(r => xAll[r * cols + c]).ToArray() : xs;
                    if (xc.Length != yc.Length) throw PyErr.ValueError($"x and y must have same first dimension, but have shapes ({xc.Length},) and ({yc.Length},)");
                    var line = MakeLine(ax, xc, yc, fc, fl, fm, kw);
                    ax.Add(line);
                    lines.Add(line);
                }
            }
            return L(lines.Select(l => W(l)));
        };

        t["semilogy"] = (ax, i, a, kw) => { ax.YScale = "log"; return t["plot"](ax, i, a, kw); };
        t["semilogx"] = (ax, i, a, kw) => { ax.XScale = "log"; return t["plot"](ax, i, a, kw); };
        t["loglog"] = (ax, i, a, kw) => { ax.XScale = "log"; ax.YScale = "log"; return t["plot"](ax, i, a, kw); };

        t["scatter"] = (ax, i, a, kw) =>
        {
            if (ax.Is3D) return Scatter3DApi(ax, a, kw);
            var x = XValues(ax, Arg(a, 0, "x"));
            var y = M.Dbl(Arg(a, 1, "y"));
            if (x.Length != y.Length) throw PyErr.ValueError("x and y must be the same size");
            var sc = new Scatter(x, y) { Alpha = kw.Dbl(1, "alpha"), ZOrder = kw.Int(1, "zorder") };
            if ((Opt(a, 2) ?? kw.Get("s")) is { } s) sc.Sizes = M.Dbl(s);
            if (kw.Str("marker") is { } mk) sc.Marker = mk;
            var cobj = Opt(a, 3) ?? kw.Get("c", "color", "facecolors", "facecolor");
            bool explicitColor = kw.Has("color") || kw.Has("facecolors", "facecolor");
            if (cobj is not null)
            {
                var cl = explicitColor ? M.ColorList(cobj) ?? new[] { M.Color(cobj) } : M.ColorList(cobj);
                if (cl is not null) sc.Colors = cl;
                else if (Conv.TryND(cobj, out var cnd) && (cnd.Ndim == 1 || (cnd.Ndim == 2 && cnd.Shape[1] == 1)) && cnd.Size == x.Length) sc.Values = cnd.ToArray<double>();
                else if (cobj is PyList or PyTuple && M.Dbl(cobj) is { Length: 3 or 4 } rgb && x.Length != rgb.Length) sc.Colors = new[] { M.Color(cobj) };
                else if (cobj is PyList or PyTuple) sc.Values = M.Dbl(cobj);
                else sc.Colors = new[] { M.Color(cobj) };
            }
            else sc.Colors = new[] { ax.NextColor() };
            sc.Cmap = Cmaps.Resolve(kw.Get("cmap"));
            sc.VMin = kw.DblOrNull("vmin"); sc.VMax = kw.DblOrNull("vmax");
            if (kw.Get("norm") is { } nm) Cmaps.ApplyNorm(nm, v => sc.VMin = v, v => sc.VMax = v);
            if (kw.Get("edgecolors", "edgecolor") is { } ec && !(ec is string es && es == "face")) sc.EdgeColor = M.Color(ec);
            sc.LineWidth = kw.Dbl(1.5, "linewidths", "linewidth", "lw");
            if (kw.Str("label") is { } lab) sc.Label = lab;
            ax.Add(sc);
            return W(sc);
        };

        t["bar"] = (ax, i, a, kw) => Bar(ax, a, kw, false);
        t["barh"] = (ax, i, a, kw) => Bar(ax, a, kw, true);
        t["hist"] = (ax, i, a, kw) => Hist(ax, a, kw);

        t["imshow"] = (ax, i, a, kw) =>
        {
            var nd = M.Nd(Arg(a, 0, "X"));
            var im = new ImageArtist { Alpha = kw.Dbl(1, "alpha") };
            if (nd.Ndim == 3 && nd.Shape[2] is 3 or 4)
            {
                int r = nd.Shape[0], c = nd.Shape[1], k = nd.Shape[2];
                var v = nd.ToArray<double>();
                bool isByte = nd.DType is DType.UInt8;
                var px = new byte[r * c * 4];
                double sc = isByte ? 1 : 255;
                for (int q = 0; q < r * c; q++)
                {
                    for (int ch = 0; ch < 3; ch++) px[4 * q + ch] = (byte)Math.Clamp(Math.Round(v[q * k + ch] * sc), 0, 255);
                    px[4 * q + 3] = k == 4 ? (byte)Math.Clamp(Math.Round(v[q * k + 3] * sc), 0, 255) : (byte)255;
                }
                im.Rgba = px; im.Rows = r; im.Cols = c;
            }
            else if (nd.Ndim == 2)
            {
                im.Rows = nd.Shape[0]; im.Cols = nd.Shape[1];
                im.Values = nd.ToArray<double>();
            }
            else throw PyErr.TypeError($"Invalid shape {string.Join(",", nd.Shape)} for image data");
            im.Cmap = Cmaps.Resolve(kw.Get("cmap"));
            im.VMin = kw.DblOrNull("vmin"); im.VMax = kw.DblOrNull("vmax");
            if (kw.Get("norm") is { } nm) Cmaps.ApplyNorm(nm, v => im.VMin = v, v => im.VMax = v);
            if (kw.Str("origin") is { } o) im.Origin = o;
            if (kw.Str("interpolation") is { } ip) im.Interpolation = ip;
            if (kw.Get("extent") is { } ex) { var e = M.Dbl(ex); im.ExtentValue = (e[0], e[1], e[2], e[3]); }
            ax.Add(im);
            if (kw.Get("aspect") is { } asp) SetAspect(ax, asp);
            var ext = im.Extent;
            ax.XLimSet = (ext.L, ext.R);
            ax.YLimSet = (ext.B, ext.T);
            return W(im);
        };

        t["contour"] = (ax, i, a, kw) => Contour(ax, a, kw, false);
        t["contourf"] = (ax, i, a, kw) => Contour(ax, a, kw, true);
        t["clabel"] = (ax, i, a, kw) => PyNone.Instance;

        t["fill_between"] = (ax, i, a, kw) =>
        {
            var x = M.Dbl(Arg(a, 0, "x"));
            var y1 = Broadcast(M.Dbl(Arg(a, 1, "y1")), x.Length);
            var y2 = Broadcast(a.Length > 2 && a[2] is not PyNone ? M.Dbl(a[2]) : kw.Get("y2") is { } y2o ? M.Dbl(y2o) : new[] { 0.0 }, x.Length);
            bool[]? where = kw.Get("where") is { } w ? Conv.ND(w).ToArray<bool>() : null;
            var color = kw.Get("color", "facecolor", "fc") is { } co ? M.Color(co) : ax.NextColor();
            var segs = new List<(int s, int e)>();
            int start = -1;
            for (int q = 0; q < x.Length; q++)
            {
                bool on = where is null || where[q];
                if (on && start < 0) start = q;
                if (!on && start >= 0) { segs.Add((start, q - 1)); start = -1; }
            }
            if (start >= 0) segs.Add((start, x.Length - 1));
            PolygonPatch? first = null;
            foreach (var (s, e) in segs)
            {
                var px = new List<double>(); var py = new List<double>();
                for (int q = s; q <= e; q++) { px.Add(x[q]); py.Add(y1[q]); }
                for (int q = e; q >= s; q--) { px.Add(x[q]); py.Add(y2[q]); }
                var poly = new PolygonPatch(px.ToArray(), py.ToArray()) { InCollections = true, FaceColor = color, Alpha = kw.Dbl(1, "alpha"), ZOrder = kw.Int(1, "zorder") };
                if (kw.Get("edgecolor", "ec") is { } ec) { poly.EdgeColor = M.Color(ec); poly.LineWidth = kw.Dbl(1, "linewidth", "lw"); }
                if (first is null) { first = poly; if (kw.Str("label") is { } lab) poly.Label = lab; }
                ax.Add(poly);
            }
            return first is null ? PyNone.Instance : W(first);
        };

        t["stem"] = (ax, i, a, kw) =>
        {
            double[] x, y;
            if (a.Length >= 2 && a[1] is not string) { x = M.Dbl(a[0]); y = M.Dbl(a[1]); }
            else { y = M.Dbl(Arg(a, 0, "y")); x = Enumerable.Range(0, y.Length).Select(v => (double)v).ToArray(); }
            var color = kw.Get("color") is { } co ? M.Color(co) : ax.NextColor();
            double baseY = kw.Dbl(0, "bottom");
            var baseLine = new Line2D(new[] { x.Min(), x.Max() }, new[] { baseY, baseY }, SKColor.Parse("#d62728")) { LineWidth = 1.5 };
            var markers = new Line2D(x, y, color) { LineStyle = "None", Marker = "o" };
            var stems = new List<Line2D>();
            for (int q = 0; q < x.Length; q++) stems.Add(new Line2D(new[] { x[q], x[q] }, new[] { baseY, y[q] }, color) { LineWidth = 1.5 });
            foreach (var s in stems) ax.Add(s);
            ax.Add(baseLine); ax.Add(markers);
            if (kw.Str("label") is { } lab) markers.Label = lab;
            return Classes.StemContainer(markers, stems, baseLine);
        };

        t["errorbar"] = (ax, i, a, kw) =>
        {
            var x = M.Dbl(Arg(a, 0, "x")); var y = M.Dbl(Arg(a, 1, "y"));
            var color = kw.Get("color", "c", "ecolor") is { } co ? M.Color(co) : ax.NextColor();
            var yerr = kw.Get("yerr") ?? Opt(a, 2); var xerr = kw.Get("xerr") ?? Opt(a, 3);
            var main = new Line2D(x, y, color) { LineWidth = kw.Dbl(1.5, "linewidth", "lw"), LineStyle = kw.Str("fmt") is "o" ? "None" : "-" };
            if (kw.Str("fmt") is { } fmt) { var (_, fl, fm) = M.ParseFmt(fmt); main.Marker = fm; main.LineStyle = fl ?? (fm is not null ? "None" : "-"); }
            if (kw.Str("marker") is { } mk) main.Marker = mk;
            if (kw.Str("label") is { } lab) main.Label = lab;
            ax.Add(main);
            if (yerr is not null)
            {
                var e = Broadcast(M.Dbl(yerr), x.Length);
                for (int q = 0; q < x.Length; q++)
                {
                    ax.Add(new Line2D(new[] { x[q], x[q] }, new[] { y[q] - e[q], y[q] + e[q] }, color) { LineWidth = 1.5 });
                    double cap = kw.Dbl(0, "capsize") * 0; _ = cap;
                }
            }
            if (xerr is not null)
            {
                var e = Broadcast(M.Dbl(xerr), x.Length);
                for (int q = 0; q < x.Length; q++) ax.Add(new Line2D(new[] { x[q] - e[q], x[q] + e[q] }, new[] { y[q], y[q] }, color) { LineWidth = 1.5 });
            }
            return W(main);
        };

        t["axhline"] = (ax, i, a, kw) =>
        {
            double y = Opt(a, 0) is { } v ? PyOps.AsDouble(v) : kw.Dbl(0, "y");
            double xmin = Opt(a, 1) is { } v1 ? PyOps.AsDouble(v1) : kw.Dbl(0, "xmin"), xmax = Opt(a, 2) is { } v2 ? PyOps.AsDouble(v2) : kw.Dbl(1, "xmax");
            var l = new Line2D(new[] { xmin, xmax }, new[] { y, y }, kw.Get("color", "c") is { } co ? M.Color(co) : SKColor.Parse("#1f77b4")) { XAxesCoords = true, LineWidth = kw.Dbl(1.5, "linewidth", "lw"), Alpha = kw.Dbl(1, "alpha") };
            if (kw.Str("linestyle", "ls") is { } ls) l.LineStyle = M.NormalizeLineStyle(ls);
            if (kw.Str("label") is { } lab) l.Label = lab;
            ax.Add(l);
            return W(l);
        };
        t["axvline"] = (ax, i, a, kw) =>
        {
            double x = Opt(a, 0) is { } v ? PyOps.AsDouble(v) : kw.Dbl(0, "x");
            double ymin = Opt(a, 1) is { } v1 ? PyOps.AsDouble(v1) : kw.Dbl(0, "ymin"), ymax = Opt(a, 2) is { } v2 ? PyOps.AsDouble(v2) : kw.Dbl(1, "ymax");
            var l = new Line2D(new[] { x, x }, new[] { ymin, ymax }, kw.Get("color", "c") is { } co ? M.Color(co) : SKColor.Parse("#1f77b4")) { YAxesCoords = true, LineWidth = kw.Dbl(1.5, "linewidth", "lw"), Alpha = kw.Dbl(1, "alpha") };
            if (kw.Str("linestyle", "ls") is { } ls) l.LineStyle = M.NormalizeLineStyle(ls);
            if (kw.Str("label") is { } lab) l.Label = lab;
            ax.Add(l);
            return W(l);
        };

        t["text"] = (ax, i, a, kw) =>
        {
            if (ax.Is3D && a.Length >= 4)
            {
                var t3 = new Text3D(Text(a[3], i), PyOps.AsDouble(a[0]), PyOps.AsDouble(a[1]), PyOps.AsDouble(a[2]));
                if (kw.Has("fontsize", "size")) t3.FontSize = FontSize(kw.Get("fontsize", "size")!, 10);
                if (kw.Get("color", "c") is { } c3) t3.Color = M.Color(c3);
                t3.HAlign = kw.Str("ha", "horizontalalignment") ?? "left"; t3.VAlign = kw.Str("va", "verticalalignment") ?? "baseline";
                ax.Add(t3);
                return W(t3);
            }
            var tx = new TextArtist(Text(Arg(a, 2, "s"), i), PyOps.AsDouble(Arg(a, 0, "x")), PyOps.AsDouble(Arg(a, 1, "y")));
            ApplyTextKw(tx, kw);
            if (kw.Get("transform") is PyInstance { Native: CoordSpace sp }) tx.Space = sp;
            ax.Add(tx);
            return W(tx);
        };

        t["annotate"] = (ax, i, a, kw) =>
        {
            var xy = M.Dbl(a.Length > 1 ? a[1] : kw.Get("xy") ?? throw PyErr.TypeError("annotate() missing required argument 'xy'"));
            var xyt = a.Length > 2 && a[2] is not PyNone ? M.Dbl(a[2]) : kw.Get("xytext") is { } xo ? M.Dbl(xo) : xy;
            var tx = new TextArtist(Text(Arg(a, 0, "text"), i), xyt[0], xyt[1]);
            ApplyTextKw(tx, kw);
            string tc = kw.Str("textcoords") ?? kw.Str("xycoords") ?? "data";
            string xc = kw.Str("xycoords") ?? "data";
            tx.Space = tc switch { "axes fraction" => CoordSpace.Axes, "figure fraction" => CoordSpace.Figure, _ => CoordSpace.Data };
            if (tc is "offset points" or "offset pixels")
            {
                tx.X = xy[0]; tx.Y = xy[1]; tx.Space = xc == "axes fraction" ? CoordSpace.Axes : CoordSpace.Data;
                tx.OffsetPoints = (xyt[0], xyt[1]);
            }
            if (kw.Get("arrowprops") is PyDict ap)
            {
                tx.ArrowTo = (xy[0], xy[1]);
                tx.ArrowSpace = xc == "axes fraction" ? CoordSpace.Axes : CoordSpace.Data;
                var props = new ArrowProps();
                if (ap.TryGet("arrowstyle", out var st) && st is string ss) props.Style = ss.Split(',')[0];
                else props.Style = "-|>";
                if (ap.TryGet("color", out var c1)) props.Color = M.Color(c1);
                else if (ap.TryGet("facecolor", out var c2)) props.Color = M.Color(c2);
                else if (ap.TryGet("edgecolor", out var c3)) props.Color = M.Color(c3);
                if (ap.TryGet("lw", out var lw) || ap.TryGet("linewidth", out lw)) props.LineWidth = PyOps.AsDouble(lw);
                if (ap.TryGet("shrinkA", out var sa)) props.ShrinkA = PyOps.AsDouble(sa);
                if (ap.TryGet("shrinkB", out var sb)) props.ShrinkB = PyOps.AsDouble(sb);
                if (ap.TryGet("mutation_scale", out var ms)) props.MutationScale = PyOps.AsDouble(ms);
                tx.Arrow = props;
            }
            ax.Add(tx);
            return W(tx);
        };

        t["arrow"] = (ax, i, a, kw) =>
        {
            double x = PyOps.AsDouble(Arg(a, 0)), y = PyOps.AsDouble(Arg(a, 1)), dx = PyOps.AsDouble(Arg(a, 2)), dy = PyOps.AsDouble(Arg(a, 3));
            double width = kw.Dbl(0.001, "width");
            double hw = kw.Dbl(3 * width, "head_width"), hl = kw.Dbl(1.5 * hw, "head_length");
            var p = FancyArrow.Create(x, y, dx, dy, width, hw, hl, kw.Bool(false, "length_includes_head"), kw.Dbl(0, "overhang"));
            if (kw.Get("color") is { } co) { p.FaceColor = M.Color(co); p.EdgeColor = p.FaceColor; }
            if (kw.Get("fc", "facecolor") is { } fc) p.FaceColor = M.Color(fc);
            if (kw.Get("ec", "edgecolor") is { } ec) p.EdgeColor = M.Color(ec);
            p.LineWidth = kw.Dbl(1, "linewidth", "lw");
            p.Alpha = kw.Dbl(1, "alpha");
            ax.Add(p);
            return W(p);
        };

        t["add_patch"] = (ax, i, a, kw) =>
        {
            if (Arg(a, 0) is PyInstance { Native: Patch p }) { ax.Add(p); return a[0]; }
            throw PyErr.TypeError("add_patch expects a Patch");
        };
        t["add_artist"] = t["add_patch"];
        t["add_line"] = (ax, i, a, kw) =>
        {
            if (Arg(a, 0) is PyInstance { Native: Artist p }) { ax.Add(p); return a[0]; }
            throw PyErr.TypeError("add_line expects a Line2D");
        };
        t["add_collection"] = t["add_patch"];

        // ------------------------------------------------------------------ labels, limits, ticks

        t["set_title"] = (ax, i, a, kw) =>
        {
            ax.Title = Text(Arg(a, 0, "label"), i);
            if (kw.Has("fontsize", "size")) ax.TitleSize = FontSize(kw.Get("fontsize", "size")!, 12);
            ax.TitleLoc = kw.Str("loc");
            return PyNone.Instance;
        };
        t["set_xlabel"] = (ax, i, a, kw) => { ax.XLabel = Text(Arg(a, 0, "xlabel"), i); if (kw.Has("fontsize", "size")) ax.LabelSize = FontSize(kw.Get("fontsize", "size")!, 10); return PyNone.Instance; };
        t["set_ylabel"] = (ax, i, a, kw) => { ax.YLabel = Text(Arg(a, 0, "ylabel"), i); if (kw.Has("fontsize", "size")) ax.LabelSize = FontSize(kw.Get("fontsize", "size")!, 10); return PyNone.Instance; };
        t["get_title"] = (ax, i, a, kw) => ax.Title ?? "";
        t["get_xlabel"] = (ax, i, a, kw) => ax.XLabel ?? "";
        t["get_ylabel"] = (ax, i, a, kw) => ax.YLabel ?? "";

        t["set_xlim"] = (ax, i, a, kw) => { SetLim(ax, a, kw, true); return M.Tup(ax.ViewX().Left, ax.ViewX().Right); };
        t["set_ylim"] = (ax, i, a, kw) => { SetLim(ax, a, kw, false); return M.Tup(ax.ViewY().Bottom, ax.ViewY().Top); };
        t["get_xlim"] = (ax, i, a, kw) => { ax.Figure.Layout(); return M.Tup(ax.ViewX().Left, ax.ViewX().Right); };
        t["get_ylim"] = (ax, i, a, kw) => { ax.Figure.Layout(); return M.Tup(ax.ViewY().Bottom, ax.ViewY().Top); };
        t["invert_xaxis"] = (ax, i, a, kw) => { var v = ax.ViewX(); ax.XLimSet = (v.Right, v.Left); ax.XInverted = !ax.XInverted; return PyNone.Instance; };
        t["invert_yaxis"] = (ax, i, a, kw) => { var v = ax.ViewY(); ax.YLimSet = (v.Top, v.Bottom); ax.YInverted = !ax.YInverted; return PyNone.Instance; };
        t["set_xscale"] = (ax, i, a, kw) => { ax.XScale = (string)Arg(a, 0); return PyNone.Instance; };
        t["set_yscale"] = (ax, i, a, kw) => { ax.YScale = (string)Arg(a, 0); return PyNone.Instance; };
        t["margins"] = (ax, i, a, kw) => PyNone.Instance;

        t["set_xticks"] = (ax, i, a, kw) => { SetTicks(ax, a, kw, true); return PyNone.Instance; };
        t["set_yticks"] = (ax, i, a, kw) => { SetTicks(ax, a, kw, false); return PyNone.Instance; };
        t["set_xticklabels"] = (ax, i, a, kw) =>
        {
            ax.XTickLabelsSet = M.StrList(Arg(a, 0));
            if (kw.Has("rotation")) ax.XTickRotation = kw.Dbl(0, "rotation");
            return PyNone.Instance;
        };
        t["set_yticklabels"] = (ax, i, a, kw) => { ax.YTickLabelsSet = M.StrList(Arg(a, 0)); return PyNone.Instance; };
        t["get_xticks"] = (ax, i, a, kw) => { ax.Figure.Layout(); return M.Arr(ax.XTicks().Positions); };
        t["get_yticks"] = (ax, i, a, kw) => { ax.Figure.Layout(); return M.Arr(ax.YTicks().Positions); };
        t["get_xticklabels"] = (ax, i, a, kw) => { ax.Figure.Layout(); return L(ax.XTicks().Labels.Select(s => W(new TextArtist(s, 0, 0)))); };
        t["get_yticklabels"] = (ax, i, a, kw) => { ax.Figure.Layout(); return L(ax.YTicks().Labels.Select(s => W(new TextArtist(s, 0, 0)))); };
        t["tick_params"] = (ax, i, a, kw) =>
        {
            string axis = kw.Str("axis") ?? (a.Length > 0 && a[0] is string s ? s : "both");
            bool x = axis is "both" or "x", y = axis is "both" or "y";
            if (kw.Has("labelsize")) ax.TickLabelSize = FontSize(kw.Get("labelsize")!, 10);
            if (kw.Has("rotation") && x) ax.XTickRotation = kw.Dbl(0, "rotation");
            if (kw.Contains("labelbottom") && x) ax.XLabelsVisible = kw.Bool(true, "labelbottom");
            if (kw.Contains("labelleft") && y) ax.YLabelsVisible = kw.Bool(true, "labelleft");
            if (kw.Contains("bottom") && x) ax.XTicksVisible = kw.Bool(true, "bottom");
            if (kw.Contains("left") && y) ax.YTicksVisible = kw.Bool(true, "left");
            return PyNone.Instance;
        };
        t["grid"] = (ax, i, a, kw) =>
        {
            bool on = a.Length > 0 && a[0] is not PyNone ? PyOps.Truthy(null!, a[0]) : kw.Contains("visible", "b") ? kw.Bool(true, "visible", "b") : !ax.Grid || kw.Contains("color", "linestyle", "ls", "alpha", "linewidth", "lw");
            ax.Grid = on;
            if (kw.Get("color", "c") is { } c) ax.GridColor = M.Color(c);
            if (kw.Str("linestyle", "ls") is { } ls) ax.GridLineStyle = M.NormalizeLineStyle(ls);
            if (kw.Has("alpha")) ax.GridAlpha = kw.Dbl(1, "alpha");
            if (kw.Has("linewidth", "lw")) ax.GridWidth = kw.Dbl(0.8, "linewidth", "lw");
            return PyNone.Instance;
        };
        t["axis"] = (ax, i, a, kw) =>
        {
            if (a.Length == 0) { ax.Figure.Layout(); return M.Tup(ax.ViewX().Left, ax.ViewX().Right, ax.ViewY().Bottom, ax.ViewY().Top); }
            switch (a[0])
            {
                case string s:
                    switch (s)
                    {
                        case "off": ax.AxisOff = true; break;
                        case "on": ax.AxisOff = false; break;
                        case "equal": ax.AspectMode = "equal"; ax.AspectIsAuto = false; ax.AspectValue = 1; break;
                        case "scaled": case "image": ax.AspectMode = "equal"; ax.AspectIsAuto = false; break;
                        case "auto": ax.AspectMode = "auto"; ax.AspectIsAuto = true; break;
                        case "tight":
                            var xl = ax.ViewX(); var yl = ax.ViewY();
                            break;
                        case "square": ax.AspectMode = "equal"; break;
                    }
                    return PyNone.Instance;
                case PyList or PyTuple:
                    var v = M.Dbl(a[0]);
                    ax.XLimSet = (v[0], v[1]); ax.YLimSet = (v[2], v[3]);
                    return PyNone.Instance;
            }
            return PyNone.Instance;
        };
        t["set_axis_off"] = (ax, i, a, kw) => { ax.AxisOff = true; return PyNone.Instance; };
        t["set_axis_on"] = (ax, i, a, kw) => { ax.AxisOff = false; return PyNone.Instance; };
        t["set_aspect"] = (ax, i, a, kw) => { SetAspect(ax, Arg(a, 0, "aspect")); return PyNone.Instance; };
        t["set_facecolor"] = (ax, i, a, kw) => { ax.FaceColor = M.Color(Arg(a, 0)); return PyNone.Instance; };
        t["set_visible"] = (ax, i, a, kw) => { ax.AxisOff = !PyOps.Truthy(i, Arg(a, 0)); return PyNone.Instance; };
        t["set_box_aspect"] = (ax, i, a, kw) => { if (!ax.Is3D && Arg(a, 0) is not (PyList or PyTuple)) ax.BoxAspect = PyOps.AsDouble(Arg(a, 0)); return PyNone.Instance; };
        t["set_position"] = (ax, i, a, kw) => { var v = M.Dbl(Arg(a, 0)); ax.Cell = null; ax.Rect = (v[0], v[1], v[2], v[3]); return PyNone.Instance; };
        t["get_position"] = (ax, i, a, kw) =>
        {
            ax.Figure.Layout();
            var b = ax.BoxAspect is not null ? ax.Slot : ax.Box; double W_ = ax.Figure.WidthPx, H_ = ax.Figure.HeightPx;
            return Classes.Bbox(b.Left / W_, 1 - b.Bottom / H_, b.Right / W_, 1 - b.Top / H_);
        };
        t["set"] = (ax, i, a, kw) =>
        {
            foreach (var key in new[] { "title", "xlabel", "ylabel", "xlim", "ylim", "xticks", "yticks", "xscale", "yscale", "aspect", "facecolor" })
                if (kw.Get(key) is { } v)
                {
                    string m = key == "aspect" ? "set_aspect" : "set_" + key;
                    t[m](ax, i, new[] { v }, new Kw(null));
                }
            return PyNone.Instance;
        };
        t["cla"] = (ax, i, a, kw) =>
        {
            ax.Artists.Clear(); ax.Legend = null; ax.Title = ax.XLabel = ax.YLabel = null; ax.XLimSet = ax.YLimSet = null;
            ax.XTicksSet = ax.YTicksSet = null; ax.XTickLabelsSet = ax.YTickLabelsSet = null; ax.ColorCycleIndex = 0;
            return PyNone.Instance;
        };
        t["clear"] = t["cla"];

        // ------------------------------------------------------------------ legend

        t["legend"] = (ax, i, a, kw) =>
        {
            var lg = ax.Legend ?? new Legend();
            lg.Entries.Clear();
            var handles = new List<object>(); var labels = new List<string>();
            if (a.Length == 2) { handles.AddRange(M.Items(a[0])); labels.AddRange(M.StrList(a[1])); }
            else if (a.Length == 1 && a[0] is PyList or PyTuple && M.IsStringSeq(a[0]))
            {
                var wanted = M.StrList(a[0]);
                var lab = ax.Artists.Where(x => x is Line2D or Scatter or RectPatch or PolygonPatch).ToList();
                for (int q = 0; q < wanted.Length && q < lab.Count; q++) { handles.Add(W(lab[q])); labels.Add(wanted[q]); }
            }
            else if (kw.Get("handles") is { } h) { handles.AddRange(M.Items(h)); labels.AddRange(kw.Get("labels") is { } ll ? M.StrList(ll) : Array.Empty<string>()); }
            else
            {
                foreach (var art in ax.Artists)
                    if (art.Label is { Length: > 0 } l && !l.StartsWith('_')) { handles.Add(W(art)); labels.Add(l); }
            }
            for (int q = 0; q < handles.Count; q++)
            {
                var art = (handles[q] as PyInstance)?.Native as Artist;
                string text = q < labels.Count ? labels[q] : art?.Label ?? "";
                lg.Entries.Add((art, text));
            }
            if (kw.Str("loc") is { } loc) lg.Location = loc;
            else if (a.Length > 0 && a[^1] is string loc2 && !M.IsStringSeq(a[^1]) && a.Length != 2) lg.Location = loc2;
            if (kw.Has("fontsize")) lg.FontSize = FontSize(kw.Get("fontsize")!, 10);
            lg.Columns = kw.Int(1, "ncol", "ncols");
            lg.Title = kw.Str("title");
            lg.Frame = kw.Bool(true, "frameon");
            ax.Legend = lg;
            return W(lg);
        };
        t["get_legend_handles_labels"] = (ax, i, a, kw) =>
        {
            var arts = ax.Artists.Where(x => x.Label is { Length: > 0 } l && !l.StartsWith('_')).ToList();
            return M.Tup(L(arts.Select(x => W(x))), L(arts.Select(x => (object)x.Label!)));
        };

        t["twinx"] = (ax, i, a, kw) => throw PyErr.NotImplementedError("twinx is not supported yet");
        t["twiny"] = (ax, i, a, kw) => throw PyErr.NotImplementedError("twiny is not supported yet");
        t["set_zlabel"] = (ax, i, a, kw) => { ax.ZLabel = Text(Arg(a, 0, "zlabel"), i); return PyNone.Instance; };
        t["set_zlim"] = (ax, i, a, kw) => { var v = a.Length == 1 ? M.Dbl(a[0]) : new[] { PyOps.AsDouble(a[0]), PyOps.AsDouble(a[1]) }; ax.ZLimSet = (v[0], v[1]); return PyNone.Instance; };
        t["invert_zaxis"] = (ax, i, a, kw) => { var v = ax.ViewZ(); ax.ZLimSet = (v.Right, v.Left); ax.ZInverted = !ax.ZInverted; return PyNone.Instance; };
        t["view_init"] = (ax, i, a, kw) => { ax.View = (Opt(a, 0) is { } e ? PyOps.AsDouble(e) : kw.Dbl(30, "elev"), Opt(a, 1) is { } z ? PyOps.AsDouble(z) : kw.Dbl(-60, "azim")); return PyNone.Instance; };
        t["add_collection3d"] = (ax, i, a, kw) =>
        {
            if (Arg(a, 0) is PyInstance { Native: Artist art }) { ax.Add(art); return a[0]; }
            throw PyErr.TypeError("add_collection3d expects a Poly3DCollection");
        };
        return t;
    }

    // ---------------------------------------------------------------------------------------------- helpers

    internal static string Text(object o, Interp i) => o is string s ? s : PyOps.Str(i, o);

    internal static double FontSize(object o, double dflt) => o switch
    {
        string s => s switch
        {
            "xx-small" => 5.79, "x-small" => 6.94, "small" => 8.33, "medium" => 10, "large" => 12, "x-large" => 14.4, "xx-large" => 17.28,
            _ => dflt,
        },
        _ => PyOps.AsDouble(o),
    };

    internal static void ApplyTextKw(TextArtist tx, Kw kw)
    {
        if (kw.Has("fontsize", "size")) tx.FontSize = FontSize(kw.Get("fontsize", "size")!, 10);
        if (kw.Get("color", "c") is { } c) tx.Color = M.Color(c);
        tx.HAlign = kw.Str("ha", "horizontalalignment") ?? "left";
        tx.VAlign = kw.Str("va", "verticalalignment") ?? "baseline";
        tx.Rotation = kw.Dbl(0, "rotation");
        tx.Bold = kw.Str("fontweight", "weight") is "bold" or "heavy" or "black" or "semibold";
        tx.Italic = kw.Str("style", "fontstyle") is "italic" or "oblique";
        tx.Alpha = kw.Dbl(1, "alpha");
        tx.ZOrder = kw.Int(3, "zorder");
        if (kw.Get("bbox") is PyDict bb)
        {
            var face = bb.TryGet("facecolor", out var f) || bb.TryGet("fc", out f) ? M.Color(f) : SKColors.White;
            double alpha = bb.TryGet("alpha", out var al) ? PyOps.AsDouble(al) : 1;
            var edge = bb.TryGet("edgecolor", out var e) || bb.TryGet("ec", out e) ? M.Color(e) : SKColor.Parse("#000000");
            double pad = bb.TryGet("pad", out var pd) ? PyOps.AsDouble(pd) : 0.3;
            tx.BBox = (face.WithAlpha((byte)Math.Round(face.Alpha * alpha)), edge, pad);
        }
    }

    internal static void SetAspect(PlotAxes ax, object o)
    {
        switch (o)
        {
            case string se when se == "equal": ax.AspectMode = "equal"; ax.AspectIsAuto = false; ax.AspectValue = 1; break;
            case string sa when sa == "auto": ax.AspectMode = "auto"; ax.AspectIsAuto = true; break;
            default: ax.AspectMode = "num"; ax.AspectIsAuto = false; ax.AspectValue = PyOps.AsDouble(o); break;
        }
    }

    private static void SetLim(PlotAxes ax, object[] a, Kw kw, bool x)
    {
        double? lo = null, hi = null;
        if (a.Length == 1 && a[0] is PyList or PyTuple) { var v = M.Items(a[0]); lo = v[0] is PyNone ? null : PyOps.AsDouble(v[0]); hi = v[1] is PyNone ? null : PyOps.AsDouble(v[1]); }
        else
        {
            if (a.Length > 0 && a[0] is not PyNone) lo = PyOps.AsDouble(a[0]);
            if (a.Length > 1 && a[1] is not PyNone) hi = PyOps.AsDouble(a[1]);
        }
        lo ??= kw.DblOrNull(x ? "left" : "bottom", "xmin", "ymin"); hi ??= kw.DblOrNull(x ? "right" : "top", "xmax", "ymax");
        var cur = x ? ax.ViewX() : ax.ViewY();
        double l = lo ?? cur.Item1, h = hi ?? cur.Item2;
        if (x) { ax.XLimSet = (l, h); ax.XInverted = l > h; } else { ax.YLimSet = (l, h); ax.YInverted = l > h; }
    }

    private static void SetTicks(PlotAxes ax, object[] a, Kw kw, bool x)
    {
        var ticks = M.Dbl(Arg(a, 0, "ticks"));
        var labels = (Opt(a, 1) ?? kw.Get("labels")) is { } l ? M.StrList(l) : null;
        if (x) { ax.XTicksSet = ticks; ax.XTickLabelsSet = labels ?? (ax.XTickLabelsSet is { } old && old.Length == ticks.Length ? old : null); if (kw.Has("rotation")) ax.XTickRotation = kw.Dbl(0, "rotation"); }
        else { ax.YTicksSet = ticks; ax.YTickLabelsSet = labels; }
    }

    // ---------------------------------------------------------------------------------------------- bar / hist

    private static object Bar(PlotAxes ax, object[] a, Kw kw, bool horizontal)
    {
        var pos = XValues(ax, Arg(a, 0, horizontal ? "y" : "x"), !horizontal);
        var size = Broadcast(M.Dbl(Opt(a, 1) ?? kw.Get(horizontal ? "width" : "height") ?? throw PyErr.TypeError("missing bar heights")), pos.Length);
        var thick = Broadcast(M.Dbl(Opt(a, 2) ?? kw.Get(horizontal ? "height" : "width") ?? 0.8), pos.Length);
        var baseV = Broadcast(Opt(a, 3) is { } b3 ? M.Dbl(b3) : kw.Get("bottom", "left") is { } bo ? M.Dbl(bo) : new[] { 0.0 }, pos.Length);
        string align = kw.Str("align") ?? "center";
        SKColor[] colors = kw.Get("color", "facecolor", "fc") is { } co ? M.ColorList(co) ?? new[] { M.Color(co) } : new[] { ax.NextColor() };
        SKColor[]? edges = kw.Get("edgecolor", "ec") is { } ec ? M.ColorList(ec) ?? new[] { M.Color(ec) } : null;
        if (kw.Get("tick_label") is { } tl)
        {
            var lab = M.StrList(tl);
            if (horizontal) { ax.YTicksSet = pos; ax.YTickLabelsSet = lab; } else { ax.XTicksSet = pos; ax.XTickLabelsSet = lab; }
        }
        var rects = new List<object>();
        for (int q = 0; q < pos.Length; q++)
        {
            double start = align == "center" ? pos[q] - thick[q] / 2 : pos[q];
            RectPatch r = horizontal ? new RectPatch(baseV[q], start, size[q], thick[q]) : new RectPatch(start, baseV[q], thick[q], size[q]);
            r.StickyBase = true; r.Horizontal = horizontal;
            r.FaceColor = colors[q % colors.Length];
            if (edges is not null) { r.EdgeColor = edges[q % edges.Length]; r.LineWidth = kw.Dbl(1, "linewidth", "lw"); }
            r.Alpha = kw.Dbl(1, "alpha"); r.ZOrder = kw.Int(1, "zorder");
            if (q == 0 && kw.Str("label") is { } label) r.Label = label;
            ax.Add(r);
            rects.Add(W(r));
        }
        return Classes.Container("BarContainer", rects);
    }

    private static object Plot3DLine(PlotAxes ax, object[] a, Kw kw)
    {
        var x = M.Dbl(Arg(a, 0)); var y = M.Dbl(Arg(a, 1));
        double[] z; string? fmt = null;
        if (a.Length > 2 && a[2] is not string) { z = M.Dbl(a[2]); if (a.Length > 3 && a[3] is string f) fmt = f; }
        else { z = Enumerable.Repeat(kw.Dbl(0, "zs"), x.Length).ToArray(); if (a.Length > 2 && a[2] is string f2) fmt = f2; }
        var (fc, fl, fm) = fmt is null ? (null, null, null) : M.ParseFmt(fmt);
        var color = kw.Get("color", "c") is { } co ? M.Color(co) : fc ?? ax.NextColor();
        var l = new Line3D(x, y, Broadcast(z, x.Length), color) { LineWidth = kw.Dbl(1.5, "linewidth", "lw"), Alpha = kw.Dbl(1, "alpha") };
        string? ls = kw.Str("linestyle", "ls") is { } k ? M.NormalizeLineStyle(k) : fl;
        l.Marker = kw.Str("marker") ?? fm;
        l.LineStyle = ls ?? (l.Marker is not null && fm is not null ? "None" : "-");
        l.MarkerSize = kw.Dbl(6, "markersize", "ms");
        if (kw.Str("label") is { } lab) l.Label = lab;
        ax.Add(l);
        return L(new[] { W(l) });
    }

    private static object Scatter3DApi(PlotAxes ax, object[] a, Kw kw)
    {
        var s = new Scatter3D { X = M.Dbl(Arg(a, 0)), Y = M.Dbl(Arg(a, 1)), Z = M.Dbl(Arg(a, 2)), Alpha = kw.Dbl(1, "alpha") };
        if (kw.Get("s") is { } sz) s.Sizes = M.Dbl(sz);
        if (kw.Str("marker") is { } mk) s.Marker = mk;
        var cobj = kw.Get("c", "color");
        if (cobj is not null && M.ColorList(cobj) is null && !(cobj is string) && Conv.TryND(cobj, out var cn) && cn.Ndim == 1 && cn.Size == s.X.Length) s.Values = cn.ToArray<double>();
        else s.Colors = cobj is not null ? (M.ColorList(cobj) ?? new[] { M.Color(cobj) }) : new[] { ax.NextColor() };
        s.Cmap = Cmaps.Resolve(kw.Get("cmap"));
        s.VMin = kw.DblOrNull("vmin"); s.VMax = kw.DblOrNull("vmax");
        if (kw.Str("label") is { } lab) s.Label = lab;
        ax.Add(s);
        return W(s);
    }

    private static object Contour(PlotAxes ax, object[] a, Kw kw, bool filled)
    {
        // contour(Z), contour(Z, levels), contour(X, Y, Z), contour(X, Y, Z, levels)
        object zObj; double[]? xs = null, ys = null; object? levelsObj = null;
        if (a.Length >= 3) { zObj = a[2]; levelsObj = a.Length > 3 ? a[3] : null; var xn = M.Nd(a[0]); var yn = M.Nd(a[1]); var zn0 = M.Nd(zObj);
            int rows0 = zn0.Shape[0], cols0 = zn0.Shape[1];
            var xv = xn.ToArray<double>(); var yv = yn.ToArray<double>();
            xs = xn.Ndim == 2 ? Enumerable.Range(0, cols0).Select(q => xv[q]).ToArray() : xv;
            ys = yn.Ndim == 2 ? Enumerable.Range(0, rows0).Select(q => yv[q * cols0]).ToArray() : yv; }
        else { zObj = Arg(a, 0, "Z"); levelsObj = a.Length > 1 ? a[1] : null; }
        var zn = M.Nd(zObj);
        if (zn.Ndim != 2) throw PyErr.TypeError($"Input z must be 2D, not {zn.Ndim}D");
        int rows = zn.Shape[0], cols = zn.Shape[1];
        var c = new ContourArtist { Filled = filled, Z = zn.ToArray<double>(), Alpha = kw.Dbl(1, "alpha"), ZOrder = filled ? 1 : 2 };
        c.Xs = xs ?? Enumerable.Range(0, cols).Select(q => (double)q).ToArray();
        c.Ys = ys ?? Enumerable.Range(0, rows).Select(q => (double)q).ToArray();
        if (kw.Get("extent") is { } ex) { var e = M.Dbl(ex); c.Xs = Enumerable.Range(0, cols).Select(q => e[0] + (e[1] - e[0]) * q / (cols - 1)).ToArray(); c.Ys = Enumerable.Range(0, rows).Select(q => e[2] + (e[3] - e[2]) * q / (rows - 1)).ToArray(); }
        var fin = c.Z.Where(double.IsFinite).ToArray();
        double zmin = fin.Min(), zmax = fin.Max();
        levelsObj ??= kw.Get("levels");
        if (levelsObj is PyList or PyTuple || (levelsObj is PyInstance { Native: NDArray ln } && ln.Ndim == 1)) c.Levels = M.Dbl(levelsObj);
        else
        {
            int n = levelsObj is null ? 7 : (int)PyOps.AsBigInt(levelsObj is PyInstance { Native: NDArray { Ndim: 0 } n0 } ? Conv.Scalarize(n0) : levelsObj, "levels");
            var lv = ContourArtist.AutoLevels(zmin, zmax, n);
            // lines: only levels inside the data range; filled: the bands must cover it
            c.Levels = lv;
        }
        if (kw.Get("colors") is { } co) c.Colors = M.ColorList(co) ?? new[] { M.Color(co) };
        c.Cmap = Cmaps.Resolve(kw.Get("cmap"));
        c.LineWidth = kw.Dbl(1.5, "linewidths", "linewidth");
        if (kw.Str("linestyles") is { } ls) c.LineStyle = M.NormalizeLineStyle(ls);
        if (!filled && c.Colors is null && kw.Get("cmap") is null) c.Cmap = Colormap.Get(State.DefaultCmap);
        ax.Add(c);
        return W(c);
    }

    private static object Hist(PlotAxes ax, object[] a, Kw kw)
    {
        var first = Arg(a, 0, "x");
        var sets = new List<double[]>();
        if (first is PyList pl && pl.Items.Count > 0 && pl.Items[0] is PyList or PyTuple or PyInstance { Native: NDArray })
            sets.AddRange(pl.Items.Select(M.Dbl));
        else if (Conv.TryND(first, out var nd) && nd.Ndim == 2 && false) { }
        else sets.Add(M.Dbl(first));
        var binsObj = Opt(a, 1) ?? kw.Get("bins");
        double[]? range = kw.Get("range") is { } rg ? M.Dbl(rg) : null;
        var all = sets.SelectMany(s => s).Where(double.IsFinite).ToArray();
        double lo = range?[0] ?? (all.Length > 0 ? all.Min() : 0), hi = range?[1] ?? (all.Length > 0 ? all.Max() : 1);
        double[] edges;
        if (binsObj is PyList or PyTuple || (binsObj is PyInstance { Native: NDArray bn } && bn.Ndim == 1)) edges = M.Dbl(binsObj);
        else
        {
            int nb = binsObj is null ? 10 : (int)PyOps.AsBigInt(binsObj is PyInstance { Native: NDArray { Ndim: 0 } n0 } ? Conv.Scalarize(n0) : binsObj, "bins");
            if (lo == hi) { lo -= 0.5; hi += 0.5; }
            edges = Enumerable.Range(0, nb + 1).Select(q => lo + (hi - lo) * q / nb).ToArray();
        }
        bool density = kw.Bool(false, "density", "normed"), cumulative = kw.Bool(false, "cumulative");
        string histtype = kw.Str("histtype") ?? "bar";
        var weights = kw.Get("weights") is { } w ? M.Dbl(w) : null;
        var colorsIn = kw.Get("color") is { } co ? (M.ColorList(co) ?? new[] { M.Color(co) }) : null;
        var counts = new List<double[]>();
        foreach (var (s, si) in sets.Select((s, si) => (s, si)))
        {
            var c = new double[edges.Length - 1];
            for (int q = 0; q < s.Length; q++)
            {
                double v = s[q];
                if (!double.IsFinite(v) || v < edges[0] || v > edges[^1]) continue;
                int idx = Array.BinarySearch(edges, v);
                if (idx < 0) idx = ~idx - 1; else if (idx == edges.Length - 1) idx--; else { /* exact edge → the bin starting here */ }
                // numpy: right-closed only for the last bin; ties land in the bin that starts at the edge
                while (idx > 0 && edges[idx] > v) idx--;
                if (idx >= c.Length) idx = c.Length - 1;
                c[idx] += weights is null ? 1 : weights[q];
            }
            if (density)
            {
                double total = c.Sum();
                for (int q = 0; q < c.Length; q++) c[q] = c[q] / (total * (edges[q + 1] - edges[q]));
            }
            if (cumulative) for (int q = 1; q < c.Length; q++) c[q] += c[q - 1];
            counts.Add(c);
        }
        int n = sets.Count;
        var patches = new List<object>();
        string orientation = kw.Str("orientation") ?? "vertical";
        string? label = kw.Str("label");
        for (int si = 0; si < n; si++)
        {
            var color = colorsIn is not null ? colorsIn[si % colorsIn.Length] : ax.NextColor();
            if (histtype is "step" or "stepfilled")
            {
                var xs = new List<double> { edges[0] }; var ys = new List<double> { 0 };
                for (int q = 0; q < counts[si].Length; q++) { xs.Add(edges[q]); ys.Add(counts[si][q]); xs.Add(edges[q + 1]); ys.Add(counts[si][q]); }
                xs.Add(edges[^1]); ys.Add(0);
                if (histtype == "step")
                {
                    var ln = new Line2D(xs.ToArray(), ys.ToArray(), color) { Alpha = kw.Dbl(1, "alpha"), LineWidth = kw.Dbl(1.5, "linewidth", "lw") };
                    if (label is not null && si == 0) ln.Label = label;
                    ax.Add(ln); patches.Add(W(ln));
                }
                else
                {
                    var poly = new PolygonPatch(xs.ToArray(), ys.ToArray()) { FaceColor = color, EdgeColor = color, Alpha = kw.Dbl(1, "alpha"), LineWidth = kw.Dbl(1, "linewidth", "lw") };
                    if (label is not null && si == 0) poly.Label = label;
                    ax.Add(poly); patches.Add(W(poly));
                }
                continue;
            }
            for (int q = 0; q < counts[si].Length; q++)
            {
                double bw = edges[q + 1] - edges[q];
                double left = edges[q] + bw * si / n, width = bw / n * (kw.Dbl(1, "rwidth") is var rw ? rw : 1);
                RectPatch r = orientation == "horizontal" ? new RectPatch(0, left, counts[si][q], width) : new RectPatch(left, 0, width, counts[si][q]);
                r.StickyBase = true; r.Horizontal = orientation == "horizontal";
                r.FaceColor = color;
                if (kw.Get("edgecolor", "ec") is { } ec) { r.EdgeColor = M.Color(ec); r.LineWidth = kw.Dbl(1, "linewidth", "lw"); }
                r.Alpha = kw.Dbl(1, "alpha");
                if (q == 0 && label is not null && si == 0) r.Label = label;
                ax.Add(r); patches.Add(W(r));
            }
        }
        if (kw.Bool(false, "log")) ax.YScale = "log";
        var nArr = counts.Count == 1 ? M.Arr(counts[0]) : M.Arr(counts.SelectMany(c => c).ToArray(), counts.Count, counts[0].Length);
        return M.Tup(nArr, M.Arr(edges), L(patches));
    }
}

internal static class Cmaps
{
    public static Colormap Resolve(object? o) => o switch
    {
        null => Colormap.Get(State.DefaultCmap),
        string s => Colormap.Get(s),
        PyInstance { Native: Colormap c } => c,
        _ => throw PyErr.TypeError("cmap must be a string or a Colormap"),
    };

    public static void ApplyNorm(object norm, Action<double> vmin, Action<double> vmax)
    {
        if (norm is PyInstance inst)
        {
            if (inst.Dict.TryGet("vmin", out var a) && a is not PyNone) vmin(PyOps.AsDouble(a));
            if (inst.Dict.TryGet("vmax", out var b) && b is not PyNone) vmax(PyOps.AsDouble(b));
        }
    }
}
