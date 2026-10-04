// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using System.Text;
using NDSharp;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary><c>read_csv</c>, <c>to_csv</c>, <c>to_dict</c>, <c>from_dict</c>, <c>from_records</c>.</summary>
internal static class PdIO
{
    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);

    private static string ReadSource(Interp i, object src)
    {
        if (src is string path)
        {
            if (path.StartsWith("http://") || path.StartsWith("https://")) throw PyErr.NotImplementedError("read_csv from a URL");
            if (!File.Exists(path)) throw PyErr.Raise(PyErr.FileNotFoundErrorClass, $"[Errno 2] No such file or directory: '{path}'");
            return File.ReadAllText(path, new UTF8Encoding(false)).TrimStart('﻿');
        }
        if (src is PyInstance pi && !pi.Class.Mro.Any(c => c.Dict.ContainsKey("read")))
        {
            string p = PyOps.Str(i, src);
            if (File.Exists(p)) return File.ReadAllText(p, new UTF8Encoding(false)).TrimStart('﻿');
            throw PyErr.Raise(PyErr.FileNotFoundErrorClass, $"[Errno 2] No such file or directory: '{p}'");
        }
        var r = i.CallMethod(src, "read", Array.Empty<object>());
        return r switch { string s => s, byte[] b => Encoding.UTF8.GetString(b), _ => PyOps.Str(i, r) };
    }

    private static char Ch(object? v, char dflt) => v is string { Length: > 0 } s ? s[0] : dflt;

    private static readonly string[] ReadCsvNames =
    {
        "filepath_or_buffer", "sep", "delimiter", "header", "names", "index_col", "usecols", "dtype", "skiprows", "nrows", "na_values", "keep_default_na", "comment",
        "decimal", "thousands", "quotechar", "escapechar", "skipinitialspace", "encoding", "parse_dates", "converters", "engine", "low_memory", "true_values", "false_values",
        "on_bad_lines", "lineterminator", "chunksize", "iterator", "na_filter",
    };

    private static object ReadCsv(Interp i, object[] a, Dictionary<string, object>? k, char defaultSep)
    {
        var p = new Args("read_csv", i, a, k, ReadCsvNames);
        int X(string n) => Array.IndexOf(ReadCsvNames, n);
        object? G(string n) => p[X(n)];
        bool H(string n) => p.Has(X(n));
        if (H("parse_dates") && G("parse_dates") is not (false or PyNone)) throw PyErr.NotImplementedError("read_csv(parse_dates=...) — datetime dtypes are not supported");
        if (H("converters") || H("true_values") || H("false_values")) throw PyErr.NotImplementedError("read_csv(converters/true_values/false_values)");
        if (H("chunksize") || p.Bool(X("iterator"), false)) throw PyErr.NotImplementedError("read_csv(chunksize/iterator)");
        string text = ReadSource(i, p.Required(0));
        char sep = H("sep") ? Ch(G("sep"), defaultSep) : H("delimiter") ? Ch(G("delimiter"), defaultSep) : defaultSep;
        int? header = G("header") is "infer" or null ? (H("names") ? null : 0) : G("header") is PyNone ? null : PdConv.ToInt(G("header")!);
        var skip = new List<int>();
        if (H("skiprows"))
        {
            var sr = G("skiprows")!;
            if (PdConv.IsInt(sr)) skip.AddRange(Enumerable.Range(0, PdConv.ToInt(sr)));
            else if (PdConv.IsListLike(sr)) skip.AddRange(PdConv.Cells(sr).Select(x => (int)(long)x!));
            else if (sr is PyFunction or PyBuiltinFunction) skip.AddRange(Enumerable.Range(0, text.Split((char)10).Length).Where(n => PyOps.Truthy(i, i.Call(sr, new object[] { new BigInteger(n) }))));
        }
        bool keepDefault = p.Bool(X("keep_default_na"), true);
        var na = keepDefault ? new HashSet<string>(Csv.DefaultNaValues) : new HashSet<string>();
        var naPer = new Dictionary<string, HashSet<string>>();
        if (H("na_values"))
        {
            var nv = G("na_values")!;
            if (nv is PyDict nd)
                foreach (var (key, v) in nd.Entries)
                {
                    var set = keepDefault ? new HashSet<string>(Csv.DefaultNaValues) : new HashSet<string>();
                    foreach (var x in PdConv.IsListLike(v) ? PdConv.Cells(v) : new List<object?> { PdConv.ToCell(v) }) set.Add(Formatter.ObjectStr(x));
                    naPer[Formatter.ObjectStr(PdConv.ToCell(key))] = set;
                }
            else foreach (var x in PdConv.IsListLike(nv) ? PdConv.Cells(nv) : new List<object?> { PdConv.ToCell(nv) }) na.Add(x is double d ? PyOps.ReprDouble(d) : Formatter.ObjectStr(x));
        }
        if (!p.Bool(X("na_filter"), true)) na = new HashSet<string>();
        object? dtypeArg = H("dtype") ? G("dtype") : null;
        Func<object?, string?>? dtypeFor = null;
        if (dtypeArg is PyDict dd)
            dtypeFor = label => dd.Entries.Where(e => Equals(Column.Key(PdConv.ToCell(e.Key)), Column.Key(label))).Select(e => PdConv.DTypeName(e.Value)).FirstOrDefault();
        else if (dtypeArg is not null) { var nm = PdConv.DTypeName(dtypeArg); dtypeFor = _ => nm; }
        var opts = new Csv.ReadOptions
        {
            Sep = sep, Quote = Ch(G("quotechar"), '"'), Escape = H("escapechar") ? Ch(G("escapechar"), (char)92) : null, Comment = H("comment") ? Ch(G("comment"), '#') : null,
            SkipInitialSpace = p.Bool(X("skipinitialspace"), false), Header = header, Names = H("names") ? PdConv.Cells(G("names")!) : null,
            SkipRows = skip.Count > 0 ? skip : null, NRows = p.IntOrNull(X("nrows")), NaValues = na, NaPerColumn = naPer,
            Decimal = Ch(G("decimal"), '.'), Thousands = H("thousands") ? Ch(G("thousands"), ',') : null, DTypeFor = dtypeFor,
        };
        var (cols, names) = Csv.Read(text, opts);
        // usecols
        if (H("usecols"))
        {
            List<int> keep;
            if (G("usecols") is PyFunction or PyBuiltinFunction) keep = Enumerable.Range(0, names.Count).Where(c => PyOps.Truthy(i, i.Call(G("usecols")!, new[] { PdConv.FromLabel(names[c]) }))).ToList();
            else
            {
                var wanted = PdConv.Cells(G("usecols")!);
                keep = Enumerable.Range(0, names.Count).Where(c => wanted.Any(w => w is long l && !(names[c] is long) ? l == c : Equals(Column.Key(w), Column.Key(names[c])))).ToList();
            }
            cols = keep.Select(c => cols[c]).ToList(); names = keep.Select(c => names[c]).ToList();
        }
        // implicit index (data rows with one more field than the header, or Names shorter than the data)
        FIndex? index = null;
        var implicitPos = names.Select((n, c) => (n, c)).Where(x => x.n is string s && s.StartsWith("__implicit_index_")).Select(x => x.c).ToList();
        if (implicitPos.Count > 0)
        {
            index = implicitPos.Count == 1 ? new FIndex(cols[implicitPos[0]], null) : FIndex.Multi(implicitPos.Select(c => cols[c]).ToList());
            cols = cols.Where((c, n) => !implicitPos.Contains(n)).ToList(); names = names.Where((n, c) => !implicitPos.Contains(c)).ToList();
        }
        object? ic = H("index_col") ? G("index_col") : null;
        if (ic is not null and not false)
        {
            var wantedIdx = PdConv.IsListLike(ic) ? PdConv.Cells(ic) : new List<object?> { PdConv.ToCell(ic) };
            var pos = wantedIdx.Select(w => w is long l && !names.Contains(w) ? (int)l : names.FindIndex(n => Equals(Column.Key(n), Column.Key(w)))).ToList();
            if (pos.Any(x => x < 0 || x >= names.Count)) throw PyErr.ValueError($"Index {PyOps.Str(i, PdConv.FromLabel(wantedIdx[pos.IndexOf(pos.First(x => x < 0 || x >= names.Count))]))} invalid");
            string? NameOf(int c) => names[c] is string s && s.StartsWith("Unnamed: ") ? null : names[c] as string ?? (names[c] is long ? Convert.ToString(names[c], CultureInfo.InvariantCulture) : null);
            index = pos.Count == 1 && AsRange(cols[pos[0]], names[pos[0]] is string sr0 && sr0.StartsWith("Unnamed: ") ? null : names[pos[0]]) is { } rangeIx ? rangeIx : pos.Count == 1 ? new FIndex(cols[pos[0]], names[pos[0]] is string s0 && s0.StartsWith("Unnamed: ") ? null : names[pos[0]]) : FIndex.Multi(pos.Select(c => cols[c]).ToList(), pos.Select(c => (object?)(names[c] is string sx && sx.StartsWith("Unnamed: ") ? null : names[c])).ToList());
            cols = cols.Where((c, n) => !pos.Contains(n)).ToList(); names = names.Where((n, c) => !pos.Contains(c)).ToList();
        }
        index ??= FIndex.Range(cols.Count == 0 ? 0 : cols[0].Length);
        return PdConv.Wrap(new DataFrame(cols, new FIndex(Column.Infer(names)), index));
    }

    /// <summary>read_csv turns an index column that is a regular integer progression into a RangeIndex.</summary>
    private static FIndex? AsRange(Column c, object? name)
    {
        if (c.Kind != Kind.Int || c.Length == 0) return null;
        long step = c.Length > 1 ? c.Longs[1] - c.Longs[0] : 1;
        if (step == 0) return null;
        for (int i = 1; i < c.Length; i++) if (c.Longs[i] - c.Longs[i - 1] != step) return null;
        return FIndex.Range((int)c.Longs[0], (int)(c.Longs[0] + step * c.Length), (int)step, name);
    }

    // ================================================================== to_csv

    private static string FormatFloat(double d, string? fmt)
    {
        if (fmt is null) return PyOps.ReprDouble(d);
        var m = System.Text.RegularExpressions.Regex.Match(fmt, @"^%\.(\d+)([fFeEgG])$");
        if (m.Success)
        {
            int digits = int.Parse(m.Groups[1].Value);
            return m.Groups[2].Value switch
            {
                "f" or "F" => d.ToString("F" + digits, CultureInfo.InvariantCulture),
                "e" or "E" => d.ToString((m.Groups[2].Value == "e" ? "0." + new string('0', digits) + "e+00" : "0." + new string('0', digits) + "E+00"), CultureInfo.InvariantCulture),
                _ => d.ToString("G" + digits, CultureInfo.InvariantCulture),
            };
        }
        var m2 = System.Text.RegularExpressions.Regex.Match(fmt, @"^\{:\.(\d+)f\}$");
        if (m2.Success) return d.ToString("F" + m2.Groups[1].Value, CultureInfo.InvariantCulture);
        throw PyErr.NotImplementedError($"to_csv(float_format={fmt})");
    }

    private static string CellText(Column c, int r, string naRep, string? floatFormat)
    {
        if (c.IsNa(r)) return naRep;
        return c.Kind switch
        {
            Kind.Int => c.LongAt(r).ToString(CultureInfo.InvariantCulture),
            Kind.Float => FormatFloat(c.DoubleAt(r), floatFormat),
            Kind.Bool => c.BoolAt(r) ? "True" : "False",
            Kind.Str => c.StrAt(r)!,
            _ => Formatter.ObjectStr(c[r]),
        };
    }

    private static string ToCsv(Interp i, DataFrame d, Args p, int sepI, int naI, int ffI, int colsI, int headerI, int indexI, int labelI, int quoteI, int ltI)
    {
        char sep = p.Has(sepI) ? Ch(p[sepI], ',') : ',';
        string na = p.Has(naI) ? (string)p[naI]! : "";
        string? ff = p.Has(ffI) ? (string)p[ffI]! : null;
        char quote = p.Has(quoteI) ? Ch(p[quoteI], '"') : '"';
        string nl = p.Has(ltI) ? (string)p[ltI]! : Environment.NewLine;
        var frame = d;
        if (p.Has(colsI)) frame = d.TakeColumns(PdConv.Cells(p[colsI]!).Select(l => d.ColumnPositions(l)[0]).ToList());
        bool index = p.Bool(indexI, true);
        bool header = !(p.Has(headerI) && p[headerI] is false);
        var headerNames = p.Has(headerI) && PdConv.IsListLike(p[headerI]!) ? PdConv.Cells(p[headerI]!).Select(x => Formatter.ObjectStr(x)).ToList() : null;
        if (frame.Columns.IsMulti) throw PyErr.NotImplementedError("to_csv with MultiIndex columns");
        string Q(string s) => Csv.Quote(s, sep, quote);
        var sb = new StringBuilder();
        var idxCols = index ? Enumerable.Range(0, frame.Index.NLevels).Select(frame.Index.Level).ToList() : new List<Column>();
        if (header)
        {
            var cells = new List<string>();
            if (index)
            {
                var labels = p.Has(labelI) ? (PdConv.IsListLike(p[labelI]!) ? PdConv.Cells(p[labelI]!) : new List<object?> { PdConv.ToCell(p[labelI]) }) : frame.Index.Names.ToList();
                cells.AddRange(labels.Select(l => l is null ? "" : Formatter.ObjectStr(l)));
            }
            cells.AddRange(headerNames ?? frame.Columns.Items().Select(l => Formatter.ObjectStr(l)).ToList());
            sb.Append(string.Join(sep.ToString(), cells.Select(Q))).Append(nl);
        }
        for (int r = 0; r < frame.NRows; r++)
        {
            var cells = new List<string>();
            foreach (var ic in idxCols) cells.Add(CellText(ic, r, na, ff));
            foreach (var c in frame.Data) cells.Add(CellText(c, r, na, ff));
            sb.Append(string.Join(sep.ToString(), cells.Select(Q))).Append(nl);
        }
        return sb.ToString();
    }

    private static object Emit(Interp i, string text, object? target, string mode)
    {
        if (target is null or PyNone) return text;
        if (target is string path)
        {
            if (mode == "a") File.AppendAllText(path, text, new UTF8Encoding(false)); else File.WriteAllText(path, text, new UTF8Encoding(false));
            return PyNone.Instance;
        }
        if (target is PyInstance pi && pi.Class.Mro.Any(c => c.Dict.ContainsKey("write"))) { i.CallMethod(target, "write", new object[] { text }); return PyNone.Instance; }
        var p2 = PyOps.Str(i, target);
        File.WriteAllText(p2, text, new UTF8Encoding(false));
        return PyNone.Instance;
    }

    // ================================================================== dict / records

    private static PyDict Dict(IEnumerable<(object key, object value)> items)
    {
        var d = new PyDict();
        foreach (var (k, v) in items) d[k] = v;
        return d;
    }

    private static object ToDict(DataFrame d, string orient)
    {
        object Cell(Column c, int r) => PdConv.FromCell(c, r);
        object Lbl(FIndex ix, int r) => PdConv.FromLabel(ix.Labels[r]);
        switch (orient)
        {
            case "dict":
                return Dict(Enumerable.Range(0, d.NCols).Select(j => (Lbl(d.Columns, j), (object)Dict(Enumerable.Range(0, d.NRows).Select(r => (Lbl(d.Index, r), Cell(d.Data[j], r)))))));
            case "list":
                return Dict(Enumerable.Range(0, d.NCols).Select(j => (Lbl(d.Columns, j), (object)new PyList(Enumerable.Range(0, d.NRows).Select(r => Cell(d.Data[j], r))))));
            case "series":
                return Dict(Enumerable.Range(0, d.NCols).Select(j => (Lbl(d.Columns, j), (object)PdConv.Wrap(d.GetColumn(j)))));
            case "records":
                return new PyList(Enumerable.Range(0, d.NRows).Select(r => (object)Dict(Enumerable.Range(0, d.NCols).Select(j => (Lbl(d.Columns, j), Cell(d.Data[j], r))))));
            case "index":
                return Dict(Enumerable.Range(0, d.NRows).Select(r => (Lbl(d.Index, r), (object)Dict(Enumerable.Range(0, d.NCols).Select(j => (Lbl(d.Columns, j), Cell(d.Data[j], r)))))));
            case "split":
            case "tight":
            {
                var res = Dict(new (object, object)[]
                {
                    ("index", new PyList(Enumerable.Range(0, d.NRows).Select(r => Lbl(d.Index, r)))),
                    ("columns", new PyList(Enumerable.Range(0, d.NCols).Select(j => Lbl(d.Columns, j)))),
                    ("data", new PyList(Enumerable.Range(0, d.NRows).Select(r => (object)new PyList(Enumerable.Range(0, d.NCols).Select(j => Cell(d.Data[j], r)))))),
                });
                if (orient == "tight")
                {
                    res["index_names"] = new PyList(d.Index.Names.Select(PdConv.FromLabel));
                    res["column_names"] = new PyList(d.Columns.Names.Select(PdConv.FromLabel));
                }
                return res;
            }
            default: throw PyErr.ValueError($"orient '{orient}' not understood");
        }
    }

    private static object FromDict(Interp i, object[] a, Dictionary<string, object>? k)
    {
        var p = new Args("from_dict", i, a, k, "data", "orient", "dtype", "columns");
        string orient = p.Has(1) ? (string)p[1]! : "columns";
        var data = p.Required(0) as PyDict ?? throw PyErr.TypeError("from_dict expects a dict");
        if (orient == "columns") return PdConv.Wrap(PdBuild.MakeDataFrame(data, null, p.Has(3) ? p[3] : null, p.Has(2) ? p[2] : null));
        if (orient != "index") throw PyErr.NotImplementedError($"from_dict(orient='{orient}')");
        var keys = data.Keys.ToList();
        object?[] first = data.Values.First() is PyDict ? new object?[0] : new object?[0];
        DataFrame df;
        if (data.Values.All(v => v is PyDict))
        {
            // dict of dicts: outer keys are rows
            var rows = data.Values.Cast<PyDict>().Select(d => d).ToList();
            var records = new PyList(rows.Cast<object>());
            var cols = new List<object?>(); var seen = new HashSet<object>();
            foreach (var r in rows) foreach (var key in r.Keys.Select(PdConv.ToCell)) if (seen.Add(Column.Key(key) ?? Column.NaNKey)) cols.Add(key);
            var columns = cols.Select(c => Column.Infer(rows.Select(r => r.Entries.Where(e => Equals(Column.Key(PdConv.ToCell(e.Key)), Column.Key(c))).Select(e => PdConv.ToCell(e.Value)).DefaultIfEmpty(null).First()).ToList())).ToList();
            df = new DataFrame(columns, new FIndex(Column.Infer(cols)), new FIndex(Column.Infer(keys.Select(PdConv.ToCell).ToList())));
        }
        else
        {
            var rows = data.Values.Select(PdConv.Cells).ToList();
            int m = rows.Max(r => r.Count);
            var colIndex = p.Has(3) ? PdBuild.Index(p[3], m) : FIndex.Range(m);
            df = new DataFrame(Enumerable.Range(0, m).Select(j => Column.Infer(rows.Select(r => j < r.Count ? r[j] : null).ToList())), colIndex, new FIndex(Column.Infer(keys.Select(PdConv.ToCell).ToList())));
        }
        if (p.Has(2)) df = new DataFrame(df.Data.Select(c => PdConv.AsType(c, p[2]!)), df.Columns, df.Index);
        return PdConv.Wrap(df);
    }

    // ================================================================== install

    public static void Install(PyModule m)
    {
        m.Dict["read_csv"] = PdClasses.Fn("read_csv", (i, a, k) => ReadCsv(i, a, k, ','));
        m.Dict["read_table"] = PdClasses.Fn("read_table", (i, a, k) => ReadCsv(i, a, k, '\t'));

        PdClasses.DataFrame.Dict["to_csv"] = PdClasses.Fn("to_csv", (i, a, k) =>
        {
            var p = A("to_csv", i, a, k, "path_or_buf", "sep", "na_rep", "float_format", "columns", "header", "index", "index_label", "mode", "encoding", "quoting", "quotechar", "lineterminator");
            return Emit(i, ToCsv(i, PdConv.D(a[0]), p, 1, 2, 3, 4, 5, 6, 7, 11, 12), p[0], p.Has(8) ? (string)p[8]! : "w");
        });
        PdClasses.Series.Dict["to_csv"] = PdClasses.Fn("to_csv", (i, a, k) =>
        {
            var p = A("to_csv", i, a, k, "path_or_buf", "sep", "na_rep", "float_format", "header", "index", "index_label", "mode", "encoding", "quoting", "quotechar", "lineterminator");
            var s = PdConv.S(a[0]);
            var d = new DataFrame(new[] { s.Values }, new FIndex(Column.Infer(new object?[] { s.Name ?? 0L })), s.Index);
            return Emit(i, ToCsvSeries(i, d, p), p[0], p.Has(7) ? (string)p[7]! : "w");
        });
        PdClasses.DataFrame.Dict["to_dict"] = PdClasses.Fn("to_dict", (i, a, k) => ToDict(PdConv.D(a[0]), A("to_dict", i, a, k, "orient", "into", "index").Has(0) ? (string)A("to_dict", i, a, k, "orient")[0]! : "dict"));
        PdClasses.Series.Dict["to_dict"] = PdClasses.Fn("to_dict", (_, a, _) =>
        {
            var s = PdConv.S(a[0]);
            return Dict(Enumerable.Range(0, s.Length).Select(r => (PdConv.FromLabel(s.Index.Labels[r]), PdConv.FromCell(s.Values, r))));
        });
        PdClasses.DataFrame.Dict["to_json"] = PdClasses.Fn("to_json", (i, a, k) =>
        {
            var p = A("to_json", i, a, k, "path_or_buf", "orient", "date_format", "double_precision", "force_ascii", "date_unit", "default_handler", "lines", "compression", "index", "indent", "mode");
            string orient = p.Has(1) ? (string)p[1]! : "columns";
            if (orient == "table") throw PyErr.NotImplementedError("to_json(orient='table')");
            string text = Json.ToJson(PdConv.D(a[0]), orient, p.Int(3, 10), p.Int(10, 0), p.Bool(7, false), p.Bool(4, true), d => PyOps.ReprDouble(d));
            return Emit(i, text, p[0], p.Has(11) ? (string)p[11]! : "w");
        });
        PdClasses.Series.Dict["to_json"] = PdClasses.Fn("to_json", (i, a, k) =>
        {
            var p = A("to_json", i, a, k, "path_or_buf", "orient", "date_format", "double_precision", "force_ascii", "date_unit", "default_handler", "lines", "compression", "index", "indent", "mode");
            string orient = p.Has(1) ? (string)p[1]! : "index";
            string text = Json.ToJson(PdConv.S(a[0]), orient, p.Int(3, 10), p.Int(10, 0), p.Bool(4, true), d => PyOps.ReprDouble(d));
            return Emit(i, text, p[0], p.Has(11) ? (string)p[11]! : "w");
        });
        m.Dict["read_json"] = PdClasses.Fn("read_json", (i, a, k) =>
        {
            var p = new Args("read_json", i, a, k, "path_or_buf", "orient", "typ", "dtype", "convert_axes", "convert_dates", "keep_default_dates", "precise_float", "date_unit", "encoding", "lines", "chunksize", "compression", "nrows");
            string text = ReadSource(i, p.Required(0));
            string? orient = p.Has(1) ? (string)p[1]! : null;
            if (p.Has(2) && (string)p[2]! == "series") return PdConv.Wrap(Json.ReadSeries(text, orient));
            return PdConv.Wrap(Json.ReadFrame(text, orient, p.Bool(10, false)));
        });
        PdClasses.DataFrame.Dict["to_html"] = PdClasses.Fn("to_html", (i, a, k) =>
        {
            var p = A("to_html", i, a, k, "buf", "columns", "col_space", "header", "index", "na_rep", "formatters", "float_format");
            var d = PdConv.D(a[0]);
            if (p.Has(1)) d = d.TakeColumns(PdConv.Cells(p[1]!).Select(l => d.ColumnPositions(l)[0]).ToList());
            return Emit(i, Html.ToHtml(d, PdOptions.Display, false, p.Bool(4, true), false), p[0], "w");
        });
        PdClasses.DataFrame.Dict["_repr_html_"] = PdClasses.Fn("_repr_html_", (i, a, k) =>
        {
            var d = PdConv.D(a[0]);
            return Html.Supports(d) ? Html.ToHtml(d, PdOptions.Display, true) : PyNone.Instance;
        });
        PdClasses.DataFrame.Dict["from_dict"] = PdClasses.Fn("from_dict", FromDict);
        PdClasses.DataFrame.Dict["from_records"] = PdClasses.Fn("from_records", (i, a, k) =>
        {
            var p = new Args("from_records", i, a, k, "data", "index", "exclude", "columns", "coerce_float", "nrows");
            var df = PdBuild.MakeDataFrame(p.Required(0), null, p.Has(3) ? p[3] : null, null);
            if (p.Has(1))
            {
                var key = PdConv.ToCell(p[1]);
                int pos = df.ColumnPositions(key)[0];
                df = new DataFrame(df.Data.Where((c, n) => n != pos), df.Columns.Take(Enumerable.Range(0, df.NCols).Where(n => n != pos).ToList()), new FIndex(df.Data[pos], key));
            }
            return PdConv.Wrap(df);
        });
    }

    private static string ToCsvSeries(Interp i, DataFrame d, Args p)
    {
        // Series.to_csv: header is the Series name (or 0); same layout as a one-column frame
        char sep = p.Has(1) ? Ch(p[1], ',') : ',';
        string na = p.Has(2) ? (string)p[2]! : "";
        string? ff = p.Has(3) ? (string)p[3]! : null;
        string nl = p.Has(11) ? (string)p[11]! : Environment.NewLine;
        bool header = !(p.Has(4) && p[4] is false), index = p.Bool(5, true);
        string Q(string s) => Csv.Quote(s, sep, '"');
        var sb = new StringBuilder();
        var idxCols = index ? Enumerable.Range(0, d.Index.NLevels).Select(d.Index.Level).ToList() : new List<Column>();
        if (header)
        {
            var cells = new List<string>();
            if (index) cells.AddRange((p.Has(6) ? PdConv.Cells(p[6]!) : d.Index.Names.ToList()).Select(l => l is null ? "" : Formatter.ObjectStr(l)));
            cells.Add(Formatter.ObjectStr(d.Columns.Labels[0]));
            sb.Append(string.Join(sep.ToString(), cells.Select(Q))).Append(nl);
        }
        for (int r = 0; r < d.NRows; r++)
        {
            var cells = idxCols.Select(ic => CellText(ic, r, na, ff)).ToList();
            cells.Add(CellText(d.Data[0], r, na, ff));
            sb.Append(string.Join(sep.ToString(), cells.Select(Q))).Append(nl);
        }
        return sb.ToString();
    }
}
