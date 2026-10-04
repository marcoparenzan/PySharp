// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>The partition of a table's rows by one or more key columns (what <c>groupby</c> computes first).</summary>
public sealed class Grouping
{
    public IReadOnlyList<Column> KeyColumns { get; }
    public IReadOnlyList<object?> KeyNames { get; }
    /// <summary>For each group, the source rows in their original order.</summary>
    public List<int[]> Rows { get; } = new();
    /// <summary>For each group, the key values (one per key column).</summary>
    public List<object?[]> Keys { get; } = new();
    /// <summary>Group number of every source row, or -1 for rows dropped because a key is missing.</summary>
    public int[] GroupOfRow { get; }
    public int NRows { get; }

    public int Count => Rows.Count;

    public Grouping(IReadOnlyList<Column> keyColumns, IReadOnlyList<object?> keyNames, int nRows, bool sort = true, bool dropNa = true)
    {
        KeyColumns = keyColumns; KeyNames = keyNames; NRows = nRows;
        GroupOfRow = new int[nRows];
        var ids = new Dictionary<object, int>();
        var rows = new List<List<int>>();
        var first = new List<int>();
        for (int i = 0; i < nRows; i++)
        {
            if (dropNa && keyColumns.Any(c => c.IsNa(i))) { GroupOfRow[i] = -1; continue; }
            object k = keyColumns.Count == 1 ? (Column.Key(keyColumns[0][i]) ?? Column.NaNKey) : new LabelTuple(keyColumns.Select(c => c[i]).ToArray());
            if (!ids.TryGetValue(k, out int g)) { g = rows.Count; ids[k] = g; rows.Add(new List<int>()); first.Add(i); }
            rows[g].Add(i);
            GroupOfRow[i] = g;
        }
        var order = Enumerable.Range(0, rows.Count).ToArray();
        if (sort && rows.Count > 1)
        {
            var firstKeys = keyColumns.Select(c => c.Take(first)).ToList();
            order = FrameOps.SortPositions(firstKeys, Enumerable.Repeat(true, firstKeys.Count).ToList(), true);
        }
        var remap = new int[rows.Count];
        for (int n = 0; n < order.Length; n++)
        {
            int g = order[n];
            remap[g] = n;
            Rows.Add(rows[g].ToArray());
            Keys.Add(keyColumns.Select(c => c[first[g]]).ToArray());
        }
        for (int i = 0; i < nRows; i++) if (GroupOfRow[i] >= 0) GroupOfRow[i] = remap[GroupOfRow[i]];
    }

    /// <summary>The group keys as the index of an aggregated result (flat for one key, a MultiIndex for several).</summary>
    public Index ResultIndex()
    {
        var firstRows = Rows.Select(r => r[0]).ToList();
        if (KeyColumns.Count == 1) return new Index(KeyColumns[0].Take(firstRows), KeyNames[0]);
        return Index.Multi(KeyColumns.Select(c => c.Take(firstRows)).ToList(), KeyNames);
    }

    /// <summary>One key (or a tuple of keys) as the label a Python caller sees.</summary>
    public object? KeyLabel(int group) => KeyColumns.Count == 1 ? Keys[group][0] : new LabelTuple(Keys[group]);
}
