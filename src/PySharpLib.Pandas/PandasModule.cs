// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp.Frame;
using PySharpLib.Importing;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary>Opt-in registration of the <c>pandas</c> module (Series / DataFrame / Index over NDSharp.Frame).</summary>
public static class PandasRegistration
{
    public static void Register(Importer importer)
    {
        importer.RegisterSourceModule("_pandas_plot", PdPlot.Source);
        importer.RegisterBuiltin("pandas", interp => { PdPlot.Remember(interp, importer); return PandasModule.Create(); });
    }
}

internal static class PandasModule
{
    private static PyClass MultiIndexClass()
    {
        var cls = new PyClass("MultiIndex", new List<PyClass> { PdClasses.Index });
        cls.InstanceCheck = o => o is PyInstance { Native: FIndex { IsMulti: true } };
        List<object?> Names(object? v) => v is null or PyNone ? new List<object?>() : PdConv.Cells(v);
        cls.Dict["from_tuples"] = PdClasses.Fn("from_tuples", (i, a, k) =>
        {
            var p = new Args("from_tuples", i, a, k, "tuples", "sortorder", "names");
            var rows = PdConv.Cells(p.Required(0)).Select(c => c is LabelTuple lt ? lt.Parts : new[] { c }).ToList();
            return PdConv.Wrap(FIndex.MultiFromTuples(rows, p.Has(2) ? Names(p[2]) : null));
        });
        cls.Dict["from_arrays"] = PdClasses.Fn("from_arrays", (i, a, k) =>
        {
            var p = new Args("from_arrays", i, a, k, "arrays", "sortorder", "names");
            var levels = (p.Required(0) is PyList l ? l.Items : ((PyTuple)p[0]!).Items.ToList()).Select(PdConv.ToColumn).ToList();
            return PdConv.Wrap(FIndex.Multi(levels, p.Has(2) ? Names(p[2]) : null));
        });
        cls.Dict["from_product"] = PdClasses.Fn("from_product", (i, a, k) =>
        {
            var p = new Args("from_product", i, a, k, "iterables", "sortorder", "names");
            var lists = (p.Required(0) is PyList l ? l.Items : ((PyTuple)p[0]!).Items.ToList()).Select(PdConv.Cells).ToList();
            var rows = new List<object?[]> { Array.Empty<object?>() };
            foreach (var lst in lists) rows = rows.SelectMany(r => lst.Select(x => r.Append(x).ToArray())).ToList();
            return PdConv.Wrap(FIndex.MultiFromTuples(rows, p.Has(2) ? Names(p[2]) : lists.Select(_ => (object?)null).ToList()));
        });
        return cls;
    }

    public static PyModule Create()
    {
        var m = new PyModule("pandas");
        m.Dict["__version__"] = "3.0.6";
        m.Dict["Series"] = PdClasses.Series;
        m.Dict["DataFrame"] = PdClasses.DataFrame;
        m.Dict["Index"] = PdClasses.Index;
        m.Dict["MultiIndex"] = MultiIndexClass();
        void Def(string name, BuiltinFn fn) => m.Dict[name] = PdClasses.Fn(name, fn);

        Def("RangeIndex", (i, a, k) =>
        {
            var p = new Args("RangeIndex", i, a, k, "start", "stop", "step", "dtype", "copy", "name");
            int start = 0, stop, step = p.Int(2, 1);
            if (p.Has(1)) { start = p.Int(0, 0); stop = p.Int(1, 0); } else stop = p.Int(0, 0);
            return PdConv.Wrap(FIndex.Range(start, stop, step, p.Has(5) ? PdConv.ToCell(p[5]) : null));
        });
        Def("set_option", (i, a, k) =>
        {
            for (int x = 0; x + 1 < a.Length; x += 2) PdOptions.Set((string)a[x], a[x + 1]);
            return PyNone.Instance;
        });
        Def("get_option", (i, a, k) => PdOptions.Get((string)a[0]));
        Def("reset_option", (i, a, k) => { PdOptions.Reset((string)a[0]); return PyNone.Instance; });
        m.Dict["options"] = PdOptions.OptionsObject();
        Def("isna", (i, a, k) => PdFunctions.IsNa(a[0]));
        m.Dict["isnull"] = m.Dict["isna"];
        Def("notna", (i, a, k) => PdFunctions.NotNa(a[0]));
        m.Dict["notnull"] = m.Dict["notna"];
        m.Dict["nan"] = double.NaN;
        PdCategorical.Install(m);
        PdMerge.Install(m);
        PdReshape.Install(m);
        PdIO.Install(m);
        Def("NamedAgg", (i, a, k) =>
        {
            var p = new Args("NamedAgg", i, a, k, "column", "aggfunc");
            return new PyTuple(new[] { p.Required(0), p.Required(1) });
        });
        return m;
    }
}

internal static class PdFunctions
{
    private static bool CellIsNa(object? c) => c is null || (c is double x && double.IsNaN(x));

    private static Column NaMask(Column c, bool negate)
        => Column.FromBools(Enumerable.Range(0, c.Length).Select(i => c.IsNa(i) != negate).ToArray());

    public static object IsNa(object v) => Mask(v, false);
    public static object NotNa(object v) => Mask(v, true);

    private static object Mask(object v, bool negate)
    {
        switch (v)
        {
            case PyInstance { Native: Series s }: return PdConv.Wrap(new Series(NaMask(s.Values, negate), s.Index, s.Name));
            case PyInstance { Native: DataFrame d }: return PdConv.Wrap(new DataFrame(d.Data.Select(c => NaMask(c, negate)), d.Columns, d.Index));
        }
        if (PdConv.IsListLike(v))
            return Conv.Wrap(NDSharp.NDArray.FromArray(PdConv.Cells(v).Select(c => CellIsNa(c) != negate).ToArray()));
        return CellIsNa(PdConv.ToCell(v)) != negate;
    }
}
