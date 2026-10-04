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

/// <summary>The Python classes of the pandas binding: Series, DataFrame, Index and the loc/iloc/at/iat accessors.</summary>
internal static class PdClasses
{
    public static readonly PyClass Series = new("Series", new List<PyClass>());
    public static readonly PyClass DataFrame = new("DataFrame", new List<PyClass>());
    public static readonly PyClass Index = new("Index", new List<PyClass>());
    public static readonly PyClass AccessorClass = new("_LocIndexer", new List<PyClass>());

    internal sealed record AccessorState(object Owner, string Kind); // Kind: loc, iloc, at, iat

    static PdClasses()
    {
        Formatter.ObjectStr = o => o switch
        {
            null => "None",
            bool b => b ? "True" : "False",
            string s => s,
            long l => l.ToString(),
            double d => PyOps.ReprDouble(d),
            IntervalValue iv => iv.ToString(),
            PyList l => "[" + string.Join(", ", l.Items.Select(x => Formatter.ObjectStr(PdConv.ToCell(x)))) + "]",
            PyTuple t => "(" + string.Join(", ", t.Items.Select(x => Formatter.ObjectStr(PdConv.ToCell(x)))) + (t.Items.Length == 1 ? ",)" : ")"),
            _ => PyOps.Str(PdConv.Interp!, o),
        };
        BuildSeries();
        BuildDataFrame();
        BuildIndex();
        BuildAccessor();
        PdOps.Install();
        PdWrangle.Install();
        PdApply.Install();
        PdStats.Install();
        PdGroupBy.Install();
        PdWindow.Install();
        PdPlot.Install();
        Conv.Converters.Add(o => o switch
        {
            PyInstance { Native: Series s } => PdArrays.ToNd(s.Values),
            PyInstance { Native: DataFrame d } => PdArrays.ToNd(d),
            PyInstance { Native: FIndex ix } => PdArrays.ToNd(ix.Labels),
            _ => null,
        });
    }

    // ================================================================== helpers

    public static PyBuiltinFunction Fn(string name, BuiltinFn fn) => new(name, (interp, a, kw) =>
    {
        PdConv.Interp = interp;
        Native.Current = interp;
        try { return fn(interp, a, kw); }
        catch (FrameException ex) { throw Map(ex); }
        catch (NDException ex) { throw Native.Translate(ex); }
        catch (KeyNotFoundException ex) { throw PyErr.Raise(PyErr.KeyErrorClass, ex.Message); }
    });

    private static PyRaise Map(FrameException ex) => ex.PyType switch
    {
        "TypeError" => PyErr.TypeError(ex.Message),
        "KeyError" => PyErr.Raise(PyErr.KeyErrorClass, ex.Message),
        "IndexError" => PyErr.IndexError(ex.Message),
        "NotImplementedError" => PyErr.NotImplementedError(ex.Message),
        "DateParseError" => PyErr.Raise(PdTime.DateParseErrorClass, ex.Message),
        _ => PyErr.ValueError(ex.Message),
    };

    internal static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names)
        => new(fn, i, a.Skip(1).ToArray(), k, names);

    private static PyProperty Prop(Func<object, object> get, Action<Interp, object, object>? set = null) => Native.Prop(get, set);

    private static DisplayOptions Opts => PdOptions.Display;

    // ================================================================== Series

    private static void BuildSeries()
    {
        var cls = Series;
        void Def(string n, BuiltinFn f) => cls.Dict[n] = Fn($"Series.{n}", f);
        Series Me(object o) => PdConv.S(o);

        cls.Dict["__new__"] = Fn("Series.__new__", (i, a, k) =>
        {
            var p = A("Series", i, a, k, "data", "index", "dtype", "name", "copy");
            return PdConv.Wrap(PdBuild.MakeSeries(p[0], p[1], p[2], p[3]));
        });
        Def("__init__", (_, _, _) => PyNone.Instance);
        Def("__repr__", (_, a, _) => Formatter.SeriesRepr(Me(a[0]), Opts));
        Def("__str__", (_, a, _) => Formatter.SeriesRepr(Me(a[0]), Opts));
        Def("__len__", (_, a, _) => new BigInteger(Me(a[0]).Length));
        Def("__iter__", (_, a, _) =>
        {
            var s = Me(a[0]);
            return new PyIterator(Enumerable.Range(0, s.Length).Select(x => PdConv.FromCell(s.Values, x)).GetEnumerator());
        });
        Def("__contains__", (_, a, _) => Me(a[0]).Index.Contains(PdConv.ToCell(a[1])));
        Def("__bool__", (_, _, _) => throw PyErr.ValueError("The truth value of a Series is ambiguous. Use a.empty, a.bool(), a.item(), a.any() or a.all()."));
        Def("__hash__", (_, _, _) => throw PyErr.TypeError("unhashable type: 'Series'"));
        Def("__getitem__", (_, a, _) => PdAccess.SeriesGet(Me(a[0]), a[1], PdAccess.Mode.Bracket));
        Def("__setitem__", (_, a, _) => { PdAccess.SeriesSet(Me(a[0]), a[1], a[2], PdAccess.Mode.Bracket); return PyNone.Instance; });
        Def("__getattr__", (_, a, _) =>
        {
            string name = (string)a[1];
            throw PyErr.AttributeError($"'Series' object has no attribute '{name}'");
        });

        cls.Dict["index"] = Prop(s => PdConv.Wrap(Me(s).Index), (_, s, v) => Me(s).Index = PdBuild.Index(v, Me(s).Length));
        cls.Dict["name"] = Prop(s => PdConv.FromLabel(Me(s).Name), (_, s, v) => Me(s).Name = PdConv.ToCell(v));
        cls.Dict["dtype"] = Prop(s => PdConv.WrapDType(Me(s).Values));
        cls.Dict["shape"] = Prop(s => new PyTuple(new object[] { new BigInteger(Me(s).Length) }));
        cls.Dict["size"] = Prop(s => new BigInteger(Me(s).Length));
        cls.Dict["ndim"] = Prop(_ => new BigInteger(1));
        cls.Dict["empty"] = Prop(s => Me(s).Length == 0);
        cls.Dict["T"] = Prop(s => s);
        cls.Dict["values"] = Prop(s => PdArrays.Values(Me(s).Values));
        cls.Dict["is_unique"] = Prop(s => Enumerable.Range(0, Me(s).Length).Select(x => Column.Key(Me(s).Values[x])).Distinct().Count() == Me(s).Length);
        cls.Dict["hasnans"] = Prop(s => Me(s).IsNa().Any(x => x));
        cls.Dict["loc"] = Prop(s => MakeAccessor("loc", s));
        cls.Dict["iloc"] = Prop(s => MakeAccessor("iloc", s));
        cls.Dict["at"] = Prop(s => MakeAccessor("at", s));
        cls.Dict["iat"] = Prop(s => MakeAccessor("iat", s));

        Def("head", (i, a, k) => PdConv.Wrap(Me(a[0]).Head(A("head", i, a, k, "n").Int(0, 5))));
        Def("tail", (i, a, k) => PdConv.Wrap(Me(a[0]).Tail(A("tail", i, a, k, "n").Int(0, 5))));
        Def("copy", (_, a, _) => PdConv.Wrap(Me(a[0]).Copy()));
        Def("tolist", (_, a, _) => { var s = Me(a[0]); return new PyList(Enumerable.Range(0, s.Length).Select(x => PdConv.FromCell(s.Values, x))); });
        cls.Dict["to_list"] = cls.Dict["tolist"];
        Def("to_numpy", (_, a, _) => PdArrays.Values(Me(a[0]).Values));
        Def("astype", (i, a, k) =>
        {
            var p = A("astype", i, a, k, "dtype", "copy", "errors");
            var s = Me(a[0]);
            return PdConv.Wrap(new Series(PdConv.AsType(s.Values, p.Required(0)), s.Index, s.Name));
        });
        Def("equals", (_, a, _) =>
        {
            if (a[1] is not PyInstance { Native: Series o }) return false;
            var s = Me(a[0]);
            if (s.Length != o.Length || s.DType != o.DType || !PdSelect.SameLabels(s.Index, o.Index)) return false;
            for (int x = 0; x < s.Length; x++)
            {
                bool na1 = s.Values.IsNa(x), na2 = o.Values.IsNa(x);
                if (na1 != na2 || (!na1 && !Equals(Column.Key(s.Values[x]), Column.Key(o.Values[x])))) return false;
            }
            return true;
        });
        Def("rename", (i, a, k) =>
        {
            var p = A("rename", i, a, k, "index");
            var s = Me(a[0]);
            if (p.Has(0) && p[0] is not (PyDict or PyBuiltinFunction or PyFunction)) return PdConv.Wrap(s.Rename(PdConv.ToCell(p[0])));
            throw PyErr.NotImplementedError("Series.rename with a mapping or function");
        });
        Def("to_string", (i, a, k) =>
        {
            var p = A("to_string", i, a, k, "buf", "na_rep", "float_format", "header", "index", "length", "dtype", "name", "max_rows");
            var s = Me(a[0]);
            var o = Opts.Clone(); o.MaxRows = 0;
            string body = Formatter.SeriesRepr(s, o);
            var lines = body.Split('\n').ToList();
            // drop the footer unless asked for
            lines.RemoveAt(lines.Count - 1);
            return string.Join("\n", lines);
        });
    }

    // ================================================================== DataFrame

    private static void BuildDataFrame()
    {
        var cls = DataFrame;
        void Def(string n, BuiltinFn f) => cls.Dict[n] = Fn($"DataFrame.{n}", f);
        DataFrame Me(object o) => PdConv.D(o);

        cls.Dict["__new__"] = Fn("DataFrame.__new__", (i, a, k) =>
        {
            var p = A("DataFrame", i, a, k, "data", "index", "columns", "dtype", "copy");
            return PdConv.Wrap(PdBuild.MakeDataFrame(p[0], p[1], p[2], p[3]));
        });
        Def("__init__", (_, _, _) => PyNone.Instance);
        Def("__repr__", (_, a, _) => Formatter.FrameRepr(Me(a[0]), Opts));
        Def("__str__", (_, a, _) => Formatter.FrameRepr(Me(a[0]), Opts));
        Def("__len__", (_, a, _) => new BigInteger(Me(a[0]).NRows));
        Def("__iter__", (_, a, _) => new PyIterator(Me(a[0]).Columns.Items().Select(PdConv.FromLabel).GetEnumerator()));
        Def("__contains__", (_, a, _) => Me(a[0]).HasColumn(PdConv.ToCell(a[1])));
        Def("__bool__", (_, _, _) => throw PyErr.ValueError("The truth value of a DataFrame is ambiguous. Use a.empty, a.bool(), a.item(), a.any() or a.all()."));
        Def("__hash__", (_, _, _) => throw PyErr.TypeError("unhashable type: 'DataFrame'"));
        Def("__getitem__", (_, a, _) => PdAccess.FrameGet(Me(a[0]), a[1]));
        Def("__setitem__", (_, a, _) => { PdAccess.FrameSetItem(Me(a[0]), a[1], a[2]); return PyNone.Instance; });
        Def("__delitem__", (_, a, _) =>
        {
            var d = Me(a[0]);
            var dropped = d.Drop(new[] { PdConv.ToCell(a[1]) }, true);
            d.Data.Clear(); d.Data.AddRange(dropped.Data); d.Columns = dropped.Columns;
            return PyNone.Instance;
        });
        Def("__getattr__", (_, a, _) =>
        {
            string name = (string)a[1];
            var d = Me(a[0]);
            if (!name.StartsWith('_') && d.HasColumn(name)) return PdConv.Wrap(d.GetColumn((object?)name));
            throw PyErr.AttributeError($"'DataFrame' object has no attribute '{name}'");
        });

        cls.Dict["index"] = Prop(d => PdConv.Wrap(Me(d).Index), (_, d, v) =>
        {
            var df = Me(d); var ix = PdBuild.Index(v, df.NRows);
            if (ix.Length != df.NRows) throw PyErr.ValueError($"Length mismatch: Expected axis has {df.NRows} elements, new values have {ix.Length} elements");
            df.Index = ix;
        });
        cls.Dict["columns"] = Prop(d => PdConv.Wrap(Me(d).Columns), (_, d, v) =>
        {
            var df = Me(d); var ix = PdBuild.Index(v, df.NCols);
            if (ix.Length != df.NCols) throw PyErr.ValueError($"Length mismatch: Expected axis has {df.NCols} elements, new values have {ix.Length} elements");
            df.Columns = ix;
        });
        cls.Dict["shape"] = Prop(d => new PyTuple(new object[] { new BigInteger(Me(d).NRows), new BigInteger(Me(d).NCols) }));
        cls.Dict["size"] = Prop(d => new BigInteger((long)Me(d).NRows * Me(d).NCols));
        cls.Dict["ndim"] = Prop(_ => new BigInteger(2));
        cls.Dict["empty"] = Prop(d => Me(d).NRows == 0 || Me(d).NCols == 0);
        cls.Dict["dtypes"] = Prop(d =>
        {
            var df = Me(d);
            return PdConv.Wrap(new Series(Column.FromObjects(df.Data.Select(c => (object?)PdConv.WrapDType(c)).ToArray()), df.Columns, null));
        });
        cls.Dict["values"] = Prop(d => PdArrays.Values(Me(d)));
        cls.Dict["loc"] = Prop(d => MakeAccessor("loc", d));
        cls.Dict["iloc"] = Prop(d => MakeAccessor("iloc", d));
        cls.Dict["at"] = Prop(d => MakeAccessor("at", d));
        cls.Dict["iat"] = Prop(d => MakeAccessor("iat", d));

        Def("head", (i, a, k) => PdConv.Wrap(Me(a[0]).Head(A("head", i, a, k, "n").Int(0, 5))));
        Def("tail", (i, a, k) => PdConv.Wrap(Me(a[0]).Tail(A("tail", i, a, k, "n").Int(0, 5))));
        Def("copy", (_, a, _) => PdConv.Wrap(Me(a[0]).Copy()));
        Def("to_numpy", (_, a, _) => PdArrays.Values(Me(a[0])));
        Def("astype", (i, a, k) =>
        {
            var p = A("astype", i, a, k, "dtype", "copy", "errors");
            var d = Me(a[0]);
            if (p.Required(0) is PyDict map)
            {
                var cols = d.Data.ToList();
                foreach (var (key, val) in map.Entries)
                    foreach (var pos in d.ColumnPositions(PdConv.ToCell(key))) cols[pos] = PdConv.AsType(cols[pos], val);
                return PdConv.Wrap(new DataFrame(cols, d.Columns, d.Index));
            }
            return PdConv.Wrap(new DataFrame(d.Data.Select(c => PdConv.AsType(c, p.Required(0))), d.Columns, d.Index));
        });
        Def("drop", (i, a, k) =>
        {
            var p = A("drop", i, a, k, "labels", "axis", "index", "columns", "level", "inplace", "errors");
            var d = Me(a[0]);
            DataFrame r = d;
            object? axis = p[1];
            bool colsAxis = axis is "columns" || (axis is BigInteger ab && ab == 1);
            if (p.Has(0)) r = r.Drop(LabelList(p[0]!), colsAxis);
            if (p.Has(2)) r = r.Drop(LabelList(p[2]!), false);
            if (p.Has(3)) r = r.Drop(LabelList(p[3]!), true);
            if (p.Bool(5, false)) { d.Data.Clear(); d.Data.AddRange(r.Data); d.Columns = r.Columns; d.Index = r.Index; return PyNone.Instance; }
            return PdConv.Wrap(r);
        });
        Def("insert", (i, a, k) =>
        {
            var p = A("insert", i, a, k, "loc", "column", "value", "allow_duplicates");
            var d = Me(a[0]);
            d.InsertColumn(p.Int(0, 0), PdConv.ToCell(p.Required(1)), PdBuild.ColumnFor(d, p.Required(2)));
            return PyNone.Instance;
        });
        Def("assign", (i, a, k) =>
        {
            var d = Me(a[0]).Copy();
            if (k is not null)
                foreach (var (name, v) in k)
                {
                    object val = v is PyFunction or PyBuiltinFunction ? i.Call(v, new object[] { PdConv.Wrap(d) }) : v;
                    d.SetColumn(name, PdBuild.ColumnFor(d, val));
                }
            return PdConv.Wrap(d);
        });
        Def("equals", (_, a, _) =>
        {
            if (a[1] is not PyInstance { Native: DataFrame o }) return false;
            var d = Me(a[0]);
            if (d.NRows != o.NRows || d.NCols != o.NCols || !PdSelect.SameLabels(d.Index, o.Index) || !PdSelect.SameLabels(d.Columns, o.Columns)) return false;
            for (int c = 0; c < d.NCols; c++)
            {
                if (d.Data[c].DTypeName != o.Data[c].DTypeName) return false;
                for (int r = 0; r < d.NRows; r++)
                {
                    bool na1 = d.Data[c].IsNa(r), na2 = o.Data[c].IsNa(r);
                    if (na1 != na2 || (!na1 && !Equals(Column.Key(d.Data[c][r]), Column.Key(o.Data[c][r])))) return false;
                }
            }
            return true;
        });
        Def("to_string", (i, a, k) =>
        {
            var d = Me(a[0]);
            var o = Opts.Clone(); o.MaxRows = 0; o.MaxColumns = 0; o.AutoFit = false; o.ShowDimensions = "False";
            return Formatter.FrameRepr(d, o);
        });
    }

    private static List<object?> LabelList(object o) => PdConv.IsListLike(o) ? PdConv.Cells(o) : new List<object?> { PdConv.ToCell(o) };

    // ================================================================== Index

    private static void BuildIndex()
    {
        var cls = Index;
        void Def(string n, BuiltinFn f) => cls.Dict[n] = Fn($"Index.{n}", f);
        FIndex Me(object o) => (FIndex)((PyInstance)o).Native!;

        cls.Dict["__new__"] = Fn("Index.__new__", (i, a, k) =>
        {
            var p = A("Index", i, a, k, "data", "dtype", "copy", "name");
            var ix = PdBuild.Index(p.Has(0) ? p[0]! : new PyList(), -1);
            if (p.Has(1)) ix = new FIndex(PdConv.AsType(ix.Labels, p[1]!), ix.Name);
            if (p.Has(3)) ix = ix.WithName(PdConv.ToCell(p[3]));
            return PdConv.Wrap(ix);
        });
        Def("__init__", (_, _, _) => PyNone.Instance);
        Def("__repr__", (_, a, _) => Formatter.IndexRepr(Me(a[0]), Opts));
        Def("__str__", (_, a, _) => Formatter.IndexRepr(Me(a[0]), Opts));
        Def("__len__", (_, a, _) => new BigInteger(Me(a[0]).Length));
        Def("__iter__", (_, a, _) => new PyIterator(Me(a[0]).Items().Select(PdConv.FromLabel).GetEnumerator()));
        Def("__contains__", (_, a, _) => Me(a[0]).Contains(PdConv.ToCell(a[1])));
        Def("__hash__", (_, _, _) => throw PyErr.TypeError("unhashable type: 'Index'"));
        Def("__getitem__", (_, a, _) =>
        {
            var ix = Me(a[0]);
            if (a[1] is PySlice sl) return PdConv.Wrap(ix.Take(PdSelect.PositionalSlice(sl, ix.Length)));
            if (PdConv.IsInt(a[1]))
            {
                int p = PdConv.ToInt(a[1]); if (p < 0) p += ix.Length;
                if (p < 0 || p >= ix.Length) throw PyErr.IndexError("index " + PdConv.ToInt(a[1]) + " is out of bounds for axis 0 with size " + ix.Length);
                return PdConv.FromLabel(ix.Labels[p]);
            }
            var mask = PdSelect.TryMask(a[1]);
            if (mask is not null) return PdConv.Wrap(ix.Take(PdSelect.MaskPositions(mask)));
            return PdConv.Wrap(ix.Take(PdConv.Cells(a[1]).Select(c => (int)(long)c!).ToArray()));
        });
        void Cmp(string dunder, Func<NDArray, NDArray, NDArray> f, bool? strEq = null) => Def(dunder, (_, a, _) =>
        {
            var ix = Me(a[0]);
            if (strEq is bool eq && ix.Labels.Kind == Kind.Str && a[1] is string str)
                return Conv.Wrap(NDArray.FromArray(Enumerable.Range(0, ix.Length).Select(x => (ix.Labels.StrAt(x) == str) == eq).ToArray()));
            if (!Conv.TryND(a[1], out var other)) return PyNotImplemented.Instance;
            return Conv.Wrap(f(PdArrays.ToNd(ix.Labels), other));
        });
        Cmp("__gt__", np.Greater); Cmp("__ge__", np.GreaterEqual); Cmp("__lt__", np.Less); Cmp("__le__", np.LessEqual);
        Cmp("__eq__", np.Equal, true); Cmp("__ne__", np.NotEqual, false);
        cls.Dict["name"] = Prop(i => PdConv.FromLabel(Me(i).Name), (_, i, v) => Me(i).Name = PdConv.ToCell(v));
        cls.Dict["names"] = Prop(i => new PyList(Me(i).Names.Select(PdConv.FromLabel)), (_, i, v) => Me(i).Names = PdConv.Cells(v).ToArray());
        cls.Dict["nlevels"] = Prop(i => new BigInteger(Me(i).NLevels));
        Def("get_level_values", (i, a, k) =>
        {
            var ix = Me(a[0]);
            int lvl = LevelNumber(ix, A("get_level_values", i, a, k, "level").Required(0));
            return PdConv.Wrap(new FIndex(ix.Level(lvl), ix.Names[lvl]));
        });
        Def("droplevel", (i, a, k) =>
        {
            var ix = Me(a[0]);
            var lv = A("droplevel", i, a, k, "level").Has(0) ? A("droplevel", i, a, k, "level")[0]! : (object)new BigInteger(0);
            var list = PdConv.IsListLike(lv) ? PdConv.Cells(lv).Select(x => LevelNumber(ix, PdConv.FromLabel(x))).ToList() : new List<int> { LevelNumber(ix, lv) };
            return PdConv.Wrap(ix.DropLevelList(list));
        });
        Def("set_names", (i, a, k) =>
        {
            var ix = Me(a[0]);
            var nm = A("set_names", i, a, k, "names", "level").Required(0);
            var names = PdConv.IsListLike(nm) ? PdConv.Cells(nm) : new List<object?> { PdConv.ToCell(nm) };
            return PdConv.Wrap(ix.WithNames(names));
        });
        cls.Dict["dtype"] = Prop(i => Me(i).IsRange ? Classes.DTypeObject(DType.Int64) : PdConv.WrapDType(Me(i).Labels));
        cls.Dict["shape"] = Prop(i => new PyTuple(new object[] { new BigInteger(Me(i).Length) }));
        cls.Dict["size"] = Prop(i => new BigInteger(Me(i).Length));
        cls.Dict["ndim"] = Prop(_ => new BigInteger(1));
        cls.Dict["values"] = Prop(i => PdArrays.Values(Me(i).Labels));
        cls.Dict["is_unique"] = Prop(i => Me(i).IsUnique);
        cls.Dict["empty"] = Prop(i => Me(i).Length == 0);
        Def("tolist", (_, a, _) => new PyList(Me(a[0]).Items().Select(PdConv.FromLabel)));
        cls.Dict["to_list"] = cls.Dict["tolist"];
        Def("to_numpy", (_, a, _) => PdArrays.Values(Me(a[0]).Labels));
        Def("get_loc", (_, a, _) =>
        {
            var l = Me(a[0]).Locs(PdConv.ToCell(a[1]));
            if (l.Count == 0) throw PdConv.KeyErr(a[1]);
            return new BigInteger(l[0]);
        });
        Def("equals", (_, a, _) => a[1] is PyInstance { Native: FIndex o } && PdSelect.SameLabels(Me(a[0]), o));
        Def("copy", (_, a, _) => PdConv.Wrap(Me(a[0])));
        Def("astype", (i, a, k) => PdConv.Wrap(new FIndex(PdConv.AsType(Me(a[0]).Labels, A("astype", i, a, k, "dtype").Required(0)), Me(a[0]).Name)));
        Def("rename", (i, a, k) => PdConv.Wrap(Me(a[0]).WithName(PdConv.ToCell(A("rename", i, a, k, "name").Required(0)))));
    }

    // ================================================================== loc / iloc / at / iat

    internal static int LevelNumber(FIndex ix, object level)
    {
        if (PdConv.IsInt(level)) { int l = PdConv.ToInt(level); return l < 0 ? l + ix.NLevels : l; }
        var cell = PdConv.ToCell(level);
        for (int k = 0; k < ix.NLevels; k++) if (Equals(ix.Names[k], cell)) return k;
        throw PyErr.KeyError(level);
    }

    private static PyInstance MakeAccessor(string kind, object owner) => new(AccessorClass) { Native = new AccessorState(owner, kind) };

    private static void BuildAccessor()
    {
        var cls = AccessorClass;
        AccessorState St(object o) => (AccessorState)((PyInstance)o).Native!;
        cls.Dict["__getitem__"] = Fn("accessor.__getitem__", (_, a, _) =>
        {
            var st = St(a[0]);
            var mode = st.Kind switch { "loc" => PdAccess.Mode.Loc, "iloc" => PdAccess.Mode.ILoc, "at" => PdAccess.Mode.At, _ => PdAccess.Mode.IAt };
            return ((PyInstance)st.Owner).Native is Series s ? PdAccess.SeriesGet(s, a[1], mode) : PdAccess.FrameLoc(PdConv.D(st.Owner), a[1], mode);
        });
        cls.Dict["__setitem__"] = Fn("accessor.__setitem__", (_, a, _) =>
        {
            var st = St(a[0]);
            var mode = st.Kind switch { "loc" => PdAccess.Mode.Loc, "iloc" => PdAccess.Mode.ILoc, "at" => PdAccess.Mode.At, _ => PdAccess.Mode.IAt };
            if (((PyInstance)st.Owner).Native is Series s) PdAccess.SeriesSet(s, a[1], a[2], mode);
            else PdAccess.FrameLocSet(PdConv.D(st.Owner), a[1], a[2], mode);
            return PyNone.Instance;
        });
    }
}
