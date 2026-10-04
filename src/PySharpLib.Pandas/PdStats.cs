// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary><c>info</c>, <c>corr</c>, <c>cov</c> and the numpy interop hooks (ufuncs on Series/DataFrame, operator precedence over ndarray).</summary>
internal static class PdStats
{
    private static Series S(object o) => PdConv.S(o);
    private static DataFrame D(object o) => PdConv.D(o);
    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);

    public static void Install()
    {
        PdClasses.Series.Dict["info"] = PdClasses.Fn("info", (i, a, k) => { i.Out.Write(Info.Describe(S(a[0])).Replace("\n", Environment.NewLine == "\n" ? "\n" : "\n")); return PyNone.Instance; });
        PdClasses.DataFrame.Dict["info"] = PdClasses.Fn("info", (i, a, k) => { i.Out.Write(Info.Describe(D(a[0]))); return PyNone.Instance; });

        PdClasses.Series.Dict["corr"] = PdClasses.Fn("corr", (i, a, k) =>
        {
            var p = A("corr", i, a, k, "other", "method", "min_periods");
            if (p.Has(1) && (string)p[1]! != "pearson") throw PyErr.NotImplementedError("corr(method=" + p[1] + ")");
            var o = S(p.Required(0));
            var s = S(a[0]);
            var (_, x, y) = Ops.Align(s, o);
            return Info.SeriesCorr(x, y, p.Int(2, 1));
        });
        PdClasses.Series.Dict["cov"] = PdClasses.Fn("cov", (i, a, k) =>
        {
            var p = A("cov", i, a, k, "other", "min_periods", "ddof");
            var (_, x, y) = Ops.Align(S(a[0]), S(p.Required(0)));
            return Info.SeriesCov(x, y, p.Int(1, 1), p.Int(2, 1));
        });
        PdClasses.DataFrame.Dict["corr"] = PdClasses.Fn("corr", (i, a, k) => Matrix(i, a, k, "corr", true));
        PdClasses.DataFrame.Dict["cov"] = PdClasses.Fn("cov", (i, a, k) => Matrix(i, a, k, "cov", false));

        Conv.DeferToOther.Add(o => o is PyInstance { Native: Series or DataFrame });
        Conv.UfuncWrappers.Add((args, result) =>
        {
            var nd = (NDArray)result.Native!;
            foreach (var arg in args)
            {
                if (arg is PyInstance { Native: Series s } && nd.Ndim == 1 && nd.Shape[0] == s.Length)
                    return PdConv.Wrap(new Series(PdConv.FromNd(nd), s.Index, s.Name));
                if (arg is PyInstance { Native: DataFrame d } && nd.Ndim == 2 && nd.Shape[0] == d.NRows && nd.Shape[1] == d.NCols)
                    return PdConv.Wrap(new DataFrame(Enumerable.Range(0, d.NCols).Select(j => PdConv.FromNd(nd.Get(new NDIndex[] { Slice.All, j }))), d.Columns, d.Index));
            }
            return null;
        });
    }

    private static object Matrix(Interp i, object[] a, Dictionary<string, object>? k, string name, bool corr)
    {
        var p = corr ? A(name, i, a, k, "method", "min_periods", "numeric_only") : A(name, i, a, k, "min_periods", "ddof", "numeric_only");
        if (corr && p.Has(0) && (string)p[0]! != "pearson") throw PyErr.NotImplementedError("corr(method=" + p[0] + ")");
        int minPeriods = corr ? p.Int(1, 1) : p.Int(0, 1);
        int ddof = corr ? 1 : p.Int(1, 1);
        bool numericOnly = p.Bool(2, false);
        var d = D(a[0]);
        var use = Enumerable.Range(0, d.NCols).Where(j => !numericOnly || Reduce.IsNumeric(d.Data[j])).ToArray();
        int m = use.Length;
        var cols = new List<Column>();
        for (int c = 0; c < m; c++)
        {
            var v = new double[m];
            for (int r = 0; r < m; r++)
                v[r] = c == r && corr ? (d.Data[use[c]].Kind is Kind.Str or Kind.Object ? throw new FrameException("could not convert string to float: ") : Info.PairStat(d.Data[use[c]], d.Data[use[c]], true, minPeriods))
                       : Info.PairStat(d.Data[use[r]], d.Data[use[c]], corr, minPeriods, ddof);
            cols.Add(Column.FromDoubles(v));
        }
        var labels = d.Columns.Take(use);
        return PdConv.Wrap(new DataFrame(cols, labels, labels));
    }
}
