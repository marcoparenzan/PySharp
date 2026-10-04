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

/// <summary>Missing data, selection helpers, sorting, uniqueness, shifting and reshaping methods shared by Series and DataFrame.</summary>
internal static class PdWrangle
{
    private static Series S(object o) => PdConv.S(o);
    private static DataFrame D(object o) => PdConv.D(o);
    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);
    private static bool IsSeries(object o) => o is PyInstance { Native: Series };

    /// <summary>Returns the result, or — for <c>inplace=True</c> — copies it into <paramref name="self"/> and returns None.</summary>
    private static object Finish(object self, object result, bool inplace)
    {
        if (!inplace) return result;
        var target = ((PyInstance)self).Native;
        var src = ((PyInstance)result).Native;
        if (target is Series ts && src is Series ss) { ts.Values = ss.Values; ts.Index = ss.Index; ts.Name = ss.Name; }
        else if (target is DataFrame td && src is DataFrame sd)
        {
            td.Data.Clear(); td.Data.AddRange(sd.Data); td.Columns = sd.Columns; td.Index = sd.Index;
        }
        return PyNone.Instance;
    }

    internal static List<Column> IndexKeys(FIndex ix) => ix.IsMulti ? Enumerable.Range(0, ix.NLevels).Select(ix.Level).ToList() : new List<Column> { ix.Labels };

    /// <summary>The index as columns for reset_index: one per level, named after the level (or index / level_k).</summary>
    internal static (List<Column> cols, List<object?> names) IndexAsColumns(FIndex ix, bool frameHasIndexColumn = false)
    {
        if (ix.IsMulti)
            return (Enumerable.Range(0, ix.NLevels).Select(ix.Level).ToList(), Enumerable.Range(0, ix.NLevels).Select(k => ix.Names[k] ?? $"level_{k}").ToList());
        return (new List<Column> { ix.Labels }, new List<object?> { ix.Name ?? (frameHasIndexColumn ? "level_0" : "index") });
    }

    private static Series NewSeries(Series like, Column c) => new(c, like.Index, like.Name);

    private static object Rebuild(object self, Func<Column, Column> f)
    {
        if (self is PyInstance { Native: Series s }) return PdConv.Wrap(NewSeries(s, f(s.Values)));
        var d = D(self);
        return PdConv.Wrap(new DataFrame(d.Data.Select(f), d.Columns, d.Index));
    }

    private static object TakeRowsOf(object self, IReadOnlyList<int> pos)
        => self is PyInstance { Native: Series s } ? PdConv.Wrap(s.Take(pos)) : PdConv.Wrap(D(self).TakeRows(pos));

    private static bool[] MaskOf(object cond, FIndex rows)
    {
        var m = PdSelect.TryMask(cond, rows);
        if (m is null) throw PyErr.ValueError("Array conditional must be same shape as self");
        return m;
    }

    private static Series BoolSeries(Series like, bool[] v) => new(Column.FromBools(v), like.Index, like.Name);

    public static void Install()
    {
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            void Def(string n, BuiltinFn f) => cls.Dict[n] = PdClasses.Fn($"{cls.Name}.{n}", f);

            // ---- missing data
            Def("isna", (_, a, _) => PdFunctions.IsNa(a[0]));
            cls.Dict["isnull"] = cls.Dict["isna"];
            Def("notna", (_, a, _) => PdFunctions.NotNa(a[0]));
            cls.Dict["notnull"] = cls.Dict["notna"];
            Def("fillna", (i, a, k) =>
            {
                var p = A("fillna", i, a, k, "value", "method", "axis", "inplace", "limit");
                if (p.Has(1)) throw PyErr.TypeError("fillna() got an unexpected keyword argument 'method'; use ffill()/bfill()");
                object result;
                if (p.Required(0) is PyDict map && a[0] is PyInstance { Native: DataFrame d })
                {
                    var cols = d.Data.ToList();
                    foreach (var (key, v) in map.Entries)
                        foreach (var pos in d.ColumnPositions(PdConv.ToCell(key))) cols[pos] = FrameOps.FillNa(cols[pos], PdConv.ToCell(v));
                    result = PdConv.Wrap(new DataFrame(cols, d.Columns, d.Index));
                }
                else if (p.Required(0) is PyInstance { Native: Series fillSeries } && a[0] is PyInstance { Native: Series target })
                {
                    var take = Ops.Reindexer(fillSeries.Index, target.Index);
                    var filler = fillSeries.Values.Take(take);
                    var cells = Enumerable.Range(0, target.Length).Select(r => target.Values.IsNa(r) ? filler[r] : target.Values[r]).ToList();
                    result = PdConv.Wrap(NewSeries(target, Column.Infer(cells)));
                }
                else
                {
                    var v = PdConv.ToCell(p.Required(0));
                    result = Rebuild(a[0], c => FrameOps.FillNa(c, v));
                }
                return Finish(a[0], result, p.Bool(3, false));
            });
            foreach (var (name, fwd) in new[] { ("ffill", true), ("bfill", false), ("pad", true), ("backfill", false) })
            {
                var nm = name; var f2 = fwd;
                Def(nm, (i, a, k) =>
                {
                    var p = A(nm, i, a, k, "axis", "inplace", "limit");
                    return Finish(a[0], Rebuild(a[0], c => FrameOps.FillDirectional(c, f2, p.IntOrNull(2))), p.Bool(1, false));
                });
            }
            Def("dropna", (i, a, k) =>
            {
                var p = A("dropna", i, a, k, "axis", "how", "thresh", "subset", "inplace", "ignore_index");
                string how = p.Has(1) ? (string)p[1]! : "any";
                object result;
                if (a[0] is PyInstance { Native: Series s })
                    result = PdConv.Wrap(s.Take(FrameOps.DropNaRows(new[] { s.Values }, s.Length, how, null)));
                else
                {
                    var d = D(a[0]);
                    int axis = p.Has(0) ? PdOps.AxisOf(p[0]!) : 0;
                    int? thresh = p.IntOrNull(2);
                    if (axis == 0)
                    {
                        var cols = p.Has(3) ? PdConv.Cells(p[3]!).Select(l => d.Data[d.ColumnPositions(l)[0]]).ToList() : d.Data;
                        result = PdConv.Wrap(d.TakeRows(FrameOps.DropNaRows(cols, d.NRows, how, thresh)));
                    }
                    else
                    {
                        var keep = new List<int>();
                        for (int j = 0; j < d.NCols; j++)
                        {
                            int na = Enumerable.Range(0, d.NRows).Count(r => d.Data[j].IsNa(r));
                            bool drop = thresh is int t ? d.NRows - na < t : how == "all" ? na == d.NRows && d.NRows > 0 : na > 0;
                            if (!drop) keep.Add(j);
                        }
                        result = PdConv.Wrap(d.TakeColumns(keep));
                    }
                }
                return Finish(a[0], result, p.Bool(4, false));
            });
            Def("where", (i, a, k) => WhereMask(i, a, k, "where", true));
            Def("mask", (i, a, k) => WhereMask(i, a, k, "mask", false));
            Def("replace", (i, a, k) =>
            {
                var p = A("replace", i, a, k, "to_replace", "value", "inplace", "regex");
                var pairs = new List<(object?, object?)>();
                var tr = p.Required(0);
                if (tr is PyDict dm) foreach (var (key, v) in dm.Entries) pairs.Add((PdConv.ToCell(key), PdConv.ToCell(v)));
                else if (PdConv.IsListLike(tr))
                {
                    var from = PdConv.Cells(tr);
                    if (p.Has(1) && PdConv.IsListLike(p[1]!))
                    {
                        var to = PdConv.Cells(p[1]!);
                        if (to.Count != from.Count) throw PyErr.ValueError("Replacement lists must match in length.");
                        for (int x = 0; x < from.Count; x++) pairs.Add((from[x], to[x]));
                    }
                    else foreach (var f in from) pairs.Add((f, PdConv.ToCell(p[1])));
                }
                else pairs.Add((PdConv.ToCell(tr), PdConv.ToCell(p[1])));
                return Finish(a[0], Rebuild(a[0], c => FrameOps.Replace(c, pairs)), p.Bool(2, false));
            });
            Def("clip", (i, a, k) =>
            {
                var p = A("clip", i, a, k, "lower", "upper", "axis", "inplace");
                double? lo = p.Has(0) ? Column.ToDouble(PdConv.ToCell(p[0])!) : null, hi = p.Has(1) ? Column.ToDouble(PdConv.ToCell(p[1])!) : null;
                return Rebuild(a[0], c => FrameOps.Clip(c, lo, hi));
            });
            Def("round", (i, a, k) =>
            {
                int dec = A("round", i, a, k, "decimals").Int(0, 0);
                return Rebuild(a[0], c => FrameOps.Round(c, dec));
            });
            Def("isin", (i, a, k) =>
            {
                var values = PdConv.Cells(A("isin", i, a, k, "values").Required(0));
                if (a[0] is PyInstance { Native: Series s }) return PdConv.Wrap(BoolSeries(s, FrameOps.IsIn(s.Values, values)));
                var d = D(a[0]);
                return PdConv.Wrap(new DataFrame(d.Data.Select(c => Column.FromBools(FrameOps.IsIn(c, values))), d.Columns, d.Index));
            });

            Def("droplevel", (i, a, k) =>
            {
                var p = A("droplevel", i, a, k, "level", "axis");
                var lv = p.Required(0);
                bool cols = a[0] is PyInstance { Native: DataFrame } && p.Has(1) && PdOps.AxisOf(p[1]!) == 1;
                FIndex ix = a[0] is PyInstance { Native: Series sr } ? sr.Index : cols ? D(a[0]).Columns : D(a[0]).Index;
                var list = PdConv.IsListLike(lv) ? PdConv.Cells(lv).Select(x => PdClasses.LevelNumber(ix, PdConv.FromLabel(x))).ToList() : new List<int> { PdClasses.LevelNumber(ix, lv) };
                var nix = ix.DropLevelList(list);
                if (a[0] is PyInstance { Native: Series s0 }) return PdConv.Wrap(new Series(s0.Values, nix, s0.Name));
                var d0 = D(a[0]);
                return PdConv.Wrap(cols ? d0.WithColumns(nix) : d0.WithIndex(nix));
            });

            // ---- ordering
            Def("sort_values", (i, a, k) =>
            {
                var p = A("sort_values", i, a, k, "by", "axis", "ascending", "inplace", "kind", "na_position", "ignore_index", "key");
                bool naLast = !(p[5] is "first");
                bool ignore = p.Bool(6, false);
                if (a[0] is PyInstance { Native: Series s })
                {
                    // Series.sort_values(axis=0, ascending=True, ...) — the first positional is axis
                    var q = A("sort_values", i, a, k, "axis", "ascending", "inplace", "kind", "na_position", "ignore_index", "key");
                    bool asc = !q.Has(1) || PyOps.Truthy(i, q[1]!);
                    var order = FrameOps.SortPositions(new[] { s.Values }, new[] { asc }, !(q[4] is "first"));
                    var r = s.Take(order);
                    if (q.Bool(5, false)) r = new Series(r.Values, FIndex.Range(r.Length), r.Name);
                    return Finish(a[0], PdConv.Wrap(r), q.Bool(2, false));
                }
                var d = D(a[0]);
                var byLabels = PdConv.IsListLike(p.Required(0)) ? PdConv.Cells(p[0]!) : new List<object?> { PdConv.ToCell(p[0]) };
                var keys = byLabels.Select(l => d.Data[d.ColumnPositions(l)[0]]).ToList();
                var ascs = p.Has(2) && PdConv.IsListLike(p[2]!) ? PdConv.Cells(p[2]!).Select(x => (bool)x!).ToList() : Enumerable.Repeat(!p.Has(2) || PyOps.Truthy(i, p[2]!), keys.Count).ToList();
                var res = d.TakeRows(FrameOps.SortPositions(keys, ascs, naLast));
                if (ignore) res = res.WithIndex(FIndex.Range(res.NRows));
                return Finish(a[0], PdConv.Wrap(res), p.Bool(3, false));
            });
            Def("sort_index", (i, a, k) =>
            {
                var p = A("sort_index", i, a, k, "axis", "level", "ascending", "inplace", "kind", "na_position", "ignore_index");
                bool asc = !p.Has(2) || PyOps.Truthy(i, p[2]!);
                bool naLast = !(p[5] is "first");
                if (a[0] is PyInstance { Native: Series s })
                {
                    var r = s.Take(FrameOps.SortPositions(IndexKeys(s.Index), Enumerable.Repeat(asc, s.Index.NLevels).ToList(), naLast));
                    if (p.Bool(6, false)) r = new Series(r.Values, FIndex.Range(r.Length), r.Name);
                    return Finish(a[0], PdConv.Wrap(r), p.Bool(3, false));
                }
                var d = D(a[0]);
                DataFrame res;
                if (p.Has(0) && PdOps.AxisOf(p[0]!) == 1)
                    res = d.TakeColumns(FrameOps.SortPositions(IndexKeys(d.Columns), Enumerable.Repeat(asc, d.Columns.NLevels).ToList(), naLast));
                else
                    res = d.TakeRows(FrameOps.SortPositions(IndexKeys(d.Index), Enumerable.Repeat(asc, d.Index.NLevels).ToList(), naLast));
                if (p.Bool(6, false)) res = res.WithIndex(FIndex.Range(res.NRows));
                return Finish(a[0], PdConv.Wrap(res), p.Bool(3, false));
            });
            foreach (var (name, largest) in new[] { ("nlargest", true), ("nsmallest", false) })
            {
                var nm = name; var lg = largest;
                Def(nm, (i, a, k) =>
                {
                    if (a[0] is PyInstance { Native: Series s })
                    {
                        var p = A(nm, i, a, k, "n", "keep");
                        int n = p.Int(0, 5);
                        var order = FrameOps.SortPositions(new[] { s.Values }, new[] { !lg }, true).Where(x => !s.Values.IsNa(x)).Take(n).ToArray();
                        return PdConv.Wrap(s.Take(order));
                    }
                    var q = A(nm, i, a, k, "n", "columns", "keep");
                    var d = D(a[0]);
                    var labels = PdConv.IsListLike(q.Required(1)) ? PdConv.Cells(q[1]!) : new List<object?> { PdConv.ToCell(q[1]) };
                    var keys = labels.Select(l => d.Data[d.ColumnPositions(l)[0]]).ToList();
                    var ord = FrameOps.SortPositions(keys, Enumerable.Repeat(!lg, keys.Count).ToList(), true).Take(q.Int(0, 5)).ToArray();
                    return PdConv.Wrap(d.TakeRows(ord));
                });
            }
            Def("reset_index", (i, a, k) =>
            {
                var p = A("reset_index", i, a, k, "level", "drop", "name", "inplace", "allow_duplicates");
                bool drop = p.Bool(1, false);
                if (a[0] is PyInstance { Native: Series s })
                {
                    if (drop) return Finish(a[0], PdConv.Wrap(new Series(s.Values, FIndex.Range(s.Length), s.Name)), p.Bool(3, false));
                    object? nm = p.Has(2) ? PdConv.ToCell(p[2]) : s.Name ?? 0L;
                    var (ic, inames) = IndexAsColumns(s.Index);
                    return PdConv.Wrap(new DataFrame(ic.Append(s.Values), new FIndex(Column.Infer(inames.Append(nm).ToList())), FIndex.Range(s.Length)));
                }
                var d = D(a[0]);
                if (drop) return Finish(a[0], PdConv.Wrap(d.WithIndex(FIndex.Range(d.NRows))), p.Bool(3, false));
                var (ic2, inames2) = IndexAsColumns(d.Index, d.HasColumn("index"));
                var cols = new List<Column>(ic2); cols.AddRange(d.Data);
                var names = new List<object?>(inames2); names.AddRange(d.Columns.Items());
                return Finish(a[0], PdConv.Wrap(new DataFrame(cols, new FIndex(Column.Infer(names), d.Columns.Name), FIndex.Range(d.NRows))), p.Bool(3, false));
            });
            Def("shift", (i, a, k) =>
            {
                var p = A("shift", i, a, k, "periods", "freq", "axis", "fill_value");
                int per = p.Int(0, 1); var fill = p.Has(3) ? PdConv.ToCell(p[3]) : null;
                return Rebuild(a[0], c => FrameOps.Shift(c, per, fill));
            });
            Def("diff", (i, a, k) => { int per = A("diff", i, a, k, "periods").Int(0, 1); return Rebuild(a[0], c => FrameOps.Diff(c, per)); });
            Def("pct_change", (i, a, k) =>
            {
                int per = A("pct_change", i, a, k, "periods").Int(0, 1);
                return Rebuild(a[0], c =>
                {
                    var f = c.Kind == Kind.Float ? c : Column.FromDoubles(Enumerable.Range(0, c.Length).Select(c.DoubleAt).ToArray());
                    return Ops.Binary(BinOp.Sub, Ops.Binary(BinOp.Div, f, FrameOps.Shift(f, per)), 1.0);
                });
            });

            // ---- uniqueness
            Def("duplicated", (i, a, k) =>
            {
                var p = A("duplicated", i, a, k, "subset", "keep");
                string keep = p.Has(1) ? (p[1] is bool b ? (b ? "first" : "none") : (string)p[1]!) : "first";
                if (a[0] is PyInstance { Native: Series s }) return PdConv.Wrap(BoolSeries(new Series(s.Values, s.Index, null), FrameOps.Duplicated(new[] { s.Values }, s.Length, keep)));
                var d = D(a[0]);
                var cols = p.Has(0) ? (PdConv.IsListLike(p[0]!) ? PdConv.Cells(p[0]!) : new List<object?> { PdConv.ToCell(p[0]) }).Select(l => d.Data[d.ColumnPositions(l)[0]]).ToList() : d.Data;
                return PdConv.Wrap(new Series(Column.FromBools(FrameOps.Duplicated(cols, d.NRows, keep)), d.Index));
            });
            Def("drop_duplicates", (i, a, k) =>
            {
                var p = A("drop_duplicates", i, a, k, "subset", "keep", "inplace", "ignore_index");
                string keep = p.Has(1) ? (p[1] is bool b ? (b ? "first" : "none") : (string)p[1]!) : "first";
                object result;
                if (a[0] is PyInstance { Native: Series s })
                {
                    var dup = FrameOps.Duplicated(new[] { s.Values }, s.Length, keep);
                    result = PdConv.Wrap(s.Take(Enumerable.Range(0, s.Length).Where(x => !dup[x]).ToArray()));
                }
                else
                {
                    var d = D(a[0]);
                    var cols = p.Has(0) ? (PdConv.IsListLike(p[0]!) ? PdConv.Cells(p[0]!) : new List<object?> { PdConv.ToCell(p[0]) }).Select(l => d.Data[d.ColumnPositions(l)[0]]).ToList() : d.Data;
                    var dup = FrameOps.Duplicated(cols, d.NRows, keep);
                    var r = d.TakeRows(Enumerable.Range(0, d.NRows).Where(x => !dup[x]).ToArray());
                    result = PdConv.Wrap(p.Bool(3, false) ? r.WithIndex(FIndex.Range(r.NRows)) : r);
                }
                return Finish(a[0], result, p.Bool(2, false));
            });
        }
        InstallSeriesOnly();
        InstallFrameOnly();
    }

    private static object WhereMask(Interp i, object[] a, Dictionary<string, object>? k, string name, bool keepWhenTrue)
    {
        var p = A(name, i, a, k, "cond", "other", "inplace", "axis", "level");
        var otherArg = p.Has(1) ? p[1] : null;
        bool otherIsFrame = otherArg is PyInstance { Native: DataFrame or Series };
        var other = p.Has(1) && !otherIsFrame && !(otherArg != null && PdConv.IsListLike(otherArg)) ? PdConv.ToCell(p[1]) : double.NaN;
        var cond = p.Required(0);
        if (cond is PyBuiltinFunction or PyFunction) cond = i.Call(cond, new[] { a[0] });
        if (a[0] is PyInstance { Native: Series s })
        {
            var sm = MaskOf(cond, s.Index);
            if (otherIsFrame || (otherArg != null && PdConv.IsListLike(otherArg)))
            {
                var oc = otherArg is PyInstance { Native: Series os } ? os.Values.Take(Ops.Reindexer(os.Index, s.Index)) : PdConv.ToColumn(otherArg!);
                var pos = Enumerable.Range(0, s.Length).Where(r => sm[r] != keepWhenTrue).ToArray();
                return Finish(a[0], PdConv.Wrap(NewSeries(s, pos.Length == 0 ? s.Values : s.Values.WithValues(pos, pos.Select(r => oc[r]).ToList()))), p.Bool(2, false));
            }
            return Finish(a[0], PdConv.Wrap(NewSeries(s, FrameOps.Where(s.Values, sm, other, keepWhenTrue))), p.Bool(2, false));
        }
        var d = D(a[0]);
        var cols = new List<Column>();
        Column? OtherCol(int j, int[] pos)
        {
            if (otherArg is PyInstance { Native: DataFrame od })
            {
                var l = od.Columns.Locs(d.Columns.Labels[j]);
                if (l.Count == 0) return null;
                return od.Data[l[0]].Take(Ops.Reindexer(od.Index, d.Index));
            }
            if (otherArg is PyInstance { Native: Series os2 }) return Column.Repeat(os2.Values[os2.Index.Locs(d.Columns.Labels[j]).FirstOrDefault()], d.NRows);
            return null;
        }
        if (cond is PyInstance { Native: DataFrame cd })
        {
            var rowMap = Ops.SameLabels(cd.Index, d.Index) ? null : Ops.Reindexer(cd.Index, d.Index);
            for (int j = 0; j < d.NCols; j++)
            {
                var cc = cd.Columns.Locs(d.Columns.Labels[j]);
                if (cc.Count == 0) throw PyErr.ValueError("Boolean array expected for the condition, not object");
                var flags = rowMap is null ? cd.Data[cc[0]] : cd.Data[cc[0]].Take(rowMap);
                var pos = Enumerable.Range(0, d.NRows).Where(r => flags.Bools[r] != keepWhenTrue).ToArray();
                var oc = OtherCol(j, pos);
                cols.Add(oc is null ? FrameOps.Where(d.Data[j], flags.Bools, other, keepWhenTrue) : pos.Length == 0 ? d.Data[j] : d.Data[j].WithValues(pos, pos.Select(r => oc[r]).ToList()));
            }
        }
        else
        {
            var m = MaskOf(cond, d.Index);
            for (int j = 0; j < d.NCols; j++)
            {
                var pos = Enumerable.Range(0, d.NRows).Where(r => m[r] != keepWhenTrue).ToArray();
                var oc = OtherCol(j, pos);
                cols.Add(oc is null ? FrameOps.Where(d.Data[j], m, other, keepWhenTrue) : pos.Length == 0 ? d.Data[j] : d.Data[j].WithValues(pos, pos.Select(r => oc[r]).ToList()));
            }
        }
        return Finish(a[0], PdConv.Wrap(new DataFrame(cols, d.Columns, d.Index)), p.Bool(2, false));
    }

    // ================================================================== Series-only

    private static void InstallSeriesOnly()
    {
        var cls = PdClasses.Series;
        void Def(string n, BuiltinFn f) => cls.Dict[n] = PdClasses.Fn($"Series.{n}", f);

        Def("between", (i, a, k) =>
        {
            var p = A("between", i, a, k, "left", "right", "inclusive");
            var s = S(a[0]);
            string inc = p.Has(2) ? (string)p[2]! : "both";
            var lo = PdConv.ToCell(p.Required(0)); var hi = PdConv.ToCell(p.Required(1));
            var l = Ops.Binary(inc is "both" or "left" ? BinOp.Ge : BinOp.Gt, s.Values, lo);
            var h = Ops.Binary(inc is "both" or "right" ? BinOp.Le : BinOp.Lt, s.Values, hi);
            return PdConv.Wrap(NewSeries(s, Ops.Binary(BinOp.And, l, h)));
        });
        Def("unique", (_, a, _) =>
        {
            var s = S(a[0]);
            var u = s.Values.Take(FrameOps.UniquePositions(s.Values));
            if (u.Kind == Kind.Category) return PdCategorical.WrapCategorical(u);
            if (Reduce.IsNumeric(u)) return PdArrays.Values(u);
            return new PyList(Enumerable.Range(0, u.Length).Select(x => PdConv.FromCell(u, x)));
        });
        Def("value_counts", (i, a, k) =>
        {
            var p = A("value_counts", i, a, k, "normalize", "sort", "ascending", "bins", "dropna");
            var s = S(a[0]);
            if (s.Values.Kind == Kind.Category) return CategoricalValueCounts(s, p.Bool(0, false), p.Bool(1, true), p.Bool(2, false), p.Bool(4, true));
            var (first, counts) = FrameOps.ValueCounts(s.Values, p.Bool(4, true), p.Bool(1, true), p.Bool(2, false));
            var idx = new FIndex(s.Values.Take(first), s.Name);
            if (p.Bool(0, false))
            {
                double total = counts.Sum();
                return PdConv.Wrap(new Series(Column.FromDoubles(counts.Select(c => c / total).ToArray()), idx, "proportion"));
            }
            return PdConv.Wrap(new Series(Column.FromLongs(counts), idx, "count"));
        });
        Def("to_frame", (i, a, k) =>
        {
            var p = A("to_frame", i, a, k, "name");
            var s = S(a[0]);
            object? nm = p.Has(0) ? PdConv.ToCell(p[0]) : s.Name ?? 0L;
            return PdConv.Wrap(new DataFrame(new[] { s.Values }, new FIndex(Column.Infer(new[] { nm })), s.Index));
        });
        Def("items", (_, a, _) =>
        {
            var s = S(a[0]);
            return new PyIterator(Enumerable.Range(0, s.Length).Select(x => (object)new PyTuple(new[] { PdConv.FromLabel(s.Index.Labels[x]), PdConv.FromCell(s.Values, x) })).GetEnumerator());
        });
        cls.Dict["iteritems"] = cls.Dict["items"];
        Def("keys", (_, a, _) => PdConv.Wrap(S(a[0]).Index));
        Def("describe", (i, a, k) =>
        {
            var p = A("describe", i, a, k, "percentiles", "include", "exclude");
            var pct = p.Has(0) ? PdConv.Cells(p[0]!).Select(x => Column.ToDouble(x!)).ToList() : null;
            return PdConv.Wrap(FrameOps.Describe(S(a[0]), pct));
        });
        Def("rename", (i, a, k) =>
        {
            var p = A("rename", i, a, k, "index", "axis", "copy", "inplace");
            var s = S(a[0]);
            var m = p.Required(0);
            if (m is not (PyDict or PyBuiltinFunction or PyFunction)) return Finish(a[0], PdConv.Wrap(s.Rename(PdConv.ToCell(m))), p.Bool(3, false));
            return Finish(a[0], PdConv.Wrap(new Series(s.Values, new FIndex(RenameLabels(i, s.Index, m), s.Index.Name), s.Name)), p.Bool(3, false));
        });
    }

    /// <summary>value_counts of a categorical Series: every category appears (unused ones with 0), most frequent first, ties in category order.</summary>
    private static object CategoricalValueCounts(Series s, bool normalize, bool sort, bool ascending, bool dropNa)
    {
        var c = s.Values;
        int k = c.Categories.Length;
        var counts = new long[k];
        long nas = 0;
        foreach (var code in c.Codes) { if (code >= 0) counts[code]++; else nas++; }
        var order = Enumerable.Range(0, k).ToList();
        if (sort) order = (ascending ? order.OrderBy(j => counts[j]) : order.OrderByDescending(j => counts[j])).ToList();
        var codes = order.ToList();
        var values = order.Select(j => counts[j]).ToList();
        Column labels = Column.FromCodes(order.ToArray(), c.Categories, c.Ordered);
        if (!dropNa && nas > 0)
        {
            labels = Column.FromCodes(order.Append(-1).ToArray(), c.Categories, c.Ordered);
            values.Add(nas);
        }
        double total = values.Sum();
        var idx = new FIndex(labels, s.Name);
        if (normalize) return PdConv.Wrap(new Series(Column.FromDoubles(values.Select(v => v / total).ToArray()), idx, "proportion"));
        return PdConv.Wrap(new Series(Column.FromLongs(values.ToArray()), idx, "count"));
    }

    internal static Column RenameLabels(Interp i, FIndex ix, object mapper)
    {
        var cells = new List<object?>();
        foreach (var l in ix.Items())
        {
            if (mapper is PyDict d)
            {
                bool found = false; object? r = l;
                foreach (var e in d.Entries) if (Equals(Column.Key(PdConv.ToCell(e.Key)), Column.Key(l))) { r = PdConv.ToCell(e.Value); found = true; break; }
                cells.Add(found ? r : l);
            }
            else cells.Add(PdConv.ToCell(i.Call(mapper, new[] { PdConv.FromLabel(l) })));
        }
        return Column.Infer(cells);
    }

    // ================================================================== DataFrame-only

    private static void InstallFrameOnly()
    {
        var cls = PdClasses.DataFrame;
        void Def(string n, BuiltinFn f) => cls.Dict[n] = PdClasses.Fn($"DataFrame.{n}", f);

        BuiltinFn transpose = (_, a, _) =>
        {
            var d = D(a[0]);
            var cols = new List<Column>();
            for (int r = 0; r < d.NRows; r++) cols.Add(Column.Infer(d.Data.Select(c => c[r]).ToList()));
            return PdConv.Wrap(new DataFrame(cols, d.Index, d.Columns));
        };
        Def("transpose", transpose);
        cls.Dict["T"] = new PyProperty { Getter = new PyBuiltinFunction("T", (i, a, k) => { PdConv.Interp = i; return transpose(i, a, k); }) };
        Def("set_index", (i, a, k) =>
        {
            var p = A("set_index", i, a, k, "keys", "drop", "append", "inplace", "verify_integrity");
            var d = D(a[0]);
            var keys = p.Required(0);
            if (p.Bool(2, false)) throw PyErr.NotImplementedError("set_index(append=True) needs a MultiIndex");
            bool drop = p.Bool(1, true);
            if (keys is string or BigInteger || (keys is PyList { Items.Count: 1 }) )
            {
                object key = keys is PyList l ? l.Items[0] : keys;
                int pos = d.ColumnPositions(PdConv.ToCell(key))[0];
                var r = new DataFrame(d.Data, d.Columns, new FIndex(d.Data[pos], d.Columns.Labels[pos]));
                if (drop) r = r.Drop(new[] { PdConv.ToCell(key) }, true).WithIndex(r.Index);
                return Finish(a[0], PdConv.Wrap(r), p.Bool(3, false));
            }
            if (PdConv.IsListLike(keys) && PdConv.Cells(keys).Count == d.NRows && !(keys is PyList kl && kl.Items.All(x => x is string && d.HasColumn(x))))
                return Finish(a[0], PdConv.Wrap(d.WithIndex(new FIndex(PdConv.ToColumn(keys)))), p.Bool(3, false));
            if (keys is PyList multi && multi.Items.All(x => d.HasColumn(PdConv.ToCell(x))))
            {
                var lbls = multi.Items.Select(PdConv.ToCell).ToList();
                var posn = lbls.Select(l => d.ColumnPositions(l)[0]).ToList();
                var mi = FIndex.Multi(posn.Select(q => d.Data[q]).ToList(), lbls);
                var r2 = new DataFrame(d.Data, d.Columns, mi);
                if (drop) r2 = r2.Drop(lbls, true).WithIndex(mi);
                return Finish(a[0], PdConv.Wrap(r2), p.Bool(3, false));
            }
            throw PyErr.NotImplementedError("set_index with this key");
        });
        Def("rename", (i, a, k) =>
        {
            var p = A("rename", i, a, k, "mapper", "index", "columns", "axis", "copy", "inplace");
            var d = D(a[0]);
            var r = d;
            if (p.Has(0))
            {
                bool cols = p.Has(3) && PdOps.AxisOf(p[3]!) == 1;
                if (!cols) r = new DataFrame(r.Data, r.Columns, new FIndex(RenameLabels(i, r.Index, p[0]!), r.Index.Name)); // mapper without axis targets the index
                else r = new DataFrame(r.Data, new FIndex(RenameLabels(i, r.Columns, p[0]!), r.Columns.Name), r.Index);
            }
            if (p.Has(1)) r = new DataFrame(r.Data, r.Columns, new FIndex(RenameLabels(i, r.Index, p[1]!), r.Index.Name));
            if (p.Has(2)) r = new DataFrame(r.Data, new FIndex(RenameLabels(i, r.Columns, p[2]!), r.Columns.Name), r.Index);
            return Finish(a[0], PdConv.Wrap(r), p.Bool(5, false));
        });
        Def("describe", (i, a, k) =>
        {
            var p = A("describe", i, a, k, "percentiles", "include", "exclude");
            if (p.Has(1) || p.Has(2)) throw PyErr.NotImplementedError("describe(include=/exclude=)");
            var d = D(a[0]);
            var pct = p.Has(0) ? PdConv.Cells(p[0]!).Select(x => Column.ToDouble(x!)).ToList() : null;
            var num = Enumerable.Range(0, d.NCols).Where(j => d.Data[j].Kind is Kind.Int or Kind.Float).ToArray();
            var use = num.Length > 0 ? num : Enumerable.Range(0, d.NCols).ToArray();
            if (use.Length == 0) throw PyErr.ValueError("Cannot describe a DataFrame without columns");
            var parts = use.Select(j => FrameOps.Describe(new Series(d.Data[j], d.Index, null), pct)).ToList();
            return PdConv.Wrap(new DataFrame(parts.Select(s => s.Values), d.Columns.Take(use), parts[0].Index));
        });
        Def("select_dtypes", (i, a, k) =>
        {
            var p = A("select_dtypes", i, a, k, "include", "exclude");
            var d = D(a[0]);
            bool Matches(Column c, object spec)
            {
                if (spec is string s)
                {
                    switch (s)
                    {
                        case "number": return c.Kind is Kind.Int or Kind.Float;
                        case "integer": return c.Kind == Kind.Int;
                        case "floating": return c.Kind == Kind.Float;
                        case "object": return c.Kind == Kind.Object;
                        case "string": case "str": return c.Kind == Kind.Str;
                    }
                }
                if (spec is PyBuiltinFunction { Name: "object" } or PyClass { Name: "object" }) return c.Kind == Kind.Object;
                return c.DTypeName == PdConv.DTypeName(spec);
            }
            var inc = p.Has(0) ? (PdConv.IsListLike(p[0]!) ? PdConv.Cells(p[0]!).Select(x => PdConv.IsListLike(p[0]!) ? (object)x! : x!).ToList() : new List<object?> { p[0] }) : null;
            var incSpecs = p.Has(0) ? (p[0] is PyList pl ? pl.Items : p[0] is PyTuple pt ? pt.Items.ToList() : new List<object> { p[0]! }) : null;
            var excSpecs = p.Has(1) ? (p[1] is PyList ql ? ql.Items : p[1] is PyTuple qt ? qt.Items.ToList() : new List<object> { p[1]! }) : null;
            var keep = Enumerable.Range(0, d.NCols).Where(j => (incSpecs is null || incSpecs.Any(s => Matches(d.Data[j], s))) && (excSpecs is null || !excSpecs.Any(s => Matches(d.Data[j], s)))).ToArray();
            return PdConv.Wrap(d.TakeColumns(keep));
        });
        Def("items", (_, a, _) =>
        {
            var d = D(a[0]);
            return new PyIterator(Enumerable.Range(0, d.NCols).Select(j => (object)new PyTuple(new object[] { PdConv.FromLabel(d.Columns.Labels[j]), PdConv.Wrap(d.GetColumn(j)) })).GetEnumerator());
        });
        cls.Dict["iteritems"] = cls.Dict["items"];
        Def("iterrows", (_, a, _) =>
        {
            var d = D(a[0]);
            return new PyIterator(Enumerable.Range(0, d.NRows).Select(r =>
                (object)new PyTuple(new object[] { PdConv.FromLabel(d.Index.Labels[r]), PdConv.Wrap(new Series(Column.Infer(d.Data.Select(c => c[r]).ToList()), d.Columns, d.Index.Labels[r])) })).GetEnumerator());
        });
        Def("keys", (_, a, _) => PdConv.Wrap(D(a[0]).Columns));
    }
}
