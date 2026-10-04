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

/// <summary>Indexing semantics of <c>[]</c>, <c>loc</c>, <c>iloc</c>, <c>at</c>, <c>iat</c> for Series and DataFrame.</summary>
internal static class PdAccess
{
    public enum Mode { Bracket, Loc, ILoc, At, IAt }

    // ------------------------------------------------------------------ Series

    private static (int[] pos, bool scalar) SeriesAxis(Series s, object key, Mode mode)
    {
        switch (mode)
        {
            case Mode.ILoc: case Mode.IAt: return PdSelect.ILocAxis(key, s.Length);
            case Mode.Bracket when key is PySlice sl:
                return PdSelect.IsPositionalSlice(sl) ? (PdSelect.PositionalSlice(sl, s.Length), false) : (PdSelect.LabelSlice(sl, s.Index), false);
            default: return PdSelect.LocAxis(key, s.Index);
        }
    }

    public static object SeriesGet(Series s, object key, Mode mode)
    {
        if (key is PyTuple && mode != Mode.Bracket && !s.Index.IsMulti) throw PyErr.IndexError("Too many indexers");
        var (pos, scalar) = SeriesAxis(s, key, mode);
        if (scalar) return PdConv.FromCell(s.Values, pos[0]);
        var taken = s.Take(pos);
        int depth = mode is Mode.Loc or Mode.Bracket ? PdSelect.PartialDepth(key, s.Index) : 0;
        return PdConv.Wrap(depth > 0 ? new Series(taken.Values, taken.Index.DropLevels(depth), taken.Name) : taken);
    }

    public static void SeriesSet(Series s, object key, object value, Mode mode)
    {
        int[] pos; bool scalar;
        try { (pos, scalar) = SeriesAxis(s, key, mode); }
        catch (PyRaise ex) when (mode is Mode.Bracket or Mode.Loc or Mode.At && ex.Value.Class == PyErr.KeyErrorClass && !PdConv.IsListLike(key) && key is not PySlice)
        {
            // setting with a new label enlarges the Series
            s.Append(PdConv.ToCell(key), ScalarOrFirst(value));
            return;
        }
        var cells = ValueCells(value, s.Index, pos, mode);
        s.Values = s.Values.WithValues(pos, cells);
    }

    private static object? ScalarOrFirst(object value) => PdConv.IsListLike(value) ? throw PyErr.ValueError("cannot set a Series with multiple values using a new label") : PdConv.ToCell(value);

    /// <summary>The replacement values for a set: a scalar broadcasts, a list-like supplies one per position (a Series aligns on its labels).</summary>
    private static List<object?> ValueCells(object value, FIndex targetIndex, int[] pos, Mode mode)
    {
        if (!PdConv.IsListLike(value)) return new List<object?> { PdConv.ToCell(value) };
        if (value is PyInstance { Native: Series vs } && mode is not (Mode.ILoc or Mode.IAt))
        {
            var r = new List<object?>();
            foreach (var p in pos)
            {
                var locs = vs.Index.Locs(targetIndex.Labels[p]);
                r.Add(locs.Count == 0 ? double.NaN : vs.Values[locs[0]]);
            }
            return r;
        }
        var cells = PdConv.Cells(value);
        if (cells.Count != pos.Length)
            throw PyErr.ValueError($"cannot set using a list-like indexer with a different length than the value");
        return cells;
    }

    // ------------------------------------------------------------------ DataFrame []

    public static object FrameGet(DataFrame df, object key)
    {
        if (key is PyTuple && !df.Columns.IsMulti) throw PyErr.NotImplementedError("DataFrame[tuple] needs MultiIndex columns");
        if (key is PyInstance { Native: DataFrame cond } && cond.Data.All(c => c.Kind == Kind.Bool))
            return PdConv.Wrap(new DataFrame(df.Data.Select((c, j) => FrameOps.Where(c, cond.Data[cond.Columns.Locs(df.Columns.Labels[j]).FirstOrDefault()].Bools, double.NaN)), df.Columns, df.Index));
        if (key is PySlice sl)
            return PdConv.Wrap(df.TakeRows(PdSelect.IsPositionalSlice(sl) ? PdSelect.PositionalSlice(sl, df.NRows) : PdSelect.LabelSlice(sl, df.Index)));
        var mask = PdSelect.TryMask(key, df.Index);
        if (mask is not null)
        {
            if (mask.Length != df.NRows) throw PyErr.ValueError($"Item wrong length {mask.Length} instead of {df.NRows}.");
            return PdConv.Wrap(df.TakeRows(PdSelect.MaskPositions(mask)));
        }
        if (PdConv.IsListLike(key) && !(key is PyTuple && df.Columns.IsMulti))
        {
            var pos = new List<int>();
            var cells = PdConv.Cells(key);
            var missing = new List<string>();
            foreach (var c in cells)
            {
                var l = df.Columns.Locs(c);
                if (l.Count == 0) missing.Add(FIndex.KeyText(c)); else pos.AddRange(l);
            }
            if (missing.Count > 0)
                throw PyErr.Raise(PyErr.KeyErrorClass, missing.Count == cells.Count ? $"\"None of [{Formatter.IndexRepr(new FIndex(Column.Infer(cells)))}] are in the [columns]\"" : $"\"[{string.Join(", ", missing)}] not in index\"");
            return PdConv.Wrap(df.TakeColumns(pos));
        }
        if (df.Columns.IsMulti && (key is PyTuple || !PdConv.IsListLike(key)))
        {
            var (cpos, cscalar) = PdSelect.LocAxis(key, df.Columns);
            int depth = PdSelect.PartialDepth(key, df.Columns);
            if (cscalar) return PdConv.Wrap(df.GetColumn(cpos[0]));
            var sub = df.TakeColumns(cpos);
            return PdConv.Wrap(depth > 0 ? new DataFrame(sub.Data, sub.Columns.DropLevels(depth), sub.Index) : sub);
        }
        var label = PdConv.ToCell(key);
        var locs = df.Columns.Locs(label);
        if (locs.Count == 0) throw PdConv.KeyErr(key);
        return locs.Count == 1 ? PdConv.Wrap(df.GetColumn(locs[0])) : PdConv.Wrap(df.TakeColumns(locs));
    }

    public static void FrameSetItem(DataFrame df, object key, object value)
    {
        if (PdConv.IsListLike(key) && PdSelect.TryMask(key) is null)
        {
            foreach (var c in PdConv.Cells(key)) df.SetColumn(c, PdBuild.ColumnFor(df, value));
            return;
        }
        if (PdSelect.TryMask(key, df.Index) is not null) throw PyErr.NotImplementedError("DataFrame[mask] = value");
        df.SetColumn(PdConv.ToCell(key), PdBuild.ColumnFor(df, value));
    }

    // ------------------------------------------------------------------ DataFrame loc / iloc / at / iat

    private static (int[] pos, bool scalar) RowAxis(DataFrame df, object key, Mode mode)
        => mode is Mode.ILoc or Mode.IAt ? PdSelect.ILocAxis(key, df.NRows) : PdSelect.LocAxis(key, df.Index);

    private static (int[] pos, bool scalar) ColAxis(DataFrame df, object key, Mode mode)
        => mode is Mode.ILoc or Mode.IAt ? PdSelect.ILocAxis(key, df.NCols) : PdSelect.LocAxis(key, df.Columns);

    public static object FrameLoc(DataFrame df, object key, Mode mode)
    {
        object rowKey = key, colKey = null!;
        bool hasCols = false;
        bool tupleIsRowKey = false;
        if (key is PyTuple tk && df.Index.IsMulti && mode == Mode.Loc && tk.Items.Length <= df.Index.NLevels && tk.Items[0] is not (PyTuple or PySlice or PyList))
        {
            // a short tuple on a MultiIndex is a row key unless it names no row (then it is (row, column))
            try { PdSelect.LocAxis(key, df.Index); tupleIsRowKey = true; } catch (PyRaise ex) when (ex.Value.Class == PyErr.KeyErrorClass) { }
        }
        if (key is PyTuple t && !tupleIsRowKey)
        {
            if (t.Items.Length != 2) throw PyErr.IndexError("Too many indexers");
            rowKey = t.Items[0]; colKey = t.Items[1]; hasCols = true;
        }
        var (rp, rs) = RowAxis(df, rowKey, mode);
        int rowDepth = mode == Mode.Loc ? PdSelect.PartialDepth(rowKey, df.Index) : 0;
        var (cp, cs) = hasCols ? ColAxis(df, colKey, mode) : (Enumerable.Range(0, df.NCols).ToArray(), false);
        if (rs && cs) return PdConv.FromCell(df.Data[cp[0]], rp[0]);
        if (rs)
        {
            var cells = cp.Select(c => df.Data[c][rp[0]]).ToList();
            var col = Column.Infer(cells);
            return PdConv.Wrap(new Series(col, df.Columns.Take(cp), df.Index.Labels[rp[0]]));
        }
        if (cs)
        {
            var rix = df.Index.Take(rp);
            return PdConv.Wrap(new Series(df.Data[cp[0]].Take(rp), rowDepth > 0 ? rix.DropLevels(rowDepth) : rix, df.Columns.Labels[cp[0]]));
        }
        var res = df.TakeRows(rp).TakeColumns(cp);
        return PdConv.Wrap(rowDepth > 0 ? res.WithIndex(res.Index.DropLevels(rowDepth)) : res);
    }

    public static void FrameLocSet(DataFrame df, object key, object value, Mode mode)
    {
        object rowKey = key, colKey = null!;
        bool hasCols = false;
        if (key is PyTuple t)
        {
            if (t.Items.Length != 2) throw PyErr.IndexError("Too many indexers");
            rowKey = t.Items[0]; colKey = t.Items[1]; hasCols = true;
        }
        // a new column label creates the column (filled with NaN) before assigning into it
        if (hasCols && mode is Mode.Loc or Mode.At && !PdConv.IsListLike(colKey) && colKey is not PySlice && !df.HasColumn(PdConv.ToCell(colKey)))
            df.SetColumn(PdConv.ToCell(colKey), Column.FromDoubles(Enumerable.Repeat(double.NaN, df.NRows).ToArray()));
        int[] rp; bool rs;
        try { (rp, rs) = RowAxis(df, rowKey, mode); }
        catch (PyRaise ex) when (mode is Mode.Loc or Mode.At && ex.Value.Class == PyErr.KeyErrorClass && !hasCols && !PdConv.IsListLike(rowKey) && rowKey is not PySlice)
        {
            AppendRow(df, PdConv.ToCell(rowKey), value);
            return;
        }
        var (cp, cs) = hasCols ? ColAxis(df, colKey, mode) : (Enumerable.Range(0, df.NCols).ToArray(), false);

        if (cp.Length == 1)
        {
            var cells = ValueCells(value, df.Index, rp, mode);
            df.Data[cp[0]] = df.Data[cp[0]].WithValues(rp, cells);
            return;
        }
        // several columns: a scalar broadcasts; a list-like must line up with the columns (one row) or be a DataFrame/rows x cols nested list
        if (!PdConv.IsListLike(value))
        {
            var cell = new List<object?> { PdConv.ToCell(value) };
            foreach (var c in cp) df.Data[c] = df.Data[c].WithValues(rp, cell);
            return;
        }
        if (value is PyInstance { Native: DataFrame vd })
        {
            for (int j = 0; j < cp.Length; j++)
            {
                var src = vd.Columns.Locs(df.Columns.Labels[cp[j]]);
                if (src.Count == 0) throw PyErr.ValueError("Cannot set with DataFrame lacking a column");
                var srcCol = vd.Data[src[0]];
                var cells = new List<object?>();
                foreach (var p in rp)
                {
                    var l = vd.Index.Locs(df.Index.Labels[p]);
                    cells.Add(l.Count == 0 ? double.NaN : srcCol[l[0]]);
                }
                df.Data[cp[j]] = df.Data[cp[j]].WithValues(rp, cells);
            }
            return;
        }
        var flat = PdConv.Cells(value);
        if (rp.Length == 1 && flat.Count == cp.Length)
        {
            for (int j = 0; j < cp.Length; j++) df.Data[cp[j]] = df.Data[cp[j]].WithValues(rp, new List<object?> { flat[j] });
            return;
        }
        if (flat.Count == rp.Length && flat.All(PdConv.IsNested))
        {
            // nested list: one inner list per selected row
            for (int j = 0; j < cp.Length; j++)
                df.Data[cp[j]] = df.Data[cp[j]].WithValues(rp, flat.Select(r => PdConv.Cells(r!)[j]).ToList());
            return;
        }
        throw PyErr.ValueError($"Must have equal len keys and value when setting with an iterable");
    }

    private static void AppendRow(DataFrame df, object? label, object value)
    {
        var cells = PdConv.IsListLike(value) ? PdConv.Cells(value) : Enumerable.Repeat(PdConv.ToCell(value), df.NCols).ToList();
        if (value is PyInstance { Native: Series vs })
            cells = df.Columns.Items().Select(c => { var l = vs.Index.Locs(c); return l.Count == 0 ? double.NaN : vs.Values[l[0]]; }).Cast<object?>().ToList();
        if (cells.Count != df.NCols) throw PyErr.ValueError($"cannot set a row with mismatched columns");
        for (int c = 0; c < df.NCols; c++)
            df.Data[c] = Column.Infer(df.Data[c].ToObjects().Append(cells[c]).ToList()) is var inferred ? PdBuildExt.Keep(df.Data[c], inferred) : df.Data[c];
        df.Index = new FIndex(Column.Infer(df.Index.Items().Append(label).ToList()), df.Index.Name);
    }
}

internal static class PdBuildExt
{
    /// <summary>After appending a value keep the column's dtype family when the inferred one agrees (int64 stays int64 for int values).</summary>
    public static Column Keep(Column old, Column inferred) => inferred;
}
