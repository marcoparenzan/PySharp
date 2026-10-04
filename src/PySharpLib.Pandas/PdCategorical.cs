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

/// <summary>The categorical dtype description (<c>pd.CategoricalDtype</c>).</summary>
internal sealed class CatDtype
{
    public Column? Categories { get; init; }
    public bool Ordered { get; init; }
}

/// <summary>Categorical data: <c>pd.Categorical</c>, <c>CategoricalDtype</c>, the <c>.cat</c> accessor, <c>Interval</c>, <c>pd.cut</c> and <c>pd.qcut</c>.</summary>
internal static class PdCategorical
{
    public static readonly PyClass DtypeClass = new("CategoricalDtype", new List<PyClass>());
    public static readonly PyClass CategoricalClass = new("Categorical", new List<PyClass>());
    public static readonly PyClass CatAccessor = new("CategoricalAccessor", new List<PyClass>());
    public static readonly PyClass IntervalClass = new("Interval", new List<PyClass>());

    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);

    // ------------------------------------------------------------------ wrapping

    public static PyInstance WrapInterval(IntervalValue iv) => new(IntervalClass) { Native = iv };
    public static PyInstance WrapCategorical(Column c) => new(CategoricalClass) { Native = c };
    public static PyInstance WrapDType(Column c) => new(DtypeClass) { Native = new CatDtype { Categories = c.Categories, Ordered = c.Ordered } };

    private static string CatsRepr(Column cats)
        => "[" + string.Join(", ", Enumerable.Range(0, cats.Length).Select(i => cats.Kind == Kind.Str ? "'" + cats.StrAt(i) + "'" : Formatter.ObjectStr(cats[i]))) + "]";

    private static Column Cat(object o) => o is PyInstance { Native: Column c } ? c : o is PyInstance { Native: Series { Values: var v } } ? v : throw PyErr.TypeError("expected categorical data");

    private static Column SeriesCat(object self)
    {
        var s = (Series)((PyInstance)self).Native!;
        if (s.Values.Kind != Kind.Category) throw PyErr.AttributeError("Can only use .cat accessor with a 'category' dtype");
        return s.Values;
    }

    private static Series Owner(object accessor) => (Series)((PyInstance)accessor).Native!;

    private static object Rewrap(object accessor, Column c) { var s = Owner(accessor); return PdConv.Wrap(new Series(c, s.Index, s.Name)); }

    /// <summary>A dtype argument as categories + ordered (a <see cref="CatDtype"/> instance), or null when it is another dtype.</summary>
    public static CatDtype? AsCatDtype(object? dtype) => dtype is PyInstance { Native: CatDtype d } ? d : null;

    // ------------------------------------------------------------------ install

    public static void Install(PyModule? m)
    {
        // ---- CategoricalDtype
        DtypeClass.Dict["__repr__"] = PdClasses.Fn("CategoricalDtype.__repr__", (_, a, _) =>
        {
            var d = (CatDtype)((PyInstance)a[0]).Native!;
            if (d.Categories is null) return $"CategoricalDtype(categories=None, ordered={(d.Ordered ? "True" : "False")}, categories_dtype=None)";
            return $"CategoricalDtype(categories={CatsRepr(d.Categories)}, ordered={(d.Ordered ? "True" : "False")}, categories_dtype={d.Categories.DTypeName})";
        });
        DtypeClass.Dict["__str__"] = PdClasses.Fn("CategoricalDtype.__str__", (_, _, _) => "category");
        DtypeClass.Dict["__hash__"] = PdClasses.Fn("CategoricalDtype.__hash__", (_, _, _) => new BigInteger(17));
        DtypeClass.Dict["__eq__"] = PdClasses.Fn("CategoricalDtype.__eq__", (_, a, _) =>
        {
            var d = (CatDtype)((PyInstance)a[0]).Native!;
            if (a[1] is string s) return s == "category";
            if (a[1] is PyInstance { Native: CatDtype o })
                return d.Ordered == o.Ordered && (d.Categories is null || o.Categories is null || Column.SameCategories(d.Categories, o.Categories));
            return false;
        });
        DtypeClass.Dict["__ne__"] = PdClasses.Fn("CategoricalDtype.__ne__", (i, a, k) => !(bool)((PyBuiltinFunction)DtypeClass.Dict["__eq__"]).Fn(i, a, k));
        DtypeClass.Dict["name"] = new PyProperty { Getter = PdClasses.Fn("name", (_, _, _) => "category") };
        DtypeClass.Dict["ordered"] = new PyProperty { Getter = PdClasses.Fn("ordered", (_, a, _) => ((CatDtype)((PyInstance)a[0]).Native!).Ordered) };
        DtypeClass.Dict["categories"] = new PyProperty
        {
            Getter = PdClasses.Fn("categories", (_, a, _) =>
            {
                var d = (CatDtype)((PyInstance)a[0]).Native!;
                return d.Categories is null ? PyNone.Instance : PdConv.Wrap(new FIndex(d.Categories));
            }),
        };
        DtypeClass.Dict["__new__"] = PdClasses.Fn("CategoricalDtype.__new__", (i, a, k) =>
        {
            var p = new Args("CategoricalDtype", i, a.Skip(1).ToArray(), k, "categories", "ordered");
            Column? cats = p.Has(0) ? PdConv.ToColumn(p[0]!) : null;
            return new PyInstance(DtypeClass) { Native = new CatDtype { Categories = cats, Ordered = p.Bool(1, false) } };
        });
        DtypeClass.Dict["__init__"] = new PyBuiltinFunction("CategoricalDtype.__init__", (_, _, _) => PyNone.Instance);

        // ---- Interval
        IntervalClass.Dict["__repr__"] = PdClasses.Fn("Interval.__repr__", (_, a, _) =>
        {
            var iv = (IntervalValue)((PyInstance)a[0]).Native!;
            string N(object v) => v is long l ? l.ToString() : PyOps.ReprDouble((double)v);
            return $"Interval({N(iv.Left)}, {N(iv.Right)}, closed='{iv.Closed}')";
        });
        IntervalClass.Dict["__str__"] = PdClasses.Fn("Interval.__str__", (_, a, _) => ((IntervalValue)((PyInstance)a[0]).Native!).Text());
        IntervalClass.Dict["__hash__"] = PdClasses.Fn("Interval.__hash__", (_, a, _) => new BigInteger(((IntervalValue)((PyInstance)a[0]).Native!).GetHashCode()));
        IntervalClass.Dict["__eq__"] = PdClasses.Fn("Interval.__eq__", (_, a, _) => a[1] is PyInstance { Native: IntervalValue o } && ((IntervalValue)((PyInstance)a[0]).Native!).Equals(o));
        IntervalClass.Dict["__contains__"] = PdClasses.Fn("Interval.__contains__", (_, a, _) => ((IntervalValue)((PyInstance)a[0]).Native!).Contains(Column.ToDouble(PdConv.ToCell(a[1])!)));
        IntervalClass.Dict["left"] = new PyProperty { Getter = PdClasses.Fn("left", (_, a, _) => PdConv.FromLabel(((IntervalValue)((PyInstance)a[0]).Native!).Left)) };
        IntervalClass.Dict["right"] = new PyProperty { Getter = PdClasses.Fn("right", (_, a, _) => PdConv.FromLabel(((IntervalValue)((PyInstance)a[0]).Native!).Right)) };
        IntervalClass.Dict["closed"] = new PyProperty { Getter = PdClasses.Fn("closed", (_, a, _) => ((IntervalValue)((PyInstance)a[0]).Native!).Closed) };
        IntervalClass.Dict["mid"] = new PyProperty { Getter = PdClasses.Fn("mid", (_, a, _) => { var iv = (IntervalValue)((PyInstance)a[0]).Native!; return (iv.LeftD + iv.RightD) / 2.0; }) };
        IntervalClass.Dict["length"] = new PyProperty
        {
            Getter = PdClasses.Fn("length", (_, a, _) => { var iv = (IntervalValue)((PyInstance)a[0]).Native!; return iv.IsInt ? new BigInteger((long)iv.Right - (long)iv.Left) : (object)(iv.RightD - iv.LeftD); }),
        };

        // ---- Categorical
        var cls = CategoricalClass;
        void Def(string n, BuiltinFn f) => cls.Dict[n] = PdClasses.Fn($"Categorical.{n}", f);
        cls.Dict["__new__"] = PdClasses.Fn("Categorical.__new__", (i, a, k) =>
        {
            var p = new Args("Categorical", i, a.Skip(1).ToArray(), k, "values", "categories", "ordered", "dtype", "fastpath", "copy");
            return WrapCategorical(MakeCategorical(p.Required(0), p.Has(1) ? p[1] : null, p.Has(2) ? p.Bool(2, false) : (bool?)null, p.Has(3) ? p[3] : null));
        });
        Def("__init__", (_, _, _) => PyNone.Instance);
        Def("__len__", (_, a, _) => new BigInteger(Cat(a[0]).Length));
        Def("__iter__", (_, a, _) => { var c = Cat(a[0]); return new PyIterator(Enumerable.Range(0, c.Length).Select(x => PdConv.FromCell(c, x)).GetEnumerator()); });
        Def("__getitem__", (_, a, _) =>
        {
            var c = Cat(a[0]);
            if (PdConv.IsInt(a[1])) { int p = PdConv.ToInt(a[1]); if (p < 0) p += c.Length; if (p < 0 || p >= c.Length) throw PyErr.IndexError("index out of bounds"); return PdConv.FromCell(c, p); }
            if (a[1] is PySlice sl) return WrapCategorical(c.Take(PdSelect.PositionalSlice(sl, c.Length)));
            var mask = PdSelect.TryMask(a[1]);
            return WrapCategorical(c.Take(mask is not null ? PdSelect.MaskPositions(mask) : PdConv.Cells(a[1]).Select(x => (int)(long)x!).ToArray()));
        });
        Def("__repr__", (_, a, _) => CategoricalRepr(Cat(a[0])));
        Def("__str__", (_, a, _) => CategoricalRepr(Cat(a[0])));
        cls.Dict["categories"] = new PyProperty { Getter = PdClasses.Fn("categories", (_, a, _) => PdConv.Wrap(new FIndex(Cat(a[0]).Categories))) };
        cls.Dict["codes"] = new PyProperty { Getter = PdClasses.Fn("codes", (_, a, _) => Conv.Wrap(NDArray.FromArray(Cat(a[0]).Codes.Select(x => (sbyte)x).ToArray()))) };
        cls.Dict["ordered"] = new PyProperty { Getter = PdClasses.Fn("ordered", (_, a, _) => Cat(a[0]).Ordered) };
        cls.Dict["dtype"] = new PyProperty { Getter = PdClasses.Fn("dtype", (_, a, _) => WrapDType(Cat(a[0]))) };
        Def("tolist", (_, a, _) => { var c = Cat(a[0]); return new PyList(Enumerable.Range(0, c.Length).Select(x => PdConv.FromCell(c, x))); });
        Def("isna", (_, a, _) => Conv.Wrap(NDArray.FromArray(Cat(a[0]).Codes.Select(x => x < 0).ToArray())));
        Def("astype", (i, a, k) => PdConv.Wrap(new Series(PdConv.AsType(Cat(a[0]), A("astype", i, a, k, "dtype").Required(0)), null)));

        // ---- .cat accessor
        PdClasses.Series.Dict["cat"] = new PyProperty { Getter = PdClasses.Fn("cat", (_, a, _) => { SeriesCat(a[0]); return new PyInstance(CatAccessor) { Native = ((PyInstance)a[0]).Native }; }) };
        void CDef(string n, BuiltinFn f) => CatAccessor.Dict[n] = PdClasses.Fn($"cat.{n}", f);
        CatAccessor.Dict["categories"] = new PyProperty { Getter = PdClasses.Fn("categories", (_, a, _) => PdConv.Wrap(new FIndex(SeriesCat(a[0]).Categories))) };
        CatAccessor.Dict["ordered"] = new PyProperty { Getter = PdClasses.Fn("ordered", (_, a, _) => SeriesCat(a[0]).Ordered) };
        CatAccessor.Dict["codes"] = new PyProperty
        {
            Getter = PdClasses.Fn("codes", (_, a, _) =>
            {
                var s = Owner(a[0]);
                return PdConv.Wrap(new Series(Column.FromLongs(SeriesCat(a[0]).Codes.Select(x => (long)x).ToArray(), DType.Int8), s.Index, null));
            }),
        };
        CDef("as_ordered", (_, a, _) => Rewrap(a[0], Column.FromCodes(SeriesCat(a[0]).Codes, SeriesCat(a[0]).Categories, true)));
        CDef("as_unordered", (_, a, _) => Rewrap(a[0], Column.FromCodes(SeriesCat(a[0]).Codes, SeriesCat(a[0]).Categories, false)));
        CDef("add_categories", (i, a, k) =>
        {
            var c = SeriesCat(a[0]);
            var add = PdConv.IsListLike(A("add_categories", i, a, k, "new_categories").Required(0)) ? PdConv.Cells(A("add_categories", i, a, k, "new_categories")[0]!) : new List<object?> { PdConv.ToCell(A("add_categories", i, a, k, "new_categories")[0]) };
            foreach (var x in add) if (Enumerable.Range(0, c.Categories.Length).Any(j => Equals(Column.Key(c.Categories[j]), Column.Key(x))))
                    throw PyErr.ValueError($"new categories must not include old categories: {{{Formatter.ObjectStr(x)}}}");
            return Rewrap(a[0], Column.FromCodes(c.Codes, Column.Concat(new[] { c.Categories, Column.Infer(add) }), c.Ordered));
        });
        CDef("remove_categories", (i, a, k) =>
        {
            var c = SeriesCat(a[0]);
            var rem = A("remove_categories", i, a, k, "removals").Required(0);
            var removals = (PdConv.IsListLike(rem) ? PdConv.Cells(rem) : new List<object?> { PdConv.ToCell(rem) }).Select(x => Column.Key(x)).ToList();
            var keep = Enumerable.Range(0, c.Categories.Length).Where(j => !removals.Contains(Column.Key(c.Categories[j]))).ToArray();
            return Rewrap(a[0], Recode(c, c.Categories.Take(keep), c.Ordered));
        });
        CDef("remove_unused_categories", (_, a, _) =>
        {
            var c = SeriesCat(a[0]);
            var used = new HashSet<int>(c.Codes.Where(x => x >= 0));
            var keep = Enumerable.Range(0, c.Categories.Length).Where(used.Contains).ToArray();
            return Rewrap(a[0], Recode(c, c.Categories.Take(keep), c.Ordered));
        });
        CDef("rename_categories", (i, a, k) =>
        {
            var c = SeriesCat(a[0]);
            var m2 = A("rename_categories", i, a, k, "new_categories").Required(0);
            Column cats;
            if (m2 is PyDict d)
                cats = PdWrangle.RenameLabels(i, new FIndex(c.Categories), d);
            else if (m2 is PyFunction or PyBuiltinFunction) cats = PdWrangle.RenameLabels(i, new FIndex(c.Categories), m2);
            else
            {
                cats = PdConv.ToColumn(m2);
                if (cats.Length != c.Categories.Length) throw PyErr.ValueError("new categories need to have the same number of items as the old categories!");
            }
            return Rewrap(a[0], Column.FromCodes(c.Codes, cats, c.Ordered));
        });
        CDef("reorder_categories", (i, a, k) =>
        {
            var c = SeriesCat(a[0]);
            var p = A("reorder_categories", i, a, k, "new_categories", "ordered");
            var cats = PdConv.ToColumn(p.Required(0));
            if (cats.Length != c.Categories.Length || !Enumerable.Range(0, cats.Length).All(j => Enumerable.Range(0, c.Categories.Length).Any(q => Equals(Column.Key(cats[j]), Column.Key(c.Categories[q])))))
                throw PyErr.ValueError("items in new_categories are not the same as in old categories");
            return Rewrap(a[0], Recode(c, cats, p.Has(1) ? p.Bool(1, false) : c.Ordered));
        });
        CDef("set_categories", (i, a, k) =>
        {
            var c = SeriesCat(a[0]);
            var p = A("set_categories", i, a, k, "new_categories", "ordered", "rename");
            var cats = PdConv.ToColumn(p.Required(0));
            if (p.Bool(2, false)) return Rewrap(a[0], Column.FromCodes(c.Codes, cats, p.Has(1) ? p.Bool(1, false) : c.Ordered));
            return Rewrap(a[0], Recode(c, cats, p.Has(1) ? p.Bool(1, false) : c.Ordered));
        });

        if (m is not null)
        {
            m.Dict["Categorical"] = CategoricalClass;
            m.Dict["CategoricalDtype"] = DtypeClass;
            m.Dict["Interval"] = IntervalClass;
            m.Dict["cut"] = PdClasses.Fn("cut", (i, a, k) => CutQ(i, a, k, false));
            m.Dict["qcut"] = PdClasses.Fn("qcut", (i, a, k) => CutQ(i, a, k, true));
        }
    }

    /// <summary>The same values under a different category set (values whose category disappeared become missing).</summary>
    private static Column Recode(Column c, Column newCats, bool ordered) => Column.ToCategory(c.Decategorized(), newCats, ordered);

    public static Column MakeCategorical(object values, object? categories, bool? ordered, object? dtype)
    {
        var dt = AsCatDtype(dtype);
        var data = PdConv.ToColumn(values);
        if (data.Kind == Kind.Category && categories is null && dt?.Categories is null) return ordered is bool o0 ? Column.FromCodes(data.Codes, data.Categories, o0) : data;
        Column? cats = categories is not null ? PdConv.ToColumn(categories) : dt?.Categories;
        bool ord = ordered ?? dt?.Ordered ?? false;
        return Column.ToCategory(data, cats, ord);
    }

    public static string CategoricalRepr(Column c)
    {
        var o = PdOptions.Display;
        var d = c.Decategorized();
        bool intervals = c.Categories.Kind == Kind.Object && c.Categories.Length > 0 && c.Categories[0] is IntervalValue;
        bool anyNa = c.Codes.Any(x => x < 0);
        string[] Fmt(Column part) => intervals
            ? Enumerable.Range(0, part.Length).Select(i => part.IsNa(i) ? "NaN" : ((IntervalValue)part[i]!).Text(anyNa)).ToArray()
            : part.Kind == Kind.Str
            ? Enumerable.Range(0, part.Length).Select(i => part.IsNa(i) ? "NaN" : "'" + part.StrAt(i) + "'").ToArray()
            : Formatter.FormatCells(part, o, false).Select(x => x.Trim()).ToArray();
        string body;
        const int maxLen = 10;
        if (c.Length > maxLen)
        {
            int num = maxLen / 2;
            var head = Fmt(d.Slice(0, num)); var tail = Fmt(d.Slice(d.Length - num, num));
            body = "[" + string.Join(", ", head) + ", ..., " + string.Join(", ", tail) + "]\nLength: " + c.Length;
        }
        else body = "[" + string.Join(", ", Fmt(d)) + "]";
        return body + "\n" + Formatter.CategoriesLine(c, o);
    }

    // ------------------------------------------------------------------ cut / qcut

    private static object CutQ(Interp i, object[] a, Dictionary<string, object>? k, bool quantile)
    {
        var p = quantile
            ? new Args("qcut", i, a, k, "x", "q", "labels", "retbins", "precision", "duplicates")
            : new Args("cut", i, a, k, "x", "bins", "right", "labels", "retbins", "precision", "include_lowest", "duplicates", "ordered");
        var x = p.Required(0);
        Series? source = x as PyInstance is { Native: Series s0 } ? s0 : null;
        var xs = PdConv.ToColumn(x);
        if (xs.Kind is not (Kind.Int or Kind.Float)) throw PyErr.TypeError("bins argument only works with numeric data.");
        var data = Enumerable.Range(0, xs.Length).Select(xs.DoubleAt).ToArray();
        object? labelsArg = p[quantile ? 2 : 3];
        bool labelsFalse = labelsArg is false;
        var labels = labelsArg is not null and not PyNone && !labelsFalse ? PdConv.Cells(labelsArg) : null;
        int precision = p.Int(quantile ? 4 : 5, 3);
        bool dropDup = (p[quantile ? 5 : 7] as string) == "drop";
        Column result; double[] bins;
        bool binsAreInt = false;
        if (quantile)
        {
            var qarg = p.Required(1);
            double[] qs = PdConv.IsInt(qarg)
                ? Enumerable.Range(0, PdConv.ToInt(qarg) + 1).Select(n => (double)n / PdConv.ToInt(qarg)).ToArray()
                : PdConv.Cells(qarg).Select(v => Column.ToDouble(v!)).ToArray();
            if (PdConv.IsInt(qarg)) qs = Linspace01(PdConv.ToInt(qarg));
            (result, bins) = Binning.QCut(data, qs, labels, labelsFalse, precision, dropDup);
        }
        else
        {
            var barg = p.Required(1);
            bool right = p.Bool(2, true);
            bool ordered = p.Bool(8, true);
            if (PdConv.IsInt(barg))
                (result, bins) = Binning.Cut(data, null, false, PdConv.ToInt(barg), right, labels, labelsFalse, precision, p.Bool(6, false), dropDup, ordered);
            else
            {
                var cells = PdConv.Cells(barg);
                binsAreInt = cells.All(c => c is long);
                (result, bins) = Binning.Cut(data, cells.Select(c => Column.ToDouble(c!)).ToArray(), binsAreInt, null, right, labels, labelsFalse, precision, p.Bool(6, false), dropDup, ordered);
            }
        }
        object output = source is not null ? PdConv.Wrap(new Series(result, source.Index, source.Name))
            : labelsFalse ? PdArrays.Values(result) : WrapCategorical(result);
        bool retbins = p.Bool(quantile ? 3 : 4, false);
        if (!retbins) return output;
        object binsOut = Conv.Wrap(binsAreInt ? NDArray.FromArray(bins.Select(b => (long)b).ToArray()) : NDArray.FromArray(bins));
        return new PyTuple(new[] { output, binsOut });
    }

    private static double[] Linspace01(int q)
    {
        var y = new double[q + 1];
        double step = 1.0 / q;
        for (int n = 0; n <= q; n++) y[n] = n * step;
        y[q] = 1.0;
        return y;
    }
}
