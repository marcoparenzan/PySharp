// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>A labelled two-dimensional table (<c>pandas.DataFrame</c>): an ordered list of immutable columns sharing one row <see cref="Index"/>.</summary>
public sealed class DataFrame
{
    public List<Column> Data { get; }
    public Index Columns { get; set; }
    public Index Index { get; set; }

    public DataFrame(IEnumerable<Column> data, Index columns, Index? index = null)
    {
        Data = data.ToList();
        Columns = columns;
        int n = Data.Count > 0 ? Data[0].Length : (index?.Length ?? 0);
        Index = index ?? Index.Range(n);
        if (Columns.Length != Data.Count) throw new FrameException("Shape of passed values is incompatible with columns");
        foreach (var c in Data) if (c.Length != Index.Length) throw new FrameException($"All columns must have the same length ({c.Length} vs {Index.Length})");
    }

    public static DataFrame Empty() => new(Array.Empty<Column>(), new Index(Column.FromObjects(Array.Empty<object?>())), Index.Range(0));

    public int NRows => Index.Length;
    public int NCols => Data.Count;

    public Series GetColumn(int pos) => new(Data[pos], Index, Columns.Labels[pos]);

    public IReadOnlyList<int> ColumnPositions(object? label)
    {
        var l = Columns.Locs(label);
        if (l.Count == 0) throw new KeyNotFoundException(Index.KeyText(label));
        return l;
    }

    public Series GetColumn(object? label) => GetColumn(ColumnPositions(label)[0]);

    public bool HasColumn(object? label) => Columns.Contains(label);

    /// <summary>Sets (or adds) a column. A <see cref="Series"/> value is aligned on the row index first.</summary>
    public void SetColumn(object? label, Column values)
    {
        if (values.Length != NRows) throw new FrameException($"Length of values ({values.Length}) does not match length of index ({NRows})");
        var l = Columns.Locs(label);
        if (l.Count > 0) { foreach (var p in l) Data[p] = values; return; }
        Data.Add(values);
        Columns = new Index(Column.Infer(Columns.Items().Append(label).ToArray()), Columns.Name);
    }

    public void InsertColumn(int loc, object? label, Column values)
    {
        if (Columns.Contains(label)) throw new FrameException($"cannot insert {Index.KeyText(label)}, already exists");
        var labels = Columns.Items().ToList();
        labels.Insert(loc, label);
        Data.Insert(loc, values);
        Columns = new Index(Column.Infer(labels), Columns.Name);
    }

    public DataFrame TakeRows(IReadOnlyList<int> pos) => new(Data.Select(c => c.Take(pos)), Columns, Index.Take(pos));
    public DataFrame TakeColumns(IReadOnlyList<int> pos) => new(pos.Select(p => Data[p]), Columns.Take(pos), Index);

    public DataFrame Head(int n = 5) => TakeRows(FrameUtil.HeadPositions(NRows, n));
    public DataFrame Tail(int n = 5) => TakeRows(FrameUtil.TailPositions(NRows, n));
    public DataFrame Copy() => new(Data, Columns, Index);
    public DataFrame WithIndex(Index index) => new(Data, Columns, index);
    public DataFrame WithColumns(Index columns) => new(Data, columns, Index);

    public DataFrame WhereRows(IReadOnlyList<bool> mask)
    {
        if (mask.Count != NRows) throw new FrameException($"Item wrong length {mask.Count} instead of {NRows}.");
        var pos = new List<int>();
        for (int i = 0; i < mask.Count; i++) if (mask[i]) pos.Add(i);
        return TakeRows(pos);
    }

    public DataFrame Drop(IEnumerable<object?> labels, bool columns)
    {
        if (columns)
        {
            var drop = new HashSet<int>();
            foreach (var l in labels) foreach (var p in ColumnPositions(l)) drop.Add(p);
            return TakeColumns(Enumerable.Range(0, NCols).Where(i => !drop.Contains(i)).ToArray());
        }
        var dropRows = new HashSet<int>();
        foreach (var l in labels)
        {
            var locs = Index.Locs(l);
            if (locs.Count == 0) throw new KeyNotFoundException($"[{Index.KeyText(l)}] not found in axis");
            foreach (var p in locs) dropRows.Add(p);
        }
        return TakeRows(Enumerable.Range(0, NRows).Where(i => !dropRows.Contains(i)).ToArray());
    }

    public object?[] RowValues(int pos) => Data.Select(c => c[pos]).ToArray();
}
