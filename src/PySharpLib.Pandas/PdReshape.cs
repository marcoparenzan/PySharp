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

/// <summary><c>pivot_table</c>, <c>pivot</c>, <c>crosstab</c>, <c>melt</c>, <c>stack</c>/<c>unstack</c> and <c>get_dummies</c>.</summary>
internal static class PdReshape
{
    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);
    private static List<object?> Labels(object? v) => v is null or PyNone ? new List<object?>() : PdConv.IsListLike(v) ? PdConv.Cells(v) : new List<object?> { PdConv.ToCell(v) };
    private static string FuncName(object f) => f switch { string s => s, PyFunction pf => pf.Name, PyBuiltinFunction bf => bf.Name, _ => "<lambda>" };

    // ================================================================== pivot_table

    private sealed record Block(object? FuncLabel, object? ValueLabel, int ValuePos, object Func);

    internal static DataFrame PivotTable(Interp i, DataFrame df, object? values, List<object?> index, List<object?> columns, object? aggfunc,
        object? fillValue, bool margins, string marginsName, bool dropNa, bool allowDuplicates = true, bool sizeOnly = false, bool observed = true)
    {
        if (index.Count == 0 && columns.Count == 0) throw new FrameException("No group keys passed!");
        var keyLabels = index.Concat(columns).ToList();
        var keyCols = keyLabels.Select(l => df.Data[df.ColumnPositions(l)[0]]).ToList();
        bool valuesIsList = values is not null && values is not PyNone && PdConv.IsListLike(values);
        int nCol0 = columns.Count;
        List<object?> valueLabels = values is null or PyNone
            ? df.Columns.Items().Where(l => !keyLabels.Any(k => Equals(Column.Key(k), Column.Key(l)))).ToList()
            : Labels(values);
        if (aggfunc is "size" && (values is null or PyNone)) sizeOnly = true;
        if (sizeOnly) valueLabels = new List<object?> { keyLabels[0] };
        bool valueLevel = !sizeOnly && (valuesIsList || values is null or PyNone || nCol0 == 0);
        bool funcIsList = aggfunc is PyList or PyTuple;
        var funcMap = new Dictionary<object, List<object>>();
        List<object> defaultFuncs = aggfunc is PyList fl ? fl.Items.ToList() : aggfunc is PyTuple ft ? ft.Items.ToList() : new List<object> { aggfunc is null or PyNone ? "mean" : aggfunc };
        var blocks = new List<Block>();
        foreach (var f in funcIsList ? defaultFuncs : new List<object> { defaultFuncs[0] })
            foreach (var vl in valueLabels)
            {
                object func = aggfunc is PyDict ad && ad.TryGet(PdConv.FromLabel(vl), out var chosen) ? chosen : f;
                blocks.Add(new Block(funcIsList ? FuncName(f) : null, vl, df.ColumnPositions(vl)[0], func));
            }
        var g = new Grouping(keyCols, keyLabels, df.NRows, true, true, observed);
        var st = new GroupByState { Frame = df, G = g, ValueCols = valueLabels.Select(l => df.ColumnPositions(l)[0]).Distinct().ToList() };
        int nIdx = index.Count, nCol = columns.Count;
        // row keys / column keys (sorted unique)
        List<object?[]> RowKeyParts(Func<object?[], object?[]> pick) => g.Keys.Select(pick).ToList();
        var rowParts = g.Keys.Select(k => k.Take(nIdx).ToArray()).ToList();
        var colParts = g.Keys.Select(k => k.Skip(nIdx).ToArray()).ToList();
        (List<object?[]> uniq, int[] map) Unique(List<object?[]> parts, int nKeys, int offset)
        {
            var ids = new Dictionary<object, int>(); var uniq = new List<object?[]>(); var map = new int[parts.Count];
            for (int q = 0; q < parts.Count; q++)
            {
                object key = new LabelTuple(parts[q]);
                if (!ids.TryGetValue(key, out int id)) { id = uniq.Count; ids[key] = id; uniq.Add(parts[q]); }
                map[q] = id;
            }
            if (nKeys == 0) return (new List<object?[]> { Array.Empty<object?>() }, new int[parts.Count]);
            var cols = Enumerable.Range(0, nKeys).Select(c => Grouping.KeyColumnFor(keyCols[offset + c], uniq.Select(u => u[c]).ToList())).ToList();
            var order = FrameOps.SortPositions(cols, Enumerable.Repeat(true, nKeys).ToList(), true);
            var rank = new int[uniq.Count];
            for (int r = 0; r < order.Length; r++) rank[order[r]] = r;
            return (order.Select(o => uniq[o]).ToList(), map.Select(m => rank[m]).ToArray());
        }
        var (rowUniq, rowOf) = Unique(rowParts, nIdx, 0);
        var (colUniq, colOf) = Unique(colParts, nCol, nIdx);
        int nR = rowUniq.Count, nC = colUniq.Count;
        var cellGroup = new Dictionary<(int, int), int>();
        for (int q = 0; q < g.Count; q++) cellGroup[(rowOf[q], colOf[q])] = q;

        // aggregated value of each block for every group
        var aggCols = new List<Column>();
        foreach (var b in blocks)
        {
            if (sizeOnly) aggCols.Add(Column.FromLongs(g.Rows.Select(r => (long)r.Length).ToArray()));
            else aggCols.Add(PdGroupBy.AggWith(i, st, b.ValuePos, b.Func));
        }
        if (!allowDuplicates && g.Rows.Any(r => r.Length > 1)) throw new FrameException("Index contains duplicate entries, cannot reshape");

        object? fill = fillValue is null or PyNone ? null : PdConv.ToCell(fillValue);
        var outCols = new List<Column>(); var outLabels = new List<object?>();
        bool singleLevelValue = !(valuesIsList || values is null or PyNone) && nCol > 0;
        object? Label(Block b, object?[]? colKey)
        {
            var parts = new List<object?>();
            if (b.FuncLabel is not null) parts.Add(b.FuncLabel);
            if (valueLevel) parts.Add(b.ValueLabel);
            if (sizeOnly && nCol == 0) parts.Add("size");
            if (colKey is not null) parts.AddRange(colKey);
            return parts.Count == 1 ? parts[0] : new LabelTuple(parts.ToArray());
        }
        for (int bi = 0; bi < blocks.Count; bi++)
            for (int c = 0; c < nC; c++)
            {
                var cells = new List<object?>(nR);
                bool any = false;
                for (int r = 0; r < nR; r++)
                {
                    if (cellGroup.TryGetValue((r, c), out int q))
                    {
                        object? v = aggCols[bi][q];
                        if (fill is not null && v is double dv && double.IsNaN(dv)) v = fill;
                        else if (!(v is double d2 && double.IsNaN(d2))) any = true;
                        cells.Add(v);
                    }
                    else cells.Add(fill ?? double.NaN);
                }
                if (dropNa && !any && fill is null) continue;
                outCols.Add(InferWith(cells, aggCols[bi]));
                outLabels.Add(Label(blocks[bi], nCol > 0 ? colUniq[c] : null));
            }
        // names of the column levels
        var colNames = new List<object?>();
        int labelLevels = (blocks.Any(b => b.FuncLabel is not null) ? 1 : 0) + (valueLevel ? 1 : 0) + (sizeOnly && nCol == 0 ? 1 : 0);
        for (int q = 0; q < labelLevels; q++) colNames.Add(null);
        colNames.AddRange(columns);
        var rowIndex = nIdx == 1
            ? new FIndex(Grouping.KeyColumnFor(keyCols[0], rowUniq.Select(r => r[0]).ToList()), index[0])
            : nIdx == 0 ? new FIndex(Column.Infer(new object?[] { sizeOnly ? "size" : "" }.ToList())) : FIndex.MultiFromTuples(rowUniq, index);
        if (nIdx == 1) rowIndex = new FIndex(keyCols[0].Kind == Kind.Category ? Grouping.KeyColumnFor(keyCols[0], rowUniq.Select(r => r[0]).ToList()) : KeyColumn(keyCols[0], rowUniq.Select(r => r[0]).ToList()), index[0]);

        if (margins) AddMargins(i, df, blocks, aggCols, g, keyCols, nIdx, nCol, rowUniq, colUniq, rowOf, colOf, ref outCols, ref outLabels, ref rowIndex, marginsName, fill, sizeOnly, valueLevel, nCol == 0 ? 0 : 1);
        var colIndex = new FIndex(Column.Infer(outLabels));
        if (colIndex.IsMulti || colNames.Any(n => n is not null)) colIndex = colIndex.WithNames(colNames.Take(colIndex.NLevels).ToList());
        return new DataFrame(outCols, colIndex, rowIndex);
    }

    private static Column KeyColumn(Column original, List<object?> cells) => original.Kind == Kind.Str ? Column.FromStrings(cells.Select(c => c as string).ToArray()) : Column.Infer(cells);

    private static Column InferWith(List<object?> cells, Column like) => like.Kind == Kind.Str && cells.All(c => c is string or null || c is double d && double.IsNaN(d))
        ? Column.FromStrings(cells.Select(c => c as string).ToArray()) : Column.Infer(cells);

    private static void AddMargins(Interp i, DataFrame df, List<Block> blocks, List<Column> aggCols, Grouping g, List<Column> keyCols, int nIdx, int nCol,
        List<object?[]> rowUniq, List<object?[]> colUniq, int[] rowOf, int[] colOf, ref List<Column> outCols, ref List<object?> outLabels, ref FIndex rowIndex,
        string marginsName, object? fill, bool sizeOnly, bool valueLevel, int _)
    {
        // rows of the source table that belong to each result row / column
        var rowsOfRow = new List<int>[rowUniq.Count]; var rowsOfCol = new List<int>[colUniq.Count];
        for (int q = 0; q < rowsOfRow.Length; q++) rowsOfRow[q] = new List<int>();
        for (int q = 0; q < rowsOfCol.Length; q++) rowsOfCol[q] = new List<int>();
        for (int q = 0; q < g.Count; q++) { rowsOfRow[rowOf[q]].AddRange(g.Rows[q]); rowsOfCol[colOf[q]].AddRange(g.Rows[q]); }
        var all = Enumerable.Range(0, df.NRows).Where(r => g.GroupOfRow[r] >= 0).ToArray();
        object? AggRows(Block b, IEnumerable<int> rows)
        {
            var arr = rows.OrderBy(x => x).ToArray();
            if (sizeOnly) return (long)arr.Length;
            if (arr.Length == 0) return double.NaN;
            var sub = df.Data[b.ValuePos].Take(arr);
            if (b.Func is string name) return name is "first" or "last" ? sub[name == "first" ? 0 : sub.Length - 1] : Reduce.Scalar(name, sub);
            return PdConv.ToCell(i.Call(b.Func, new object[] { PdConv.Wrap(new Series(sub, df.Index.Take(arr), df.Columns.Labels[b.ValuePos])) }));
        }
        int nBlocks = blocks.Count;
        // existing columns get one more cell (the "All" row)
        var newCols = new List<Column>();
        int ci = 0;
        for (int bi = 0; bi < nBlocks; bi++)
        {
            int perBlock = outCols.Count / nBlocks; // columns kept per block (all-NaN columns may have been dropped)
            for (int c = 0; c < colUniq.Count && ci < outCols.Count; c++)
            {
                if (ci >= (bi + 1) * perBlock) break;
                var cells = outCols[ci].Values().ToList();
                cells.Add(AggRows(blocks[bi], rowsOfCol[c]));
                newCols.Add(Column.Infer(cells));
                ci++;
            }
        }
        var labels = new List<object?>(outLabels);
        // the "All" column for each block (only when there are column keys)
        if (nCol > 0)
        {
            for (int bi = 0; bi < nBlocks; bi++)
            {
                var cells = Enumerable.Range(0, rowUniq.Count).Select(r => AggRows(blocks[bi], rowsOfRow[r])).ToList();
                cells.Add(AggRows(blocks[bi], all));
                newCols.Insert((bi + 1) * (newCols.Count / nBlocks) + bi, Column.Infer(cells));
            }
            int per = outLabels.Count / nBlocks;
            var rebuilt = new List<object?>();
            for (int bi = 0; bi < nBlocks; bi++)
            {
                for (int q = 0; q < per; q++) rebuilt.Add(labels[bi * per + q]);
                var parts = new List<object?>();
                if (blocks[bi].FuncLabel is not null) parts.Add(blocks[bi].FuncLabel);
                if (valueLevel) parts.Add(blocks[bi].ValueLabel);
                parts.Add(marginsName);
                for (int q = 1; q < nCol; q++) parts.Add("");
                rebuilt.Add(parts.Count == 1 ? parts[0] : new LabelTuple(parts.ToArray()));
            }
            labels = rebuilt;
        }
        else
        {
            // no column keys: the margin is just one more row
        }
        outCols = newCols; outLabels = labels;
        var rowLabelCells = rowIndex.Items().ToList();
        if (nIdx == 1) { rowLabelCells.Add(marginsName); rowIndex = new FIndex(Column.Infer(rowLabelCells), rowIndex.Name); }
        else
        {
            var rows = rowUniq.Select(r => (object?[])r).ToList();
            rows.Add(new object?[] { marginsName }.Concat(Enumerable.Repeat<object?>("", nIdx - 1)).ToArray());
            rowIndex = FIndex.MultiFromTuples(rows, rowIndex.Names);
        }
    }

    // ================================================================== install

    public static void Install(PyModule m)
    {
        BuiltinFn pivotTable = (i, a, k) =>
        {
            bool isMethod = a.Length > 0 && a[0] is PyInstance { Native: DataFrame };
            var p = new Args("pivot_table", i, (isMethod ? a.Skip(1) : a).ToArray(), k, isMethod
                ? new[] { "values", "index", "columns", "aggfunc", "fill_value", "margins", "dropna", "margins_name", "observed", "sort" }
                : new[] { "data", "values", "index", "columns", "aggfunc", "fill_value", "margins", "dropna", "margins_name", "observed", "sort" });
            int o = isMethod ? 0 : 1;
            var df = isMethod ? PdConv.D(a[0]) : PdConv.D(p.Required(0));
            return PdConv.Wrap(PivotTable(i, df, p[o], Labels(p[o + 1]), Labels(p[o + 2]), p[o + 3], p[o + 4], p.Bool(o + 5, false), p.Has(o + 7) ? (string)p[o + 7]! : "All", p.Bool(o + 6, true), observed: p.Bool(o + 8, true)));
        };
        m.Dict["pivot_table"] = PdClasses.Fn("pivot_table", pivotTable);
        PdClasses.DataFrame.Dict["pivot_table"] = PdClasses.Fn("pivot_table", pivotTable);

        BuiltinFn pivot = (i, a, k) =>
        {
            bool isMethod = a.Length > 0 && a[0] is PyInstance { Native: DataFrame };
            var p = new Args("pivot", i, (isMethod ? a.Skip(1) : a).ToArray(), k, isMethod ? new[] { "columns", "index", "values" } : new[] { "data", "columns", "index", "values" });
            int o = isMethod ? 0 : 1;
            var df = isMethod ? PdConv.D(a[0]) : PdConv.D(p.Required(0));
            var idx = Labels(p[o + 1]);
            if (idx.Count == 0)
            {
                // pivot on the row index
                var cols = new List<Column> { df.Index.Labels }; cols.AddRange(df.Data);
                var names = new List<object?> { df.Index.Name ?? "__index__" }; names.AddRange(df.Columns.Items());
                df = new DataFrame(cols, new FIndex(Column.Infer(names)), FIndex.Range(df.NRows));
                idx = new List<object?> { names[0] };
            }
            var res = PivotTable(i, df, p[o + 2] ?? (p.Has(o + 2) ? p[o + 2] : null), idx, Labels(p[o]), "first", null, false, "All", false, allowDuplicates: false);
            if (df.Columns.Labels[0] is string s0 && s0 == "__index__") res = res.WithIndex(res.Index.WithName(null));
            return PdConv.Wrap(res);
        };
        m.Dict["pivot"] = PdClasses.Fn("pivot", pivot);
        PdClasses.DataFrame.Dict["pivot"] = PdClasses.Fn("pivot", pivot);

        BuiltinFn melt = (i, a, k) =>
        {
            bool isMethod = a.Length > 0 && a[0] is PyInstance { Native: DataFrame };
            var p = new Args("melt", i, (isMethod ? a.Skip(1) : a).ToArray(), k, isMethod
                ? new[] { "id_vars", "value_vars", "var_name", "value_name", "col_level", "ignore_index" }
                : new[] { "frame", "id_vars", "value_vars", "var_name", "value_name", "col_level", "ignore_index" });
            int o = isMethod ? 0 : 1;
            var df = isMethod ? PdConv.D(a[0]) : PdConv.D(p.Required(0));
            var ids = Labels(p[o]);
            var vals = p.Has(o + 1) ? Labels(p[o + 1]) : df.Columns.Items().Where(l => !ids.Any(x => Equals(Column.Key(x), Column.Key(l)))).ToList();
            return PdConv.Wrap(Reshape.Melt(df, ids, vals, p.Has(o + 2) ? PdConv.ToCell(p[o + 2]) : null, p.Has(o + 3) ? PdConv.ToCell(p[o + 3]) : null));
        };
        m.Dict["melt"] = PdClasses.Fn("melt", melt);
        PdClasses.DataFrame.Dict["melt"] = PdClasses.Fn("melt", melt);

        PdClasses.Series.Dict["unstack"] = PdClasses.Fn("unstack", (i, a, k) =>
        {
            var s = PdConv.S(a[0]);
            var p = A("unstack", i, a, k, "level", "fill_value");
            int lvl = p.Has(0) ? PdClasses.LevelNumber(s.Index, p[0]!) : -1;
            var d = new DataFrame(new[] { s.Values }, new FIndex(Column.Infer(new[] { s.Name })), s.Index);
            var r = Reshape.Unstack(d, lvl, true, p.Has(1) ? PdConv.ToCell(p[1]) : null);
            return PdConv.Wrap(r);
        });
        PdClasses.DataFrame.Dict["unstack"] = PdClasses.Fn("unstack", (i, a, k) =>
        {
            var d = PdConv.D(a[0]);
            var p = A("unstack", i, a, k, "level", "fill_value");
            int lvl = p.Has(0) ? PdClasses.LevelNumber(d.Index, p[0]!) : -1;
            var r = Reshape.Unstack(d, lvl, false, p.Has(1) ? PdConv.ToCell(p[1]) : null);
            return PdConv.Wrap(r);
        });
        PdClasses.DataFrame.Dict["stack"] = PdClasses.Fn("stack", (i, a, k) =>
        {
            var p = A("stack", i, a, k, "level", "dropna", "sort", "future_stack");
            if (p.Has(1)) throw PyErr.ValueError("dropna must be unspecified as the new implementation does not introduce rows of NA values. This argument will be removed in a future version of pandas.");
            return PdConv.Wrap(Reshape.Stack(PdConv.D(a[0]), false));
        });

        m.Dict["crosstab"] = PdClasses.Fn("crosstab", (i, a, k) =>
        {
            var p = new Args("crosstab", i, a, k, "index", "columns", "values", "rownames", "colnames", "aggfunc", "margins", "margins_name", "dropna", "normalize");
            var idxArgs = p.Required(0) is PyList il && il.Items.All(x => x is PyInstance { Native: Series } || PdConv.IsListLike(x)) ? il.Items : new List<object> { p[0]! };
            var colArgs = p.Required(1) is PyList cl && cl.Items.All(x => x is PyInstance { Native: Series } || PdConv.IsListLike(x)) ? cl.Items : new List<object> { p[1]! };
            var cols = new List<Column>(); var names = new List<object?>();
            var rn = p.Has(3) ? PdConv.Cells(p[3]!) : null; var cn = p.Has(4) ? PdConv.Cells(p[4]!) : null;
            FIndex? rowsIx = null;
            void Add(object src, string defaultName, List<object?>? given, int n, List<object?> into)
            {
                var col = PdConv.ToColumn(src);
                object? nm = given is not null ? given[n] : src is PyInstance { Native: Series s } && s.Name is not null ? s.Name : defaultName;
                if (src is PyInstance { Native: Series s2 }) rowsIx ??= s2.Index;
                cols.Add(col); names.Add(nm); into.Add(nm);
            }
            var rowNames = new List<object?>(); var colNames = new List<object?>();
            for (int n = 0; n < idxArgs.Count; n++) Add(idxArgs[n], "row_" + n, rn, n, rowNames);
            for (int n = 0; n < colArgs.Count; n++) Add(colArgs[n], "col_" + n, cn, n, colNames);
            bool hasValues = p.Has(2);
            if (hasValues) { cols.Add(PdConv.ToColumn(p[2]!)); names.Add("__values__"); }
            var df = new DataFrame(cols, new FIndex(Column.Infer(names)), rowsIx ?? FIndex.Range(cols[0].Length));
            bool margins = p.Bool(6, false);
            string mname = p.Has(7) ? (string)p[7]! : "All";
            DataFrame res;
            if (!hasValues) res = PivotTable(i, df, null, rowNames, colNames, "size", 0L, margins, mname, false, sizeOnly: true);
            else res = PivotTable(i, df, "__values__", rowNames, colNames, p.Has(5) ? p[5] : "mean", null, margins, mname, false);
            if (hasValues) res = new DataFrame(res.Data, res.Columns.WithNames(colNames), res.Index);
            if (p.Has(9) && p[9] is not (false or PyNone))
            {
                string norm = p[9] is true ? "all" : (string)p[9]!;
                int nr = res.NRows, nc = res.NCols;
                double[,] v = new double[nr, nc];
                for (int c = 0; c < nc; c++) for (int r = 0; r < nr; r++) v[r, c] = res.Data[c].DoubleAt(r);
                double total = 0; for (int c = 0; c < nc; c++) for (int r = 0; r < nr; r++) total += v[r, c];
                var rt = Enumerable.Range(0, nr).Select(r => Enumerable.Range(0, nc).Sum(c => v[r, c])).ToArray();
                var ct = Enumerable.Range(0, nc).Select(c => Enumerable.Range(0, nr).Sum(r => v[r, c])).ToArray();
                var outc = Enumerable.Range(0, nc).Select(c => Column.FromDoubles(Enumerable.Range(0, nr).Select(r => norm switch { "index" => v[r, c] / rt[r], "columns" => v[r, c] / ct[c], _ => v[r, c] / total }).ToArray())).ToList();
                res = new DataFrame(outc, res.Columns, res.Index);
            }
            return PdConv.Wrap(res);
        });

        m.Dict["get_dummies"] = PdClasses.Fn("get_dummies", (i, a, k) =>
        {
            var p = new Args("get_dummies", i, a, k, "data", "prefix", "prefix_sep", "dummy_na", "columns", "sparse", "drop_first", "dtype");
            string sep = p.Has(2) ? (string)p[2]! : "_";
            bool dummyNa = p.Bool(3, false), dropFirst = p.Bool(6, false);
            string dtype = p.Has(7) ? PdConv.DTypeName(p[7]!) : "bool";
            (List<Column>, List<object?>) Encode(Column col, string? prefix)
            {
                var cats = new List<object?>(); var seen = new HashSet<object>();
                if (col.Kind == Kind.Category)
                    for (int q = 0; q < col.Categories.Length; q++) cats.Add(col.Categories[q]);
                else
                {
                    var order = FrameOps.SortPositions(new[] { col }, new[] { true }, true).Where(r => !col.IsNa(r)).ToList();
                    foreach (var r in order) if (seen.Add(Column.Key(col[r])!)) cats.Add(col[r]);
                }
                if (dropFirst && cats.Count > 0) cats.RemoveAt(0);
                var outc = new List<Column>(); var labels = new List<object?>();
                foreach (var c in cats)
                {
                    var flags = Enumerable.Range(0, col.Length).Select(r => !col.IsNa(r) && Equals(Column.Key(col[r]), Column.Key(c))).ToArray();
                    outc.Add(PdConv.AsType(Column.FromBools(flags), dtype));
                    labels.Add(prefix is null ? c : prefix + sep + Formatter.ObjectStr(c));
                }
                if (dummyNa)
                {
                    outc.Add(PdConv.AsType(Column.FromBools(Enumerable.Range(0, col.Length).Select(col.IsNa).ToArray()), dtype));
                    labels.Add(prefix is null ? double.NaN : prefix + sep + "nan");
                }
                return (outc, labels);
            }
            var data = p.Required(0);
            if (data is PyInstance { Native: Series s })
            {
                var (cs, ls) = Encode(s.Values, p.Has(1) ? Formatter.ObjectStr(PdConv.ToCell(p[1])) : null);
                return PdConv.Wrap(new DataFrame(cs, new FIndex(Column.Infer(ls)), s.Index));
            }
            var d = PdConv.D(data);
            var encodeCols = p.Has(4) ? Labels(p[4]).Select(l => d.ColumnPositions(l)[0]).ToList()
                : Enumerable.Range(0, d.NCols).Where(j => d.Data[j].Kind is Kind.Str or Kind.Object or Kind.Category).ToList();
            var prefixes = p.Has(1) ? (PdConv.IsListLike(p[1]!) ? PdConv.Cells(p[1]!) : Enumerable.Repeat(PdConv.ToCell(p[1]), encodeCols.Count).ToList()) : null;
            var outCols = new List<Column>(); var outLabels = new List<object?>();
            for (int j = 0; j < d.NCols; j++)
                if (!encodeCols.Contains(j)) { outCols.Add(d.Data[j]); outLabels.Add(d.Columns.Labels[j]); }
            for (int n = 0; n < encodeCols.Count; n++)
            {
                int j = encodeCols[n];
                var (cs, ls) = Encode(d.Data[j], prefixes is not null ? Formatter.ObjectStr(prefixes[n]) : Formatter.ObjectStr(d.Columns.Labels[j]));
                outCols.AddRange(cs); outLabels.AddRange(ls);
            }
            return PdConv.Wrap(new DataFrame(outCols, new FIndex(Column.Infer(outLabels)), d.Index));
        });
    }
}
