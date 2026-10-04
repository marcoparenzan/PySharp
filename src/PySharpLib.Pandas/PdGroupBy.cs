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

/// <summary>The state behind a <c>DataFrameGroupBy</c> / <c>SeriesGroupBy</c> object.</summary>
internal sealed class GroupByState
{
    public required DataFrame Frame { get; init; }
    public required Grouping G { get; init; }
    public required List<int> ValueCols { get; init; }
    public bool AsIndex { get; init; } = true;
    public bool IsSeries { get; init; }
    public bool SingleSelected { get; init; }
    public object? SeriesName { get; init; }
    public bool KeysAreColumns { get; init; }
    public List<int> KeyColPositions { get; init; } = new();
    public bool DropNa { get; init; } = true;
    /// <summary>The original timestamps of a resample (to reindex upsampled results).</summary>
    public Column? TimeSource { get; init; }

    public GroupByState With(List<int> valueCols, bool isSeries) => new()
    {
        Frame = Frame, G = G, ValueCols = valueCols, AsIndex = AsIndex, IsSeries = isSeries, SingleSelected = isSeries, SeriesName = isSeries ? Frame.Columns.Labels[valueCols[0]] : null,
        KeysAreColumns = KeysAreColumns, KeyColPositions = KeyColPositions, DropNa = DropNa, TimeSource = TimeSource,
    };
}

internal static class PdGroupBy
{
    public static readonly PyClass DataFrameGroupBy = new("DataFrameGroupBy", new List<PyClass>());
    public static readonly PyClass SeriesGroupBy = new("SeriesGroupBy", new List<PyClass>());

    private static GroupByState St(object o) => (GroupByState)((PyInstance)o).Native!;
    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);
    private static PyInstance Wrap(GroupByState st) => new(st.IsSeries ? SeriesGroupBy : DataFrameGroupBy) { Native = st };

    // ================================================================== creation

    public static object Create(Interp i, object self, object[] a, Dictionary<string, object>? k)
    {
        var p = new Args("groupby", i, a.Skip(1).ToArray(), k, "by", "axis", "level", "as_index", "sort", "group_keys", "observed", "dropna");
        bool isSeries = self is PyInstance { Native: Series };
        DataFrame frame = isSeries ? SeriesFrame((Series)((PyInstance)self).Native!) : PdConv.D(self);
        FIndex rows = frame.Index;
        var keyCols = new List<Column>();
        var keyNames = new List<object?>();
        var keyPos = new List<int>();
        bool keysAreColumns = false;
        string? grouperFreq = null;
        List<object?[]>? forced = null;
        if (p.Has(2))
        {
            var lv = p[2]!;
            var levels = PdConv.IsListLike(lv) ? PdConv.Cells(lv).Select(x => PdClasses.LevelNumber(rows, PdConv.FromLabel(x))).ToList() : new List<int> { PdClasses.LevelNumber(rows, lv) };
            foreach (var l in levels) { keyCols.Add(rows.Level(l)); keyNames.Add(rows.Names[l]); }
        }
        else
        {
            if (!p.Has(0)) throw PyErr.TypeError("You have to supply one of 'by' and 'level'");
            var by = p[0]!;
            var items = by is PyList bl && !IsValueList(frame, bl) ? bl.Items : new List<object> { by };
            foreach (var item in items)
            {
                if (item is PyInstance { Class: var gcls, Native: PyDict gd } && gcls == PdDates.GrouperClass)
                {
                    var (gcol, gname, glabels, gfreq, gpos) = PdDates.GrouperKey(frame, gd);
                    keyCols.Add(gcol); keyNames.Add(gname);
                    if (gpos >= 0) { keyPos.Add(gpos); keysAreColumns = true; }
                    if (glabels is not null && items.Count == 1) { forced = glabels; grouperFreq = gfreq; }
                    continue;
                }
                if (item is PyInstance { Native: Series ks })
                {
                    keyCols.Add(PdSelect.SameLabels(ks.Index, rows) ? ks.Values : ks.Values.Take(Ops.Reindexer(ks.Index, rows)));
                    keyNames.Add(ks.Name);
                }
                else if (item is PyBuiltinFunction or PyFunction)
                {
                    keyCols.Add(Column.Infer(rows.Items().Select(l => PdConv.ToCell(i.Call(item, new[] { PdConv.FromLabel(l) }))).ToList()));
                    keyNames.Add(null);
                }
                else if (PdConv.IsListLike(item) && !(item is PyTuple && frame.HasColumn(PdConv.ToCell(item))))
                {
                    var col = PdConv.ToColumn(item);
                    if (col.Length != frame.NRows) throw PyErr.ValueError($"Length of grouper ({col.Length}) and axis ({frame.NRows}) must be same length");
                    keyCols.Add(col); keyNames.Add(null);
                }
                else
                {
                    var label = PdConv.ToCell(item);
                    if (!frame.HasColumn(label))
                    {
                        if (frame.Index.Names.Any(n => Equals(n, label)))
                        {
                            int lv = PdClasses.LevelNumber(rows, item);
                            keyCols.Add(rows.Level(lv)); keyNames.Add(label);
                            continue;
                        }
                        throw PdConv.KeyErr(item);
                    }
                    int pos = frame.ColumnPositions(label)[0];
                    keyCols.Add(frame.Data[pos]); keyNames.Add(label); keyPos.Add(pos); keysAreColumns = true;
                }
            }
        }
        var g = new Grouping(keyCols, keyNames, frame.NRows, p.Bool(4, true), p.Bool(7, true), p.Bool(6, true), forced) { Freq = grouperFreq };
        var valueCols = Enumerable.Range(0, frame.NCols).Where(j => !keyPos.Contains(j)).ToList();
        return Wrap(new GroupByState
        {
            Frame = frame, G = g, ValueCols = valueCols, AsIndex = p.Bool(3, true), IsSeries = isSeries, SingleSelected = isSeries,
            SeriesName = isSeries ? ((Series)((PyInstance)self).Native!).Name : null,
            KeysAreColumns = keysAreColumns, KeyColPositions = keyPos, DropNa = p.Bool(7, true),
        });
    }

    /// <summary>A list passed as <c>by</c> is either a list of labels or the group values themselves (one per row).</summary>
    private static bool IsValueList(DataFrame frame, PyList l)
        => l.Items.Count == frame.NRows && !l.Items.All(x => frame.HasColumn(PdConv.ToCell(x))) && !l.Items.All(x => x is PyInstance { Native: Series } || PdConv.IsListLike(x));

    internal static DataFrame SeriesFrame(Series s) => new(new[] { s.Values }, new FIndex(Column.Infer(new[] { s.Name })), s.Index);

    // ================================================================== resample support

    internal static object FromGrouping(object self, DataFrame frame, Grouping g, List<int> keyPos, bool isSeries, Column timeSource)
    {
        var valueCols = Enumerable.Range(0, frame.NCols).Where(j => !keyPos.Contains(j)).ToList();
        return Wrap(new GroupByState
        {
            Frame = frame, G = g, ValueCols = valueCols, AsIndex = true, IsSeries = isSeries, SingleSelected = isSeries,
            SeriesName = isSeries ? ((Series)((PyInstance)self).Native!).Name : null,
            KeysAreColumns = false, KeyColPositions = keyPos, DropNa = true, TimeSource = timeSource,
        });
    }

    internal static object ResampleFill(Interp i, object self, string name, object[] args, Dictionary<string, object>? kw)
    {
        var st = St(self);
        var times = st.TimeSource ?? throw PyErr.NotImplementedError($"{name}() is only available on resample objects");
        var target = st.G.ResultIndex().Labels;
        var unit = DateTimeCore.Finer(times.Unit, target.Unit);
        var src = times.WithUnit(unit).Ticks;
        var tgt = target.WithUnit(unit).Ticks;
        var order = Enumerable.Range(0, src.Length).Where(r => src[r] != DateTimeCore.NaT).OrderBy(r => src[r]).ToArray();
        var pos = new int[tgt.Length];
        for (int n = 0; n < tgt.Length; n++)
        {
            long t = tgt[n];
            int lo = 0, hi = order.Length;                 // first sorted position with src >= t
            while (lo < hi) { int mid = (lo + hi) / 2; if (src[order[mid]] < t) lo = mid + 1; else hi = mid; }
            bool exact = lo < order.Length && src[order[lo]] == t;
            pos[n] = name switch
            {
                "asfreq" => exact ? order[lo] : -1,
                "ffill" or "pad" => exact ? order[lo] : lo > 0 ? order[lo - 1] : -1,
                "bfill" or "backfill" => lo < order.Length ? order[lo] : -1,
                _ => exact ? order[lo] : lo == 0 ? (order.Length > 0 ? order[0] : -1) : lo >= order.Length ? order[^1]
                    : (t - src[order[lo - 1]] <= src[order[lo]] - t ? order[lo - 1] : order[lo]),
            };
        }
        if (st.IsSeries) return AssembleSeries(st, st.Frame.Data[st.ValueCols[0]].Take(pos), st.SeriesName);
        return AssembleFrame(st, st.ValueCols.Select(j => st.Frame.Data[j].Take(pos)).ToList(), ValueLabels(st));
    }

    internal static object Ohlc(Interp i, object self)
    {
        var st = St(self);
        var names = new[] { "open", "high", "low", "close" };
        var aggs = new[] { "first", "max", "min", "last" };
        if (st.IsSeries)
            return AssembleFrame(st, aggs.Select(a => AggColumn(st, st.Frame.Data[st.ValueCols[0]], a)).ToList(), new FIndex(Column.FromStrings(names)));
        var cols = new List<Column>(); var labels = new List<object?>();
        foreach (var j in st.ValueCols)
            for (int x = 0; x < 4; x++) { cols.Add(AggColumn(st, st.Frame.Data[j], aggs[x])); labels.Add(new LabelTuple(new[] { st.Frame.Columns.Labels[j], names[x] })); }
        return AssembleFrame(st, cols, new FIndex(Column.Infer(labels)));
    }

    // ================================================================== helpers

    private static Column InferLike(Column original, List<object?> cells)
    {
        if (original.Nullable && original.Kind != Kind.Str || original.Nullable && cells.All(c => c is string or null or NAValue))
        {
            var mask = cells.Select(c => c is null or NAValue || c is double dn && double.IsNaN(dn) && original.Kind != Kind.Float).ToArray();
            var valid = cells.Where((c, i) => !mask[i]).ToList();
            if (cells.Count == 0) return Column.Empty(original.Kind);
            if (valid.All(c => c is long) && valid.Count > 0 && original.Kind is Kind.Int) return Column.MakeNullable(Column.FromLongs(cells.Select(c => c is long l ? l : 0L).ToArray(), original.Num!.Value), mask);
            if (valid.All(c => c is long or double)) return Column.MakeNullable(Column.FromDoubles(cells.Select(c => c is long l ? l : c is double d ? d : double.NaN).ToArray()), mask);
            if (valid.All(c => c is bool)) return Column.MakeNullable(Column.FromBools(cells.Select(c => c is true).ToArray()), mask);
            if (valid.All(c => c is string)) return Column.MakeNullable(Column.FromStrings(cells.Select(c => c as string).ToArray()));
        }
        if (cells.Count == 0) return Column.Empty(original.Kind == Kind.Category ? Kind.Object : original.Kind);
        if (original.Kind == Kind.Str && cells.All(c => c is string or null || c is double d && double.IsNaN(d)))
            return Column.FromStrings(cells.Select(c => c as string).ToArray());
        if (original.Kind == Kind.Bool && cells.All(c => c is bool)) return Column.FromBools(cells.Select(c => (bool)c!).ToArray());
        return Column.Infer(cells);
    }

    private static object? FirstValid(Column c, int[] rows, bool last)
    {
        var seq = last ? rows.Reverse() : rows;
        foreach (var r in seq) if (!c.IsNa(r)) return c[r];
        return c.Kind == Kind.Str ? null : double.NaN;
    }

    private static Column AggColumn(GroupByState st, Column col, string name, bool skipna = true, int ddof = 1, int minCount = 0)
    {
        var cells = new List<object?>(st.G.Count);
        foreach (var rows in st.G.Rows)
        {
            switch (name)
            {
                case "size": cells.Add((long)rows.Length); break;
                case "first": cells.Add(FirstValid(col, rows, false)); break;
                case "last": cells.Add(FirstValid(col, rows, true)); break;
                default: cells.Add(Reduce.Scalar(name, col.Take(rows), skipna, ddof, minCount)); break;
            }
        }
        return InferLike(col, cells);
    }

    private static Series GroupSeries(GroupByState st, int colPos, int[] rows)
        => new(st.Frame.Data[colPos].Take(rows), st.Frame.Index.Take(rows), st.Frame.Columns.Labels[colPos]);

    private static DataFrame GroupFrame(GroupByState st, int[] rows, bool valueColsOnly)
    {
        var cols = valueColsOnly ? st.ValueCols : Enumerable.Range(0, st.Frame.NCols).ToList();
        return new DataFrame(cols.Select(c => st.Frame.Data[c].Take(rows)), st.Frame.Columns.Take(cols), st.Frame.Index.Take(rows));
    }

    private static object AssembleFrame(GroupByState st, List<Column> cols, FIndex columnLabels)
    {
        if (st.AsIndex) return PdConv.Wrap(new DataFrame(cols, columnLabels, st.G.ResultIndex()));
        var keyCols = st.G.ResultKeyColumns();
        var labels = new List<object?>(st.G.KeyNames.Select((n, j) => n ?? $"level_{j}"));
        labels.AddRange(columnLabels.Items());
        keyCols.AddRange(cols);
        return PdConv.Wrap(new DataFrame(keyCols, new FIndex(Column.Infer(labels)), FIndex.Range(st.G.Count)));
    }

    private static object AssembleSeries(GroupByState st, Column col, object? name)
    {
        if (st.AsIndex) return PdConv.Wrap(new Series(col, st.G.ResultIndex(), name));
        var keyCols = st.G.ResultKeyColumns();
        var labels = new List<object?>(st.G.KeyNames.Select((n, j) => n ?? $"level_{j}")) { name ?? 0L };
        keyCols.Add(col);
        return PdConv.Wrap(new DataFrame(keyCols, new FIndex(Column.Infer(labels)), FIndex.Range(st.G.Count)));
    }

    private static FIndex ValueLabels(GroupByState st) => st.Frame.Columns.Take(st.ValueCols);

    private static string FuncName(object f) => f switch { string s => s, PyFunction pf => pf.Name, PyBuiltinFunction bf => bf.Name, _ => "<lambda>" };

    private static object? CallOnGroup(Interp i, object f, Series group)
        => PdConv.ToCell(i.Call(f, new object[] { PdConv.Wrap(group) }));

    // ================================================================== reductions by name

    private static object Reduction(Interp i, object[] a, Dictionary<string, object>? k, string name)
    {
        var st = St(a[0]);
        var p = A(name, i, a, k, "numeric_only", "min_count", "skipna", "ddof", "engine", "engine_kwargs");
        bool skipna = p.Bool(2, true);
        int ddof = p.Int(3, 1), minCount = p.Int(1, name is "sum" or "prod" ? 0 : 0);
        bool numericOnly = p.Bool(0, false);
        if (name == "size")
        {
            var sizes = Column.FromLongs(st.G.Rows.Select(r => (long)r.Length).ToArray());
            return st.AsIndex ? PdConv.Wrap(new Series(sizes, st.G.ResultIndex())) : AssembleSeries(st, sizes, "size");
        }
        if (st.IsSeries)
        {
            var col = AggColumn(st, st.Frame.Data[st.ValueCols[0]], name, skipna, ddof, minCount);
            return AssembleSeries(st, col, st.SeriesName);
        }
        var use = st.ValueCols.Where(j => !numericOnly || Reduce.IsNumeric(st.Frame.Data[j])).ToList();
        var cols = use.Select(j => AggColumn(st, st.Frame.Data[j], name, skipna, ddof, minCount)).ToList();
        return AssembleFrame(st, cols, st.Frame.Columns.Take(use));
    }

    // ================================================================== aggregate

    internal static Column AggWith(Interp i, GroupByState st, int colPos, object func)
    {
        if (func is string name) return AggColumn(st, st.Frame.Data[colPos], name);
        var cells = st.G.Rows.Select(rows => CallOnGroup(i, func, GroupSeries(st, colPos, rows))).ToList();
        return InferLike(st.Frame.Data[colPos], cells);
    }

    private static object Aggregate(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var st = St(a[0]);
        var args = a.Skip(1).ToArray();
        object? spec = args.Length > 0 ? args[0] : null;
        var named = k?.Where(e => e.Key is not ("engine" or "engine_kwargs")).ToList() ?? new List<KeyValuePair<string, object>>();
        if (st.IsSeries)
        {
            int pos = st.ValueCols[0];
            if (spec is null && named.Count > 0)
            {
                var cols = named.Select(e => AggWith(i, st, pos, e.Value)).ToList();
                return AssembleFrame(st, cols, new FIndex(Column.FromStrings(named.Select(e => (string?)e.Key).ToArray())));
            }
            if (spec is PyList or PyTuple)
            {
                var funcs = spec is PyList l ? l.Items : ((PyTuple)spec).Items.ToList();
                var cols = funcs.Select(f => AggWith(i, st, pos, f)).ToList();
                return AssembleFrame(st, cols, new FIndex(Column.FromStrings(funcs.Select(f => (string?)FuncName(f)).ToArray())));
            }
            return AssembleSeries(st, AggWith(i, st, pos, spec ?? throw PyErr.TypeError("agg() missing func")), st.SeriesName);
        }
        if (spec is null && named.Count > 0)
        {
            var cols = new List<Column>();
            foreach (var e in named)
            {
                if (e.Value is not PyTuple { Items.Length: 2 } t) throw PyErr.TypeError("Must provide 'func' or tuples of '(column, aggfunc)'.");
                int pos = st.Frame.ColumnPositions(PdConv.ToCell(t.Items[0]))[0];
                cols.Add(AggWith(i, st, pos, t.Items[1]));
            }
            return AssembleFrame(st, cols, new FIndex(Column.FromStrings(named.Select(e => (string?)e.Key).ToArray())));
        }
        if (spec is PyDict d)
        {
            bool anyList = d.Values.Any(v => v is PyList or PyTuple);
            var cols = new List<Column>(); var labels = new List<object?>();
            foreach (var (key, val) in d.Entries)
            {
                int pos = st.Frame.ColumnPositions(PdConv.ToCell(key))[0];
                var funcs = val is PyList l ? l.Items : val is PyTuple t ? t.Items.ToList() : new List<object> { val };
                foreach (var f in funcs)
                {
                    cols.Add(AggWith(i, st, pos, f));
                    labels.Add(anyList ? new LabelTuple(new[] { PdConv.ToCell(key), FuncName(f) }) : PdConv.ToCell(key));
                }
            }
            return AssembleFrame(st, cols, new FIndex(Column.Infer(labels)));
        }
        if (spec is PyList or PyTuple)
        {
            var funcs = spec is PyList l ? l.Items : ((PyTuple)spec).Items.ToList();
            var cols = new List<Column>(); var labels = new List<object?>();
            foreach (var j in st.ValueCols)
                foreach (var f in funcs) { cols.Add(AggWith(i, st, j, f)); labels.Add(new LabelTuple(new[] { st.Frame.Columns.Labels[j], FuncName(f) })); }
            return AssembleFrame(st, cols, new FIndex(Column.Infer(labels)));
        }
        if (spec is null) throw PyErr.TypeError("agg() missing func");
        return AssembleFrame(st, st.ValueCols.Select(j => AggWith(i, st, j, spec)).ToList(), ValueLabels(st));
    }

    // ================================================================== transform / apply / filter

    private static Column PlaceByGroup(GroupByState st, Func<int, int[], IReadOnlyList<object?>> perGroup, Column original)
    {
        var cells = new object?[st.G.NRows];
        for (int g = 0; g < st.G.Count; g++)
        {
            var rows = st.G.Rows[g];
            var vals = perGroup(g, rows);
            for (int n = 0; n < rows.Length; n++) cells[rows[n]] = vals.Count == 1 && rows.Length != 1 ? vals[0] : vals[n];
        }
        return InferLike(original, cells.ToList());
    }

    private static object Transform(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var st = St(a[0]);
        var p = A("transform", i, a, k, "func");
        var f = p.Required(0);
        Column One(int j)
        {
            var col = st.Frame.Data[j];
            if (f is string name)
            {
                if (name is "cumsum" or "cumprod" or "cummax" or "cummin")
                    return PlaceByGroup(st, (g, rows) => Reduce.Cumulative(name, col.Take(rows)).Values().ToList(), col);
                if (name is "shift") return PlaceByGroup(st, (g, rows) => FrameOps.Shift(col.Take(rows), 1).Values().ToList(), col);
                if (name is "diff") return PlaceByGroup(st, (g, rows) => FrameOps.Diff(col.Take(rows), 1).Values().ToList(), col);
                var agg = AggColumn(st, col, name);
                return PlaceByGroup(st, (g, rows) => new[] { agg[g] }, agg);
            }
            return PlaceByGroup(st, (g, rows) =>
            {
                var r = i.Call(f, new object[] { PdConv.Wrap(GroupSeries(st, j, rows)) });
                if (r is PyInstance { Native: Series rs }) return rs.Values.Values().ToList();
                if (PdConv.IsListLike(r)) return PdConv.Cells(r);
                return new[] { PdConv.ToCell(r) };
            }, col);
        }
        if (st.IsSeries) return PdConv.Wrap(new Series(One(st.ValueCols[0]), st.Frame.Index, st.SeriesName));
        return PdConv.Wrap(new DataFrame(st.ValueCols.Select(One), ValueLabels(st), st.Frame.Index));
    }

    private static object Apply(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var st = St(a[0]);
        var f = a[1];
        var extra = a.Skip(2).ToArray();
        var results = new List<object>();
        for (int g = 0; g < st.G.Count; g++)
        {
            var rows = st.G.Rows[g];
            object arg = st.IsSeries ? PdConv.Wrap(GroupSeries(st, st.ValueCols[0], rows)) : PdConv.Wrap(GroupFrame(st, rows, true));
            results.Add(i.Call(f, new[] { arg }.Concat(extra).ToArray(), k));
        }
        if (results.Count == 0) return PdConv.Wrap(new Series(Column.FromObjects(Array.Empty<object?>()), st.G.ResultIndex()));
        if (results.All(r => r is PyInstance { Native: DataFrame }))
            return ConcatWithKeys(st, results.Select(r => (DataFrame)((PyInstance)r).Native!).ToList());
        if (results.All(r => r is PyInstance { Native: Series }))
        {
            var ss = results.Select(r => (Series)((PyInstance)r).Native!).ToList();
            if (ss.All(s => PdSelect.SameLabels(s.Index, ss[0].Index)))
            {
                var cols = Enumerable.Range(0, ss[0].Length).Select(c => Column.Infer(ss.Select(s => s.Values[c]).ToList())).ToList();
                return PdConv.Wrap(new DataFrame(cols, ss[0].Index, st.G.ResultIndex()));
            }
            // differing indexes: stack with the group key as an outer level
            var frames = ss.Select(s => new DataFrame(new[] { s.Values }, new FIndex(Column.Infer(new[] { s.Name })), s.Index)).ToList();
            var cat = ConcatWithKeys(st, frames);
            var df = (DataFrame)((PyInstance)cat).Native!;
            return PdConv.Wrap(new Series(df.Data[0], df.Index, null));
        }
        var cells = results.Select(PdConv.ToCell).ToList();
        return AssembleSeries(st, Column.Infer(cells), st.IsSeries ? st.SeriesName : null);
    }

    private static object ConcatWithKeys(GroupByState st, List<DataFrame> frames)
    {
        var rowsPerFrame = frames.Select(f => f.NRows).ToList();
        var levels = new List<List<object?>>();
        for (int kcol = 0; kcol < st.G.KeyColumns.Count; kcol++)
            levels.Add(frames.SelectMany((f, g) => Enumerable.Repeat(st.G.Keys[g][kcol], f.NRows)).ToList());
        var inner = frames.Select(f => f.Index).Aggregate((x, y) => x.Concat(y));
        var innerLevels = Enumerable.Range(0, inner.NLevels).Select(inner.Level).ToList();
        var allLevels = levels.Select(l => Column.Infer(l)).Concat(innerLevels).ToList();
        var names = st.G.KeyNames.Concat(inner.Names).ToList();
        var idx = FIndex.Multi(allLevels, names);
        var first = frames[0];
        var cols = Enumerable.Range(0, first.NCols).Select(c => Column.Concat(frames.Select(f => f.Data[c]).ToList())).ToList();
        return PdConv.Wrap(new DataFrame(cols, first.Columns, idx));
    }

    private static object Filter(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var st = St(a[0]);
        var keep = new List<int>();
        for (int g = 0; g < st.G.Count; g++)
        {
            var rows = st.G.Rows[g];
            object arg = st.IsSeries ? PdConv.Wrap(GroupSeries(st, st.ValueCols[0], rows)) : PdConv.Wrap(GroupFrame(st, rows, false));
            if (PyOps.Truthy(i, i.Call(a[1], new[] { arg }, k))) keep.AddRange(rows);
        }
        keep.Sort();
        if (st.IsSeries) return PdConv.Wrap(new Series(st.Frame.Data[st.ValueCols[0]].Take(keep), st.Frame.Index.Take(keep), st.SeriesName));
        return PdConv.Wrap(st.Frame.TakeRows(keep));
    }

    // ================================================================== install

    public static void Install()
    {
        PdClasses.Series.Dict["groupby"] = PdClasses.Fn("groupby", (i, a, k) => Create(i, a[0], a, k));
        PdClasses.DataFrame.Dict["groupby"] = PdClasses.Fn("groupby", (i, a, k) => Create(i, a[0], a, k));

        foreach (var cls in new[] { DataFrameGroupBy, SeriesGroupBy })
        {
            void Def(string n, BuiltinFn f) => cls.Dict[n] = PdClasses.Fn($"{cls.Name}.{n}", f);
            foreach (var name in new[] { "sum", "mean", "median", "min", "max", "std", "var", "count", "prod", "first", "last", "nunique", "any", "all", "size" })
            {
                var nm = name;
                Def(nm, (i, a, k) => Reduction(i, a, k, nm));
            }
            Def("agg", Aggregate);
            Def("aggregate", Aggregate);
            Def("transform", Transform);
            Def("apply", Apply);
            Def("filter", Filter);
            Def("pipe", (i, a, k) => i.Call(a[1], new[] { a[0] }.Concat(a.Skip(2)).ToArray(), k));
            Def("__len__", (_, a, _) => new BigInteger(St(a[0]).G.Count));
            cls.Dict["ngroups"] = new PyProperty { Getter = PdClasses.Fn("ngroups", (_, a, _) => new BigInteger(St(a[0]).G.Count)) };
            Def("ngroup", (_, a, _) =>
            {
                var st = St(a[0]);
                return PdConv.Wrap(new Series(Column.FromLongs(st.G.GroupOfRow.Select(x => (long)x).ToArray()), st.Frame.Index));
            });
            Def("cumcount", (_, a, _) =>
            {
                var st = St(a[0]);
                var r = new long[st.G.NRows];
                foreach (var rows in st.G.Rows) for (int n = 0; n < rows.Length; n++) r[rows[n]] = n;
                return PdConv.Wrap(new Series(Column.FromLongs(r), st.Frame.Index));
            });
            Def("get_group", (i, a, k) =>
            {
                var st = St(a[0]);
                var key = A("get_group", i, a, k, "name").Required(0);
                var cell = PdConv.ToCell(key);
                for (int g = 0; g < st.G.Count; g++)
                    if (Equals(Column.Key(st.G.KeyLabel(g)) ?? Column.NaNKey, Column.Key(cell) ?? Column.NaNKey))
                        return st.IsSeries ? PdConv.Wrap(GroupSeries(st, st.ValueCols[0], st.G.Rows[g])) : PdConv.Wrap(GroupFrame(st, st.G.Rows[g], false));
                throw PdConv.KeyErr(key);
            });
            cls.Dict["groups"] = new PyProperty
            {
                Getter = PdClasses.Fn("groups", (_, a, _) =>
                {
                    var st = St(a[0]);
                    var d = new PyDict();
                    for (int g = 0; g < st.G.Count; g++)
                        d[PdConv.FromLabel(st.G.KeyLabel(g))] = new PyList(st.G.Rows[g].Select(r => PdConv.FromLabel(st.Frame.Index.Labels[r])));
                    return d;
                }),
            };
            Def("__iter__", (_, a, _) =>
            {
                var st = St(a[0]);
                return new PyIterator(Enumerable.Range(0, st.G.Count).Select(g =>
                    (object)new PyTuple(new object[]
                    {
                        PdConv.FromLabel(st.G.KeyLabel(g)),
                        st.IsSeries ? PdConv.Wrap(GroupSeries(st, st.ValueCols[0], st.G.Rows[g])) : PdConv.Wrap(GroupFrame(st, st.G.Rows[g], false)),
                    })).GetEnumerator());
            });
            foreach (var name in new[] { "cumsum", "cumprod", "cummax", "cummin" })
            {
                var nm = name;
                Def(nm, (i, a, k) => Transform(i, new[] { a[0], (object)nm }, null));
            }
            Def("shift", (i, a, k) => Transform(i, new[] { a[0], (object)"shift" }, null));
            Def("diff", (i, a, k) => Transform(i, new[] { a[0], (object)"diff" }, null));
            foreach (var (name, fromEnd) in new[] { ("head", false), ("tail", true) })
            {
                var nm = name; var fe = fromEnd;
                Def(nm, (i, a, k) =>
                {
                    var st = St(a[0]);
                    int n = A(nm, i, a, k, "n").Int(0, 5);
                    var keep = st.G.Rows.SelectMany(r => fe ? r.Skip(Math.Max(0, r.Length - n)) : r.Take(n)).OrderBy(x => x).ToList();
                    if (st.IsSeries) return PdConv.Wrap(new Series(st.Frame.Data[st.ValueCols[0]].Take(keep), st.Frame.Index.Take(keep), st.SeriesName));
                    return PdConv.Wrap(st.Frame.TakeRows(keep));
                });
            }
            Def("quantile", (i, a, k) =>
            {
                var st = St(a[0]);
                double q = A("quantile", i, a, k, "q").Has(0) ? Column.ToDouble(PdConv.ToCell(A("quantile", i, a, k, "q")[0])!) : 0.5;
                Column Q(int j) => Column.FromDoubles(st.G.Rows.Select(r => Reduce.Quantile(st.Frame.Data[j].Take(r), q)).ToArray());
                if (st.IsSeries) return AssembleSeries(st, Q(st.ValueCols[0]), st.SeriesName);
                return AssembleFrame(st, st.ValueCols.Select(Q).ToList(), ValueLabels(st));
            });
            Def("describe", (i, a, k) =>
            {
                var st = St(a[0]);
                var parts = st.G.Rows.Select(r => FrameOps.Describe(GroupSeries(st, st.ValueCols[0], r))).ToList();
                if (!st.IsSeries) throw PyErr.NotImplementedError("DataFrameGroupBy.describe");
                var labels = parts[0].Index;
                var cols = Enumerable.Range(0, labels.Length).Select(c => Column.Infer(parts.Select(p => p.Values[c]).ToList())).ToList();
                return PdConv.Wrap(new DataFrame(cols, labels, st.G.ResultIndex()));
            });
            Def("__getitem__", (i, a, k) =>
            {
                var st = St(a[0]);
                var key = a[1];
                if (key is PyList or PyTuple && PdConv.IsListLike(key))
                {
                    var pos = PdConv.Cells(key).Select(l => st.Frame.ColumnPositions(l)[0]).ToList();
                    return Wrap(st.With(pos, false));
                }
                var label = PdConv.ToCell(key);
                if (!st.Frame.HasColumn(label)) throw PdConv.KeyErr(key);
                return Wrap(st.With(new List<int> { st.Frame.ColumnPositions(label)[0] }, true));
            });
            Def("__getattr__", (_, a, _) =>
            {
                var st = St(a[0]);
                string name = (string)a[1];
                if (!name.StartsWith('_') && st.Frame.HasColumn(name)) return Wrap(st.With(new List<int> { st.Frame.ColumnPositions(name)[0] }, true));
                throw PyErr.AttributeError($"'{((PyInstance)a[0]).Class.Name}' object has no attribute '{name}'");
            });
        }
    }
}
