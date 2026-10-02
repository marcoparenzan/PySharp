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
using PlotFigure = NDSharp.Plot.Figure;

namespace PySharpLib.Matplotlib;

/// <summary>The Python classes of the matplotlib object model: Figure, Axes, the artists, containers, colormaps.</summary>
internal static class Classes
{
    private static PyClass NewClass(string name, string module = "matplotlib")
    {
        var c = new PyClass(name, new List<PyClass>());
        c.Dict["__module__"] = module;
        return c;
    }

    private static void Def(PyClass c, string name, BuiltinFn fn) => c.Dict[name] = Native.Fn(c.Name + "." + name, fn);

    private static T Self<T>(object o) => o is PyInstance { Native: T t } ? t : throw PyErr.TypeError($"descriptor requires a {typeof(T).Name} object");

    private static void Prop<T>(PyClass c, string name, Func<T, object> get, Action<Interp, T, object>? set = null)
        => c.Dict[name] = new PyProperty
        {
            Getter = new PyBuiltinFunction(name, (_, a, _) => get(Self<T>(a[0]))),
            Setter = set is null ? null : new PyBuiltinFunction(name, (i, a, _) => { set(i, Self<T>(a[0]), a[1]); return PyNone.Instance; }),
        };

    private static readonly PyClass CanvasClass = BuildCanvas();
    public static readonly PyClass FigureClass = BuildFigure();
    public static readonly PyClass AxesClass = BuildAxes();
    public static readonly PyClass AxesGridClass = BuildGrid();
    public static readonly PyClass Line2DClass = BuildLine();
    public static readonly PyClass ImageClass = BuildImage();
    public static readonly PyClass CollectionClass = BuildCollection();
    public static readonly PyClass PatchClass = BuildPatch("Patch");
    public static readonly PyClass ContourClass = BuildContour();
    public static readonly PyClass TextClass = BuildText();
    public static readonly PyClass LegendClass = NewClass("Legend", "matplotlib.legend");
    public static readonly PyClass ColorbarClass = BuildColorbar();
    public static readonly PyClass TransformClass = NewClass("Transform", "matplotlib.transforms");
    public static readonly PyClass BboxClass = BuildBbox();
    public static readonly PyClass ColormapClass = BuildColormap();
    public static readonly PyClass SpineClass = BuildSpine();
    public static readonly PyClass AxisClass = BuildAxis();
    public static readonly PyClass ContainerClass = BuildContainer();

    public static object W(object native) => State.Wrap(native, n => n switch
    {
        PlotAxes => AxesClass,
        PlotFigure => FigureClass,
        Line2D => Line2DClass,
        ImageArtist => ImageClass,
        Artist3D => Line2DClass,
        Scatter => CollectionClass,
        ContourArtist => ContourClass,
        Patch => PatchClass,
        TextArtist => TextClass,
        Legend => LegendClass,
        Colorbar => ColorbarClass,
        Colormap => ColormapClass,
        _ => throw new InvalidOperationException($"no Python class for {n.GetType().Name}"),
    });

    public static PyInstance Transform(CoordSpace s) => new(TransformClass) { Native = s };

    private static PyClass BuildContainer()
    {
        var c = NewClass("Container");
        static List<object> Items(object o) => (List<object>)((PyInstance)o).Native!;
        Def(c, "__len__", (_, a, _) => new BigInteger(Items(a[0]).Count));
        Def(c, "__iter__", (_, a, _) => new PyIterator(Items(a[0]).GetEnumerator()));
        Def(c, "__getitem__", (_, a, _) => a[1] is BigInteger bi ? Items(a[0])[(int)(bi < 0 ? bi + Items(a[0]).Count : bi)] : throw PyErr.TypeError("container indices must be integers"));
        return c;
    }

    public static object Container(string name, List<object> items)
    {
        var inst = new PyInstance(ContainerClass) { Native = items };
        inst.Dict["__name__"] = name;
        return inst;
    }

    public static object StemContainer(Line2D markers, List<Line2D> stems, Line2D baseline)
    {
        var items = new List<object> { W(markers), L(stems.Select(s => W(s))), W(baseline) };
        return M.Tup(items.ToArray());
    }

    private static PyList L(IEnumerable<object> v) => new(v);

    public static object Bbox(double x0, double y0, double x1, double y1)
        => new PyInstance(BboxClass) { Native = (x0, y0, x1, y1) };

    // ---------------------------------------------------------------------------------------------- Axes

    private static PyClass BuildAxes()
    {
        var c = NewClass("Axes", "matplotlib.axes");
        foreach (var (name, fn) in AxesApi.Table)
        {
            var f = fn;
            Def(c, name, (i, a, k) => f(Self<PlotAxes>(a[0]), i, a.Skip(1).ToArray(), new Kw(k)));
        }
        // Axes.plot_surface etc. (3-D) are separate; here the remaining object-model members
        Prop<PlotAxes>(c, "figure", ax => W(ax.Figure));
        Prop<PlotAxes>(c, "transAxes", _ => Transform(CoordSpace.Axes));
        Prop<PlotAxes>(c, "transData", _ => Transform(CoordSpace.Data));
        Prop<PlotAxes>(c, "lines", ax => L(ax.Artists.OfType<Line2D>().Select(x => W(x))));
        Prop<PlotAxes>(c, "patches", ax => L(ax.Artists.OfType<Patch>().Where(p => !p.InCollections).Select(x => W(x))));
        Prop<PlotAxes>(c, "images", ax => L(ax.Artists.OfType<ImageArtist>().Select(x => W(x))));
        Prop<PlotAxes>(c, "texts", ax => L(ax.Artists.OfType<TextArtist>().Select(x => W(x))));
        Prop<PlotAxes>(c, "collections", ax => L(ax.Artists.Where(a => a is Scatter or ContourArtist || a is Patch { InCollections: true }).Select(x => W(x))));
        Prop<PlotAxes>(c, "xaxis", ax => new PyInstance(AxisClass) { Native = (ax, true) });
        Prop<PlotAxes>(c, "yaxis", ax => new PyInstance(AxisClass) { Native = (ax, false) });
        Prop<PlotAxes>(c, "spines", ax =>
        {
            var d = new PyDict();
            foreach (var k in new[] { "left", "right", "top", "bottom" }) d[k] = new PyInstance(SpineClass) { Native = (ax, k) };
            return d;
        });
        Prop<PlotAxes>(c, "title", ax => W(new TextArtist(ax.Title ?? "", 0, 0)));
        Def(c, "get_figure", (_, a, _) => W(Self<PlotAxes>(a[0]).Figure));
        Def(c, "get_subplotspec", (_, a, _) => PyNone.Instance);
        Def(c, "set_xmargin", (_, a, _) => PyNone.Instance);
        Def(c, "set_ymargin", (_, a, _) => PyNone.Instance);
        Def(c, "get_legend", (_, a, _) => Self<PlotAxes>(a[0]).Legend is { } lg ? W(lg) : PyNone.Instance);
        Def(c, "get_aspect", (_, a, _) => { var ax = Self<PlotAxes>(a[0]); return ax.AspectIsAuto ? "auto" : ax.AspectMode is null ? (ax.Artists.OfType<ImageArtist>().Any() ? 1.0 : "auto") : ax.AspectMode == "equal" ? 1.0 : ax.AspectMode == "num" ? ax.AspectValue : "auto"; });
        Def(c, "__repr__", (_, a, _) => "<Axes>");
        return c;
    }

    private static PyClass BuildAxis()
    {
        var c = NewClass("Axis", "matplotlib.axis");
        (PlotAxes ax, bool x) S(object o) => ((PlotAxes, bool))((PyInstance)o).Native!;
        Def(c, "set_visible", (i, a, _) =>
        {
            var (ax, x) = S(a[0]); bool v = PyOps.Truthy(i, a[1]);
            if (x) { ax.XTicksVisible = v; ax.XLabelsVisible = v; } else { ax.YTicksVisible = v; ax.YLabelsVisible = v; }
            return PyNone.Instance;
        });
        Def(c, "set_ticks", (i, a, k) => { var (ax, x) = S(a[0]); AxesApi.Table[x ? "set_xticks" : "set_yticks"](ax, i, a.Skip(1).ToArray(), new Kw(k)); return PyNone.Instance; });
        Def(c, "set_ticklabels", (i, a, k) => { var (ax, x) = S(a[0]); AxesApi.Table[x ? "set_xticklabels" : "set_yticklabels"](ax, i, a.Skip(1).ToArray(), new Kw(k)); return PyNone.Instance; });
        Def(c, "set_tick_params", (i, a, k) => { var (ax, x) = S(a[0]); var kk = k is null ? new Dictionary<string, object>() : new Dictionary<string, object>(k); kk["axis"] = x ? "x" : "y"; AxesApi.Table["tick_params"](ax, i, Array.Empty<object>(), new Kw(kk)); return PyNone.Instance; });
        Def(c, "set_label_text", (i, a, k) => { var (ax, x) = S(a[0]); if (x) ax.XLabel = (string)a[1]; else ax.YLabel = (string)a[1]; return PyNone.Instance; });
        Def(c, "get_ticklabels", (i, a, k) => { var (ax, x) = S(a[0]); return AxesApi.Table[x ? "get_xticklabels" : "get_yticklabels"](ax, i, Array.Empty<object>(), new Kw(null)); });
        Def(c, "set_major_locator", (_, a, _) => PyNone.Instance);
        Def(c, "set_major_formatter", (_, a, _) => PyNone.Instance);
        return c;
    }

    private static PyClass BuildSpine()
    {
        var c = NewClass("Spine", "matplotlib.spines");
        Def(c, "set_visible", (i, a, _) =>
        {
            var (ax, name) = ((PlotAxes, string))((PyInstance)a[0]).Native!;
            ax.Spines[name] = PyOps.Truthy(i, a[1]);
            return PyNone.Instance;
        });
        Def(c, "set_color", (_, a, _) => PyNone.Instance);
        Def(c, "set_linewidth", (_, a, _) => PyNone.Instance);
        return c;
    }

    private static PyClass BuildBbox()
    {
        var c = NewClass("Bbox", "matplotlib.transforms");
        (double x0, double y0, double x1, double y1) B(object o) => ((double, double, double, double))((PyInstance)o).Native!;
        Prop<(double, double, double, double)>(c, "bounds", b => M.Tup(b.Item1, b.Item2, b.Item3 - b.Item1, b.Item4 - b.Item2));
        Prop<(double, double, double, double)>(c, "x0", b => b.Item1);
        Prop<(double, double, double, double)>(c, "y0", b => b.Item2);
        Prop<(double, double, double, double)>(c, "x1", b => b.Item3);
        Prop<(double, double, double, double)>(c, "y1", b => b.Item4);
        Prop<(double, double, double, double)>(c, "width", b => b.Item3 - b.Item1);
        Prop<(double, double, double, double)>(c, "height", b => b.Item4 - b.Item2);
        Prop<(double, double, double, double)>(c, "xmin", b => b.Item1);
        Prop<(double, double, double, double)>(c, "ymin", b => b.Item2);
        Prop<(double, double, double, double)>(c, "xmax", b => b.Item3);
        Prop<(double, double, double, double)>(c, "ymax", b => b.Item4);
        Def(c, "__repr__", (_, a, _) => { var b = B(a[0]); return $"Bbox(x0={b.x0}, y0={b.y0}, x1={b.x1}, y1={b.y1})"; });
        return c;
    }

    // ---------------------------------------------------------------------------------------------- Figure

    private static PyClass BuildFigure()
    {
        var c = NewClass("Figure", "matplotlib.figure");
        Def(c, "add_subplot", (i, a, k) => AddSubplot(Self<PlotFigure>(a[0]), a.Skip(1).ToArray(), new Kw(k)));
        Def(c, "add_axes", (i, a, k) =>
        {
            var fig = Self<PlotFigure>(a[0]);
            var r = M.Dbl(a[1]);
            return W(State.SetCurrent(fig.AddAxes(null, (r[0], r[1], r[2], r[3]))));
        });
        Def(c, "subplots", (i, a, k) => Subplots(Self<PlotFigure>(a[0]), a.Skip(1).ToArray(), new Kw(k)));
        Def(c, "suptitle", (i, a, k) =>
        {
            var fig = Self<PlotFigure>(a[0]);
            fig.SuptitleText = AxesApi.Text(a[1], i);
            var kw = new Kw(k);
            if (kw.Has("fontsize", "size")) fig.SuptitleSize = AxesApi.FontSize(kw.Get("fontsize", "size")!, 12);
            return W(new TextArtist(fig.SuptitleText, 0.5, 0.98));
        });
        Def(c, "tight_layout", (i, a, k) =>
        {
            var fig = Self<PlotFigure>(a[0]); var kw = new Kw(k);
            fig.Tight = true; fig.TightPad = kw.Dbl(1.08, "pad"); fig.TightWPad = kw.DblOrNull("w_pad"); fig.TightHPad = kw.DblOrNull("h_pad"); fig.Layout();
            return PyNone.Instance;
        });
        Def(c, "colorbar", (i, a, k) => ColorbarFor(Self<PlotFigure>(a[0]), a.Skip(1).ToArray(), new Kw(k)));
        Def(c, "savefig", (i, a, k) => PlotUtil.Savefig(Self<PlotFigure>(a[0]), i, a.Skip(1).ToArray(), new Kw(k)));
        Def(c, "set_size_inches", (i, a, k) =>
        {
            var fig = Self<PlotFigure>(a[0]);
            double[] v = a.Length > 2 ? new[] { PyOps.AsDouble(a[1]), PyOps.AsDouble(a[2]) } : M.Dbl(a[1]);
            fig.WidthIn = v[0]; fig.HeightIn = v[1];
            return PyNone.Instance;
        });
        Def(c, "get_size_inches", (i, a, k) => { var f = Self<PlotFigure>(a[0]); return M.Arr(new[] { f.WidthIn, f.HeightIn }); });
        Def(c, "set_dpi", (i, a, k) => { Self<PlotFigure>(a[0]).Dpi = PyOps.AsDouble(a[1]); return PyNone.Instance; });
        Def(c, "get_dpi", (i, a, k) => Self<PlotFigure>(a[0]).Dpi);
        Def(c, "set_facecolor", (i, a, k) => { Self<PlotFigure>(a[0]).FaceColor = M.Color(a[1]); return PyNone.Instance; });
        Def(c, "subplots_adjust", (i, a, k) => { SubplotsAdjust(Self<PlotFigure>(a[0]), new Kw(k)); return PyNone.Instance; });
        Def(c, "gca", (i, a, k) => W(State.Gca()));
        Def(c, "clf", (i, a, k) => { var f = Self<PlotFigure>(a[0]); f.Axes.Clear(); f.SuptitleText = null; return PyNone.Instance; });
        Def(c, "text", (i, a, k) =>
        {
            var fig = Self<PlotFigure>(a[0]);
            var ax = fig.Axes.FirstOrDefault() ?? fig.AddAxes(GridCell.Single(1, 1, 0));
            var tx = new TextArtist(AxesApi.Text(a[3], i), PyOps.AsDouble(a[1]), PyOps.AsDouble(a[2])) { Space = CoordSpace.Figure };
            AxesApi.ApplyTextKw(tx, new Kw(k));
            ax.Add(tx);
            return W(tx);
        });
        Prop<PlotFigure>(c, "axes", f => L(f.Axes.Select(x => W(x))));
        Prop<PlotFigure>(c, "subplotpars", f => { var e = f.Effective; var d = new PyInstance(NewClass("SubplotParams")); d.Dict["left"] = e.Left; d.Dict["right"] = e.Right; d.Dict["bottom"] = e.Bottom; d.Dict["top"] = e.Top; d.Dict["wspace"] = e.WSpace; d.Dict["hspace"] = e.HSpace; return d; });
        Prop<PlotFigure>(c, "number", f => new BigInteger(f.Number));
        Prop<PlotFigure>(c, "dpi", f => f.Dpi, (_, f, v) => f.Dpi = PyOps.AsDouble(v));
        Prop<PlotFigure>(c, "figsize", f => M.Arr(new[] { f.WidthIn, f.HeightIn }));
        Prop<PlotFigure>(c, "transFigure", _ => Transform(CoordSpace.Figure));
        Prop<PlotFigure>(c, "canvas", f => new PyInstance(CanvasClass) { Native = f });
        Def(c, "__repr__", (_, a, _) => { var f = Self<PlotFigure>(a[0]); return $"<Figure size {(int)(f.WidthIn * f.Dpi)}x{(int)(f.HeightIn * f.Dpi)} with {f.Axes.Count(x => !x.HasColorbarAxes)} Axes>"; });
        return c;
    }

    private static PyClass BuildCanvas()
    {
        var c = NewClass("FigureCanvasAgg", "matplotlib.backends.backend_agg");
        Def(c, "draw", (_, a, _) => { Self<PlotFigure>(a[0]).Layout(); return PyNone.Instance; });
        Def(c, "draw_idle", (_, a, _) => PyNone.Instance);
        Def(c, "flush_events", (_, a, _) => PyNone.Instance);
        return c;
    }

    public static void SubplotsAdjust(PlotFigure fig, Kw kw)
    {
        var p = fig.Params;
        if (kw.Has("left")) p.Left = kw.Dbl(p.Left, "left");
        if (kw.Has("right")) p.Right = kw.Dbl(p.Right, "right");
        if (kw.Has("bottom")) p.Bottom = kw.Dbl(p.Bottom, "bottom");
        if (kw.Has("top")) p.Top = kw.Dbl(p.Top, "top");
        if (kw.Has("wspace")) p.WSpace = kw.Dbl(p.WSpace, "wspace");
        if (kw.Has("hspace")) p.HSpace = kw.Dbl(p.HSpace, "hspace");
        fig.Tight = false;
    }

    public static object AddSubplot(PlotFigure fig, object[] a, Kw kw)
    {
        GridCell cell;
        if (a.Length == 1 && a[0] is PyInstance { Native: GridCell gc }) cell = gc;
        else if (a.Length == 1 && a[0] is BigInteger code && code >= 100)
        {
            int n = (int)code; cell = GridCell.Single(n / 100, n / 10 % 10, n % 10 - 1);
        }
        else if (a.Length >= 3)
        {
            int rows = (int)PyOps.AsBigInt(a[0], "nrows"), cols = (int)PyOps.AsBigInt(a[1], "ncols");
            if (a[2] is PyTuple tp)
            {
                int lo = (int)PyOps.AsBigInt(tp.Items[0], "index") - 1, hi = (int)PyOps.AsBigInt(tp.Items[1], "index") - 1;
                cell = new GridCell(rows, cols, lo / cols, hi / cols + 1, Math.Min(lo % cols, hi % cols), Math.Max(lo % cols, hi % cols) + 1);
            }
            else
            {
                int idx = (int)PyOps.AsBigInt(a[2], "index");
                if (idx < 1 || idx > rows * cols) throw PyErr.ValueError($"num must be an integer with 1 <= num <= {rows * cols}, not {idx}");
                cell = GridCell.Single(rows, cols, idx - 1);
            }
        }
        else if (a.Length == 0) cell = GridCell.Single(1, 1, 0);
        else throw PyErr.TypeError("add_subplot expects (nrows, ncols, index), a 3-digit integer or a SubplotSpec");
        var ax = fig.AddAxes(cell);
        if (kw.Str("projection") == "3d") { ax.Is3D = true; ax.Anchor = (0.5, 0.5); }
        return W(State.SetCurrent(ax));
    }

    public static object Subplots(PlotFigure fig, object[] a, Kw kw)
    {
        int rows = a.Length > 0 ? (int)PyOps.AsBigInt(a[0], "nrows") : kw.Int(1, "nrows");
        int cols = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "ncols") : kw.Int(1, "ncols");
        bool squeeze = kw.Bool(true, "squeeze");
        if (kw.Get("gridspec_kw") is PyDict gk)
        {
            var p = fig.Params;
            if (gk.TryGet("wspace", out var ws)) p.WSpace = PyOps.AsDouble(ws);
            if (gk.TryGet("hspace", out var hs)) p.HSpace = PyOps.AsDouble(hs);
        }
        var axes = new PlotAxes[rows * cols];
        for (int q = 0; q < axes.Length; q++) axes[q] = fig.AddAxes(GridCell.Single(rows, cols, q));
        State.SetCurrent(axes[0]);
        bool shareX = kw.Get("sharex") is true or "all" or "col", shareY = kw.Get("sharey") is true or "all" or "row";
        if (shareX) for (int q = 0; q < axes.Length; q++) if (q / cols != rows - 1) axes[q].XLabelsVisible = false;
        if (shareY) for (int q = 0; q < axes.Length; q++) if (q % cols != 0) axes[q].YLabelsVisible = false;
        if (rows * cols == 1 && squeeze) return W(axes[0]);
        int[] shape = squeeze ? (rows == 1 ? new[] { cols } : cols == 1 ? new[] { rows } : new[] { rows, cols }) : new[] { rows, cols };
        return new PyInstance(AxesGridClass) { Native = new AxesGrid(axes, shape) };
    }

    // ---------------------------------------------------------------------------------------------- colorbar

    public static object ColorbarFor(PlotFigure fig, object[] a, Kw kw)
    {
        var mappable = (a.Length > 0 ? a[0] : kw.Get("mappable")) is PyInstance { Native: Artist art } ? art : throw PyErr.TypeError("colorbar expects a mappable (an image or scatter)");
        PlotAxes host = kw.Get("ax") is PyInstance { Native: PlotAxes h } ? h
            : kw.Get("ax") is PyList or PyTuple && M.Items(kw.Get("ax")!)[0] is PyInstance { Native: PlotAxes h2 } ? h2
            : kw.Get("ax") is PyInstance { Native: AxesGrid g } ? g.Flat[0]
            : mappable.Parent ?? State.Gca();
        var cax = fig.AddAxes(null);
        cax.ColorbarHost = host;
        cax.CbFraction = kw.Dbl(0.15, "fraction");
        cax.CbPad = kw.Dbl(0.05, "pad");
        cax.CbShrink = kw.Dbl(1, "shrink");
        var group = kw.Get("ax") is PyInstance { Native: AxesGrid gg } ? gg.Flat.ToList()
            : kw.Get("ax") is PyList or PyTuple ? M.Items(kw.Get("ax")!).Select(o => (PlotAxes)((PyInstance)o).Native!).ToList() : null;
        if (group is { Count: > 1 }) cax.ColorbarHostGroup = group;
        var cb = new Colorbar(mappable, cax);
        cax.Colorbar = null;
        host.Colorbar = cb;
        var (lo, hi) = cb.Range;
        const int N = 256;
        var px = new byte[N * 4];
        for (int q = 0; q < N; q++)
        {
            // row 0 is the lowest value (origin lower)
            var col = cb.Cmap.At((q + 0.5) / N);
            px[4 * q] = col.Red; px[4 * q + 1] = col.Green; px[4 * q + 2] = col.Blue; px[4 * q + 3] = 255;
        }
        var img = new ImageArtist { Rgba = px, Rows = N, Cols = 1, Origin = "lower", ExtentValue = (0, 1, lo, hi), Interpolation = "nearest" };
        cax.Add(img);
        cax.XLimSet = (0, 1); cax.YLimSet = (lo, hi);
        cax.YOnRight = true; cax.XTicksVisible = false; cax.XLabelsVisible = false;
        cax.BoxAspect = 20; cax.Anchor = (0, 0.5);
        if (kw.Str("label") is { } lab) { cax.YLabel = lab; cb.Label = lab; }
        return W(cb);
    }

    private static PyClass BuildColorbar()
    {
        var c = NewClass("Colorbar", "matplotlib.colorbar");
        Def(c, "set_label", (i, a, k) => { var cb = Self<Colorbar>(a[0]); cb.Axes.YLabel = AxesApi.Text(a[1], i); return PyNone.Instance; });
        Def(c, "set_ticks", (i, a, k) => { AxesApi.Table["set_yticks"](Self<Colorbar>(a[0]).Axes, i, a.Skip(1).ToArray(), new Kw(k)); return PyNone.Instance; });
        Def(c, "set_ticklabels", (i, a, k) => { Self<Colorbar>(a[0]).Axes.YTickLabelsSet = M.StrList(a[1]); return PyNone.Instance; });
        Prop<Colorbar>(c, "ax", cb => W(cb.Axes));
        return c;
    }

    // ---------------------------------------------------------------------------------------------- subplot grid

    private static PyClass BuildGrid()
    {
        var c = NewClass("ndarray", "numpy");
        static AxesGrid G(object o) => Self<AxesGrid>(o);
        static object Pick(AxesGrid g, object index)
        {
            var idx = new NDArray(DType.Int64, Enumerable.Range(0, g.Flat.Length).Select(v => (long)v).ToArray(), (int[])g.Shape.Clone());
            var (items, allInts) = Conv.ParseIndex(index);
            var r = idx.Get(items);
            if (r.Ndim == 0) return W(g.Flat[(int)Convert.ToInt64(r.GetAt(0))]);
            var flat = r.ToArray<long>().Select(v => g.Flat[(int)v]).ToArray();
            return new PyInstance(AxesGridClass) { Native = new AxesGrid(flat, (int[])r.Shape.Clone()) };
        }
        Def(c, "__getitem__", (_, a, _) => Pick(G(a[0]), a[1]));
        Def(c, "__len__", (_, a, _) => new BigInteger(G(a[0]).Shape[0]));
        Def(c, "__iter__", (_, a, _) =>
        {
            var g = G(a[0]);
            IEnumerable<object> It()
            {
                for (int q = 0; q < g.Shape[0]; q++) yield return Pick(g, new BigInteger(q));
            }
            return new PyIterator(It().GetEnumerator());
        });
        Def(c, "ravel", (_, a, _) => new PyInstance(AxesGridClass) { Native = new AxesGrid(G(a[0]).Flat, new[] { G(a[0]).Flat.Length }) });
        Def(c, "flatten", (_, a, _) => new PyInstance(AxesGridClass) { Native = new AxesGrid(G(a[0]).Flat, new[] { G(a[0]).Flat.Length }) });
        Prop<AxesGrid>(c, "flat", g => new PyInstance(AxesGridClass) { Native = new AxesGrid(g.Flat, new[] { g.Flat.Length }) });
        Prop<AxesGrid>(c, "shape", g => new PyTuple(g.Shape.Select(v => (object)new BigInteger(v)).ToArray()));
        Prop<AxesGrid>(c, "size", g => new BigInteger(g.Flat.Length));
        Prop<AxesGrid>(c, "ndim", g => new BigInteger(g.Shape.Length));
        Prop<AxesGrid>(c, "T", g =>
        {
            if (g.Shape.Length != 2) return new PyInstance(AxesGridClass) { Native = g };
            int r = g.Shape[0], cc = g.Shape[1];
            var flat = new PlotAxes[r * cc];
            for (int i = 0; i < r; i++) for (int j = 0; j < cc; j++) flat[j * r + i] = g.Flat[i * cc + j];
            return new PyInstance(AxesGridClass) { Native = new AxesGrid(flat, new[] { cc, r }) };
        });
        Def(c, "tolist", (_, a, _) => L(G(a[0]).Flat.Select(x => W(x))));
        return c;
    }

    // ---------------------------------------------------------------------------------------------- artists

    private static void CommonArtist(PyClass c)
    {
        Def(c, "set_alpha", (_, a, _) => { Self<Artist>(a[0]).Alpha = a[1] is PyNone ? 1 : PyOps.AsDouble(a[1]); return PyNone.Instance; });
        Def(c, "get_alpha", (_, a, _) => Self<Artist>(a[0]).Alpha);
        Def(c, "set_label", (i, a, _) => { Self<Artist>(a[0]).Label = AxesApi.Text(a[1], i); return PyNone.Instance; });
        Def(c, "get_label", (_, a, _) => Self<Artist>(a[0]).Label ?? "");
        Def(c, "set_visible", (i, a, _) => { Self<Artist>(a[0]).Visible = PyOps.Truthy(i, a[1]); return PyNone.Instance; });
        Def(c, "set_zorder", (_, a, _) => { Self<Artist>(a[0]).ZOrder = (int)PyOps.AsBigInt(a[1], "zorder"); return PyNone.Instance; });
        Def(c, "remove", (_, a, _) => { var art = Self<Artist>(a[0]); art.Parent?.Artists.Remove(art); return PyNone.Instance; });
        Prop<Artist>(c, "axes", art => art.Parent is { } p ? W(p) : PyNone.Instance);
    }

    private static PyClass BuildLine()
    {
        var c = NewClass("Line2D", "matplotlib.lines");
        CommonArtist(c);
        Def(c, "set_data", (_, a, _) => { var l = Self<Line2D>(a[0]); l.X = M.Dbl(a[1]); l.Y = M.Dbl(a[2]); return PyNone.Instance; });
        Def(c, "set_xdata", (_, a, _) => { Self<Line2D>(a[0]).X = M.Dbl(a[1]); return PyNone.Instance; });
        Def(c, "set_ydata", (_, a, _) => { Self<Line2D>(a[0]).Y = M.Dbl(a[1]); return PyNone.Instance; });
        Def(c, "get_xdata", (_, a, _) => M.Arr(Self<Line2D>(a[0]).X));
        Def(c, "get_ydata", (_, a, _) => M.Arr(Self<Line2D>(a[0]).Y));
        Def(c, "get_data", (_, a, _) => M.Tup(M.Arr(Self<Line2D>(a[0]).X), M.Arr(Self<Line2D>(a[0]).Y)));
        Def(c, "set_color", (_, a, _) => { Self<Line2D>(a[0]).Color = M.Color(a[1]); return PyNone.Instance; });
        Def(c, "get_color", (_, a, _) => M.Hex(Self<Line2D>(a[0]).Color));
        Def(c, "set_linewidth", (_, a, _) => { Self<Line2D>(a[0]).LineWidth = PyOps.AsDouble(a[1]); return PyNone.Instance; });
        Def(c, "set_linestyle", (_, a, _) => { Self<Line2D>(a[0]).LineStyle = M.NormalizeLineStyle((string)a[1]); return PyNone.Instance; });
        Def(c, "set_marker", (_, a, _) => { Self<Line2D>(a[0]).Marker = a[1] as string; return PyNone.Instance; });
        Def(c, "set_markersize", (_, a, _) => { Self<Line2D>(a[0]).MarkerSize = PyOps.AsDouble(a[1]); return PyNone.Instance; });
        return c;
    }

    private static PyClass BuildImage()
    {
        var c = NewClass("AxesImage", "matplotlib.image");
        CommonArtist(c);
        Def(c, "set_data", (_, a, _) =>
        {
            var im = Self<ImageArtist>(a[0]);
            var nd = M.Nd(a[1]);
            if (nd.Ndim == 2) { im.Rows = nd.Shape[0]; im.Cols = nd.Shape[1]; im.Values = nd.ToArray<double>(); im.Rgba = null; }
            return PyNone.Instance;
        });
        Def(c, "set_cmap", (_, a, _) => { Self<ImageArtist>(a[0]).Cmap = Cmaps.Resolve(a[1]); return PyNone.Instance; });
        Def(c, "get_cmap", (_, a, _) => W(Self<ImageArtist>(a[0]).Cmap));
        Def(c, "set_clim", (_, a, k) =>
        {
            var im = Self<ImageArtist>(a[0]); var kw = new Kw(k);
            if (a.Length > 1 && a[1] is not PyNone) im.VMin = PyOps.AsDouble(a[1]); else im.VMin = kw.DblOrNull("vmin");
            if (a.Length > 2 && a[2] is not PyNone) im.VMax = PyOps.AsDouble(a[2]); else im.VMax = kw.DblOrNull("vmax");
            return PyNone.Instance;
        });
        Def(c, "get_clim", (_, a, _) => { var r = Self<ImageArtist>(a[0]).Range; return M.Tup(r.lo, r.hi); });
        Def(c, "get_array", (_, a, _) => { var im = Self<ImageArtist>(a[0]); return im.Values is null ? PyNone.Instance : M.Arr(im.Values, im.Rows, im.Cols); });
        Def(c, "set_extent", (_, a, _) => { var e = M.Dbl(a[1]); Self<ImageArtist>(a[0]).ExtentValue = (e[0], e[1], e[2], e[3]); return PyNone.Instance; });
        return c;
    }

    private static PyClass BuildCollection()
    {
        var c = NewClass("PathCollection", "matplotlib.collections");
        CommonArtist(c);
        Def(c, "set_offsets", (_, a, _) =>
        {
            var s = Self<Scatter>(a[0]); var nd = M.Nd(a[1]); var v = nd.ToArray<double>();
            int n = nd.Shape[0]; s.X = new double[n]; s.Y = new double[n];
            for (int q = 0; q < n; q++) { s.X[q] = v[2 * q]; s.Y[q] = v[2 * q + 1]; }
            return PyNone.Instance;
        });
        Def(c, "set_array", (_, a, _) => { Self<Scatter>(a[0]).Values = M.Dbl(a[1]); return PyNone.Instance; });
        Def(c, "set_sizes", (_, a, _) => { Self<Scatter>(a[0]).Sizes = M.Dbl(a[1]); return PyNone.Instance; });
        Def(c, "set_clim", (_, a, k) => { var s = Self<Scatter>(a[0]); if (a.Length > 1 && a[1] is not PyNone) s.VMin = PyOps.AsDouble(a[1]); if (a.Length > 2 && a[2] is not PyNone) s.VMax = PyOps.AsDouble(a[2]); return PyNone.Instance; });
        Def(c, "set_cmap", (_, a, _) => { Self<Scatter>(a[0]).Cmap = Cmaps.Resolve(a[1]); return PyNone.Instance; });
        Def(c, "set_color", (_, a, _) => { Self<Scatter>(a[0]).Colors = new[] { M.Color(a[1]) }; Self<Scatter>(a[0]).Values = null; return PyNone.Instance; });
        Def(c, "set_facecolor", (_, a, _) => { Self<Scatter>(a[0]).Colors = new[] { M.Color(a[1]) }; return PyNone.Instance; });
        return c;
    }

    private static PyClass BuildPatch(string name)
    {
        var c = NewClass(name, "matplotlib.patches");
        CommonArtist(c);
        Def(c, "set_facecolor", (_, a, _) => { Self<Patch>(a[0]).FaceColor = M.Color(a[1]); return PyNone.Instance; });
        Def(c, "set_edgecolor", (_, a, _) => { Self<Patch>(a[0]).EdgeColor = M.Color(a[1]); return PyNone.Instance; });
        Def(c, "set_color", (_, a, _) => { var p = Self<Patch>(a[0]); p.FaceColor = M.Color(a[1]); p.EdgeColor = p.FaceColor; return PyNone.Instance; });
        Def(c, "set_linewidth", (_, a, _) => { Self<Patch>(a[0]).LineWidth = PyOps.AsDouble(a[1]); return PyNone.Instance; });
        Def(c, "set_fill", (i, a, _) => { Self<Patch>(a[0]).Fill = PyOps.Truthy(i, a[1]); return PyNone.Instance; });
        Def(c, "set_height", (_, a, _) => { Self<RectPatch>(a[0]).Height = PyOps.AsDouble(a[1]); return PyNone.Instance; });
        Def(c, "set_width", (_, a, _) => { Self<RectPatch>(a[0]).Width = PyOps.AsDouble(a[1]); return PyNone.Instance; });
        Def(c, "set_xy", (_, a, _) => { var r = Self<RectPatch>(a[0]); var v = M.Dbl(a[1]); r.X = v[0]; r.Y = v[1]; return PyNone.Instance; });
        Def(c, "get_height", (_, a, _) => Self<RectPatch>(a[0]).Height);
        Def(c, "get_width", (_, a, _) => Self<RectPatch>(a[0]).Width);
        Def(c, "get_x", (_, a, _) => Self<RectPatch>(a[0]).X);
        Def(c, "get_y", (_, a, _) => Self<RectPatch>(a[0]).Y);
        Def(c, "set_center", (_, a, _) => { var e = Self<EllipsePatch>(a[0]); var v = M.Dbl(a[1]); e.CX = v[0]; e.CY = v[1]; return PyNone.Instance; });
        Def(c, "set_radius", (_, a, _) => { var e = Self<EllipsePatch>(a[0]); e.RX = e.RY = PyOps.AsDouble(a[1]); return PyNone.Instance; });
        return c;
    }

    private static PyClass BuildContour()
    {
        var c = NewClass("QuadContourSet", "matplotlib.contour");
        CommonArtist(c);
        Prop<ContourArtist>(c, "levels", ca => M.Arr(ca.Levels));
        Def(c, "set_clim", (_, a, _) => PyNone.Instance);
        return c;
    }

    private static PyClass BuildText()
    {
        var c = NewClass("Text", "matplotlib.text");
        CommonArtist(c);
        Def(c, "set_text", (i, a, _) => { Self<TextArtist>(a[0]).Text = AxesApi.Text(a[1], i); return PyNone.Instance; });
        Def(c, "get_text", (_, a, _) => Self<TextArtist>(a[0]).Text);
        Def(c, "set_position", (_, a, _) => { var t = Self<TextArtist>(a[0]); var v = M.Dbl(a[1]); t.X = v[0]; t.Y = v[1]; return PyNone.Instance; });
        Def(c, "set_color", (_, a, _) => { Self<TextArtist>(a[0]).Color = M.Color(a[1]); return PyNone.Instance; });
        Def(c, "set_fontsize", (_, a, _) => { Self<TextArtist>(a[0]).FontSize = AxesApi.FontSize(a[1], 10); return PyNone.Instance; });
        Def(c, "set_rotation", (_, a, _) => { Self<TextArtist>(a[0]).Rotation = PyOps.AsDouble(a[1]); return PyNone.Instance; });
        return c;
    }

    // ---------------------------------------------------------------------------------------------- colormaps

    private static PyClass BuildColormap()
    {
        var c = NewClass("Colormap", "matplotlib.colors");
        Def(c, "__call__", (i, a, k) =>
        {
            var cm = Self<Colormap>(a[0]);
            object arg = a[1];
            if (arg is double d) { var v = cm.AtFloat(d); return M.Tup(v.r, v.g, v.b, v.a); }
            if (arg is BigInteger bi) { var v = cm.AtIndex((int)bi); return M.Tup(v.r, v.g, v.b, v.a); }
            var nd = M.Nd(arg);
            bool ints = nd.DType is not (DType.Float32 or DType.Float64 or DType.Float16);
            var vals = nd.ToArray<double>();
            var o = new double[vals.Length * 4];
            for (int q = 0; q < vals.Length; q++)
            {
                var v = ints ? cm.AtIndex((int)vals[q]) : cm.AtFloat(vals[q]);
                o[4 * q] = v.r; o[4 * q + 1] = v.g; o[4 * q + 2] = v.b; o[4 * q + 3] = v.a;
            }
            return M.Arr(o, nd.Shape.Concat(new[] { 4 }).ToArray());
        });
        Prop<Colormap>(c, "colors", cm => new PyList(Enumerable.Range(0, cm.N).Select(q => { var v = cm.AtIndex(q); return (object)M.Tup(v.r, v.g, v.b); })));
        Def(c, "copy", (_, a, _) => a[0]);
        Def(c, "set_bad", (_, a, _) => PyNone.Instance);
        Def(c, "set_under", (_, a, _) => PyNone.Instance);
        Def(c, "set_over", (_, a, _) => PyNone.Instance);
        Prop<Colormap>(c, "name", cm => cm.Name);
        Prop<Colormap>(c, "N", cm => new BigInteger(cm.N));
        Def(c, "reversed", (_, a, _) => W(Colormap.Get(Self<Colormap>(a[0]).Name.EndsWith("_r") ? Self<Colormap>(a[0]).Name[..^2] : Self<Colormap>(a[0]).Name + "_r")));
        Def(c, "__repr__", (_, a, _) => $"<matplotlib.colors.Colormap object ({Self<Colormap>(a[0]).Name})>");
        return c;
    }
}
