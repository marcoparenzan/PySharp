// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>stack / unstack / melt.</summary>
public static class Reshape
{
    private static int[] SortedUnique(IReadOnlyList<Column> keys, int n, out List<int> firstRows)
    {
        var seen = new Dictionary<object, int>();
        firstRows = new List<int>();
        for (int i = 0; i < n; i++)
        {
            object k = keys.Count == 1 ? (Column.Key(keys[0][i]) ?? Column.NaNKey) : new LabelTuple(keys.Select(c => c[i]).ToArray());
            if (!seen.ContainsKey(k)) { seen[k] = firstRows.Count; firstRows.Add(i); }
        }
        var fr = firstRows;
        var firstKeys = keys.Select(c => c.Take(fr)).ToList();
        return FrameOps.SortPositions(firstKeys, Enumerable.Repeat(true, firstKeys.Count).ToList(), true);
    }

    private static object KeyOf(IReadOnlyList<Column> keys, int i)
        => keys.Count == 1 ? (Column.Key(keys[0][i]) ?? Column.NaNKey) : new LabelTuple(keys.Select(c => c[i]).ToArray());

    /// <summary>Moves one level of a MultiIndex into the columns. <paramref name="fromSeries"/> gives flat columns named after the level.</summary>
    public static DataFrame Unstack(DataFrame d, int level, bool fromSeries, object? fill = null)
    {
        var ix = d.Index;
        if (!ix.IsMulti) throw new FrameException("index must be a MultiIndex to unstack, <class 'pandas.RangeIndex'> was passed");
        int nl = ix.NLevels;
        if (level < 0) level += nl;
        var keepLevels = Enumerable.Range(0, nl).Where(k => k != level).ToList();
        var rowKeys = keepLevels.Select(ix.Level).ToList();
        var lvlCol = ix.Level(level);
        var rowOrder = SortedUnique(rowKeys, d.NRows, out var rowFirst);
        var colOrder = SortedUnique(new[] { lvlCol }, d.NRows, out var colFirst);
        var rowPos = new Dictionary<object, int>();
        for (int r = 0; r < rowOrder.Length; r++) rowPos[KeyOf(rowKeys.Select(c => c.Take(rowFirst)).ToList(), rowOrder[r])] = r;
        var colPos = new Dictionary<object, int>();
        var lvlFirst = lvlCol.Take(colFirst);
        for (int c = 0; c < colOrder.Length; c++) colPos[Column.Key(lvlFirst[colOrder[c]]) ?? Column.NaNKey] = c;
        int nR = rowOrder.Length, nC = colOrder.Length;
        var cellRow = new int[d.NRows]; var cellCol = new int[d.NRows];
        for (int i = 0; i < d.NRows; i++) { cellRow[i] = rowPos[KeyOf(rowKeys, i)]; cellCol[i] = colPos[Column.Key(lvlCol[i]) ?? Column.NaNKey]; }

        var cols = new List<Column>();
        var labels = new List<object?>();
        for (int j = 0; j < d.NCols; j++)
            for (int c = 0; c < nC; c++)
            {
                var cells = new object?[nR];
                for (int i = 0; i < d.NRows; i++) if (cellCol[i] == c) cells[cellRow[i]] = d.Data[j][i];
                var present = new bool[nR];
                for (int i = 0; i < d.NRows; i++) if (cellCol[i] == c) present[cellRow[i]] = true;
                var take = Enumerable.Range(0, nR).Select(r => present[r] ? -1 : -2).ToArray();
                // rebuild through Take so the dtype family (int -> float when a cell is missing) follows reindexing rules
                var srcRows = new int[nR];
                Array.Fill(srcRows, -1);
                for (int i = 0; i < d.NRows; i++) if (cellCol[i] == c) srcRows[cellRow[i]] = i;
                if (fill is not null && d.NRows > 0)
                {
                    var missingPos = Enumerable.Range(0, nR).Where(r => srcRows[r] < 0).ToArray();
                    var safe = srcRows.Select(x => x < 0 ? 0 : x).ToArray();
                    var taken = d.Data[j].Take(safe);
                    cols.Add(missingPos.Length == 0 ? taken : taken.WithValues(missingPos, new[] { fill }));
                }
                else cols.Add(d.Data[j].Take(srcRows));
                object? colLevel = lvlFirst[colOrder[c]];
                labels.Add(fromSeries ? colLevel : new LabelTuple(new[] { d.Columns.Labels[j], colLevel }));
            }
        var firstOfRow = rowOrder.Select(o => rowFirst[o]).ToList();
        Index newIndex = keepLevels.Count == 1
            ? new Index(rowKeys[0].Take(firstOfRow), ix.Names[keepLevels[0]])
            : Index.Multi(rowKeys.Select(c => c.Take(firstOfRow)).ToList(), keepLevels.Select(k => ix.Names[k]).ToList());
        Index newCols = fromSeries
            ? new Index(Column.Infer(labels), ix.Names[level])
            : new Index(Column.Infer(labels), null).WithNames(new[] { d.Columns.Name, ix.Names[level] });
        if (!fromSeries && d.Columns.IsMulti) throw new FrameException("unstack with MultiIndex columns is not supported", "NotImplementedError");
        return new DataFrame(cols, newCols, newIndex);
    }

    /// <summary>Moves the (flat) columns into the innermost row level, dropping missing cells like <c>stack()</c> does.</summary>
    public static Series Stack(DataFrame d, bool dropNa = true)
    {
        if (d.Columns.IsMulti) throw new FrameException("stack with MultiIndex columns is not supported", "NotImplementedError");
        var cells = new List<object?>();
        var rowLevels = Enumerable.Range(0, d.Index.NLevels).Select(k => new List<object?>()).ToList();
        var colLevel = new List<object?>();
        for (int i = 0; i < d.NRows; i++)
            for (int j = 0; j < d.NCols; j++)
            {
                if (dropNa && d.Data[j].IsNa(i)) continue;
                cells.Add(d.Data[j][i]);
                for (int k = 0; k < rowLevels.Count; k++) rowLevels[k].Add(d.Index.IsMulti ? ((LabelTuple)d.Index.Labels[i]!).Parts[k] : d.Index.Labels[i]);
                colLevel.Add(d.Columns.Labels[j]);
            }
        var levels = rowLevels.Select(l => Column.Infer(l)).Append(Column.Infer(colLevel)).ToList();
        var names = d.Index.Names.Append(d.Columns.Name).ToList();
        return new Series(Column.Infer(cells), Index.Multi(levels, names), null);
    }

    public static DataFrame Melt(DataFrame d, IReadOnlyList<object?> idVars, IReadOnlyList<object?> valueVars, object? varName, object? valueName)
    {
        var idPos = idVars.Select(l => d.ColumnPositions(l)[0]).ToList();
        var valPos = valueVars.Select(l => d.ColumnPositions(l)[0]).ToList();
        int n = d.NRows, m = valPos.Count;
        var cols = new List<Column>();
        var labels = new List<object?>();
        foreach (var p in idPos)
        {
            cols.Add(Column.Concat(Enumerable.Repeat(d.Data[p], Math.Max(m, 1)).ToList()));
            labels.Add(d.Columns.Labels[p]);
        }
        var variable = Column.Infer(valPos.SelectMany(p => Enumerable.Repeat(d.Columns.Labels[p], n)).ToList());
        cols.Add(variable); labels.Add(varName ?? d.Columns.Name ?? "variable");
        var value = Column.Concat(valPos.Select(p => d.Data[p]).ToList());
        cols.Add(value); labels.Add(valueName ?? "value");
        return new DataFrame(cols, new Index(Column.Infer(labels)), Index.Range(n * m));
    }
}
