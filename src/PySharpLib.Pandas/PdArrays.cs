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
            default: throw PyErr.NotImplementedError($"converting a '{c.DTypeName}' column to a numpy array (NDSharp has no object arrays)");
        }
    }

    public static object Values(Column c) => Conv.Wrap(ToNd(c));

    public static NDArray ToNd(DataFrame d)
    {
        int n = d.NRows, m = d.NCols;
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
