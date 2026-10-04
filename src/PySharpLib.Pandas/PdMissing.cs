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

/// <summary>dtype selection shared by <c>select_dtypes</c> and <c>describe(include=, exclude=)</c>, and <c>interpolate</c>.</summary>
internal static class PdMissing
{
    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);

    // ------------------------------------------------------------------ dtype specs

    /// <summary>Does a column match a pandas dtype spec (<c>'number'</c>, <c>np.number</c>, <c>'object'</c>, <c>str</c>, <c>'datetime'</c>, <c>'category'</c>, <c>'int64'</c> ...)?</summary>
    public static bool Matches(Column c, object spec)
    {
        string? name = spec switch
        {
            string s => s,
            PyBuiltinFunction f => f.Name,
            PyClass { Name: var n } => n,
            _ => null,
        };
        switch (name)
        {
            case "number": case "np.number": return c.Kind is Kind.Int or Kind.Float or Kind.Timedelta;
            case "integer": return c.Kind == Kind.Int;
            case "floating": return c.Kind == Kind.Float;
            case "bool": case "bool_": return c.Kind == Kind.Bool;
            case "object": case "O": return c.Kind is Kind.Object or Kind.Str;
            case "str": case "string": return c.Kind == Kind.Str;
            case "category": return c.Kind == Kind.Category;
            case "datetime": case "datetime64": return c.Kind == Kind.DateTime && c.Tz is null;
            case "datetimetz": return c.Kind == Kind.DateTime && c.Tz is not null;
            case "timedelta": case "timedelta64": return c.Kind == Kind.Timedelta;
            case "int": return c.Kind == Kind.Int;
            case "float": return c.Kind == Kind.Float;
        }
        if (spec is PyClass cls && Classes.TryDTypeOfClass(cls, out var dt)) return c.DTypeName == dt.Name();
        return c.DTypeName == PdConv.DTypeName(spec);
    }

    private static readonly HashSet<string> KnownNames = new()
    {
        "number", "integer", "floating", "bool", "bool_", "object", "O", "str", "string", "category", "datetime", "datetime64", "datetimetz", "timedelta", "timedelta64", "int", "float",
        "i8", "i4", "f8", "f4", "u1", "u2", "u4", "u8", "i1", "i2", "?", "U", "S", "complex", "complex128", "complex64",
    };

    private static bool Known(string name)
        => KnownNames.Contains(name) || Enum.TryParse<DType>(name, true, out _) || name.StartsWith("period[") || name.StartsWith("datetime64[") || name.StartsWith("timedelta64[");

    private static List<object>? Specs(object? o)
        => o is null or PyNone ? null : o is PyList l ? l.Items.ToList() : o is PyTuple t ? t.Items.ToList() : new List<object> { o };

    /// <summary>The columns picked by <c>include</c> / <c>exclude</c>.</summary>
    public static int[] Select(DataFrame d, object? include, object? exclude)
    {
        var inc = Specs(include); var exc = Specs(exclude);
        foreach (var s in (inc ?? new List<object>()).Concat(exc ?? new List<object>()))
            if (s is string str && !Known(str)) throw PyErr.TypeError($"data type '{str}' not understood");
        return Enumerable.Range(0, d.NCols).Where(j => (inc is null || inc.Any(s => Matches(d.Data[j], s))) && (exc is null || !exc.Any(s => Matches(d.Data[j], s)))).ToArray();
    }

    // ------------------------------------------------------------------ interpolate

    private static readonly HashSet<string> Scipy = new() { "nearest", "zero", "slinear", "quadratic", "cubic", "barycentric", "krogh", "polynomial", "spline", "piecewise_polynomial", "from_derivatives", "pchip", "akima", "cubicspline" };

    /// <summary>One column: linear interpolation over <paramref name="x"/> with pandas' rules for the limit, its direction and the area.</summary>
    public static Column InterpolateColumn(Column c, double[] x, int? limit, string direction, string? area)
    {
        if (c.Kind != Kind.Float) return c;
        int n = c.Length;
        var v = c.Doubles;
        var valid = Enumerable.Range(0, n).Where(i => !double.IsNaN(v[i])).ToArray();
        if (valid.Length == 0 || valid.Length == n) return c;
        int first = valid[0], last = valid[^1];
        var invalid = new bool[n];
        for (int i = 0; i < n; i++) invalid[i] = double.IsNaN(v[i]);
        bool[] Violators(bool forward, int? lim)
        {
            var r = new bool[n];
            if (forward)
            {
                int run = 0; bool seen = false;
                for (int i = 0; i < n; i++)
                {
                    if (!invalid[i]) { seen = true; run = 0; continue; }
                    run++;
                    r[i] = !seen || lim is int l && run > l;
                }
            }
            else
            {
                int run = 0; bool seen = false;
                for (int i = n - 1; i >= 0; i--)
                {
                    if (!invalid[i]) { seen = true; run = 0; continue; }
                    run++;
                    r[i] = !seen || lim is int l && run > l;
                }
            }
            return r;
        }
        var preserve = new bool[n];
        var fw = Violators(true, limit); var bw = Violators(false, limit);
        for (int i = 0; i < n; i++)
            preserve[i] = direction switch
            {
                "forward" => fw[i],
                "backward" => bw[i],
                _ => fw[i] && bw[i],
            };
        for (int i = 0; i < n; i++)
        {
            if (area == "inside" && (i < first || i > last)) preserve[i] = true;
            if (area == "outside" && i > first && i < last) preserve[i] = true;
        }
        var res = (double[])v.Clone();
        int vp = 0;
        for (int i = 0; i < n; i++)
        {
            if (!invalid[i] || preserve[i]) continue;
            if (i < first) { res[i] = v[first]; continue; }
            if (i > last) { res[i] = v[last]; continue; }
            while (valid[vp + 1] < i) vp++;
            int lo = valid[vp], hi = valid[vp + 1];
            res[i] = v[lo] + (v[hi] - v[lo]) * (x[i] - x[lo]) / (x[hi] - x[lo]);
        }
        return Column.FromDoubles(res, c.Num!.Value);
    }

    private static double[] IndexValues(FIndex ix, string method)
    {
        var l = ix.Labels;
        if (method == "time" && l.Kind != Kind.DateTime) throw PyErr.ValueError("time-weighted interpolation only works on Series or DataFrames with a DatetimeIndex");
        if (l.Kind == Kind.DateTime) return l.Ticks.Select(t => (double)t).ToArray();
        if (l.Kind is Kind.Int or Kind.Float) return Enumerable.Range(0, l.Length).Select(i => l.DoubleAt(i)).ToArray();
        throw PyErr.ValueError("Index column must be numeric or datetime type when using " + method + " method other than linear. Try setting a numeric or datetime index column before interpolating.");
    }

    public static void Install()
    {
        foreach (var cls in new[] { PdClasses.Series, PdClasses.DataFrame })
        {
            bool isSeries = cls == PdClasses.Series;
            cls.Dict["interpolate"] = PdClasses.Fn("interpolate", (i, a, k) =>
            {
                var p = A("interpolate", i, a, k, "method", "axis", "limit", "inplace", "limit_direction", "limit_area", "downcast", "order");
                string method = p.Has(0) ? (string)p[0]! : "linear";
                if (method is "pad" or "ffill" or "bfill" or "backfill") throw PyErr.ValueError($"Can not interpolate with method={method}.");
                if (Scipy.Contains(method)) throw PyErr.NotImplementedError($"interpolate(method='{method}') needs spline/scipy interpolation, which is not implemented");
                if (method is not ("linear" or "index" or "values" or "time"))
                    throw PyErr.ValueError($"method must be one of ['linear', 'time', 'index', 'values', 'nearest', 'zero', 'slinear', 'quadratic', 'cubic', 'barycentric', 'krogh', 'spline', 'polynomial', 'from_derivatives', 'piecewise_polynomial', 'pchip', 'akima', 'cubicspline']. Got '{method}' instead.");
                int? limit = p.IntOrNull(2);
                if (limit is < 1) throw PyErr.ValueError("Limit must be greater than 0");
                string dir = p.Has(4) ? (string)p[4]! : "forward";
                if (dir is not ("forward" or "backward" or "both")) throw PyErr.ValueError($"Invalid limit_direction: expecting one of ['forward', 'backward', 'both'], got '{dir}'.");
                string? area = p.Has(5) ? (string)p[5]! : null;
                if (area is not (null or "inside" or "outside")) throw PyErr.ValueError($"Invalid limit_area: expecting one of ['inside', 'outside'], got {area}.");
                object result;
                if (isSeries)
                {
                    var s = PdConv.S(a[0]);
                    if (s.Values.Kind is Kind.Str or Kind.Object) throw PyErr.TypeError($"Cannot interpolate with {s.Values.DTypeName} dtype");
                    var x = method == "linear" ? Enumerable.Range(0, s.Length).Select(x0 => (double)x0).ToArray() : IndexValues(s.Index, method);
                    result = PdConv.Wrap(new Series(InterpolateColumn(s.Values, x, limit, dir, area), s.Index, s.Name));
                }
                else
                {
                    var d = PdConv.D(a[0]);
                    int axis = p.Has(1) ? PdOps.AxisOf(p[1]!) : 0;
                    if (!d.Data.Any(c => c.Kind is Kind.Float or Kind.Int or Kind.Bool or Kind.DateTime or Kind.Timedelta))
                        throw PyErr.TypeError("Cannot interpolate with all object-dtype columns in the DataFrame. Try setting at least one column to a numeric dtype.");
                    if (axis == 0)
                    {
                        var x = method == "linear" ? Enumerable.Range(0, d.NRows).Select(x0 => (double)x0).ToArray() : IndexValues(d.Index, method);
                        result = PdConv.Wrap(new DataFrame(d.Data.Select(c => InterpolateColumn(c, x, limit, dir, area)), d.Columns, d.Index));
                    }
                    else
                    {
                        var cols = d.Data.Select(c => c.Kind == Kind.Int ? Column.FromDoubles(Enumerable.Range(0, c.Length).Select(c.DoubleAt).ToArray()) : c).ToArray();
                        var outCols = cols.Select(c => new double[d.NRows]).ToArray();
                        var xs = method == "linear" ? Enumerable.Range(0, d.NCols).Select(x0 => (double)x0).ToArray() : IndexValues(d.Columns, method);
                        for (int r = 0; r < d.NRows; r++)
                        {
                            var rowCol = Column.FromDoubles(cols.Select(c => c.Kind == Kind.Float ? c.DoubleAt(r) : double.NaN).ToArray());
                            var filled = InterpolateColumn(rowCol, xs, limit, dir, area);
                            for (int j = 0; j < cols.Length; j++) outCols[j][r] = filled.DoubleAt(j);
                        }
                        result = PdConv.Wrap(new DataFrame(outCols.Select(o => Column.FromDoubles(o)), d.Columns, d.Index));
                    }
                }
                return PdWrangle.Finish(a[0], result, p.Bool(3, false));
            });
        }
    }
}
