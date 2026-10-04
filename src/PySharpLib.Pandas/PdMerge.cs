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

/// <summary><c>pd.merge</c>, <c>DataFrame.merge/join</c> and <c>pd.concat</c>.</summary>
internal static class PdMerge
{
    private static List<object?> Labels(object? v) => v is null or PyNone ? new List<object?>() : PdConv.IsListLike(v) && v is not PyTuple { Items.Length: 0 } ? PdConv.Cells(v) : new List<object?> { PdConv.ToCell(v) };

    private static DataFrame AsFrame(object o) => o switch
    {
        PyInstance { Native: DataFrame d } => d,
        PyInstance { Native: Series s } => new DataFrame(new[] { s.Values }, new FIndex(Column.Infer(new[] { s.Name })), s.Index),
        _ => throw PyErr.TypeError($"Can only merge Series or DataFrame objects, a {PyOps.TypeName(o)} was passed"),
    };

    private static (string, string) Suffixes(object? v)
    {
        if (v is PyTuple { Items.Length: 2 } t) return (t.Items[0] as string ?? "", t.Items[1] as string ?? "");
        if (v is PyList { Items.Count: 2 } l) return (l.Items[0] as string ?? "", l.Items[1] as string ?? "");
        return ("_x", "_y");
    }

    private static object DoMerge(DataFrame left, DataFrame right, Args p, int onIdx, int leftOnIdx, int rightOnIdx, int leftIndexIdx, int rightIndexIdx, int howIdx, int sortIdx, int suffixIdx, int indicatorIdx)
    {
        string how = p.Has(howIdx) ? (string)p[howIdx]! : "inner";
        bool li = p.Bool(leftIndexIdx, false), ri = p.Bool(rightIndexIdx, false);
        List<object?> lon = Labels(p[leftOnIdx]), ron = Labels(p[rightOnIdx]);
        var on = Labels(p[onIdx]);
        if (on.Count > 0) { lon = on; ron = on; }
        else if (how != "cross" && lon.Count == 0 && ron.Count == 0 && !li && !ri)
        {
            // default: the columns both frames share
            lon = left.Columns.Items().Where(l => right.HasColumn(l)).ToList();
            ron = lon;
            if (lon.Count == 0) throw new FrameException("No common columns to perform merge on. Merge options: left_on=None, right_on=None, left_index=False, right_index=False");
        }
        if (!li && lon.Count > 0) foreach (var l in lon) if (!left.HasColumn(l)) throw PyErr.KeyError(PdConv.FromLabel(l));
        if (!ri && ron.Count > 0) foreach (var l in ron) if (!right.HasColumn(l)) throw PyErr.KeyError(PdConv.FromLabel(l));
        var (sl, sr) = Suffixes(p[suffixIdx]);
        var spec = new Merge.Spec
        {
            LeftOn = lon, RightOn = ron, LeftIndex = li, RightIndex = ri, How = how, SuffixLeft = sl, SuffixRight = sr,
            Sort = p.Bool(sortIdx, false), Indicator = p.Bool(indicatorIdx, false),
        };
        return PdConv.Wrap(Merge.Join(left, right, spec));
    }

    public static void Install(PyModule m)
    {
        m.Dict["merge"] = PdClasses.Fn("merge", (i, a, k) =>
        {
            var p = new Args("merge", i, a, k, "left", "right", "how", "on", "left_on", "right_on", "left_index", "right_index", "sort", "suffixes", "copy", "indicator", "validate");
            return DoMerge(AsFrame(p.Required(0)), AsFrame(p.Required(1)), p, 3, 4, 5, 6, 7, 2, 8, 9, 11);
        });
        PdClasses.DataFrame.Dict["merge"] = PdClasses.Fn("merge", (i, a, k) =>
        {
            var p = new Args("merge", i, a.Skip(1).ToArray(), k, "right", "how", "on", "left_on", "right_on", "left_index", "right_index", "sort", "suffixes", "copy", "indicator", "validate");
            return DoMerge(PdConv.D(a[0]), AsFrame(p.Required(0)), p, 2, 3, 4, 5, 6, 1, 7, 8, 10);
        });
        PdClasses.DataFrame.Dict["join"] = PdClasses.Fn("join", (i, a, k) =>
        {
            var p = new Args("join", i, a.Skip(1).ToArray(), k, "other", "on", "how", "lsuffix", "rsuffix", "sort", "validate");
            var left = PdConv.D(a[0]);
            var others = p.Required(0) is PyList ol ? ol.Items.Select(AsFrame).ToList() : new List<DataFrame> { AsFrame(p[0]!) };
            string how = p.Has(2) ? (string)p[2]! : "left";
            var on = Labels(p[1]);
            DataFrame cur = left;
            foreach (var right in others)
            {
                var spec = new Merge.Spec
                {
                    LeftOn = on, RightOn = Array.Empty<object?>(), LeftIndex = on.Count == 0, RightIndex = true, How = how,
                    SuffixLeft = p.Has(3) ? (string)p[3]! : "", SuffixRight = p.Has(4) ? (string)p[4]! : "", Sort = p.Bool(5, false), KeepAllColumns = true,
                };
                cur = Merge.Join(cur, right, spec);
            }
            return PdConv.Wrap(cur);
        });
        m.Dict["concat"] = PdClasses.Fn("concat", (i, a, k) =>
        {
            var p = new Args("concat", i, a, k, "objs", "axis", "join", "ignore_index", "keys", "levels", "names", "verify_integrity", "sort", "copy");
            var raw = p.Required(0);
            List<object> items; List<object?>? keys = p.Has(4) ? PdConv.Cells(p[4]!) : null;
            if (raw is PyDict dd) { items = dd.Values.ToList(); keys ??= dd.Keys.Select(PdConv.ToCell).ToList(); }
            else items = PdConv.Cells(raw).Count == 0 ? new List<object>() : (raw is PyList l ? l.Items.ToList() : raw is PyTuple t ? t.Items.ToList() : PdConv.IsListLike(raw) ? PyOps.Iterate(i, raw).ToList() : throw PyErr.TypeError("first argument must be an iterable of pandas objects"));
            var keep = items.Select((o, n) => (o, n)).Where(x => x.o is not PyNone).ToList();
            if (keep.Count == 0) throw PyErr.ValueError("No objects to concatenate");
            if (keys is not null) keys = keep.Select(x => keys[x.n]).ToList();
            items = keep.Select(x => x.o).ToList();
            int axis = p.Has(1) ? PdOps.AxisOf(p[1]!) : 0;
            bool inner = p.Has(2) && (string)p[2]! == "inner";
            bool ignore = p.Bool(3, false);
            var names = p.Has(6) ? PdConv.Cells(p[6]!) : null;
            bool allSeries = items.All(o => o is PyInstance { Native: Series });
            bool allFrames = items.All(o => o is PyInstance { Native: DataFrame });
            if (axis == 0)
            {
                if (allSeries)
                {
                    var ss = items.Select(o => (Series)((PyInstance)o).Native!).ToList();
                    var values = Column.Concat(ss.Select(s => s.Values).ToList());
                    FIndex idx;
                    if (ignore) idx = FIndex.Range(values.Length);
                    else if (keys is not null)
                    {
                        var inner0 = ss.Select(s => s.Index).Aggregate((x, y) => x.Concat(y));
                        var outer = Column.Infer(ss.SelectMany((s, g) => Enumerable.Repeat(keys[g], s.Length)).ToList());
                        idx = FIndex.Multi(new[] { outer }.Concat(Enumerable.Range(0, inner0.NLevels).Select(inner0.Level)).ToList(), (names ?? new List<object?> { null }).Concat(inner0.Names).Take(1 + inner0.NLevels).ToList());
                    }
                    else idx = ss.Select(s => s.Index).Aggregate((x, y) => x.Concat(y));
                    object? nm = ss.All(s => Equals(s.Name, ss[0].Name)) ? ss[0].Name : null;
                    return PdConv.Wrap(new Series(values, idx, nm));
                }
                if (!allFrames) throw PyErr.NotImplementedError("concat of a mix of Series and DataFrames along axis=0");
                return PdConv.Wrap(Merge.ConcatRows(items.Select(o => (DataFrame)((PyInstance)o).Native!).ToList(), inner, ignore, keys, names));
            }
            var parts = new List<(FIndex, IReadOnlyList<Column>, IReadOnlyList<object?>)>();
            for (int n = 0; n < items.Count; n++)
            {
                if (items[n] is PyInstance { Native: Series s })
                    parts.Add((s.Index, new[] { s.Values }, new[] { keys is not null ? keys[n] : s.Name ?? (object)(long)n }));
                else
                {
                    var d = (DataFrame)((PyInstance)items[n]).Native!;
                    object?[] nm = keys is not null ? d.Columns.Items().Select(c => (object?)new LabelTuple(new[] { keys[n], c })).ToArray() : d.Columns.Items().ToArray();
                    parts.Add((d.Index, d.Data, nm));
                }
            }
            return PdConv.Wrap(Merge.ConcatColumns(parts, inner, ignore));
        });
    }
}
