// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;
using NDSharp.Frame;
using PySharpLib.Numpy;
using PySharpLib.Runtime;

namespace PySharpLib.Pandas;

/// <summary>Frame data → numpy arrays (<c>.values</c>, <c>to_numpy()</c>, <c>np.asarray</c>).</summary>
internal static class PdArrays
{
    public static NDArray ToNd(Column c)
    {
        switch (c.Kind)
        {
            case Kind.Int: return NDArray.FromArray((long[])c.Longs.Clone()).AsType(c.Num!.Value);
            case Kind.Float: return NDArray.FromArray((double[])c.Doubles.Clone()).AsType(c.Num!.Value);
            case Kind.Bool: return NDArray.FromArray((bool[])c.Bools.Clone());
            case Kind.DateTime:
            case Kind.Timedelta:
            {
                string unit = DateTimeCore.UnitName(c.Unit);
                var dt = c.Kind == Kind.DateTime ? DTypes.DateTime64Of(unit) : DTypes.TimeDelta64Of(unit);
                return new NDArray(dt, (long[])c.Ticks.Clone(), new[] { c.Length });
            }
            default: throw PyErr.NotImplementedError($"converting a '{c.DTypeName}' column to a numpy array (NDSharp has no object arrays)");
        }
    }

    public static object Values(Column c) => Conv.Wrap(ToNd(c));

    public static NDArray ToNd(DataFrame d)
    {
        int n = d.NRows, m = d.NCols;
        if (m > 0 && d.Data.All(c => c.Kind is Kind.DateTime or Kind.Timedelta) && d.Data.Select(c => (c.Kind, c.Unit)).Distinct().Count() == 1)
        {
            var t = new long[n * m];
            for (int j = 0; j < m; j++) for (int i = 0; i < n; i++) t[i * m + j] = d.Data[j].Ticks[i];
            var first = d.Data[0];
            string unit = DateTimeCore.UnitName(first.Unit);
            return new NDArray(first.Kind == Kind.DateTime ? DTypes.DateTime64Of(unit) : DTypes.TimeDelta64Of(unit), t, new[] { n, m });
        }
        if (d.Data.Any(c => c.Kind is Kind.DateTime or Kind.Timedelta or Kind.Period or Kind.Category))
            throw PyErr.NotImplementedError("converting a frame that mixes datetime/period/category columns with other dtypes to a numpy array (NDSharp has no object arrays)");
        if (d.Data.Any(c => c.Kind is Kind.Str or Kind.Object))
            throw PyErr.NotImplementedError("converting a frame with str/object columns to a numpy array (NDSharp has no object arrays)");
        if (m == 0) return NDArray.FromArray(new double[0], n, 0);
        if (d.Data.All(c => c.Kind == Kind.Bool))
        {
            var b = new bool[n * m];
            for (int j = 0; j < m; j++) for (int i = 0; i < n; i++) b[i * m + j] = d.Data[j].BoolAt(i);
            return NDArray.FromArray(b, n, m);
        }
        if (d.Data.All(c => c.Kind == Kind.Int))
        {
            var l = new long[n * m];
            for (int j = 0; j < m; j++) for (int i = 0; i < n; i++) l[i * m + j] = d.Data[j].LongAt(i);
            return NDArray.FromArray(l, n, m);
        }
        var f = new double[n * m];
        for (int j = 0; j < m; j++) for (int i = 0; i < n; i++) f[i * m + j] = d.Data[j].DoubleAt(i);
        var r = NDArray.FromArray(f, n, m);
        return d.Data.All(c => c.Kind == Kind.Float && c.Num == DType.Float32) ? r.AsType(DType.Float32) : r;
    }

    public static object Values(DataFrame d) => Conv.Wrap(ToNd(d));
}
