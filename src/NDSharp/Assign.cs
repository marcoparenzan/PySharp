// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp;

/// <summary>Writes one array's values into another's (possibly a view), broadcasting and
/// converting as numpy does for in-place operations and assignment.</summary>
public static class Assign
{
    /// <summary>numpy <c>copyto(dst, src, casting)</c>: <paramref name="src"/> is broadcast to
    /// <paramref name="dst"/>'s shape and converted to its dtype; <paramref name="casting"/>
    /// decides which conversions are allowed. Overlapping memory is handled by copying first.</summary>
    public static void Copy(NDArray dst, NDArray src, Casting casting = Casting.SameKind)
    {
        if (src.DType != dst.DType && casting != Casting.Unsafe && !CastingRules.CanCast(src.DType, dst.DType, casting)
            && !WeakFits(src, dst.DType))
            throw new NDTypeException(
                $"Cannot cast array data from dtype('{src.DType.Name()}') to dtype('{dst.DType.Name()}') according to the rule '{casting.Name()}'");

        var strides = Broadcasting.StridesFor(src, dst.Shape);
        var s = src.CastTo(dst.DType);
        if (ReferenceEquals(s.Buffer, dst.Buffer))
        {
            s = s.Copy();
            strides = Broadcasting.StridesFor(s, dst.Shape);
        }
        else if (!ReferenceEquals(s, src))
            strides = Broadcasting.StridesFor(s, dst.Shape);
        Run(dst, s, strides);
    }

    // A Python int/float literal may be stored into any array of the same or a wider kind.
    private static bool WeakFits(NDArray src, DType to)
        => src.IsWeakScalar && src.DType.Kind() switch
        {
            'b' => true,
            'i' or 'u' => to.Kind() != 'b',
            _ => to.IsFloat(),
        };

    private static void Run(NDArray dst, NDArray s, int[] ss)
    {
        switch (dst.DType)
        {
            case DType.Bool: Loop((bool[])s.Buffer, s.Offset, ss, (bool[])dst.Buffer, dst); break;
            case DType.Int8: Loop((sbyte[])s.Buffer, s.Offset, ss, (sbyte[])dst.Buffer, dst); break;
            case DType.UInt8: Loop((byte[])s.Buffer, s.Offset, ss, (byte[])dst.Buffer, dst); break;
            case DType.Int16: Loop((short[])s.Buffer, s.Offset, ss, (short[])dst.Buffer, dst); break;
            case DType.UInt16: Loop((ushort[])s.Buffer, s.Offset, ss, (ushort[])dst.Buffer, dst); break;
            case DType.Int32: Loop((int[])s.Buffer, s.Offset, ss, (int[])dst.Buffer, dst); break;
            case DType.UInt32: Loop((uint[])s.Buffer, s.Offset, ss, (uint[])dst.Buffer, dst); break;
            case DType.Int64: Loop((long[])s.Buffer, s.Offset, ss, (long[])dst.Buffer, dst); break;
            case DType.UInt64: Loop((ulong[])s.Buffer, s.Offset, ss, (ulong[])dst.Buffer, dst); break;
            case DType.Float16: Loop((Half[])s.Buffer, s.Offset, ss, (Half[])dst.Buffer, dst); break;
            case DType.Float32: Loop((float[])s.Buffer, s.Offset, ss, (float[])dst.Buffer, dst); break;
            default: Loop((double[])s.Buffer, s.Offset, ss, (double[])dst.Buffer, dst); break;
        }
    }

    private static void Loop<T>(T[] src, int so, int[] ss, T[] d, NDArray dst) where T : unmanaged
    {
        var w = new RowWalker(dst.Shape, dst.Strides, ss);
        do
        {
            int q = dst.Offset + w.Off0, p = so + w.Off1, n = w.InnerLen, s0 = w.InnerStride0, s1 = w.InnerStride1;
            if (s0 == 1 && s1 == 1)
                Array.Copy(src, p, d, q, n);
            else
                for (int i = 0; i < n; i++) d[q + i * s0] = src[p + i * s1];
        } while (w.Next());
    }
}
