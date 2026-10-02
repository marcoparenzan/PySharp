// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Plot;
using PySharpLib.Importing;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using SkiaSharp;
using PlotAxes = NDSharp.Plot.Axes;
using PlotFigure = NDSharp.Plot.Figure;

namespace PySharpLib.Matplotlib;

/// <summary>Opt-in registration of <c>matplotlib</c> (pyplot, patches, cm, colors, gridspec) over NDSharp.Plot.</summary>
public static class MatplotlibRegistration
{
    /// <summary>Registers the modules; <paramref name="showSink"/> receives a PNG for every figure shown (plt.show() / end of cell).</summary>
    public static void Register(Importer importer, Action<byte[]>? showSink = null)
    {
        State.ShowSink = showSink;
        importer.RegisterBuiltin("matplotlib", _ => Modules.Root());
        importer.RegisterBuiltin("matplotlib.pyplot", _ => Modules.PyplotModule());
        importer.RegisterBuiltin("matplotlib.patches", _ => Modules.Patches());
        importer.RegisterBuiltin("matplotlib.cm", _ => Modules.Cm());
        importer.RegisterBuiltin("matplotlib.colors", _ => Modules.ColorsModule());
        importer.RegisterBuiltin("matplotlib.gridspec", _ => Modules.GridSpecModule());
        importer.RegisterBuiltin("matplotlib.style", _ => Modules.Style());
        importer.RegisterBuiltin("matplotlib.figure", _ => Modules.FigureModule());
        importer.RegisterBuiltin("matplotlib.axes", _ => Modules.AxesModule());
        importer.RegisterBuiltin("mpl_toolkits", _ => new PyModule("mpl_toolkits"));
        importer.RegisterBuiltin("mpl_toolkits.mplot3d", _ => Modules.Mplot3d());
        importer.RegisterBuiltin("mpl_toolkits.mplot3d.art3d", _ => Modules.Art3d());
    }

    /// <summary>The inline backend's end-of-cell flush: renders and closes the open figures. Returns how many were shown.</summary>
    public static int FlushFigures()
    {
        int n = State.Figures.Count(f => f.Axes.Count > 0);
        State.ShowAll();
        return n;
    }

    /// <summary>Closes every open figure (a fresh session).</summary>
    public static void ResetFigures() => State.CloseAll();

    /// <summary>Changes where shown figures go (a kernel installs its image sink here).</summary>
    public static void SetShowSink(Action<byte[]>? sink) => State.ShowSink = sink;
}

internal static class PlotUtil
{
    public static object Savefig(PlotFigure fig, Interp interp, object[] a, Kw kw)
    {
        object target = a.Length > 0 ? a[0] : kw.Get("fname") ?? throw PyErr.TypeError("savefig() missing required argument 'fname'");
        double? dpi = kw.Get("dpi") is { } d && d is not string ? PyOps.AsDouble(d) : null;
        bool tight = kw.Str("bbox_inches") == "tight";
        string? fmt = kw.Str("format");
        string? path = target as string;
        if (path is null && PyOps.TypeName(target) is "PosixPath" or "WindowsPath" or "Path") path = PyOps.Str(interp, target);
        fmt ??= path is not null && Path.GetExtension(path).TrimStart('.').ToLowerInvariant() is { Length: > 0 } e ? e : "png";
        if (fmt is "pdf" or "svg" or "ps" or "eps") throw PyErr.NotImplementedError($"savefig: the '{fmt}' format is not supported (png/jpg only)");
        var png = fig.RenderPng(dpi, tight, kw.Bool(false, "transparent"));
        byte[] bytes = png;
        if (fmt is "jpg" or "jpeg")
        {
            using var bmp = SKBitmap.Decode(png);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, 95);
            bytes = data.ToArray();
        }
        if (path is not null) File.WriteAllBytes(path, bytes);
        else if (!interp.TryCallMethod(target, "write", new object[] { new PyBytes(bytes) }, out _))
            throw PyErr.TypeError("savefig: fname must be a path or a binary file-like object");
        return PyNone.Instance;
    }
}

internal static class Modules
{
    private static PyModule? _root, _pyplot, _patches, _cm, _colors, _gridspec, _style, _figure, _axes;

    private static void Def(PyModule m, string name, BuiltinFn fn) => m.Dict[name] = Native.Fn(name, fn);

    public static PyModule Root() => _root ??= BuildRoot();

    private static PyModule BuildRoot()
    {
        var m = new PyModule("matplotlib");
        m.Dict["__version__"] = "3.11.2-pysharp (NDSharp.Plot)";
        m.Dict["rcParams"] = State.RcParams;
        Def(m, "use", (_, _, _) => PyNone.Instance);
        Def(m, "get_backend", (_, _, _) => "module://matplotlib_inline.backend_inline");
        Def(m, "rc", (_, a, k) =>
        {
            if (a.Length >= 1 && a[0] is string group && k is not null)
                foreach (var (key, v) in k) State.RcParams[group + "." + key] = v;
            return PyNone.Instance;
        });
        return m;
    }

    // ------------------------------------------------------------------------------------------ pyplot

    public static PyModule PyplotModule() => _pyplot ??= BuildPyplot();

    private static PlotAxes CurrentAxes() => State.Gca();

    private static PyModule BuildPyplot()
    {
        var m = new PyModule("matplotlib.pyplot");
        m.Dict["rcParams"] = State.RcParams;
        m.Dict["Figure"] = Classes.FigureClass;
        m.Dict["Axes"] = Classes.AxesClass;
        m.Dict["Line2D"] = Classes.Line2DClass;

        // everything on Axes that has the same name in pyplot targets the current axes
        foreach (var name in new[]
        {
            "plot", "scatter", "bar", "barh", "hist", "imshow", "fill_between", "stem", "errorbar", "axhline", "axvline", "text",
            "annotate", "arrow", "legend", "grid", "axis", "semilogy", "semilogx", "loglog", "tick_params", "margins", "cla",
        })
        {
            var fn = AxesApi.Table[name];
            Def(m, name, (i, a, k) => fn(CurrentAxes(), i, a, new Kw(k)));
        }
        void Alias(string plt, string axName)
        {
            var fn = AxesApi.Table[axName];
            Def(m, plt, (i, a, k) => fn(CurrentAxes(), i, a, new Kw(k)));
        }
        Alias("title", "set_title"); Alias("xlabel", "set_xlabel"); Alias("ylabel", "set_ylabel");
        Alias("xscale", "set_xscale"); Alias("yscale", "set_yscale");
        Def(m, "xlim", (i, a, k) => a.Length == 0 && (k is null || k.Count == 0) ? AxesApi.Table["get_xlim"](CurrentAxes(), i, a, new Kw(k)) : AxesApi.Table["set_xlim"](CurrentAxes(), i, a, new Kw(k)));
        Def(m, "ylim", (i, a, k) => a.Length == 0 && (k is null || k.Count == 0) ? AxesApi.Table["get_ylim"](CurrentAxes(), i, a, new Kw(k)) : AxesApi.Table["set_ylim"](CurrentAxes(), i, a, new Kw(k)));
        Def(m, "xticks", (i, a, k) =>
        {
            var ax = CurrentAxes();
            if (a.Length == 0) return AxesApi.Table["get_xticks"](ax, i, a, new Kw(k));
            AxesApi.Table["set_xticks"](ax, i, a, new Kw(k));
            return PyNone.Instance;
        });
        Def(m, "yticks", (i, a, k) =>
        {
            var ax = CurrentAxes();
            if (a.Length == 0) return AxesApi.Table["get_yticks"](ax, i, a, new Kw(k));
            AxesApi.Table["set_yticks"](ax, i, a, new Kw(k));
            return PyNone.Instance;
        });
        Def(m, "gca", (_, _, _) => Classes.W(CurrentAxes()));
        Def(m, "gcf", (_, _, _) => Classes.W(State.Gcf()));
        Def(m, "sca", (_, a, _) => { State.SetCurrent((PlotAxes)((PyInstance)a[0]).Native!); return PyNone.Instance; });
        Def(m, "figure", (i, a, k) =>
        {
            var kw = new Kw(k);
            double[]? fs = (kw.Get("figsize") ?? (a.Length > 1 ? a[1] : null)) is { } f ? M.Dbl(f) : null;
            if (a.Length > 0 && a[0] is BigInteger num && State.Figures.FirstOrDefault(x => x.Number == (int)num) is { } existing)
            {
                State.Current = existing;
                return Classes.W(existing);
            }
            var fig = State.NewFigure(fs?[0], fs?[1], kw.DblOrNull("dpi"));
            if (a.Length > 0 && a[0] is BigInteger n2) fig.Number = (int)n2;
            if (kw.Get("facecolor", "fc") is { } fc) fig.FaceColor = M.Color(fc);
            return Classes.W(fig);
        });
        Def(m, "subplots", (i, a, k) =>
        {
            var kw = new Kw(k);
            double[]? fs = kw.Get("figsize") is { } f ? M.Dbl(f) : null;
            var fig = State.NewFigure(fs?[0], fs?[1], kw.DblOrNull("dpi"));
            var axes = Classes.Subplots(fig, a, kw);
            return M.Tup(Classes.W(fig), axes);
        });
        Def(m, "subplot", (i, a, k) =>
        {
            var fig = State.Gcf();
            if (a.Length == 1 && a[0] is BigInteger code && code >= 100)
            {
                int n = (int)code; var cell = GridCell.Single(n / 100, n / 10 % 10, n % 10 - 1);
                var existing = fig.Axes.FirstOrDefault(x => x.Cell == cell && !x.HasColorbarAxes);
                return Classes.W(State.SetCurrent(existing ?? fig.AddAxes(cell)));
            }
            return Classes.AddSubplot(fig, a, new Kw(k));
        });
        Def(m, "axes", (i, a, k) =>
        {
            var fig = State.Gcf();
            if (a.Length > 0 && a[0] is PyList or PyTuple) { var r = M.Dbl(a[0]); return Classes.W(State.SetCurrent(fig.AddAxes(null, (r[0], r[1], r[2], r[3])))); }
            return Classes.W(State.SetCurrent(fig.AddAxes(GridCell.Single(1, 1, 0))));
        });
        Def(m, "suptitle", (i, a, k) => Classes.FigureClass.Dict.TryGet("suptitle", out var f) ? ((PyBuiltinFunction)f).Fn(i, new[] { Classes.W(State.Gcf()) }.Concat(a).ToArray(), k) : PyNone.Instance);
        Def(m, "tight_layout", (i, a, k) => { var kw = new Kw(k); var f = State.Gcf(); f.Tight = true; f.TightPad = kw.Dbl(1.08, "pad"); f.TightWPad = kw.DblOrNull("w_pad"); f.TightHPad = kw.DblOrNull("h_pad"); f.Layout(); return PyNone.Instance; });
        Def(m, "subplots_adjust", (i, a, k) => { Classes.SubplotsAdjust(State.Gcf(), new Kw(k)); return PyNone.Instance; });
        Def(m, "colorbar", (i, a, k) =>
        {
            var kw = new Kw(k);
            var args = a;
            if (args.Length == 0 && kw.Get("mappable") is null)
            {
                var ax = CurrentAxes();
                var art = ax.Artists.LastOrDefault(x => x is ImageArtist or Scatter) ?? throw PyErr.RuntimeError("No mappable was found to use for colorbar creation.");
                args = new[] { Classes.W(art) };
            }
            var d = k is null ? new Dictionary<string, object>() : new Dictionary<string, object>(k);
            if (!d.ContainsKey("ax") && args.Length > 0 && args[0] is PyInstance { Native: Artist ar } && ar.Parent is { } host) d["ax"] = Classes.W(host);
            return Classes.ColorbarFor(State.Gcf(), args, new Kw(d));
        });
        Def(m, "savefig", (i, a, k) => PlotUtil.Savefig(State.Gcf(), i, a, new Kw(k)));
        Def(m, "show", (_, _, _) => { State.ShowAll(); return PyNone.Instance; });
        Def(m, "draw", (_, _, _) => PyNone.Instance);
        Def(m, "pause", (_, _, _) => PyNone.Instance);
        Def(m, "ion", (_, _, _) => PyNone.Instance);
        Def(m, "ioff", (_, _, _) => PyNone.Instance);
        Def(m, "close", (_, a, _) =>
        {
            if (a.Length == 0) { if (State.Current is { } c) State.Close(c); }
            else if (a[0] is string s && s == "all") State.CloseAll();
            else if (a[0] is PyInstance { Native: PlotFigure f }) State.Close(f);
            else if (a[0] is BigInteger n && State.Figures.FirstOrDefault(x => x.Number == (int)n) is { } ff) State.Close(ff);
            return PyNone.Instance;
        });
        Def(m, "clf", (_, _, _) => { var f = State.Gcf(); f.Axes.Clear(); f.SuptitleText = null; return PyNone.Instance; });
        Def(m, "get_cmap", (_, a, _) => Classes.W(Cmaps.Resolve(a.Length > 0 ? a[0] : null)));
        Def(m, "rc", (_, a, k) =>
        {
            if (a.Length >= 1 && a[0] is string group && k is not null)
                foreach (var (key, v) in k) State.RcParams[group + "." + key] = v;
            return PyNone.Instance;
        });
        Def(m, "set_cmap", (_, a, _) => { State.RcParams["image.cmap"] = a[0] is string s ? s : ((Colormap)((PyInstance)a[0]).Native!).Name; return PyNone.Instance; });
        Def(m, "fignum_exists", (_, a, _) => a[0] is BigInteger n && State.Figures.Any(x => x.Number == (int)n));
        Def(m, "get_fignums", (_, _, _) => new PyList(State.Figures.Select(f => (object)new BigInteger(f.Number))));
        foreach (var n in new[] { "Circle", "Rectangle", "Polygon", "Ellipse" }) m.Dict[n] = Patches().Dict[n];
        m.Dict["cm"] = Cm();
        m.Dict["style"] = Style();
        m.Dict["colormaps"] = Cm();
        return m;
    }

    // ------------------------------------------------------------------------------------------ patches

    public static PyModule Patches() => _patches ??= BuildPatches();

    private static PyModule BuildPatches()
    {
        var m = new PyModule("matplotlib.patches");
        void Shape(string name, Func<object[], Kw, Patch> make)
        {
            var cls = new PyClass(name, new List<PyClass> { Classes.PatchClass });
            cls.Dict["__module__"] = "matplotlib.patches";
            cls.Dict["__new__"] = Native.Fn(name + ".__new__", (i, a, k) =>
            {
                var kw = new Kw(k);
                var p = make(a.Skip(1).ToArray(), kw);
                if (kw.Get("facecolor", "fc", "color") is { } fc) p.FaceColor = M.Color(fc);
                if (kw.Get("edgecolor", "ec", "color") is { } ec) p.EdgeColor = M.Color(ec);
                if (kw.Contains("fill")) p.Fill = kw.Bool(true, "fill");
                if (kw.Has("linewidth", "lw")) p.LineWidth = kw.Dbl(1, "linewidth", "lw");
                if (kw.Str("linestyle", "ls") is { } ls) p.LineStyle = M.NormalizeLineStyle(ls);
                p.Alpha = kw.Dbl(1, "alpha"); p.ZOrder = kw.Int(1, "zorder");
                if (kw.Str("label") is { } lab) p.Label = lab;
                return State.Wrap(p, _ => cls);
            });
            cls.Dict["__init__"] = new PyBuiltinFunction(name + ".__init__", (_, _, _) => PyNone.Instance);
            m.Dict[name] = cls;
        }
        Shape("Rectangle", (a, kw) =>
        {
            var xy = M.Dbl(a[0]);
            return new RectPatch(xy[0], xy[1], PyOps.AsDouble(a.Length > 1 ? a[1] : kw.Get("width")!), PyOps.AsDouble(a.Length > 2 ? a[2] : kw.Get("height")!)) { Angle = kw.Dbl(0, "angle") };
        });
        Shape("Circle", (a, kw) =>
        {
            var xy = M.Dbl(a[0]); double r = PyOps.AsDouble(a.Length > 1 ? a[1] : kw.Get("radius")!);
            return new EllipsePatch(xy[0], xy[1], r, r);
        });
        Shape("Ellipse", (a, kw) =>
        {
            var xy = M.Dbl(a[0]);
            double w = PyOps.AsDouble(a.Length > 1 ? a[1] : kw.Get("width")!), h = PyOps.AsDouble(a.Length > 2 ? a[2] : kw.Get("height")!);
            return new EllipsePatch(xy[0], xy[1], w / 2, h / 2);
        });
        Shape("Polygon", (a, kw) =>
        {
            var nd = M.Nd(a[0]); var v = nd.ToArray<double>(); int n = nd.Shape[0];
            var xs = new double[n]; var ys = new double[n];
            for (int q = 0; q < n; q++) { xs[q] = v[2 * q]; ys[q] = v[2 * q + 1]; }
            return new PolygonPatch(xs, ys) { Closed = kw.Bool(true, "closed") };
        });
        Shape("FancyArrow", (a, kw) =>
        {
            double width = kw.Dbl(0.001, "width");
            double hw = kw.Dbl(3 * width, "head_width"), hl = kw.Dbl(1.5 * hw, "head_length");
            return FancyArrow.Create(PyOps.AsDouble(a[0]), PyOps.AsDouble(a[1]), PyOps.AsDouble(a[2]), PyOps.AsDouble(a[3]), width, hw, hl, kw.Bool(false, "length_includes_head"));
        });
        m.Dict["Patch"] = Classes.PatchClass;
        return m;
    }

    // ------------------------------------------------------------------------------------------ cm / colors

    public static PyModule Cm() => _cm ??= BuildCm();

    private static PyModule BuildCm()
    {
        var m = new PyModule("matplotlib.cm");
        foreach (var name in Colormap.Names) m.Dict[name] = Classes.W(Colormap.Get(name));
        foreach (var name in Colormap.Names.ToList()) m.Dict[name + "_r"] = Classes.W(Colormap.Get(name + "_r"));
        Def(m, "get_cmap", (_, a, _) => Classes.W(Cmaps.Resolve(a.Length > 0 ? a[0] : null)));
        Def(m, "__getitem__", (_, a, _) => Classes.W(Cmaps.Resolve(a[0])));
        return m;
    }

    public static PyModule ColorsModule() => _colors ??= BuildColors();

    private static PyModule BuildColors()
    {
        var m = new PyModule("matplotlib.colors");
        var norm = new PyClass("Normalize", new List<PyClass>());
        norm.Dict["__init__"] = Native.Fn("Normalize.__init__", (i, a, k) =>
        {
            var self = (PyInstance)a[0]; var kw = new Kw(k);
            self.Dict["vmin"] = a.Length > 1 ? a[1] : kw.Get("vmin") ?? PyNone.Instance;
            self.Dict["vmax"] = a.Length > 2 ? a[2] : kw.Get("vmax") ?? PyNone.Instance;
            return PyNone.Instance;
        });
        m.Dict["Normalize"] = norm;
        var listed = new PyClass("ListedColormap", new List<PyClass>());
        m.Dict["ListedColormap"] = listed;
        Def(m, "to_rgb", (_, a, _) => { var c = M.Color(a[0]); return M.Tup(c.Red / 255.0, c.Green / 255.0, c.Blue / 255.0); });
        Def(m, "to_rgba", (_, a, _) => M.Rgba(M.Color(a[0])));
        Def(m, "to_hex", (_, a, _) => M.Hex(M.Color(a[0])));
        Def(m, "is_color_like", (_, a, _) => { try { M.Color(a[0]); return true; } catch (PyRaise) { return false; } });
        m.Dict["Colormap"] = Classes.ColormapClass;
        return m;
    }

    // ------------------------------------------------------------------------------------------ gridspec, style, figure, axes

    public static PyModule GridSpecModule() => _gridspec ??= BuildGridSpec();

    private static PyModule BuildGridSpec()
    {
        var m = new PyModule("matplotlib.gridspec");
        var gs = new PyClass("GridSpec", new List<PyClass>());
        gs.Dict["__init__"] = Native.Fn("GridSpec.__init__", (i, a, k) =>
        {
            var self = (PyInstance)a[0]; var kw = new Kw(k);
            int rows = a.Length > 1 ? (int)PyOps.AsBigInt(a[1], "nrows") : kw.Int(1, "nrows");
            int cols = a.Length > 2 ? (int)PyOps.AsBigInt(a[2], "ncols") : kw.Int(1, "ncols");
            self.Native = (rows, cols);
            if (kw.Get("figure") is PyInstance { Native: PlotFigure f })
            {
                if (kw.Has("wspace")) f.Params.WSpace = kw.Dbl(0.2, "wspace");
                if (kw.Has("hspace")) f.Params.HSpace = kw.Dbl(0.2, "hspace");
            }
            return PyNone.Instance;
        });
        gs.Dict["__getitem__"] = Native.Fn("GridSpec.__getitem__", (i, a, k) =>
        {
            var (rows, cols) = ((int, int))((PyInstance)a[0]).Native!;
            (int s, int e) Span(object o, int n)
            {
                if (o is PySlice sl)
                {
                    int s0 = sl.Start is PyNone ? 0 : (int)PyOps.AsBigInt(sl.Start, "start"), e0 = sl.Stop is PyNone ? n : (int)PyOps.AsBigInt(sl.Stop, "stop");
                    if (s0 < 0) s0 += n; if (e0 < 0) e0 += n;
                    return (s0, e0);
                }
                int v = (int)PyOps.AsBigInt(o, "index"); if (v < 0) v += n;
                return (v, v + 1);
            }
            GridCell cell;
            if (a[1] is PyTuple t)
            {
                var (rs, re) = Span(t.Items[0], rows); var (cs, ce) = Span(t.Items[1], cols);
                cell = new GridCell(rows, cols, rs, re, cs, ce);
            }
            else if (rows == 1) { var (cs, ce) = Span(a[1], cols); cell = new GridCell(rows, cols, 0, 1, cs, ce); }
            else { var (rs, re) = Span(a[1], rows); cell = new GridCell(rows, cols, rs, re, 0, cols); }
            var spec = new PyClass("SubplotSpec", new List<PyClass>());
            return new PyInstance(spec) { Native = cell };
        });
        m.Dict["GridSpec"] = gs;
        return m;
    }

    public static PyModule Style()
    {
        if (_style is not null) return _style;
        _style = new PyModule("matplotlib.style");
        Def(_style, "use", (_, _, _) => PyNone.Instance);
        _style.Dict["available"] = new PyList(new object[] { "default", "classic" });
        return _style;
    }

    public static PyModule FigureModule()
    {
        if (_figure is not null) return _figure;
        _figure = new PyModule("matplotlib.figure");
        _figure.Dict["Figure"] = Classes.FigureClass;
        return _figure;
    }

    public static PyModule Mplot3d()
    {
        var m = new PyModule("mpl_toolkits.mplot3d");
        m.Dict["Axes3D"] = Classes.AxesClass;
        return m;
    }

    public static PyModule Art3d()
    {
        var m = new PyModule("mpl_toolkits.mplot3d.art3d");
        var cls = new PyClass("Poly3DCollection", new List<PyClass> { Classes.PatchClass });
        cls.Dict["__new__"] = Native.Fn("Poly3DCollection.__new__", (i, a, k) =>
        {
            var kw = new Kw(k);
            var poly = new Poly3D { Alpha = kw.Dbl(1, "alpha") };
            foreach (var p in M.Items(a[1]))
            {
                var pts = M.Items(p).Select(v => M.Dbl(v)).ToArray();
                poly.Polygons.Add(pts);
            }
            if (kw.Get("facecolor", "facecolors", "fc") is { } fc) poly.FaceColor = M.Color(fc);
            if (kw.Get("edgecolor", "edgecolors", "ec") is { } ec) poly.EdgeColor = M.Color(ec);
            poly.LineWidth = kw.Dbl(1, "linewidth", "linewidths", "lw");
            return State.Wrap(poly, _ => cls);
        });
        cls.Dict["__init__"] = new PyBuiltinFunction("Poly3DCollection.__init__", (_, _, _) => PyNone.Instance);
        m.Dict["Poly3DCollection"] = cls;
        return m;
    }

    public static PyModule AxesModule()
    {
        if (_axes is not null) return _axes;
        _axes = new PyModule("matplotlib.axes");
        _axes.Dict["Axes"] = Classes.AxesClass;
        return _axes;
    }
}
