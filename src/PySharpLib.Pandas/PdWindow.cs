// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary><c>rolling</c>, <c>expanding</c> and <c>rank</c>.</summary>
internal static class PdWindow
{
    public static readonly PyClass Rolling = new("Rolling", new List<PyClass>());
    public static readonly PyClass Expanding = new("Expanding", new List<PyClass>());
    public static readonly PyClass Ewm = new("ExponentialMovingWindow", new List<PyClass>());

    private sealed record EwmState(object Source, double Alpha, int MinPeriods, bool Adjust, bool IgnoreNa);

    private sealed record WindowState(object Source, int Window, int MinPeriods, bool Center, bool IsExpanding, long? TimeNanos = null, object? On = null, string Closed = "right");

    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);
    private static WindowState St(object o) => (WindowState)((PyInstance)o).Native!;

    private static object Run(WindowState st, Func<Column, Series?, Column> f)
    {
        if (st.Source is PyInstance { Native: Series s }) return PdConv.Wrap(new Series(f(s.Values, s), s.Index, s.Name));
        var d = PdConv.D(st.Source);
        int onPos = st.On is not null ? d.ColumnPositions(st.On)[0] : -1;
        return PdConv.Wrap(new DataFrame(d.Data.Select((c, j) => j == onPos ? c : f(c, d.GetColumn(j))), d.Columns, d.Index));
    }

    /// <summary>Window bounds of an offset window (<c>rolling('3D')</c>) over the index (or the <c>on</c> column).</summary>
    private static (int[] start, int[] end) TimeWindow(WindowState st)
    {
        Column times = st.Source is PyInstance { Native: Series s } ? s.Index.Labels
            : st.On is not null ? PdConv.D(st.Source).Data[PdConv.D(st.Source).ColumnPositions(st.On)[0]] : PdConv.D(st.Source).Index.Labels;
        if (times.Kind != Kind.DateTime) throw PyErr.ValueError("window must be an integer 0 or greater");
        return Window.TimeBounds(times.Ticks, st.TimeNanos!.Value / DateTimeCore.NanosPerTick(times.Unit), st.Closed);
    }

    private static string NameOf(object f) => f switch { string s => s, PyFunction pf => pf.Name, PyBuiltinFunction bf => bf.Name, _ => "<lambda>" };

    public static void Install()
    {
        foreach (var owner in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            owner.Dict["rolling"] = PdClasses.Fn("rolling", (i, a, k) =>
            {
                var p = A("rolling", i, a, k, "window", "min_periods", "center", "win_type", "on", "axis", "closed", "step", "method");
                if (!PdConv.IsInt(p.Required(0)))
                {
                    if (p.Has(3)) throw PyErr.NotImplementedError("rolling(win_type=)");
                    var spec = PdDates.OffsetArg(p[0]) as DateOffsetSpec.TickOffset ?? throw PyErr.ValueError("window must be a fixed frequency such as '3D' or '2h'");
                    int tminp = p.Has(1) ? PdConv.ToInt(p[1]!) : 1;
                    return new PyInstance(Rolling) { Native = new WindowState(a[0], 0, tminp, false, false, spec.Nanos * spec.N, p.Has(4) ? PdConv.ToCell(p[4]) : null, p.Has(6) ? (string)p[6]! : "right") };
                }
                if (p.Has(3) || p.Has(4)) throw PyErr.NotImplementedError("rolling(win_type=/on=)");
                int w = PdConv.ToInt(p[0]!);
                if (w < 0) throw PyErr.ValueError("window must be an integer 0 or greater");
                int minp = p.Has(1) ? PdConv.ToInt(p[1]!) : w;
                if (minp > w) throw PyErr.ValueError($"min_periods {minp} must be <= window {w}");
                return new PyInstance(Rolling) { Native = new WindowState(a[0], w, minp, p.Bool(2, false), false) };
            });
            owner.Dict["expanding"] = PdClasses.Fn("expanding", (i, a, k) =>
            {
                var p = A("expanding", i, a, k, "min_periods", "axis", "method");
                return new PyInstance(Expanding) { Native = new WindowState(a[0], 0, p.Int(0, 1), false, true) };
            });
            owner.Dict["ewm"] = PdClasses.Fn("ewm", (i, a, k) =>
            {
                var p = A("ewm", i, a, k, "com", "span", "halflife", "alpha", "min_periods", "adjust", "ignore_na", "axis", "times", "method");
                if (p.Has(8)) throw PyErr.NotImplementedError("ewm(times=...)");
                double? D(int x) => p.Has(x) ? Column.ToDouble(PdConv.ToCell(p[x])!) : null;
                double alpha = Window.Alpha(D(0), D(1), D(2), D(3));
                return new PyInstance(Ewm) { Native = new EwmState(a[0], alpha, p.Int(4, 0), p.Bool(5, true), p.Bool(6, false)) };
            });
            owner.Dict["rank"] = PdClasses.Fn("rank", (i, a, k) =>
            {
                var p = A("rank", i, a, k, "axis", "method", "numeric_only", "na_option", "ascending", "pct");
                string method = p.Has(1) ? (string)p[1]! : "average";
                string na = p.Has(3) ? (string)p[3]! : "keep";
                bool asc = p.Bool(4, true), pct = p.Bool(5, false);
                return PdOps.Map(a[0], c => Window.Rank(c, method, asc, pct, na));
            });
        }
        foreach (var name in new[] { "mean", "std", "var" })
        {
            var nm = name;
            Ewm.Dict[nm] = PdClasses.Fn("ExponentialMovingWindow." + nm, (i, a, k) =>
            {
                var st = (EwmState)((PyInstance)a[0]).Native!;
                var p = A(nm, i, a, k, "bias", "numeric_only", "engine", "engine_kwargs");
                bool bias = p.Bool(0, false);
                Column F(Column c) => Window.Ewm(nm, c, st.Alpha, st.MinPeriods, st.Adjust, st.IgnoreNa, bias);
                if (st.Source is PyInstance { Native: Series s }) return PdConv.Wrap(new Series(F(s.Values), s.Index, s.Name));
                var d = PdConv.D(st.Source);
                return PdConv.Wrap(new DataFrame(d.Data.Select(F), d.Columns, d.Index));
            });
        }
        foreach (var cls in new[] { Rolling, Expanding })
        {
            void Def(string n, BuiltinFn f) => cls.Dict[n] = PdClasses.Fn($"{cls.Name}.{n}", f);
            foreach (var name in new[] { "sum", "mean", "min", "max", "count", "median", "std", "var" })
            {
                var nm = name;
                Def(nm, (i, a, k) =>
                {
                    var st = St(a[0]);
                    var p = A(nm, i, a, k, "numeric_only", "ddof", "engine", "engine_kwargs");
                    int ddof = p.Int(1, 1);
                    int minp = st.MinPeriods;
                    if (st.TimeNanos is not null) { var (ts, te) = TimeWindow(st); return Run(st, (c, _) => Window.ApplyBounds(nm, c, ts, te, minp, ddof)); }
                    return Run(st, (c, _) => Window.Apply(nm, c, st.Window, minp, st.Center, st.IsExpanding, ddof));
                });
            }
            Def("apply", (i, a, k) =>
            {
                var st = St(a[0]);
                var p = A("apply", i, a, k, "func", "raw", "engine", "engine_kwargs", "args", "kwargs");
                var f = p.Required(0);
                bool raw = p.Bool(1, false);
                return Run(st, (c, owner) =>
                {
                    int n = c.Length;
                    var (start, end) = st.TimeNanos is not null ? TimeWindow(st) : Window.Bounds(n, st.Window, st.Center, st.IsExpanding);
                    var res = new double[n];
                    for (int r = 0; r < n; r++)
                    {
                        var pos = Enumerable.Range(start[r], end[r] - start[r]).ToArray();
                        var sub = c.Take(pos);
                        int valid = Enumerable.Range(0, sub.Length).Count(x => !sub.IsNa(x));
                        if (valid < st.MinPeriods) { res[r] = double.NaN; continue; }
                        object arg = raw ? PdArrays.Values(sub) : PdConv.Wrap(new Series(sub, owner!.Index.Take(pos), owner.Name));
                        var o = PdConv.ToCell(i.Call(f, new[] { arg }));
                        res[r] = o is null ? double.NaN : Column.ToDouble(o);
                    }
                    return Column.FromDoubles(res);
                });
            });
            BuiltinFn agg = (i, a, k) =>
            {
                var st = St(a[0]);
                var spec = A("agg", i, a, k, "func").Required(0);
                if (spec is string name) return i.CallMethod(a[0], name, Array.Empty<object>());
                if (spec is PyList l)
                {
                    var parts = l.Items.Select(f => (NameOf(f), i.CallMethod(a[0], NameOf(f), Array.Empty<object>()))).ToList();
                    if (st.Source is PyInstance { Native: Series s })
                        return PdConv.Wrap(new DataFrame(parts.Select(x => ((Series)((PyInstance)x.Item2).Native!).Values), new FIndex(Column.FromStrings(parts.Select(x => (string?)x.Item1).ToArray())), s.Index));
                    var d = PdConv.D(st.Source);
                    var cols = new List<Column>(); var labels = new List<object?>();
                    for (int j = 0; j < d.NCols; j++)
                        foreach (var (nm, res) in parts)
                        {
                            cols.Add(((DataFrame)((PyInstance)res).Native!).Data[j]);
                            labels.Add(new LabelTuple(new[] { d.Columns.Labels[j], nm }));
                        }
                    return PdConv.Wrap(new DataFrame(cols, new FIndex(Column.Infer(labels)), d.Index));
                }
                throw PyErr.NotImplementedError("rolling.agg with this argument");
            };
            Def("agg", agg);
            Def("aggregate", agg);
        }
    }
}
