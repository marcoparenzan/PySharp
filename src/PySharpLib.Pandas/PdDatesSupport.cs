// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary><c>reindex</c> (with <c>method=</c>), <c>asfreq</c> support and <c>shift(freq=)</c>.</summary>
internal static class PdDatesSupport
{
    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);

    /// <summary>Source positions for every label of <paramref name="target"/>; with a method the nearest earlier/later (sorted) label is used.</summary>
    private static int[] Positions(FIndex source, FIndex target, string? method)
    {
        if (method is null) return Ops.Reindexer(source, target);
        if (source.Labels.Kind != Kind.DateTime && source.Labels.Kind is not (Kind.Int or Kind.Float))
            throw PyErr.NotImplementedError("reindex(method=...) needs a sorted numeric or datetime index");
        bool dt = source.Labels.Kind == Kind.DateTime;
        var unit = dt ? DateTimeCore.Finer(source.Labels.Unit, target.Labels.Unit) : DateUnit.Second;
        double[] Keys(FIndex ix) => dt ? ix.Labels.WithUnit(unit).Ticks.Select(t => (double)t).ToArray() : Enumerable.Range(0, ix.Length).Select(ix.Labels.DoubleAt).ToArray();
        var src = Keys(source); var tgt = Keys(target);
        var order = Enumerable.Range(0, src.Length).OrderBy(r => src[r]).ToArray();
        var pos = new int[tgt.Length];
        for (int n = 0; n < tgt.Length; n++)
        {
            double t = tgt[n];
            int lo = 0, hi = order.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (src[order[mid]] < t) lo = mid + 1; else hi = mid; }
            bool exact = lo < order.Length && src[order[lo]] == t;
            pos[n] = method switch
            {
                "ffill" or "pad" => exact ? order[lo] : lo > 0 ? order[lo - 1] : -1,
                "bfill" or "backfill" => lo < order.Length ? order[lo] : -1,
                "nearest" => exact ? order[lo] : lo == 0 ? (order.Length > 0 ? order[0] : -1) : lo >= order.Length ? order[^1] : (t - src[order[lo - 1]] <= src[order[lo]] - t ? order[lo - 1] : order[lo]),
                _ => throw PyErr.ValueError($"Invalid fill method. Expecting pad (ffill), backfill (bfill) or nearest. Got {method}"),
            };
        }
        return pos;
    }

    private static Column Fill(Column c, int[] pos, object? fill)
    {
        var taken = c.Take(pos);
        if (fill is null) return taken;
        var cells = Enumerable.Range(0, c.Length == 0 ? 0 : pos.Length).Select(n => pos[n] < 0 ? fill : c[pos[n]]).ToList();
        return pos.Length == 0 ? taken : Column.Infer(cells);
    }

    public static object Reindex(object self, FIndex newIndex, string? method, object? fill)
    {
        var srcName = self is PyInstance { Native: Series s0 } ? s0.Index.Name : PdConv.D(self).Index.Name;
        if (newIndex.Name is null && srcName is not null && !newIndex.IsMulti) newIndex = newIndex.WithName(srcName);
        if (self is PyInstance { Native: Series s })
        {
            var pos = Positions(s.Index, newIndex, method);
            return PdConv.Wrap(new Series(Fill(s.Values, pos, fill), newIndex, s.Name));
        }
        var d = PdConv.D(self);
        var p = Positions(d.Index, newIndex, method);
        return PdConv.Wrap(new DataFrame(d.Data.Select(c => Fill(c, p, fill)), d.Columns, newIndex));
    }

    private static FIndex IndexOf(object o) => o is PyInstance { Native: FIndex ix } ? ix : PdBuild.Index(o, -1);

    private static void InstallIndexSetOps()
    {
        var cls = PdClasses.Index;
        FIndex Me(object o) => (FIndex)((PyInstance)o).Native!;
        void Def(string n, BuiltinFn f) { if (!cls.Dict.ContainsKey(n)) cls.Dict[n] = PdClasses.Fn("Index." + n, f); }
        HashSet<object> KeySet(FIndex ix) => new(ix.Items().Select(l => Column.Key(l) ?? Column.NaNKey));
        FIndex Sorted(Column c, object? name, bool sort)
            => new(sort && c.Kind != Kind.Object && c.Length > 1 ? c.Take(FrameOps.SortPositions(new[] { c }, new[] { true }, true)) : c, name);
        Def("union", (i, a, k) =>
        {
            var p = A("union", i, a, k, "other", "sort");
            var x = Me(a[0]); var y = IndexOf(p.Required(0));
            if (p.Has(1) && p[1] is false)
            {
                var seen = new HashSet<object>(); var all = new List<object?>();
                foreach (var l in x.Items().Concat(y.Items())) if (seen.Add(Column.Key(l) ?? Column.NaNKey)) all.Add(l);
                return PdConv.Wrap(new FIndex(Column.Infer(all), Equals(x.Name, y.Name) ? x.Name : null));
            }
            var u = Ops.Union(x, y);
            return PdConv.Wrap(Ops.SameLabels(x, y) && x.Labels.Tz == y.Labels.Tz ? x : u);
        });
        Def("intersection", (i, a, k) =>
        {
            var p = A("intersection", i, a, k, "other", "sort");
            var x = Me(a[0]); var y = IndexOf(p.Required(0));
            var yk = KeySet(y); var seen = new HashSet<object>();
            var keep = x.Items().Where(l => yk.Contains(Column.Key(l) ?? Column.NaNKey) && seen.Add(Column.Key(l) ?? Column.NaNKey)).ToList();
            var col = keep.Count == 0 ? x.Labels.Take(Array.Empty<int>()) : Column.Infer(keep);
            return PdConv.Wrap(Sorted(col, Equals(x.Name, y.Name) ? x.Name : null, !(p.Has(1) && p[1] is false)));
        });
        Def("difference", (i, a, k) =>
        {
            var p = A("difference", i, a, k, "other", "sort");
            var x = Me(a[0]); var y = IndexOf(p.Required(0));
            var yk = KeySet(y); var seen = new HashSet<object>();
            var keep = x.Items().Where(l => !yk.Contains(Column.Key(l) ?? Column.NaNKey) && seen.Add(Column.Key(l) ?? Column.NaNKey)).ToList();
            var col = keep.Count == 0 ? x.Labels.Take(Array.Empty<int>()) : Column.Infer(keep);
            return PdConv.Wrap(Sorted(col, x.Name, !(p.Has(1) && p[1] is false)));
        });
        Def("unique", (i, a, k) => { var x = Me(a[0]); return PdConv.Wrap(new FIndex(x.Labels.Take(FrameOps.UniquePositions(x.Labels)), x.Name)); });
        Def("drop_duplicates", (i, a, k) => { var x = Me(a[0]); return PdConv.Wrap(new FIndex(x.Labels.Take(FrameOps.UniquePositions(x.Labels)), x.Name)); });
        Def("nunique", (i, a, k) => new BigInteger(FrameOps.UniquePositions(Me(a[0]).Labels).Count(p => !Me(a[0]).Labels.IsNa(p))));
        Def("isin", (i, a, k) =>
        {
            var set = new HashSet<object>(PdConv.Cells(A("isin", i, a, k, "values").Required(0)).Select(l => Column.Key(l) ?? Column.NaNKey));
            var x = Me(a[0]);
            return Conv.Wrap(NDSharp.NDArray.FromArray(x.Items().Select(l => set.Contains(Column.Key(l) ?? Column.NaNKey)).ToArray()));
        });
        Def("isna", (i, a, k) => { var x = Me(a[0]); return Conv.Wrap(NDSharp.NDArray.FromArray(Enumerable.Range(0, x.Length).Select(x.Labels.IsNa).ToArray())); });
        cls.Dict["isnull"] = cls.Dict["isna"];
        Def("notna", (i, a, k) => { var x = Me(a[0]); return Conv.Wrap(NDSharp.NDArray.FromArray(Enumerable.Range(0, x.Length).Select(n => !x.Labels.IsNa(n)).ToArray())); });
        Def("sort_values", (i, a, k) =>
        {
            var p = A("sort_values", i, a, k, "return_indexer", "ascending", "na_position", "key");
            var x = Me(a[0]);
            bool asc = p.Bool(1, true);
            var order = FrameOps.SortPositions(new[] { x.Labels }, new[] { asc }, !(p.Has(2) && (string)p[2]! == "first"));
            var r = new FIndex(x.Labels.Take(order), x.Name);
            if (p.Bool(0, false)) return new PyTuple(new object[] { PdConv.Wrap(r), Conv.Wrap(NDSharp.NDArray.FromArray(order.Select(v => (long)v).ToArray())) });
            return PdConv.Wrap(r);
        });
        Def("append", (i, a, k) =>
        {
            var x = Me(a[0]); var other = A("append", i, a, k, "other").Required(0);
            var parts = other is PyList or PyTuple ? PdConv.Cells(other).Select(_ => (FIndex?)null).ToList() : null;
            var seq = other is PyList pl ? pl.Items.Select(IndexOf) : other is PyTuple pt ? pt.Items.Select(IndexOf) : new[] { IndexOf(other) };
            return PdConv.Wrap(seq.Aggregate(x, (acc, y) => acc.Concat(y)));
        });
    }

    public static void Install()
    {
        InstallIndexSetOps();
        if (!PdClasses.Index.Dict.ContainsKey("sum"))
            PdClasses.Index.Dict["sum"] = PdClasses.Fn("sum", (i, a, k) => PdOps.Out(Reduce.Scalar("sum", ((FIndex)((PyInstance)a[0]).Native!).Labels, true, 1, 0)));
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            bool isSeries = cls == PdClasses.Series;
            cls.Dict["rename_axis"] = PdClasses.Fn("rename_axis", (i, a, k) =>
            {
                var p = A("rename_axis", i, a, k, "mapper", "index", "columns", "axis", "copy", "inplace");
                object? nm(int x) => PdConv.ToCell(p[x]);
                if (isSeries)
                {
                    var s = PdConv.S(a[0]);
                    object? name = p.Has(1) ? nm(1) : p.Has(0) ? nm(0) : null;
                    var ns = new Series(s.Values, s.Index.WithName(name), s.Name);
                    return PdWrangle.Finish(a[0], PdConv.Wrap(ns), p.Bool(5, false));
                }
                var d = PdConv.D(a[0]);
                var idx = d.Index; var cols = d.Columns;
                bool onCols = p.Has(2) || p.Has(3) && PdOps.AxisOf(p[3]!) == 1;
                if (p.Has(1)) idx = idx.WithName(nm(1));
                if (p.Has(2)) cols = cols.WithName(nm(2));
                if (p.Has(0)) { if (onCols && !p.Has(2)) cols = cols.WithName(nm(0)); else if (!p.Has(1)) idx = idx.WithName(nm(0)); }
                return PdWrangle.Finish(a[0], PdConv.Wrap(new DataFrame(d.Data, cols, idx)), p.Bool(5, false));
            });
        }
        foreach (var (name, mx) in new[] { ("argmax", true), ("argmin", false) })
        {
            var max = mx;
            if (!PdClasses.Series.Dict.ContainsKey(name))
                PdClasses.Series.Dict[name] = PdClasses.Fn(name, (i, a, k) =>
                {
                    int pos = Reduce.ArgExtreme(PdConv.S(a[0]).Values, max, A(name, i, a, k, "axis", "skipna").Bool(1, true));
                    return new BigInteger(pos);
                });
        }
        if (!PdClasses.Index.Dict.ContainsKey("argsort"))
            PdClasses.Index.Dict["argsort"] = PdClasses.Fn("argsort", (i, a, k) =>
                Conv.Wrap(NDSharp.NDArray.FromArray(FrameOps.SortPositions(new[] { ((FIndex)((PyInstance)a[0]).Native!).Labels }, new[] { true }, true).Select(v => (long)v).ToArray())));
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            foreach (var (name, first) in new[] { ("first_valid_index", true), ("last_valid_index", false) })
            {
                var f2 = first;
                cls.Dict[name] = PdClasses.Fn(name, (i, a, k) =>
                {
                    var (index, cols) = a[0] is PyInstance { Native: Series s } ? (s.Index, new[] { s.Values }) : (PdConv.D(a[0]).Index, PdConv.D(a[0]).Data.ToArray());
                    var rows = f2 ? Enumerable.Range(0, index.Length) : Enumerable.Range(0, index.Length).Reverse();
                    foreach (var r in rows) if (cols.Any(c => !c.IsNa(r))) return PdConv.FromLabel(index.Labels[r]);
                    return PyNone.Instance;
                });
            }
            cls.Dict["truncate"] = PdClasses.Fn("truncate", (i, a, k) =>
            {
                var p = A("truncate", i, a, k, "before", "after", "axis", "copy");
                var index = a[0] is PyInstance { Native: Series s0 } ? s0.Index : PdConv.D(a[0]).Index;
                var pos = PdSelect.LabelSlice(new PySlice(p.Has(0) ? p[0]! : PyNone.Instance, p.Has(1) ? p[1]! : PyNone.Instance, PyNone.Instance), index);
                return a[0] is PyInstance { Native: Series s } ? PdConv.Wrap(s.Take(pos)) : PdConv.Wrap(PdConv.D(a[0]).TakeRows(pos));
            });
        }
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            bool isSeries = cls == PdClasses.Series;
            if (!cls.Dict.ContainsKey("reindex"))
                cls.Dict["reindex"] = PdClasses.Fn("reindex", (i, a, k) =>
                {
                    var p = A("reindex", i, a, k, "labels", "index", "columns", "axis", "method", "copy", "level", "fill_value", "limit", "tolerance");
                    string? method = p.Has(4) ? (string)p[4]! : null;
                    object? fill = p.Has(7) ? PdConv.ToCell(p[7]) : null;
                    var target = p.Has(1) ? p[1] : p.Has(0) && !(p.Has(3) && PdOps.AxisOf(p[3]!) == 1) ? p[0] : null;
                    object result = a[0];
                    if (target is not null) result = Reindex(result, PdBuild.Index(target, -1), method, fill);
                    var cols = p.Has(2) ? p[2] : p.Has(0) && p.Has(3) && PdOps.AxisOf(p[3]!) == 1 ? p[0] : null;
                    if (cols is not null && !isSeries)
                    {
                        var d = PdConv.D(result);
                        var ncols = PdBuild.Index(cols, -1);
                        var pos = Ops.Reindexer(d.Columns, ncols);
                        result = PdConv.Wrap(new DataFrame(pos.Select(x => x < 0 ? Column.FromDoubles(Enumerable.Repeat(double.NaN, d.NRows).ToArray()) : d.Data[x]), ncols, d.Index));
                    }
                    return result;
                });
        }
    }
}
