// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary>Constructors: what <c>pd.Series(...)</c>, <c>pd.DataFrame(...)</c> and <c>pd.Index(...)</c> accept.</summary>
internal static class PdBuild
{
    /// <summary>An Index from any list-like (an Index passes through). <paramref name="expected"/> only matters for the error text.</summary>
    public static FIndex Index(object? v, int expected)
    {
        if (v is PyInstance { Native: FIndex ix }) return ix;
        if (v is PyRange r && r.Step == 1 && r.Start >= 0)
            return FIndex.Range((int)r.Start, (int)r.Stop, 1);
        if (v is null or PyNone) return FIndex.Range(Math.Max(expected, 0));
        if (!PdConv.IsListLike(v)) throw PyErr.TypeError($"Index(...) must be called with a collection of some kind, {PyOps.Repr(PdConv.Interp!, v)} was passed");
        var name = v is PyInstance { Native: Series s } ? s.Name : null;
        return new FIndex(PdConv.ToColumn(v), name);
    }

    public static Series MakeSeries(object? data, object? index, object? dtype, object? name)
    {
        object? nm = name is null or PyNone ? null : PdConv.ToCell(name);
        Column col; FIndex ix;
        switch (data)
        {
            case null or PyNone:
                ix = index is null or PyNone ? FIndex.Range(0) : Index(index, 0);
                col = Column.FromObjects(new object?[ix.Length]);
                break;
            case PyDict d:
            {
                var keys = d.Keys.Select(PdConv.ToCell).ToList();
                var vals = d.Values.Select(PdConv.ToCell).ToList();
                ix = new FIndex(Column.Infer(keys));
                col = Column.Infer(vals);
                if (index is not null and not PyNone)
                {
                    var target = Index(index, 0);
                    var pos = target.Items().Select(l => { var loc = ix.Locs(l); return loc.Count == 0 ? -1 : loc[0]; }).ToArray();
                    col = col.Take(pos); ix = target;
                }
                break;
            }
            case PyInstance { Native: Series src }:
            {
                col = src.Values; ix = src.Index; nm ??= src.Name;
                if (index is not null and not PyNone)
                {
                    var target = Index(index, 0);
                    col = col.Take(target.Items().Select(l => { var loc = src.Index.Locs(l); return loc.Count == 0 ? -1 : loc[0]; }).ToArray());
                    ix = target;
                }
                break;
            }
            default:
                if (PdConv.IsListLike(data))
                {
                    col = PdConv.ToColumn(data);
                    ix = index is null or PyNone ? FIndex.Range(col.Length) : Index(index, col.Length);
                    if (ix.Length != col.Length) throw PyErr.ValueError($"Length of values ({col.Length}) does not match length of index ({ix.Length})");
                }
                else
                {
                    // scalar: broadcast over the index (length 1 without one)
                    ix = index is null or PyNone ? FIndex.Range(1) : Index(index, 1);
                    col = Column.Repeat(PdConv.ToCell(data), ix.Length);
                }
                break;
        }
        if (dtype is not null and not PyNone) col = PdConv.AsType(col, dtype);
        return new Series(col, ix, nm);
    }

    /// <summary>One column for <c>df[label] = value</c>: scalar broadcast, list-like of the right length, or a Series aligned on the frame's index.</summary>
    public static Column ColumnFor(DataFrame df, object value)
    {
        if (value is PyInstance { Native: Series s })
        {
            if (PdSelect.SameLabels(s.Index, df.Index)) return s.Values;
            return s.Values.Take(df.Index.Items().Select(l => { var loc = s.Index.Locs(l); return loc.Count == 0 ? -1 : loc[0]; }).ToArray());
        }
        if (PdConv.IsListLike(value))
        {
            var col = PdConv.ToColumn(value);
            if (col.Length != df.NRows) throw PyErr.ValueError($"Length of values ({col.Length}) does not match length of index ({df.NRows})");
            return col;
        }
        return Column.Repeat(PdConv.ToCell(value), df.NRows);
    }

    private static bool IsScalar(object v) => !PdConv.IsListLike(v) && v is not PyDict;

    public static DataFrame MakeDataFrame(object? data, object? index, object? columns, object? dtype)
    {
        DataFrame df = Build(data, index, columns);
        if (dtype is not null and not PyNone)
            df = new DataFrame(df.Data.Select(c => PdConv.AsType(c, dtype)), df.Columns, df.Index);
        return df;
    }

    private static DataFrame Build(object? data, object? index, object? columns)
    {
        FIndex? ix = index is null or PyNone ? null : Index(index, 0);
        FIndex? cols = columns is null or PyNone ? null : Index(columns, 0);
        switch (data)
        {
            case null or PyNone:
            {
                var c = cols ?? new FIndex(Column.FromObjects(Array.Empty<object?>()));
                var rows = ix ?? FIndex.Range(0);
                return new DataFrame(c.Items().Select(_ => Column.FromObjects(new object?[rows.Length])), c, rows);
            }
            case PyDict d: return FromDict(d, ix, cols);
            case PyInstance { Native: DataFrame src }:
            {
                var r = src;
                if (cols is not null) r = r.TakeColumns(cols.Items().Select(l => src.ColumnPositions(l)[0]).ToArray());
                if (ix is not null) r = r.TakeRows(ix.Items().Select(l => { var loc = src.Index.Locs(l); return loc.Count == 0 ? -1 : loc[0]; }).ToArray());
                return r.Copy();
            }
            case PyInstance { Native: Series s }:
            {
                var name = s.Name ?? (object)0L;
                return new DataFrame(new[] { s.Values }, new FIndex(Column.Infer(new[] { s.Name ?? 0L })), s.Index);
            }
            case PyInstance { Native: NDArray nd }: return FromNd(nd, ix, cols);
            case PyList or PyTuple or PyRange:
            {
                var items = data is PyList l ? l.Items : data is PyTuple t ? t.Items.ToList() : PdConv.Cells(data).Select(PdConv.FromLabel).ToList();
                if (items.Count == 0)
                    return new DataFrame((cols ?? new FIndex(Column.FromObjects(Array.Empty<object?>()))).Items().Select(_ => Column.FromObjects(Array.Empty<object?>())), cols ?? new FIndex(Column.FromObjects(Array.Empty<object?>())), ix ?? FIndex.Range(0));
                if (items.All(x => x is PyDict)) return FromRecords(items.Cast<PyDict>().ToList(), ix, cols);
                if (items.All(x => x is PyList or PyTuple || x is PyInstance { Native: NDArray { Ndim: 1 } }))
                    return FromRows(items.Select(PdConv.Cells).ToList(), ix, cols);
                if (items.All(x => x is PyInstance { Native: Series }))
                    throw PyErr.NotImplementedError("DataFrame from a list of Series");
                // a flat list: one column named 0
                var col = Column.Infer(items.Select(PdConv.ToCell).ToList());
                var rows = ix ?? FIndex.Range(col.Length);
                return new DataFrame(new[] { col }, cols ?? new FIndex(Column.FromLongs(new long[] { 0 })), rows);
            }
            default:
                if (IsScalar(data!))
                    throw PyErr.ValueError("DataFrame constructor not properly called!");
                throw PyErr.TypeError($"DataFrame data of type '{PyOps.TypeName(data!)}' is not supported");
        }
    }

    private static DataFrame FromDict(PyDict d, FIndex? ix, FIndex? cols)
    {
        var entries = d.Entries.Select(e => (Key: PdConv.ToCell(e.Key), Value: e.Value)).ToList();
        if (cols is not null)
        {
            // select / reorder; keys that are absent give an all-NaN column
            var map = entries.ToDictionary(e => Column.Key(e.Key) ?? Column.NaNKey, e => e.Value);
            entries = cols.Items().Select(l => (Key: l, Value: map.TryGetValue(Column.Key(l) ?? Column.NaNKey, out var v) ? v : (object)PyNone.Instance)).ToList();
        }
        // rows: explicit index, else the union of the Series indexes, else 0..n-1 from the list-likes
        FIndex? rows = ix;
        if (rows is null)
        {
            var seriesIdx = entries.Where(e => e.Value is PyInstance { Native: Series }).Select(e => ((Series)((PyInstance)e.Value).Native!).Index).ToList();
            if (seriesIdx.Count > 0) rows = seriesIdx.Skip(1).All(x => PdSelect.SameLabels(x, seriesIdx[0])) ? seriesIdx[0] : Union(seriesIdx);
        }
        int n = rows?.Length ?? -1;
        if (n < 0)
        {
            var lens = entries.Where(e => PdConv.IsListLike(e.Value)).Select(e => PdConv.Cells(e.Value).Count).Distinct().ToList();
            if (lens.Count > 1) throw PyErr.ValueError("All arrays must be of the same length");
            if (lens.Count == 0)
            {
                if (entries.Count == 0) { n = 0; }
                else throw PyErr.ValueError("If using all scalar values, you must pass an index");
            }
            else n = lens[0];
            rows = FIndex.Range(n);
        }
        var data = new List<Column>();
        foreach (var (key, val) in entries)
        {
            Column c;
            if (val is PyInstance { Native: Series s })
                c = rows is not null && PdSelect.SameLabels(s.Index, rows) ? s.Values : s.Values.Take(rows!.Items().Select(l => { var loc = s.Index.Locs(l); return loc.Count == 0 ? -1 : loc[0]; }).ToArray());
            else if (val is PyNone && cols is not null) c = Column.FromObjects(Enumerable.Repeat((object?)double.NaN, n).ToArray());
            else if (PdConv.IsListLike(val))
            {
                c = PdConv.ToColumn(val);
                if (c.Length != n) throw PyErr.ValueError(ix is not null ? $"Length of values ({c.Length}) does not match length of index ({n})" : "All arrays must be of the same length");
            }
            else c = Column.Repeat(PdConv.ToCell(val), n);
            data.Add(c);
        }
        var colIndex = cols ?? new FIndex(Column.Infer(entries.Select(e => e.Key).ToList()));
        return new DataFrame(data, colIndex, rows ?? FIndex.Range(0));
    }

    private static FIndex Union(List<FIndex> idx)
    {
        var seen = new HashSet<object>();
        var all = new List<object?>();
        foreach (var ix in idx)
            foreach (var l in ix.Items())
                if (seen.Add(Column.Key(l) ?? Column.NaNKey)) all.Add(l);
        // pandas sorts the union when the indexes differ
        try { all.Sort(PdSelect.Compare); } catch (PyRaise) { }
        return new FIndex(Column.Infer(all));
    }

    private static DataFrame FromRecords(List<PyDict> rows, FIndex? ix, FIndex? cols)
    {
        var keys = new List<object?>();
        var seen = new HashSet<object>();
        foreach (var r in rows)
            foreach (var k in r.Keys.Select(PdConv.ToCell))
                if (seen.Add(Column.Key(k) ?? Column.NaNKey)) keys.Add(k);
        var colIndex = cols ?? new FIndex(Column.Infer(keys));
        var data = new List<Column>();
        foreach (var key in colIndex.Items())
        {
            var vals = new List<object?>();
            foreach (var r in rows)
            {
                object? found = null; bool has = false;
                foreach (var e in r.Entries)
                    if (Equals(Column.Key(PdConv.ToCell(e.Key)), Column.Key(key))) { found = PdConv.ToCell(e.Value); has = true; break; }
                vals.Add(has ? found : null);
            }
            data.Add(Column.Infer(vals));
        }
        return new DataFrame(data, colIndex, ix ?? FIndex.Range(rows.Count));
    }

    private static DataFrame FromRows(List<List<object?>> rows, FIndex? ix, FIndex? cols)
    {
        int m = rows.Max(r => r.Count);
        if (rows.Any(r => r.Count != m) && cols is null) { /* ragged rows pad with NaN, like pandas */ }
        var colIndex = cols ?? FIndex.Range(m);
        if (colIndex.Length != m) throw PyErr.ValueError($"{m} columns passed, passed data had {m} columns".Replace(m.ToString() + " columns passed", colIndex.Length + " columns passed"));
        var data = new List<Column>();
        for (int j = 0; j < m; j++) data.Add(Column.Infer(rows.Select(r => j < r.Count ? r[j] : null).ToList()));
        return new DataFrame(data, colIndex, ix ?? FIndex.Range(rows.Count));
    }

    private static DataFrame FromNd(NDArray nd, FIndex? ix, FIndex? cols)
    {
        if (nd.Ndim == 1)
        {
            var col = PdConv.FromNd(nd);
            return new DataFrame(new[] { col }, cols ?? FIndex.Range(1), ix ?? FIndex.Range(col.Length));
        }
        if (nd.Ndim != 2) throw PyErr.ValueError($"Must pass 2-d input. shape={string.Join(", ", nd.Shape)}");
        int n = nd.Shape[0], m = nd.Shape[1];
        var data = new List<Column>();
        for (int j = 0; j < m; j++) data.Add(PdConv.FromNd(nd.Get(new NDIndex[] { Slice.All, j })));
        return new DataFrame(data, cols ?? FIndex.Range(m), ix ?? FIndex.Range(n));
    }
}
