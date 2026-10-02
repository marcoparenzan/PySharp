// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp;

/// <summary>The elementwise engines behind the <see cref="np"/> ufunc-style functions: result-type
/// resolution (numpy 2 / NEP 50 — Python-number scalars are "weak"), broadcasting, and the generic
/// loops. The public surface is in <c>np.Elementwise.cs</c>.</summary>
internal static class Ew
{
    // ================================================================ result types

    private static int Rank(DType d) => d.Kind() switch { 'b' => 0, 'i' or 'u' => 1, 'f' => 2, _ => 3 };

    /// <summary>numpy <c>result_type</c> for two operands, honoring weak scalars.</summary>
    public static DType ResultType(NDArray a, NDArray b, bool forCompare = false)
    {
        if (a.IsWeakScalar == b.IsWeakScalar)
            return DTypes.Promote(a.DType, b.DType);
        var weak = a.IsWeakScalar ? a : b;
        var strong = a.IsWeakScalar ? b : a;
        return ResolveWeak(strong.DType, weak, forCompare);
    }

    private static DType ResolveWeak(DType strong, NDArray weak, bool forCompare)
    {
        int wr = Rank(weak.DType), sr = Rank(strong);
        if (sr < wr)
        {
            // Strong operand has a "lower" kind: the Python scalar's default dtype wins.
            if (weak.DType == DType.Complex128)
                return strong is DType.Float16 or DType.Float32 ? DType.Complex64 : DType.Complex128;
            return weak.DType == DType.UInt64 && strong == DType.Bool ? DType.UInt64 : weak.DType;
        }
        if (wr == 1 && sr == 1 && !WeakIntFits(weak, strong))
        {
            if (forCompare) return DTypes.Promote(strong, weak.DType);
            throw new NDOverflowException($"Python integer {WeakIntText(weak)} out of bounds for {strong.Name()}");
        }
        return strong;
    }

    private static string WeakIntText(NDArray weak)
        => weak.DType == DType.UInt64 ? ((ulong[])weak.Buffer)[weak.Offset].ToString() : ((long[])weak.Buffer)[weak.Offset].ToString();

    private static bool WeakIntFits(NDArray weak, DType d)
    {
        if (weak.DType == DType.UInt64)
        {
            ulong u = ((ulong[])weak.Buffer)[weak.Offset];
            return d == DType.UInt64 || (d == DType.Int64 && u <= long.MaxValue) || (d.ItemSize() < 8 && u <= MaxOf(d));
        }
        long v = ((long[])weak.Buffer)[weak.Offset];
        return d switch
        {
            DType.Int8 => v is >= sbyte.MinValue and <= sbyte.MaxValue,
            DType.UInt8 => v is >= 0 and <= byte.MaxValue,
            DType.Int16 => v is >= short.MinValue and <= short.MaxValue,
            DType.UInt16 => v is >= 0 and <= ushort.MaxValue,
            DType.Int32 => v is >= int.MinValue and <= int.MaxValue,
            DType.UInt32 => v is >= 0 and <= uint.MaxValue,
            DType.UInt64 => v >= 0,
            _ => true,
        };
    }

    private static ulong MaxOf(DType d) => d switch
    {
        DType.Int8 => (ulong)sbyte.MaxValue,
        DType.UInt8 => byte.MaxValue,
        DType.Int16 => (ulong)short.MaxValue,
        DType.UInt16 => ushort.MaxValue,
        DType.Int32 => int.MaxValue,
        DType.UInt32 => uint.MaxValue,
        _ => ulong.MaxValue,
    };

    // ================================================================ plumbing

    private static Array Alloc(DType d, int[] shape) => Array.CreateInstance(d.ClrType(), NDArray.SizeOf(shape));

    private static NDArray Wrap(DType d, Array buf, int[] shape) => new(d, buf, shape);

    // ================================================================ binary, numeric

    /// <summary>Binary operation computed in dtype <paramref name="rt"/> (any numeric dtype; bool is
    /// computed as uint8 and folded back to bool, which gives numpy's logical or/and for add/mul).</summary>
    public static NDArray Num<TK>(NDArray a, NDArray b, DType rt) where TK : struct, INumBinary
    {
        if (rt == DType.Bool)
            return Num<TK>(a, b, DType.UInt8).CastTo(DType.Bool);
        var shape = Broadcasting.Shape(a.Shape, b.Shape);
        var ac = a.CastTo(rt);
        var bc = b.CastTo(rt);
        var sa = Broadcasting.StridesFor(ac, shape);
        var sb = Broadcasting.StridesFor(bc, shape);
        var o = Alloc(rt, shape);
        switch (rt)
        {
            case DType.Int8: NumLoop<sbyte, TK>(ac, sa, bc, sb, (sbyte[])o, shape); break;
            case DType.UInt8: NumLoop<byte, TK>(ac, sa, bc, sb, (byte[])o, shape); break;
            case DType.Int16: NumLoop<short, TK>(ac, sa, bc, sb, (short[])o, shape); break;
            case DType.UInt16: NumLoop<ushort, TK>(ac, sa, bc, sb, (ushort[])o, shape); break;
            case DType.Int32: NumLoop<int, TK>(ac, sa, bc, sb, (int[])o, shape); break;
            case DType.UInt32: NumLoop<uint, TK>(ac, sa, bc, sb, (uint[])o, shape); break;
            case DType.Int64: NumLoop<long, TK>(ac, sa, bc, sb, (long[])o, shape); break;
            case DType.UInt64: NumLoop<ulong, TK>(ac, sa, bc, sb, (ulong[])o, shape); break;
            case DType.Float16: NumLoop<Half, TK>(ac, sa, bc, sb, (Half[])o, shape); break;
            case DType.Float32: NumLoop<float, TK>(ac, sa, bc, sb, (float[])o, shape); break;
            case DType.Float64: NumLoop<double, TK>(ac, sa, bc, sb, (double[])o, shape); break;
            default: throw new NDNotSupportedException($"unsupported dtype {rt}");
        }
        return Wrap(rt, o, shape);
    }

    private static void NumLoop<T, TK>(NDArray a, int[] sa, NDArray b, int[] sb, T[] o, int[] shape)
        where T : unmanaged, INumber<T> where TK : struct, INumBinary
    {
        var x = (T[])a.Buffer;
        var y = (T[])b.Buffer;
        var w = new RowWalker(shape, sa, sb);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, q = b.Offset + w.Off1, n = w.InnerLen, s0 = w.InnerStride0, s1 = w.InnerStride1;
            if (s0 == 1 && s1 == 1)
                for (int i = 0; i < n; i++) o[k + i] = TK.Apply(x[p + i], y[q + i]);
            else
                for (int i = 0; i < n; i++) o[k + i] = TK.Apply(x[p + i * s0], y[q + i * s1]);
            k += n;
        } while (w.Next());
    }

    // ================================================================ binary, integer

    public static NDArray Int<TK>(NDArray a, NDArray b, DType rt) where TK : struct, IIntBinary
    {
        if (rt == DType.Bool)
            return Int<TK>(a, b, DType.UInt8).CastTo(DType.Bool);
        if (!rt.IsInteger())
            throw new NDTypeException("ufunc not supported for the input types, and the inputs could not be safely coerced to any supported types according to the casting rule ''safe''");
        var shape = Broadcasting.Shape(a.Shape, b.Shape);
        var ac = a.CastTo(rt);
        var bc = b.CastTo(rt);
        var sa = Broadcasting.StridesFor(ac, shape);
        var sb = Broadcasting.StridesFor(bc, shape);
        var o = Alloc(rt, shape);
        switch (rt)
        {
            case DType.Int8: IntLoop<sbyte, TK>(ac, sa, bc, sb, (sbyte[])o, shape); break;
            case DType.UInt8: IntLoop<byte, TK>(ac, sa, bc, sb, (byte[])o, shape); break;
            case DType.Int16: IntLoop<short, TK>(ac, sa, bc, sb, (short[])o, shape); break;
            case DType.UInt16: IntLoop<ushort, TK>(ac, sa, bc, sb, (ushort[])o, shape); break;
            case DType.Int32: IntLoop<int, TK>(ac, sa, bc, sb, (int[])o, shape); break;
            case DType.UInt32: IntLoop<uint, TK>(ac, sa, bc, sb, (uint[])o, shape); break;
            case DType.Int64: IntLoop<long, TK>(ac, sa, bc, sb, (long[])o, shape); break;
            case DType.UInt64: IntLoop<ulong, TK>(ac, sa, bc, sb, (ulong[])o, shape); break;
            default: throw new NDNotSupportedException($"unsupported dtype {rt}");
        }
        return Wrap(rt, o, shape);
    }

    private static void IntLoop<T, TK>(NDArray a, int[] sa, NDArray b, int[] sb, T[] o, int[] shape)
        where T : unmanaged, IBinaryInteger<T> where TK : struct, IIntBinary
    {
        var x = (T[])a.Buffer;
        var y = (T[])b.Buffer;
        var w = new RowWalker(shape, sa, sb);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, q = b.Offset + w.Off1, n = w.InnerLen, s0 = w.InnerStride0, s1 = w.InnerStride1;
            for (int i = 0; i < n; i++) o[k + i] = TK.Apply(x[p + i * s0], y[q + i * s1]);
            k += n;
        } while (w.Next());
    }

    // ================================================================ binary, float

    /// <summary>Binary operation in a floating dtype; the result dtype is <paramref name="ft"/>
    /// (callers pass <see cref="DTypes.FloatResult"/> of the promoted type, or float64 for
    /// true division of integers).</summary>
    public static NDArray Float<TK>(NDArray a, NDArray b, DType ft) where TK : struct, IFloatBinary
    {
        var shape = Broadcasting.Shape(a.Shape, b.Shape);
        var ac = a.CastTo(ft);
        var bc = b.CastTo(ft);
        var sa = Broadcasting.StridesFor(ac, shape);
        var sb = Broadcasting.StridesFor(bc, shape);
        var o = Alloc(ft, shape);
        switch (ft)
        {
            case DType.Float16: FloatLoop<Half, TK>(ac, sa, bc, sb, (Half[])o, shape); break;
            case DType.Float32: FloatLoop<float, TK>(ac, sa, bc, sb, (float[])o, shape); break;
            case DType.Float64: FloatLoop<double, TK>(ac, sa, bc, sb, (double[])o, shape); break;
            default: throw new NDNotSupportedException($"{ft} is not a floating dtype");
        }
        return Wrap(ft, o, shape);
    }

    private static void FloatLoop<T, TK>(NDArray a, int[] sa, NDArray b, int[] sb, T[] o, int[] shape)
        where T : unmanaged, IFloatingPointIeee754<T> where TK : struct, IFloatBinary
    {
        var x = (T[])a.Buffer;
        var y = (T[])b.Buffer;
        var w = new RowWalker(shape, sa, sb);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, q = b.Offset + w.Off1, n = w.InnerLen, s0 = w.InnerStride0, s1 = w.InnerStride1;
            if (s0 == 1 && s1 == 1)
                for (int i = 0; i < n; i++) o[k + i] = TK.Apply(x[p + i], y[q + i]);
            else
                for (int i = 0; i < n; i++) o[k + i] = TK.Apply(x[p + i * s0], y[q + i * s1]);
            k += n;
        } while (w.Next());
    }

    // ================================================================ comparisons

    public static NDArray Compare<TK>(NDArray a, NDArray b) where TK : struct, INumCompare
    {
        var ct = ResultType(a, b, forCompare: true);
        if (ct == DType.Bool) ct = DType.UInt8;
        var shape = Broadcasting.Shape(a.Shape, b.Shape);
        var ac = a.CastTo(ct);
        var bc = b.CastTo(ct);
        var sa = Broadcasting.StridesFor(ac, shape);
        var sb = Broadcasting.StridesFor(bc, shape);
        var o = new bool[NDArray.SizeOf(shape)];
        switch (ct)
        {
            case DType.Int8: CmpLoop<sbyte, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.UInt8: CmpLoop<byte, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.Int16: CmpLoop<short, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.UInt16: CmpLoop<ushort, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.Int32: CmpLoop<int, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.UInt32: CmpLoop<uint, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.Int64: CmpLoop<long, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.UInt64: CmpLoop<ulong, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.Float16: CmpLoop<Half, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.Float32: CmpLoop<float, TK>(ac, sa, bc, sb, o, shape); break;
            case DType.Float64: CmpLoop<double, TK>(ac, sa, bc, sb, o, shape); break;
            default: throw new NDNotSupportedException($"unsupported dtype {ct}");
        }
        return Wrap(DType.Bool, o, shape);
    }

    private static void CmpLoop<T, TK>(NDArray a, int[] sa, NDArray b, int[] sb, bool[] o, int[] shape)
        where T : unmanaged, INumber<T> where TK : struct, INumCompare
    {
        var x = (T[])a.Buffer;
        var y = (T[])b.Buffer;
        var w = new RowWalker(shape, sa, sb);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, q = b.Offset + w.Off1, n = w.InnerLen, s0 = w.InnerStride0, s1 = w.InnerStride1;
            for (int i = 0; i < n; i++) o[k + i] = TK.Apply(x[p + i * s0], y[q + i * s1]);
            k += n;
        } while (w.Next());
    }

    // ================================================================ unary, dtype-preserving

    public static NDArray UnNum<TK>(NDArray a) where TK : struct, INumUnary
    {
        var d = a.DType;
        if (d == DType.Bool)
            return UnNum<TK>(a.CastTo(DType.UInt8)).CastTo(DType.Bool);
        var o = Alloc(d, a.Shape);
        switch (d)
        {
            case DType.Int8: UnLoop<sbyte, TK>(a, (sbyte[])o); break;
            case DType.UInt8: UnLoop<byte, TK>(a, (byte[])o); break;
            case DType.Int16: UnLoop<short, TK>(a, (short[])o); break;
            case DType.UInt16: UnLoop<ushort, TK>(a, (ushort[])o); break;
            case DType.Int32: UnLoop<int, TK>(a, (int[])o); break;
            case DType.UInt32: UnLoop<uint, TK>(a, (uint[])o); break;
            case DType.Int64: UnLoop<long, TK>(a, (long[])o); break;
            case DType.UInt64: UnLoop<ulong, TK>(a, (ulong[])o); break;
            case DType.Float16: UnLoop<Half, TK>(a, (Half[])o); break;
            case DType.Float32: UnLoop<float, TK>(a, (float[])o); break;
            case DType.Float64: UnLoop<double, TK>(a, (double[])o); break;
            default: throw new NDNotSupportedException($"unsupported dtype {d}");
        }
        return Wrap(d, o, (int[])a.Shape.Clone());
    }

    private static void UnLoop<T, TK>(NDArray a, T[] o) where T : unmanaged, INumber<T> where TK : struct, INumUnary
    {
        var x = (T[])a.Buffer;
        var w = new RowWalker(a.Shape, a.Strides);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, n = w.InnerLen, s0 = w.InnerStride0;
            for (int i = 0; i < n; i++) o[k + i] = TK.Apply(x[p + i * s0]);
            k += n;
        } while (w.Next());
    }

    // ================================================================ unary, floating

    public static NDArray UnFloat<TK>(NDArray a) where TK : struct, IFloatUnary
        => UnFloatAs<TK>(a, DTypes.FloatResult(a.DType));

    public static NDArray UnFloatAs<TK>(NDArray a, DType ft) where TK : struct, IFloatUnary
    {
        var ac = a.CastTo(ft);
        var o = Alloc(ft, a.Shape);
        switch (ft)
        {
            case DType.Float16: UnFloatLoop<Half, TK>(ac, (Half[])o); break;
            case DType.Float32: UnFloatLoop<float, TK>(ac, (float[])o); break;
            case DType.Float64: UnFloatLoop<double, TK>(ac, (double[])o); break;
            default: throw new NDNotSupportedException($"{ft} is not a floating dtype");
        }
        return Wrap(ft, o, (int[])a.Shape.Clone());
    }

    private static void UnFloatLoop<T, TK>(NDArray a, T[] o) where T : unmanaged, IFloatingPointIeee754<T> where TK : struct, IFloatUnary
    {
        var x = (T[])a.Buffer;
        var w = new RowWalker(a.Shape, a.Strides);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, n = w.InnerLen, s0 = w.InnerStride0;
            for (int i = 0; i < n; i++) o[k + i] = TK.Apply(x[p + i * s0]);
            k += n;
        } while (w.Next());
    }

    /// <summary>Predicate on floats (isnan/isinf/isfinite/signbit); non-float input is answered
    /// directly: no NaN, no inf, always finite.</summary>
    public static NDArray Predicate<TK>(NDArray a, bool nonFloatAnswer) where TK : struct, IFloatPredicate
    {
        var o = new bool[a.Size];
        if (!a.DType.IsFloat())
        {
            if (nonFloatAnswer) Array.Fill(o, true);
            return Wrap(DType.Bool, o, (int[])a.Shape.Clone());
        }
        switch (a.DType)
        {
            case DType.Float16: PredLoop<Half, TK>(a, o); break;
            case DType.Float32: PredLoop<float, TK>(a, o); break;
            default: PredLoop<double, TK>(a, o); break;
        }
        return Wrap(DType.Bool, o, (int[])a.Shape.Clone());
    }

    private static void PredLoop<T, TK>(NDArray a, bool[] o) where T : unmanaged, IFloatingPointIeee754<T> where TK : struct, IFloatPredicate
    {
        var x = (T[])a.Buffer;
        var w = new RowWalker(a.Shape, a.Strides);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, n = w.InnerLen, s0 = w.InnerStride0;
            for (int i = 0; i < n; i++) o[k + i] = TK.Apply(x[p + i * s0]);
            k += n;
        } while (w.Next());
    }

    // ================================================================ rounding with decimals

    public static NDArray RoundFloat(NDArray a, int decimals)
    {
        var ft = a.DType;
        var o = Alloc(ft, a.Shape);
        switch (ft)
        {
            case DType.Float16: RoundLoop<Half>(a, (Half[])o, decimals); break;
            case DType.Float32: RoundLoop<float>(a, (float[])o, decimals); break;
            default: RoundLoop<double>(a, (double[])o, decimals); break;
        }
        return Wrap(ft, o, (int[])a.Shape.Clone());
    }

    private static void RoundLoop<T>(NDArray a, T[] o, int decimals) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var x = (T[])a.Buffer;
        T scale = T.Pow(T.CreateTruncating(10), T.CreateTruncating(Math.Abs(decimals)));
        var w = new RowWalker(a.Shape, a.Strides);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, n = w.InnerLen, s0 = w.InnerStride0;
            for (int i = 0; i < n; i++)
            {
                T v = x[p + i * s0];
                o[k + i] = decimals >= 0
                    ? T.Round(v * scale, MidpointRounding.ToEven) / scale
                    : T.Round(v / scale, MidpointRounding.ToEven) * scale;
            }
            k += n;
        } while (w.Next());
    }

    // ================================================================ where

    public static NDArray Where(NDArray cond, NDArray x, NDArray y)
    {
        var rt = ResultType(x, y);
        var shape = Broadcasting.Shape(cond.Shape, x.Shape, y.Shape);
        var cc = cond.CastTo(DType.Bool);
        var xc = x.CastTo(rt);
        var yc = y.CastTo(rt);
        var sc = Broadcasting.StridesFor(cc, shape);
        var sx = Broadcasting.StridesFor(xc, shape);
        var sy = Broadcasting.StridesFor(yc, shape);
        var o = Alloc(rt, shape);
        switch (rt)
        {
            case DType.Bool: WhereLoop(cc, sc, (bool[])xc.Buffer, xc.Offset, sx, (bool[])yc.Buffer, yc.Offset, sy, (bool[])o, shape); break;
            case DType.Int8: WhereLoop(cc, sc, (sbyte[])xc.Buffer, xc.Offset, sx, (sbyte[])yc.Buffer, yc.Offset, sy, (sbyte[])o, shape); break;
            case DType.UInt8: WhereLoop(cc, sc, (byte[])xc.Buffer, xc.Offset, sx, (byte[])yc.Buffer, yc.Offset, sy, (byte[])o, shape); break;
            case DType.Int16: WhereLoop(cc, sc, (short[])xc.Buffer, xc.Offset, sx, (short[])yc.Buffer, yc.Offset, sy, (short[])o, shape); break;
            case DType.UInt16: WhereLoop(cc, sc, (ushort[])xc.Buffer, xc.Offset, sx, (ushort[])yc.Buffer, yc.Offset, sy, (ushort[])o, shape); break;
            case DType.Int32: WhereLoop(cc, sc, (int[])xc.Buffer, xc.Offset, sx, (int[])yc.Buffer, yc.Offset, sy, (int[])o, shape); break;
            case DType.UInt32: WhereLoop(cc, sc, (uint[])xc.Buffer, xc.Offset, sx, (uint[])yc.Buffer, yc.Offset, sy, (uint[])o, shape); break;
            case DType.Int64: WhereLoop(cc, sc, (long[])xc.Buffer, xc.Offset, sx, (long[])yc.Buffer, yc.Offset, sy, (long[])o, shape); break;
            case DType.UInt64: WhereLoop(cc, sc, (ulong[])xc.Buffer, xc.Offset, sx, (ulong[])yc.Buffer, yc.Offset, sy, (ulong[])o, shape); break;
            case DType.Float16: WhereLoop(cc, sc, (Half[])xc.Buffer, xc.Offset, sx, (Half[])yc.Buffer, yc.Offset, sy, (Half[])o, shape); break;
            case DType.Float32: WhereLoop(cc, sc, (float[])xc.Buffer, xc.Offset, sx, (float[])yc.Buffer, yc.Offset, sy, (float[])o, shape); break;
            case DType.Complex64:
            case DType.Complex128: WhereLoop(cc, sc, (Complex[])xc.Buffer, xc.Offset, sx, (Complex[])yc.Buffer, yc.Offset, sy, (Complex[])o, shape); break;
            default: WhereLoop(cc, sc, (double[])xc.Buffer, xc.Offset, sx, (double[])yc.Buffer, yc.Offset, sy, (double[])o, shape); break;
        }
        return Wrap(rt, o, shape);
    }

    private static void WhereLoop<T>(NDArray c, int[] sc, T[] x, int xo, int[] sx, T[] y, int yo, int[] sy, T[] o, int[] shape)
        where T : unmanaged
    {
        var cb = (bool[])c.Buffer;
        var w = new RowWalker(shape, sc, sx, sy);
        int k = 0;
        do
        {
            int p = c.Offset + w.Off0, q = xo + w.Off1, r = yo + w.Off2, n = w.InnerLen;
            int s0 = w.InnerStride0, s1 = w.InnerStride1, s2 = w.InnerStride2;
            for (int i = 0; i < n; i++)
                o[k + i] = cb[p + i * s0] ? x[q + i * s1] : y[r + i * s2];
            k += n;
        } while (w.Next());
    }
}
