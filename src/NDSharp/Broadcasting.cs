// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp;

/// <summary>numpy broadcasting: shapes are right-aligned, each dimension pair must match or have a
/// 1 on one side (which stretches). A stretched dimension is read with stride 0 — no data is
/// duplicated.</summary>
public static class Broadcasting
{
    /// <summary>numpy-style shape text: <c>(2,3)</c> / <c>(4,)</c> (no spaces, as in numpy's broadcast error).</summary>
    public static string ShapeText(int[] shape)
        => shape.Length == 1 ? $"({shape[0]},)" : "(" + string.Join(",", shape) + ")";

    /// <summary>Python tuple text: <c>(2, 3)</c> / <c>(4,)</c> — as in repr() and most numpy messages.</summary>
    public static string ShapeRepr(int[] shape)
        => shape.Length == 1 ? $"({shape[0]},)" : "(" + string.Join(", ", shape) + ")";

    public static int[] Shape(int[] a, int[] b)
    {
        int ndim = Math.Max(a.Length, b.Length);
        var result = new int[ndim];
        int padA = ndim - a.Length, padB = ndim - b.Length;
        for (int i = 0; i < ndim; i++)
        {
            int da = i < padA ? 1 : a[i - padA];
            int db = i < padB ? 1 : b[i - padB];
            if (da == db) result[i] = da;
            else if (da == 1) result[i] = db;
            else if (db == 1) result[i] = da;
            else
                throw new NDValueException(
                    $"operands could not be broadcast together with shapes {ShapeText(a)} {ShapeText(b)} ");
        }
        return result;
    }

    public static int[] Shape(params int[][] shapes)
    {
        var acc = shapes[0];
        for (int i = 1; i < shapes.Length; i++)
            acc = Shape(acc, shapes[i]);
        return acc;
    }

    /// <summary>The strides of <paramref name="a"/> reinterpreted against <paramref name="outShape"/>
    /// (stride 0 where a dimension is missing or stretched). Throws if <paramref name="a"/> cannot
    /// broadcast to <paramref name="outShape"/>.</summary>
    public static int[] StridesFor(NDArray a, int[] outShape)
    {
        int ndim = outShape.Length;
        int pad = ndim - a.Ndim;
        if (pad < 0)
            throw new NDValueException(
                $"input operand has more dimensions than allowed by the axis remapping");
        var result = new int[ndim];
        for (int i = 0; i < ndim; i++)
        {
            if (i < pad) continue;
            int dim = a.Shape[i - pad];
            if (dim == outShape[i]) result[i] = a.Strides[i - pad];
            else if (dim == 1) result[i] = 0;
            else
                throw new NDValueException(
                    $"could not broadcast input array from shape {ShapeText(a.Shape)} into shape {ShapeText(outShape)}");
        }
        return result;
    }

    /// <summary>numpy <c>broadcast_to</c>: a read-only-in-spirit view of <paramref name="a"/> with shape <paramref name="shape"/>.</summary>
    public static NDArray BroadcastTo(NDArray a, int[] shape)
        => a.ViewWith((int[])shape.Clone(), StridesFor(a, shape), a.Offset);
}
