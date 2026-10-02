// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Runtime.CompilerServices;
using NDSharp;
using NDSharp.Plot;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using PlotAxes = NDSharp.Plot.Axes;
using PlotFigure = NDSharp.Plot.Figure;

namespace PySharpLib.Matplotlib;

/// <summary>pyplot's global state: the open figures, the current figure/axes, rcParams and the image sink figures are shown to.</summary>
internal static class State
{
    public static readonly List<PlotFigure> Figures = new();
    public static PlotFigure? Current;
    private static readonly Dictionary<PlotFigure, PlotAxes> CurrentAxes = new();
    private static int _nextNumber = 1;
    public static Action<byte[]>? ShowSink;
    public static readonly PyDict RcParams = NewRc();

    private static PyDict NewRc()
    {
        var d = new PyDict();
        d["figure.figsize"] = new PyList(new object[] { 6.4, 4.8 });
        d["figure.dpi"] = 100.0;
        d["font.size"] = 10.0;
        d["lines.linewidth"] = 1.5;
        d["axes.grid"] = false;
        d["image.cmap"] = "viridis";
        d["savefig.dpi"] = "figure";
        d["axes.titlesize"] = "large";
        d["axes.labelsize"] = "medium";
        return d;
    }

    public static string DefaultCmap => RcParams.TryGet("image.cmap", out var v) && v is string s ? s : "viridis";

    public static PlotFigure NewFigure(double? w = null, double? h = null, double? dpi = null)
    {
        var fig = new PlotFigure { Number = _nextNumber++ };
        if (RcParams.TryGet("figure.figsize", out var fs) && fs is PyList or PyTuple)
        {
            var v = M.Dbl(fs);
            fig.WidthIn = v[0]; fig.HeightIn = v[1];
        }
        if (RcParams.TryGet("figure.dpi", out var dp)) fig.Dpi = PyOps.AsDouble(dp);
        if (w is { } ww) fig.WidthIn = ww;
        if (h is { } hh) fig.HeightIn = hh;
        if (dpi is { } d) fig.Dpi = d;
        Figures.Add(fig);
        Current = fig;
        return fig;
    }

    public static PlotFigure Gcf() => Current is { } c && Figures.Contains(c) ? c : NewFigure();

    public static PlotAxes Gca()
    {
        var fig = Gcf();
        if (CurrentAxes.TryGetValue(fig, out var ax) && fig.Axes.Contains(ax)) return ax;
        return SetCurrent(fig.AddAxes(GridCell.Single(1, 1, 0)));
    }

    public static PlotAxes SetCurrent(PlotAxes ax)
    {
        CurrentAxes[ax.Figure] = ax;
        return ax;
    }

    public static void Close(PlotFigure fig)
    {
        Figures.Remove(fig);
        CurrentAxes.Remove(fig);
        if (Current == fig) Current = Figures.LastOrDefault();
    }

    public static void CloseAll()
    {
        Figures.Clear();
        CurrentAxes.Clear();
        Current = null;
    }

    /// <summary>Renders every open figure to the sink and closes them (the inline backend's behaviour at plt.show() and at cell end).</summary>
    public static void ShowAll()
    {
        var figs = Figures.ToList();
        foreach (var f in figs)
        {
            if (f.Axes.Count == 0) continue;
            ShowSink?.Invoke(f.RenderPng());
        }
        if (ShowSink is not null) CloseAll();
    }

    // ------------------------------------------------------------------------------------------ Python wrappers

    private static readonly ConditionalWeakTable<object, PyInstance> Wrappers = new();

    public static PyInstance Wrap(object native, Func<object, PyClass> classOf)
    {
        if (Wrappers.TryGetValue(native, out var inst)) return inst;
        inst = new PyInstance(classOf(native)) { Native = native };
        Wrappers.Add(native, inst);
        return inst;
    }
}

/// <summary>The 2-D grid of axes that <c>plt.subplots</c> returns (numpy's object array, with the indexing that matters).</summary>
internal sealed class AxesGrid
{
    public PlotAxes[] Flat { get; }
    public int[] Shape { get; }
    public AxesGrid(PlotAxes[] flat, int[] shape) { Flat = flat; Shape = shape; }
}
