// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Runtime.CompilerServices;
using NDSharp.Frame;
using PySharpLib.Importing;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Pandas;

/// <summary><c>Series.plot</c> / <c>DataFrame.plot</c> (and <c>hist</c>) drawn through the PySharp.Matplotlib binding: the drawing logic is a small Python
/// module (<c>_pandas_plot</c>) that follows pandas' defaults (index as x axis, one line per column, legend for several columns, bar positions and widths).</summary>
internal static class PdPlot
{
    public static readonly PyClass Accessor = new("PlotAccessor", new List<PyClass>());
    private static readonly ConditionalWeakTable<Interp, Importer> Importers = new();

    public static void Remember(Interp interp, Importer importer) => Importers.AddOrUpdate(interp, importer);

    public const string Source = """
import math
import matplotlib.pyplot as plt
import numpy as np

_KINDS = ('line', 'bar', 'barh', 'hist', 'scatter', 'area', 'pie', 'box', 'kde', 'density')


def _columns(obj, y):
    if hasattr(obj, 'columns'):
        if y is not None:
            return [y] if not isinstance(y, (list, tuple)) else list(y)
        return [c for c in obj.columns if obj[c].dtype != 'str' and obj[c].dtype != 'object']
    return [None]


def _values(obj, col):
    s = obj if col is None else obj[col]
    return s.to_numpy()


def _name(obj, col):
    if col is not None:
        return str(col)
    return None if obj.name is None else str(obj.name)


def _is_numeric_index(index):
    return index.dtype != 'str' and index.dtype != 'object'


def _is_dt(dtype):
    return str(dtype).startswith('datetime64')


def _date_numbers(idx):
    import pandas as pd
    return np.array(list((idx - pd.Timestamp('1970-01-01')).total_seconds() / 86400.0))


def _date_labels(idx):
    midnight = bool((idx == idx.normalize()).all())
    return list(idx.strftime('%Y-%m-%d' if midnight else '%Y-%m-%d %H:%M:%S'))


def _kde(data, ind=None):
    # Gaussian kernel density estimate, Scott's bandwidth (what scipy.stats.gaussian_kde and pandas use)
    data = data[data == data]
    n = len(data)
    h = float(np.std(data, ddof=1)) * n ** (-1.0 / 5.0)
    lo = float(data.min())
    hi = float(data.max())
    r = hi - lo
    if ind is None:
        ind = np.linspace(lo - 0.5 * r, hi + 0.5 * r, 1000)
    elif isinstance(ind, int):
        ind = np.linspace(lo - 0.5 * r, hi + 0.5 * r, ind)
    else:
        ind = np.asarray(ind, dtype=float)
    z = (ind[:, None] - data[None, :]) / h
    dens = np.exp(-0.5 * z * z).sum(axis=1) / (n * h * math.sqrt(2.0 * math.pi))
    return ind, dens


def _pie(ax, obj, y, autopct, startangle, color, labels=None):
    if hasattr(obj, 'columns'):
        if y is None:
            if len(obj.columns) != 1:
                raise ValueError("pie requires either y column or 'subplots=True'")
            y = obj.columns[0]
        series = obj[y]
    else:
        series = obj
    vals = [float(v) for v in series.to_numpy()]
    if any(v < 0 for v in vals):
        raise ValueError("pie plot doesn't allow negative values")
    names = labels if labels is not None else [str(i) for i in series.index]
    total = sum(vals)
    angle = float(startangle)
    for k, v in enumerate(vals):
        frac = v / total if total > 0 else 0.0
        sweep = 360.0 * frac
        steps = max(2, int(sweep) + 1)
        xs = [0.0]
        ys = [0.0]
        for t in range(steps + 1):
            a = math.radians(angle + sweep * t / steps)
            xs.append(math.cos(a))
            ys.append(math.sin(a))
        col = color[k % len(color)] if isinstance(color, (list, tuple)) else 'C%d' % (k % 10)
        ax.add_patch(plt.Polygon(list(zip(xs, ys)), facecolor=col, edgecolor=col))
        mid = math.radians(angle + sweep / 2.0)
        ha = 'left' if math.cos(mid) >= 0 else 'right'
        ax.text(1.1 * math.cos(mid), 1.1 * math.sin(mid), names[k], ha=ha, va='center')
        if autopct is not None:
            txt = autopct % (100.0 * frac) if isinstance(autopct, str) else autopct(100.0 * frac)
            ax.text(0.6 * math.cos(mid), 0.6 * math.sin(mid), txt, ha='center', va='center')
        angle += sweep
    ax.set_xlim(-1.25, 1.25)
    ax.set_ylim(-1.25, 1.25)
    ax.set_aspect('equal')
    ax.axis('off')


def _box(ax, obj, cols, color, vert=True):
    names = []
    for k, col in enumerate(cols):
        data = _values(obj, col)
        data = data[data == data]
        names.append(_name(obj, col) or '1')
        pos = k + 1
        q1, med, q3 = [float(q) for q in np.percentile(data, [25, 50, 75])]
        iqr = q3 - q1
        lo_fence, hi_fence = q1 - 1.5 * iqr, q3 + 1.5 * iqr
        inside = data[(data >= lo_fence) & (data <= hi_fence)]
        lo, hi = float(inside.min()), float(inside.max())
        fliers = data[(data < lo_fence) | (data > hi_fence)]
        w = 0.25
        cw = 0.125
        blue = '#1f77b4'
        green = '#2ca02c'
        if vert:
            ax.plot([pos - w, pos + w, pos + w, pos - w, pos - w], [q1, q1, q3, q3, q1], color=blue)
            ax.plot([pos - w, pos + w], [med, med], color=green)
            ax.plot([pos, pos], [q1, lo], color=blue)
            ax.plot([pos, pos], [q3, hi], color=blue)
            ax.plot([pos - cw, pos + cw], [lo, lo], color=blue)
            ax.plot([pos - cw, pos + cw], [hi, hi], color=blue)
            if len(fliers):
                ax.plot([pos] * len(fliers), list(fliers), marker='+', linestyle='none', color='k')
        else:
            ax.plot([q1, q1, q3, q3, q1], [pos - w, pos + w, pos + w, pos - w, pos - w], color=blue)
            ax.plot([med, med], [pos - w, pos + w], color=green)
            ax.plot([q1, lo], [pos, pos], color=blue)
            ax.plot([q3, hi], [pos, pos], color=blue)
            ax.plot([lo, lo], [pos - cw, pos + cw], color=blue)
            ax.plot([hi, hi], [pos - cw, pos + cw], color=blue)
            if len(fliers):
                ax.plot(list(fliers), [pos] * len(fliers), marker='+', linestyle='none', color='k')
    ticks = list(range(1, len(cols) + 1))
    if vert:
        ax.set_xticks(ticks)
        ax.set_xticklabels(names)
        ax.set_xlim(0.5, len(cols) + 0.5)
    else:
        ax.set_yticks(ticks)
        ax.set_yticklabels(names)
        ax.set_ylim(0.5, len(cols) + 0.5)


def plot(obj, kind='line', x=None, y=None, ax=None, figsize=None, title=None, legend=None, grid=None,
         xlabel=None, ylabel=None, color=None, label=None, logx=False, logy=False, stacked=False,
         rot=None, bins=10, alpha=None, xlim=None, ylim=None, width=None, s=None, c=None, marker=None,
         linestyle=None, linewidth=None, xticks=None, yticks=None, subplots=False, **kwargs):
    if kind not in _KINDS:
        raise NotImplementedError("plot kind '%s' is not supported" % kind)
    if ax is None:
        fig = plt.figure(figsize=figsize) if figsize is not None else plt.figure()
        ax = fig.add_subplot(111)
    is_frame = hasattr(obj, 'columns')
    cols = _columns(obj, y)
    n = len(cols)
    style = {}
    if alpha is not None:
        style['alpha'] = alpha
    if kind == 'pie':
        _pie(ax, obj, y, kwargs.get('autopct'), kwargs.get('startangle', 0), color, labels=kwargs.get('labels'))
        if title is not None:
            ax.set_title(title)
        return ax
    if kind == 'box':
        _box(ax, obj, cols, color, vert=kwargs.get('vert', True))
        if title is not None:
            ax.set_title(title)
        if grid:
            ax.grid(True)
        return ax
    if kind in ('kde', 'density'):
        for k, col in enumerate(cols):
            xs, ys = _kde(_values(obj, col), kwargs.get('ind'))
            kw = dict(style)
            if color is not None:
                kw['color'] = color[k] if isinstance(color, (list, tuple)) else color
            if linestyle is not None:
                kw['linestyle'] = linestyle
            if linewidth is not None:
                kw['linewidth'] = linewidth
            ax.plot(xs, ys, label=label if (label is not None and n == 1) else _name(obj, col), **kw)
        ax.set_ylabel('Density')
    elif kind == 'scatter':
        xs = _values(obj, x)
        ys = _values(obj, y if not isinstance(y, (list, tuple)) else y[0])
        extra = {}
        if s is not None:
            extra['s'] = s
        if c is not None:
            extra['c'] = _values(obj, c) if isinstance(c, str) and c in list(obj.columns) else c
        elif color is not None:
            extra['c'] = color
        ax.scatter(xs, ys, **extra, **style)
        ax.set_xlabel(str(x))
        ax.set_ylabel(str(y))
    elif kind == 'hist':
        for k, col in enumerate(cols):
            data = _values(obj, col)
            ax.hist(data[data == data], bins=bins, label=_name(obj, col), **style)
        if is_frame and n > 1 and alpha is None:
            pass
        ax.set_ylabel('Frequency')
    else:
        index = obj.index
        dt_index = x is None and _is_dt(index.dtype)
        numeric_x = _is_numeric_index(index) if x is None else True
        if dt_index:
            numeric_x = False
        if x is not None:
            xs_all = _values(obj, x)
            if _is_dt(obj[x].dtype):
                xs_all = _date_numbers(obj[x])
            cols = [cc for cc in cols if cc != x]
            n = len(cols)
        elif dt_index and kind in ('line', 'area'):
            xs_all = _date_numbers(index)
        else:
            xs_all = index.to_numpy() if numeric_x else list(range(len(index)))
        if kind == 'line':
            for k, col in enumerate(cols):
                kw = dict(style)
                if color is not None:
                    kw['color'] = color[k] if isinstance(color, (list, tuple)) else color
                if linestyle is not None:
                    kw['linestyle'] = linestyle
                if linewidth is not None:
                    kw['linewidth'] = linewidth
                if marker is not None:
                    kw['marker'] = marker
                ax.plot(xs_all, _values(obj, col), label=label if (label is not None and n == 1) else _name(obj, col), **kw)
        elif kind == 'area':
            base = None
            for k, col in enumerate(cols):
                ys = _values(obj, col)
                if stacked and base is not None:
                    top = base + ys
                    ax.fill_between(xs_all, base, top, label=_name(obj, col), alpha=alpha if alpha is not None else 0.5)
                    base = top
                else:
                    ax.fill_between(xs_all, 0, ys, label=_name(obj, col), alpha=alpha if alpha is not None else 0.5)
                    base = ys if stacked else None
        else:
            pos = list(range(len(index)))
            w = width if width is not None else 0.5
            horizontal = kind == 'barh'
            bottoms = None
            for k, col in enumerate(cols):
                ys = _values(obj, col)
                kw = dict(style)
                if color is not None:
                    kw['color'] = color[k] if isinstance(color, (list, tuple)) else color
                lab = label if (label is not None and n == 1) else _name(obj, col)
                if stacked:
                    if horizontal:
                        ax.barh(pos, ys, w, left=bottoms, label=lab, **kw)
                    else:
                        ax.bar(pos, ys, w, bottom=bottoms, label=lab, **kw)
                    bottoms = ys if bottoms is None else bottoms + ys
                else:
                    bw = w / n
                    shifted = [p - w / 2 + bw * (k + 0.5) for p in pos] if n > 1 else pos
                    if horizontal:
                        ax.barh(shifted, ys, bw if n > 1 else w, label=lab, **kw)
                    else:
                        ax.bar(shifted, ys, bw if n > 1 else w, label=lab, **kw)
            labels = _date_labels(index) if dt_index else [str(v) for v in index]
            if horizontal:
                ax.set_yticks(pos)
                ax.set_yticklabels(labels)
            else:
                ax.set_xticks(pos)
                ax.set_xticklabels(labels, rotation=90 if rot is None else rot)
        if x is None and index.name is not None:
            ax.set_xlabel(str(index.name))
        elif x is not None:
            ax.set_xlabel(str(x))
        if not is_frame and obj.name is not None and kind in ('line', 'bar', 'barh', 'area') and False:
            ax.set_ylabel(str(obj.name))
        if dt_index and kind in ('line', 'area'):
            step = max(1, (len(index) + 6) // 7)
            ax.set_xticks(list(xs_all[::step]))
            ax.set_xticklabels(_date_labels(index)[::step], rotation=30 if rot is None else rot)
        elif not numeric_x and kind in ('line', 'area'):
            ax.set_xticks(xs_all)
            ax.set_xticklabels([str(v) for v in index], rotation=0 if rot is None else rot)
    if logx:
        ax.set_xscale('log')
    if logy:
        ax.set_yscale('log')
    if title is not None:
        ax.set_title(title)
    if xlabel is not None:
        ax.set_xlabel(xlabel)
    if ylabel is not None:
        ax.set_ylabel(ylabel)
    if xlim is not None:
        ax.set_xlim(xlim)
    if ylim is not None:
        ax.set_ylim(ylim)
    if grid:
        ax.grid(True)
    show_legend = legend if legend is not None else (n > 1 or label is not None or (not is_frame and False))
    if kind == 'scatter':
        show_legend = bool(legend)
    if show_legend:
        ax.legend()
    return ax


def hist_frame(obj, column=None, bins=10, figsize=None, layout=None, sharex=False, sharey=False, **kwargs):
    cols = _columns(obj, column) if hasattr(obj, 'columns') else [None]
    n = len(cols)
    ncols = 1 if n == 1 else (2 if n <= 4 else 3)
    nrows = (n + ncols - 1) // ncols
    fig, axes = plt.subplots(nrows, ncols, figsize=figsize) if figsize is not None else plt.subplots(nrows, ncols)
    flat = []
    try:
        flat = list(axes.flatten())
    except Exception:
        flat = [axes]
    for k, col in enumerate(cols):
        data = _values(obj, col)
        flat[k].hist(data[data == data], bins=bins)
        flat[k].set_title(_name(obj, col) or '')
    return axes
""";

    public static void Install()
    {
        var plotProp = new PyProperty { Getter = PdClasses.Fn("plot", (i, a, k) => new PyInstance(Accessor) { Native = a[0] }) };
        PdClasses.Series.Dict["plot"] = plotProp;
        PdClasses.DataFrame.Dict["plot"] = plotProp;

        object CallPlot(Interp i, object owner, string? kind, object[] args, Dictionary<string, object>? kw)
        {
            var importer = Importers.TryGetValue(i, out var imp) ? imp : throw PyErr.RuntimeError("pandas plotting needs the registered importer");
            var module = importer.ImportAbsolute(i, "_pandas_plot");
            var fn = module.Dict["plot"];
            var kwargs = new Dictionary<string, object>(kw ?? new Dictionary<string, object>());
            if (kind is not null) kwargs["kind"] = kind;
            else if (args.Length > 0) { kwargs["x"] = args[0]; if (args.Length > 1) kwargs["y"] = args[1]; if (args.Length > 2) kwargs["kind"] = args[2]; }
            return i.Call(fn, new[] { owner }, kwargs);
        }

        Accessor.Dict["__call__"] = PdClasses.Fn("plot.__call__", (i, a, k) => CallPlot(i, ((PyInstance)a[0]).Native!, null, a.Skip(1).ToArray(), k));
        foreach (var kind in new[] { "line", "bar", "barh", "hist", "scatter", "area", "pie", "box", "kde", "density" })
        {
            var kd = kind;
            Accessor.Dict[kd] = PdClasses.Fn("plot." + kd, (i, a, k) =>
            {
                var owner = ((PyInstance)a[0]).Native!;
                var args = a.Skip(1).ToArray();
                var kwargs = new Dictionary<string, object>(k ?? new Dictionary<string, object>());
                if (args.Length > 0 && !kwargs.ContainsKey("x")) kwargs["x"] = args[0];
                if (args.Length > 1 && !kwargs.ContainsKey("y")) kwargs["y"] = args[1];
                return CallPlot(i, owner, kd, Array.Empty<object>(), kwargs);
            });
        }
        foreach (var kind in new[] { "hexbin" })
        {
            var kd = kind;
            Accessor.Dict[kd] = PdClasses.Fn("plot." + kd, (_, _, _) => throw PyErr.NotImplementedError($"plot.{kd}() is not implemented"));
        }
        foreach (var owner in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            owner.Dict["hist"] = PdClasses.Fn("hist", (i, a, k) =>
            {
                var importer = Importers.TryGetValue(i, out var imp) ? imp : throw PyErr.RuntimeError("pandas plotting needs the registered importer");
                var module = importer.ImportAbsolute(i, "_pandas_plot");
                var self = a[0];
                if (self is PyInstance { Native: Series }) return i.Call(module.Dict["plot"], new[] { self }, new Dictionary<string, object>(k ?? new()) { ["kind"] = "hist" });
                return i.Call(module.Dict["hist_frame"], new[] { self }, k);
            });
        }
    }
}
