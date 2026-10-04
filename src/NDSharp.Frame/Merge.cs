// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>Database-style joins and concatenation.</summary>
public static class Merge
{
    public sealed class Spec
    {
        public IReadOnlyList<object?> LeftOn { get; init; } = Array.Empty<object?>();
        public IReadOnlyList<object?> RightOn { get; init; } = Array.Empty<object?>();
        public bool LeftIndex { get; init; }
        public bool RightIndex { get; init; }
        public string How { get; init; } = "inner";
        public string SuffixLeft { get; init; } = "_x";
        public string SuffixRight { get; init; } = "_y";
        public bool Sort { get; init; }
        public bool Indicator { get; init; }
        /// <summary>join(): keep every left and right column, no key de-duplication.</summary>
        public bool KeepAllColumns { get; init; }
    }

    private static List<Column> KeyColumns(DataFrame d, IReadOnlyList<object?> labels, bool useIndex)
    {
        if (useIndex) return Enumerable.Range(0, d.Index.NLevels).Select(d.Index.Level).ToList();
        return labels.Select(l => d.Data[d.ColumnPositions(l)[0]]).ToList();
    }

    private static object RowKey(IReadOnlyList<Column> cols, int i)
        => cols.Count == 1 ? (Column.Key(cols[0][i]) ?? Column.NaNKey) : new LabelTuple(cols.Select(c => c[i]).ToArray());

    public static DataFrame Join(DataFrame left, DataFrame right, Spec spec)
    {
        string how = spec.How;
        List<(int l, int r)> pairs = new();
        var lk = how == "cross" ? new List<Column>() : KeyColumns(left, spec.LeftOn, spec.LeftIndex);
        var rk = how == "cross" ? new List<Column>() : KeyColumns(right, spec.RightOn, spec.RightIndex);
        if (how != "cross" && lk.Count != rk.Count) throw new FrameException("len(right_on) must equal len(left_on)");

        if (how == "cross")
        {
            for (int i = 0; i < left.NRows; i++) for (int j = 0; j < right.NRows; j++) pairs.Add((i, j));
        }
        else
        {
            var rightMap = new Dictionary<object, List<int>>();
            for (int j = 0; j < right.NRows; j++)
            {
                var k = RowKey(rk, j);
                if (!rightMap.TryGetValue(k, out var l)) rightMap[k] = l = new List<int>();
                l.Add(j);
            }
            var leftMap = new Dictionary<object, List<int>>();
            for (int i = 0; i < left.NRows; i++)
            {
                var k = RowKey(lk, i);
                if (!leftMap.TryGetValue(k, out var l)) leftMap[k] = l = new List<int>();
                l.Add(i);
            }
            if (how == "right")
            {
                for (int j = 0; j < right.NRows; j++)
                {
                    if (leftMap.TryGetValue(RowKey(rk, j), out var ls)) foreach (var i in ls) pairs.Add((i, j));
                    else pairs.Add((-1, j));
                }
            }
            else
            {
                var matchedRight = new HashSet<int>();
                for (int i = 0; i < left.NRows; i++)
                {
                    if (rightMap.TryGetValue(RowKey(lk, i), out var rs)) foreach (var j in rs) { pairs.Add((i, j)); matchedRight.Add(j); }
                    else if (how is "left" or "outer") pairs.Add((i, -1));
                }
                if (how == "outer")
                    for (int j = 0; j < right.NRows; j++) if (!matchedRight.Contains(j)) pairs.Add((-1, j));
            }
            if (spec.Sort || how == "outer")
            {
                // keys of the result, left value first and right value for right-only rows
                var keyCols = Enumerable.Range(0, lk.Count).Select(c =>
                    Column.Infer(pairs.Select(p => p.l >= 0 ? lk[c][p.l] : rk[c][p.r]).ToList())).ToList();
                var order = FrameOps.SortPositions(keyCols, Enumerable.Repeat(true, keyCols.Count).ToList(), true);
                pairs = order.Select(o => pairs[o]).ToList();
            }
        }

        var lpos = pairs.Select(p => p.l).ToArray();
        var rpos = pairs.Select(p => p.r).ToArray();
        int n = pairs.Count;

        // which right columns are the (shared) join keys and disappear from the output
        var dropRight = new HashSet<int>();
        var coalesce = new Dictionary<int, int>(); // left column position -> right column position
        if (!spec.KeepAllColumns && how != "cross" && !spec.LeftIndex && !spec.RightIndex)
        {
            for (int c = 0; c < spec.LeftOn.Count; c++)
            {
                if (Equals(Column.Key(spec.LeftOn[c]), Column.Key(spec.RightOn[c])))
                {
                    int lp = left.ColumnPositions(spec.LeftOn[c])[0], rp = right.ColumnPositions(spec.RightOn[c])[0];
                    dropRight.Add(rp); coalesce[lp] = rp;
                }
            }
        }
        var leftNames = left.Columns.Items().ToList();
        var rightNames = right.Columns.Items().Select((l, j) => (l, j)).Where(x => !dropRight.Contains(x.j)).ToList();
        var leftSet = new HashSet<object>(leftNames.Select(l => Column.Key(l) ?? Column.NaNKey));
        var rightSet = new HashSet<object>(rightNames.Select(x => Column.Key(x.l) ?? Column.NaNKey));
        var outCols = new List<Column>();
        var outNames = new List<object?>();
        for (int c = 0; c < left.NCols; c++)
        {
            var col = left.Data[c].Take(lpos);
            if (coalesce.TryGetValue(c, out var rp) && pairs.Any(p => p.l < 0))
            {
                var cells = Enumerable.Range(0, n).Select(i => lpos[i] >= 0 ? left.Data[c][lpos[i]] : right.Data[rp][rpos[i]]).ToList();
                col = Column.Infer(cells);
                if (left.Data[c].Kind == Kind.Str && right.Data[rp].Kind == Kind.Str) col = Column.FromStrings(cells.Select(x => x as string).ToArray());
            }
            outCols.Add(col);
            bool overlap = rightSet.Contains(Column.Key(leftNames[c]) ?? Column.NaNKey);
            outNames.Add(overlap ? Suffix(leftNames[c], spec.SuffixLeft, spec.KeepAllColumns) : leftNames[c]);
        }
        foreach (var (l, j) in rightNames)
        {
            outCols.Add(right.Data[j].Take(rpos));
            bool overlap = leftSet.Contains(Column.Key(l) ?? Column.NaNKey);
            outNames.Add(overlap ? Suffix(l, spec.SuffixRight, spec.KeepAllColumns) : l);
        }
        if (spec.Indicator)
        {
            outCols.Add(Column.FromStrings(pairs.Select(p => p.l >= 0 && p.r >= 0 ? "both" : p.l >= 0 ? "left_only" : "right_only").ToArray()));
            outNames.Add("_merge");
        }
        Index index;
        if (spec.LeftIndex && spec.RightIndex)
            index = new Index(Column.Infer(pairs.Select(p => p.l >= 0 ? left.Index.Labels[p.l] : right.Index.Labels[p.r]).ToList()), left.Index.Name);
        else if (spec.RightIndex && !spec.LeftIndex) index = left.Index.Take(lpos);
        else if (spec.LeftIndex && !spec.RightIndex) index = right.Index.Take(rpos);
        else index = Index.Range(n);
        return new DataFrame(outCols, new Index(Column.Infer(outNames)), index);
    }

    private static object? Suffix(object? name, string suffix, bool join)
    {
        if (suffix is null or "")
            throw new FrameException($"columns overlap but no suffix specified: Index(['{name}'], dtype='str')");
        return Convert.ToString(name) + suffix;
    }

    // ------------------------------------------------------------------------------------------ concat

    private static Column MissingLike(Column c, int n) => c.Take(Enumerable.Repeat(-1, n).ToArray());

    public static DataFrame ConcatRows(IReadOnlyList<DataFrame> frames, bool innerJoin, bool ignoreIndex, IReadOnlyList<object?>? keys = null, IReadOnlyList<object?>? names = null)
    {
        // columns: union in order of first appearance (or the intersection for join='inner')
        var labels = new List<object?>();
        var seen = new HashSet<object>();
        foreach (var f in frames)
            foreach (var l in f.Columns.Items())
                if (seen.Add(Column.Key(l) ?? Column.NaNKey)) labels.Add(l);
        if (innerJoin) labels = labels.Where(l => frames.All(f => f.HasColumn(l))).ToList();
        var cols = new List<Column>();
        foreach (var l in labels)
        {
            var parts = new List<Column>();
            Column? proto = frames.Select(f => f.HasColumn(l) ? f.Data[f.ColumnPositions(l)[0]] : null).FirstOrDefault(c => c is not null);
            foreach (var f in frames)
                parts.Add(f.HasColumn(l) ? f.Data[f.ColumnPositions(l)[0]] : MissingLike(proto!, f.NRows));
            cols.Add(Column.Concat(parts));
        }
        Index index;
        if (ignoreIndex) index = Index.Range(frames.Sum(f => f.NRows));
        else if (keys is not null)
        {
            var inner = frames.Select(f => f.Index).Aggregate((x, y) => x.Concat(y));
            var outer = Column.Infer(frames.SelectMany((f, g) => Enumerable.Repeat(keys[g], f.NRows)).ToList());
            index = Index.Multi(new[] { outer }.Concat(Enumerable.Range(0, inner.NLevels).Select(inner.Level)).ToList(), (names ?? new object?[] { null }).Concat(inner.Names).Take(1 + inner.NLevels).ToList());
        }
        else index = frames.Select(f => f.Index).Aggregate((x, y) => x.Concat(y));
        var colIndex = new Index(Column.Infer(labels), frames.All(f => Equals(f.Columns.Name, frames[0].Columns.Name)) ? frames[0].Columns.Name : null);
        return new DataFrame(cols, colIndex, index);
    }

    public static DataFrame ConcatColumns(IReadOnlyList<(Index index, IReadOnlyList<Column> cols, IReadOnlyList<object?> names)> parts, bool innerJoin, bool ignoreIndex)
    {
        Index index = parts[0].index;
        if (!parts.All(p => Ops.SameLabels(p.index, index)))
        {
            var all = new List<object?>();
            var seen = new HashSet<object>();
            foreach (var p in parts) foreach (var l in p.index.Items()) if (seen.Add(Column.Key(l) ?? Column.NaNKey)) all.Add(l);
            if (innerJoin) all = all.Where(l => parts.All(p => p.index.Contains(l))).ToList();
            index = new Index(Column.Infer(all), parts.All(p => Equals(p.index.Name, parts[0].index.Name)) ? parts[0].index.Name : null);
        }
        var cols = new List<Column>(); var names = new List<object?>();
        foreach (var p in parts)
        {
            var take = Ops.SameLabels(p.index, index) ? null : Ops.Reindexer(p.index, index);
            foreach (var c in p.cols) cols.Add(take is null ? c : c.Take(take));
            names.AddRange(p.names);
        }
        return new DataFrame(cols, ignoreIndex ? Index.Range(cols.Count) : new Index(Column.Infer(names)), index);
    }
}
