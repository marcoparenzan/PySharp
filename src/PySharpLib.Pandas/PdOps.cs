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

/// <summary>Operators and reductions of Series and DataFrame: arithmetic with alignment, comparison, logic, aggregations, cumulative functions.</summary>
internal static class PdOps
{
    private static Series S(object o) => PdConv.S(o);
    private static DataFrame D(object o) => PdConv.D(o);

    /// <summary>A reduction/computation result as a Python value.</summary>
    public static object Out(object? v) => v switch
    {
        null => double.NaN,
        long l => new BigInteger(l),
        _ => v,
    };

    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);

    // ================================================================== operands

    private static object Arith(BinOp op, object self, object other, bool reversed)
    {
        if (self is PyInstance { Native: Series s })
        {
            switch (other)
            {
                case PyInstance { Native: Series o }: return PdConv.Wrap(reversed ? Ops.Binary(op, o, s) : Ops.Binary(op, s, o));
                case PyInstance { Native: DataFrame }: return PyNotImplemented.Instance;
                case PyNone: return PdConv.Wrap(Ops.Binary(op, s, (object?)null, reversed));
            }
            if (PdConv.IsListLike(other))
            {
                var col = PdConv.ToColumn(other);
                if (col.Length != s.Length) throw PyErr.ValueError($"Lengths must match to compare");
                var o = new Series(col, s.Index, s.Name);
                return PdConv.Wrap(reversed ? Ops.Binary(op, o, s) : Ops.Binary(op, s, o));
            }
            if (!IsScalarOperand(other)) return PyNotImplemented.Instance;
            return PdConv.Wrap(Ops.Binary(op, s, PdConv.ToCell(other), reversed));
        }
        var d = D(self);
        switch (other)
        {
            case PyInstance { Native: DataFrame o }: return PdConv.Wrap(reversed ? Ops.Binary(op, o, d) : Ops.Binary(op, d, o));
            case PyInstance { Native: Series o }: return PdConv.Wrap(Ops.Binary(op, d, o, true, reversed));
        }
        if (PdConv.IsListLike(other))
        {
            var col = PdConv.ToColumn(other);
            if (col.Length != d.NCols) throw PyErr.ValueError($"Unable to coerce to Series, length must be {d.NCols}: given {col.Length}");
            return PdConv.Wrap(Ops.Binary(op, d, new Series(col, d.Columns), true, reversed));
        }
        if (!IsScalarOperand(other)) return PyNotImplemented.Instance;
        return PdConv.Wrap(Ops.Binary(op, d, PdConv.ToCell(other), reversed));
    }

    private static bool IsScalarOperand(object o) => o is bool or BigInteger or double or string or PyNone || o is PyInstance { Native: ScalarBox };

    // ================================================================== install

    public static void Install()
    {
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            void Def(string n, BuiltinFn f) => cls.Dict[n] = PdClasses.Fn($"{cls.Name}.{n}", f);

            void Bin(string dunder, string rdunder, BinOp op)
            {
                Def(dunder, (_, a, _) => Arith(op, a[0], a[1], false));
                if (rdunder is not null) Def(rdunder, (_, a, _) => Arith(op, a[0], a[1], true));
            }
            Bin("__add__", "__radd__", BinOp.Add); Bin("__sub__", "__rsub__", BinOp.Sub); Bin("__mul__", "__rmul__", BinOp.Mul);
            Bin("__truediv__", "__rtruediv__", BinOp.Div); Bin("__floordiv__", "__rfloordiv__", BinOp.FloorDiv);
            Bin("__mod__", "__rmod__", BinOp.Mod); Bin("__pow__", "__rpow__", BinOp.Pow);
            Bin("__and__", "__rand__", BinOp.And); Bin("__or__", "__ror__", BinOp.Or); Bin("__xor__", "__rxor__", BinOp.Xor);
            Bin("__eq__", null!, BinOp.Eq); Bin("__ne__", null!, BinOp.Ne); Bin("__lt__", null!, BinOp.Lt);
            Bin("__le__", null!, BinOp.Le); Bin("__gt__", null!, BinOp.Gt); Bin("__ge__", null!, BinOp.Ge);

            // named forms: df.add(other, axis=...), s.sub(other), ...
            void Named(string name, BinOp op)
            {
                Def(name, (i, a, k) =>
                {
                    var p = A(name, i, a, k, "other", "axis", "level", "fill_value");
                    if (p.Has(3)) throw PyErr.NotImplementedError($"{name}(fill_value=...)");
                    if (a[0] is PyInstance { Native: DataFrame df } && p.Required(0) is PyInstance { Native: Series sv })
                    {
                        bool onCols = !(p[1] is "index" or "rows" || (p[1] is BigInteger ab && ab == 0));
                        return PdConv.Wrap(Ops.Binary(op, df, sv, onCols, false));
                    }
                    return Arith(op, a[0], p.Required(0), false);
                });
                Def("r" + name, (i, a, k) => Arith(op, a[0], A(name, i, a, k, "other").Required(0), true));
            }
            Named("add", BinOp.Add); Named("sub", BinOp.Sub); Named("mul", BinOp.Mul); Named("truediv", BinOp.Div); Named("div", BinOp.Div);
            Named("floordiv", BinOp.FloorDiv); Named("mod", BinOp.Mod); Named("pow", BinOp.Pow);
            foreach (var (n, op) in new[] { ("eq", BinOp.Eq), ("ne", BinOp.Ne), ("lt", BinOp.Lt), ("le", BinOp.Le), ("gt", BinOp.Gt), ("ge", BinOp.Ge) })
            {
                var nm = n; var o2 = op;
                Def(nm, (i, a, k) => Arith(o2, a[0], A(nm, i, a, k, "other").Required(0), false));
            }

            Def("__neg__", (_, a, _) => Map(a[0], Ops.Negate));
            Def("__pos__", (_, a, _) => a[0]);
            Def("__abs__", (_, a, _) => Map(a[0], Ops.Abs));
            Def("abs", (_, a, _) => Map(a[0], Ops.Abs));
            Def("__invert__", (_, a, _) => Map(a[0], Ops.Invert));
        }
        InstallReductions();
    }

    /// <summary>Applies a column function to a Series, or to every column of a DataFrame.</summary>
    public static object Map(object self, Func<Column, Column> f)
    {
        if (self is PyInstance { Native: Series s }) return PdConv.Wrap(new Series(f(s.Values), s.Index, s.Name));
        var d = D(self);
        return PdConv.Wrap(new DataFrame(d.Data.Select(f), d.Columns, d.Index));
    }

    // ================================================================== reductions

    private static void InstallReductions()
    {
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            void Def(string n, BuiltinFn f) => cls.Dict[n] = PdClasses.Fn($"{cls.Name}.{n}", f);

            foreach (var name in new[] { "sum", "prod", "mean", "median", "min", "max", "count", "std", "var", "nunique", "any", "all" })
            {
                var nm = name;
                Def(nm, (i, a, k) =>
                {
                    var p = A(nm, i, a, k, "axis", "skipna", "numeric_only", "min_count", "ddof", "dropna");
                    bool skipna = p.Bool(1, true);
                    int ddof = p.Int(4, 1);
                    int minCount = p.Int(3, 0);
                    if (a[0] is PyInstance { Native: Series s }) return Out(Reduce.Scalar(nm, s.Values, skipna, ddof, minCount));
                    return ReduceFrame(D(a[0]), nm, p.Has(0) ? AxisOf(p[0]!) : 0, skipna, p.Bool(2, false), ddof, minCount);
                });
            }
            foreach (var name in new[] { "cumsum", "cumprod", "cummax", "cummin" })
            {
                var nm = name;
                Def(nm, (i, a, k) =>
                {
                    var p = A(nm, i, a, k, "axis", "skipna");
                    bool skipna = p.Bool(1, true);
                    return Map(a[0], c => Reduce.Cumulative(nm, c, skipna));
                });
            }
            foreach (var (name, max) in new[] { ("idxmax", true), ("idxmin", false) })
            {
                var nm = name; var mx = max;
                Def(nm, (i, a, k) =>
                {
                    var p = A(nm, i, a, k, "axis", "skipna");
                    bool skipna = p.Bool(1, true);
                    if (a[0] is PyInstance { Native: Series s })
                    {
                        int pos = Reduce.ArgExtreme(s.Values, mx, skipna);
                        if (pos < 0) throw PyErr.ValueError($"Encountered all NA values");
                        return PdConv.FromLabel(s.Index.Labels[pos]);
                    }
                    var d = D(a[0]);
                    var labels = d.Data.Select(c => { int pos = Reduce.ArgExtreme(c, mx, skipna); return pos < 0 ? null : d.Index.Labels[pos]; }).ToList();
                    return PdConv.Wrap(new Series(Column.Infer(labels), d.Columns));
                });
            }
            Def("quantile", (i, a, k) =>
            {
                var p = A("quantile", i, a, k, "q", "axis", "numeric_only", "interpolation", "method");
                bool list = p.Has(0) && PdConv.IsListLike(p[0]!);
                var qs = !p.Has(0) ? new List<double> { 0.5 } : list ? PdConv.Cells(p[0]!).Select(x => Column.ToDouble(x!)).ToList() : new List<double> { Column.ToDouble(PdConv.ToCell(p[0])!) };
                if (a[0] is PyInstance { Native: Series s })
                {
                    if (!list) return Reduce.Quantile(s.Values, qs[0]);
                    return PdConv.Wrap(new Series(Column.FromDoubles(qs.Select(q => Reduce.Quantile(s.Values, q)).ToArray()), new FIndex(Column.FromDoubles(qs.ToArray())), s.Name));
                }
                var d = D(a[0]);
                var numeric = Enumerable.Range(0, d.NCols).Where(j => Reduce.IsNumeric(d.Data[j])).ToArray();
                if (!list)
                    return PdConv.Wrap(new Series(Column.FromDoubles(numeric.Select(j => Reduce.Quantile(d.Data[j], qs[0])).ToArray()), d.Columns.Take(numeric), qs[0]));
                return PdConv.Wrap(new DataFrame(numeric.Select(j => Column.FromDoubles(qs.Select(q => Reduce.Quantile(d.Data[j], q)).ToArray())), d.Columns.Take(numeric), new FIndex(Column.FromDoubles(qs.ToArray()))));
            });
        }
    }

    public static int AxisOf(object o) => o switch
    {
        BigInteger b => (int)b,
        "index" or "rows" => 0,
        "columns" => 1,
        _ => throw PyErr.ValueError($"No axis named {PyOps.Str(PdConv.Interp!, o)} for object type DataFrame"),
    };

    private static readonly HashSet<string> NumericOnly = new() { "mean", "median", "std", "var" };

    private static object ReduceFrame(DataFrame d, string name, int axis, bool skipna, bool numericOnly, int ddof, int minCount)
    {
        if (axis == 0)
        {
            var use = Enumerable.Range(0, d.NCols).Where(j => !numericOnly || Reduce.IsNumeric(d.Data[j])).ToArray();
            var cells = use.Select(j => Reduce.Scalar(name, d.Data[j], skipna, ddof, minCount)).ToList();
            return PdConv.Wrap(new Series(Column.Infer(cells), d.Columns.Take(use)));
        }
        var cols = Enumerable.Range(0, d.NCols).Where(j => !numericOnly || Reduce.IsNumeric(d.Data[j])).Select(j => d.Data[j]).ToArray();
        var rows = new List<object?>();
        // bool next to other numbers makes pandas reduce an object array: results keep Python's int/float distinction
        bool objectRows = cols.Any(c => c.Kind == Kind.Bool) && cols.Any(c => c.Kind != Kind.Bool);
        for (int i = 0; i < d.NRows; i++)
        {
            var cells = cols.Select(c => c[i]).ToList();
            var r = Reduce.Scalar(name, Column.Infer(cells), skipna, ddof, minCount);
            if (objectRows && r is double dv && name is "sum" or "prod" or "min" or "max" && !double.IsNaN(dv) && cells.Where(x => !(x is double nan && double.IsNaN(nan))).All(x => x is long or bool))
                r = (long)dv;
            rows.Add(r);
        }
        return PdConv.Wrap(new Series(objectRows ? Column.FromObjects(rows.ToArray()) : Column.Infer(rows), d.Index));
    }
}
