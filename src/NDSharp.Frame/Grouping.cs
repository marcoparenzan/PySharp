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

    /// <summary>Frequency of a resample/Grouper key, carried to the result index.</summary>
    public string? Freq { get; set; }

    public Grouping(IReadOnlyList<Column> keyColumns, IReadOnlyList<object?> keyNames, int nRows, bool sort = true, bool dropNa = true, bool observed = true, IReadOnlyList<object?[]>? forcedKeys = null)
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
        var keyValues = first.Select(f => keyColumns.Select(c => c[f]).ToArray()).ToList();
        if (!observed && keyColumns.Any(c => c.Kind == Kind.Category))
        {
            // unobserved categories form (empty) groups too: the product of every key's values (all categories for categorical keys)
            var perKey = keyColumns.Select((c, k) => c.Kind == Kind.Category
                ? Enumerable.Range(0, c.Categories.Length).Select(i => c.Categories[i]).ToList()
                : keyValues.Select(v => v[k]).Distinct().ToList()).ToList();
            var combos = new List<object?[]> { Array.Empty<object?>() };
            foreach (var vals in perKey) combos = combos.SelectMany(prefix => vals.Select(v => prefix.Append(v).ToArray())).ToList();
            foreach (var combo in combos)
            {
                object k = keyColumns.Count == 1 ? (Column.Key(combo[0]) ?? Column.NaNKey) : new LabelTuple(combo);
                if (!ids.ContainsKey(k)) { ids[k] = rows.Count; rows.Add(new List<int>()); first.Add(-1); keyValues.Add(combo); }
            }
        }
        if (forcedKeys is not null)
            foreach (var combo in forcedKeys)
            {
                object k = keyColumns.Count == 1 ? (Column.Key(combo[0]) ?? Column.NaNKey) : new LabelTuple(combo);
                if (!ids.ContainsKey(k)) { ids[k] = rows.Count; rows.Add(new List<int>()); first.Add(-1); keyValues.Add(combo); }
            }
        var order = Enumerable.Range(0, rows.Count).ToArray();
        if (sort && rows.Count > 1)
        {
            var sortKeys = first.All(f => f >= 0) ? keyColumns.Select(c => c.Take(first)).ToList() : Enumerable.Range(0, keyColumns.Count).Select(k => KeyColumnFor(keyColumns[k], keyValues.Select(v => v[k]).ToList())).ToList();
            order = FrameOps.SortPositions(sortKeys, Enumerable.Repeat(true, sortKeys.Count).ToList(), true);
        }
        var remap = new int[rows.Count];
        for (int n = 0; n < order.Length; n++)
        {
            int g = order[n];
            remap[g] = n;
            Rows.Add(rows[g].ToArray());
            Keys.Add(keyValues[g]);
            FirstRows.Add(first[g]);
        }
        for (int i = 0; i < nRows; i++) if (GroupOfRow[i] >= 0) GroupOfRow[i] = remap[GroupOfRow[i]];
    }

    /// <summary>A column of the given key values with the dtype of the source key column (categorical keys stay categorical).</summary>
    public static Column KeyColumnFor(Column source, IReadOnlyList<object?> values)
    {
        if (source.Kind == Kind.Category)
        {
            var lookup = new Dictionary<object, int>();
            for (int i = 0; i < source.Categories.Length; i++) lookup[Column.Key(source.Categories[i]) ?? Column.NaNKey] = i;
            return Column.FromCodes(values.Select(v => v is null ? -1 : lookup.TryGetValue(Column.Key(v)!, out var c) ? c : -1).ToArray(), source.Categories, source.Ordered);
        }
        if (source.Kind == Kind.Period) return Column.FromPeriod(values.Select(v => v is Per p ? p.Ordinal : DateTimeCore.NaT).ToArray(), source.PFreq);
        if (source.Kind == Kind.DateTime) return Column.FromDateTime(values.Select(v => v is Ts t ? DateTimeCore.Scale(t.Ticks, t.Unit, source.Unit) : DateTimeCore.NaT).ToArray(), source.Unit, source.Tz);
        if (source.Kind == Kind.Timedelta) return Column.FromTimedelta(values.Select(v => v is Td t ? DateTimeCore.Scale(t.Ticks, t.Unit, source.Unit) : DateTimeCore.NaT).ToArray(), source.Unit);
        var inferred = Column.Infer(values);
        return source.Kind == Kind.Str && inferred.Kind != Kind.Str ? Column.FromStrings(values.Select(v => v as string).ToArray()) : inferred;
    }

    /// <summary>The key columns of the groups (one value per group), in group order.</summary>
    public List<Column> ResultKeyColumns() => FirstRows.All(f => f >= 0)
        ? KeyColumns.Select(c => c.Take(FirstRows)).ToList()
        : Enumerable.Range(0, KeyColumns.Count).Select(k => KeyColumnFor(KeyColumns[k], Keys.Select(v => v[k]).ToList())).ToList();

    /// <summary>For each group, its first source row (-1 for a group without rows).</summary>
    public List<int> FirstRows { get; } = new();

    /// <summary>The group keys as the index of an aggregated result (flat for one key, a MultiIndex for several).</summary>
    public Index ResultIndex()
    {
        var keyCols = ResultKeyColumns();
        if (KeyColumns.Count == 1) return new Index(keyCols[0], KeyNames[0]) { Freq = Freq };
        return Index.Multi(keyCols, KeyNames);
    }

    /// <summary>One key (or a tuple of keys) as the label a Python caller sees.</summary>
    public object? KeyLabel(int group) => KeyColumns.Count == 1 ? Keys[group][0] : new LabelTuple(Keys[group]);
}
