// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Runtime.InteropServices;

namespace NDSharp;

/// <summary>dtype conversion with numpy's actual (x86-64) semantics: integer → integer wraps
/// modulo 2^n; float → integer truncates toward zero through a C-style conversion (so
/// <c>300.0 → uint8</c> is 44, <c>-1.0 → uint8</c> is 255, NaN gives the "integer indefinite"
/// value); anything → bool is "is non-zero" (NaN is true).</summary>
internal static class Cast
{
    /// <summary>Converts every element of <paramref name="src"/> (visited in C-order, honoring its
    /// strides) into a fresh contiguous buffer of <paramref name="to"/>.</summary>
    public static Array ToBuffer(NDArray src, DType to)
    {
        if (src.DType.IsTemporal() || to.IsTemporal())
        {
            var source = src.DType.IsTemporal() || src.DType == DType.Int64 ? src : src.AsType(DType.Int64);
            var ticks = Temporal.Convert(source, to);
            if (to == DType.Int64 || to.IsTemporal()) return ticks;
            return ToBuffer(new NDArray(DType.Int64, ticks, (int[])src.Shape.Clone()), to);
        }
        var dst = Array.CreateInstance(to.ClrType(), src.Size);
        if (src.Size == 0) return dst;
        return src.DType.Storage() switch
        {
            DType.Bool => From<byte>(MemoryMarshal.Cast<bool, byte>((bool[])src.Buffer).ToArray(), src, to, dst),
            DType.Int8 => From((sbyte[])src.Buffer, src, to, dst),
            DType.UInt8 => From((byte[])src.Buffer, src, to, dst),
            DType.Int16 => From((short[])src.Buffer, src, to, dst),
            DType.UInt16 => From((ushort[])src.Buffer, src, to, dst),
            DType.Int32 => From((int[])src.Buffer, src, to, dst),
            DType.UInt32 => From((uint[])src.Buffer, src, to, dst),
            DType.Int64 => From((long[])src.Buffer, src, to, dst),
            DType.UInt64 => From((ulong[])src.Buffer, src, to, dst),
            DType.Float16 => From((Half[])src.Buffer, src, to, dst),
            DType.Float32 => From((float[])src.Buffer, src, to, dst),
            DType.Float64 => From((double[])src.Buffer, src, to, dst),
            DType.Complex64 or DType.Complex128 => From((Complex[])src.Buffer, src, to, dst),
            _ => throw new NDNotSupportedException($"unsupported dtype {src.DType}"),
        };
    }

    private static Array From<TS>(TS[] buf, NDArray src, DType to, Array dst) where TS : unmanaged, INumberBase<TS>
    {
        switch (to.Storage())
        {
            case DType.Bool: Loop(buf, src, (bool[])dst); break;
            case DType.Int8: Loop(buf, src, (sbyte[])dst); break;
            case DType.UInt8: Loop(buf, src, (byte[])dst); break;
            case DType.Int16: Loop(buf, src, (short[])dst); break;
            case DType.UInt16: Loop(buf, src, (ushort[])dst); break;
            case DType.Int32: Loop(buf, src, (int[])dst); break;
            case DType.UInt32: Loop(buf, src, (uint[])dst); break;
            case DType.Int64: Loop(buf, src, (long[])dst); break;
            case DType.UInt64: Loop(buf, src, (ulong[])dst); break;
            case DType.Float16: Loop(buf, src, (Half[])dst); break;
            case DType.Float32: Loop(buf, src, (float[])dst); break;
            case DType.Float64: Loop(buf, src, (double[])dst); break;
            case DType.Complex64:
            case DType.Complex128:
                Loop(buf, src, (Complex[])dst);
                if (to == DType.Complex64) Cx.RoundC64((Complex[])dst);
                break;
            default: throw new NDNotSupportedException($"unsupported dtype {to}");
        }
        return dst;
    }

    private static void Loop<TS, TD>(TS[] buf, NDArray src, TD[] dst)
        where TS : unmanaged, INumberBase<TS>
        where TD : unmanaged
    {
        // Contiguous fast path: one straight run.
        if (src.IsContiguous)
        {
            int o = src.Offset;
            for (int i = 0; i < dst.Length; i++)
                dst[i] = Conv<TS, TD>(buf[o + i]);
            return;
        }
        var w = new RowWalker(src.Shape, src.Strides);
        int k = 0;
        do
        {
            int p = w.Off0;
            for (int i = 0; i < w.InnerLen; i++, p += w.InnerStride0)
                dst[k++] = Conv<TS, TD>(buf[src.Offset + p]);
        } while (w.Next());
    }

    private static bool IsFloat<T>() => typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half);

    /// <summary>One element conversion. The <c>typeof</c> tests are JIT-time constants, so each
    /// (TS, TD) instantiation compiles down to just the branch that applies.</summary>
    public static TD Conv<TS, TD>(TS v) where TS : unmanaged, INumberBase<TS> where TD : unmanaged
    {
        if (typeof(TD) == typeof(bool))
            return (TD)(object)(v != TS.Zero);

        if (typeof(TS) == typeof(Complex))
        {
            var c = (Complex)(object)v;
            if (typeof(TD) == typeof(Complex)) return (TD)(object)c;
            return Conv<double, TD>(c.Real); // like numpy: the imaginary part is discarded
        }
        if (typeof(TD) == typeof(Complex))
            return (TD)(object)new Complex(double.CreateTruncating(v), 0);

        if (IsFloat<TS>() && !IsFloat<TD>())
        {
            double d = double.CreateTruncating(v);
            return FloatToInt<TD>(d);
        }

        // int → int (wrap), int → float, float → float
        return Generic<TS, TD>(v);
    }

    private static TD Generic<TS, TD>(TS v) where TS : unmanaged, INumberBase<TS> where TD : unmanaged
    {
        if (typeof(TD) == typeof(sbyte)) return (TD)(object)sbyte.CreateTruncating(v);
        if (typeof(TD) == typeof(byte)) return (TD)(object)byte.CreateTruncating(v);
        if (typeof(TD) == typeof(short)) return (TD)(object)short.CreateTruncating(v);
        if (typeof(TD) == typeof(ushort)) return (TD)(object)ushort.CreateTruncating(v);
        if (typeof(TD) == typeof(int)) return (TD)(object)int.CreateTruncating(v);
        if (typeof(TD) == typeof(uint)) return (TD)(object)uint.CreateTruncating(v);
        if (typeof(TD) == typeof(long)) return (TD)(object)long.CreateTruncating(v);
        if (typeof(TD) == typeof(ulong)) return (TD)(object)ulong.CreateTruncating(v);
        if (typeof(TD) == typeof(Half)) return (TD)(object)Half.CreateTruncating(v);
        if (typeof(TD) == typeof(float)) return (TD)(object)float.CreateTruncating(v);
        if (typeof(TD) == typeof(double)) return (TD)(object)double.CreateTruncating(v);
        throw new NDNotSupportedException($"unsupported dtype {typeof(TD).Name}");
    }

    /// <summary>C-style double → integer conversion as compiled for x86-64 by numpy's loops:
    /// small ints and int32 go through a 32-bit <c>cvttsd2si</c> (out-of-range → INT_MIN, then wrap
    /// to the target width), uint32/int64 through the 64-bit one (out-of-range → LONG_MIN),
    /// uint64 additionally handles [2^63, 2^64).</summary>
    private static TD FloatToInt<TD>(double d) where TD : unmanaged
    {
        if (typeof(TD) == typeof(ulong))
        {
            if (d >= 9223372036854775808.0 && d < 18446744073709551616.0)
                return (TD)(object)(ulong)d;
            return (TD)(object)(ulong)Cvt64(d);
        }
        if (typeof(TD) == typeof(long) || typeof(TD) == typeof(uint))
        {
            long l = Cvt64(d);
            return typeof(TD) == typeof(long) ? (TD)(object)l : (TD)(object)(uint)l;
        }
        long w = Cvt32(d);
        if (typeof(TD) == typeof(int)) return (TD)(object)(int)w;
        if (typeof(TD) == typeof(short)) return (TD)(object)(short)w;
        if (typeof(TD) == typeof(ushort)) return (TD)(object)(ushort)w;
        if (typeof(TD) == typeof(sbyte)) return (TD)(object)(sbyte)w;
        if (typeof(TD) == typeof(byte)) return (TD)(object)(byte)w;
        throw new NDNotSupportedException($"unsupported dtype {typeof(TD).Name}");
    }

    private static long Cvt32(double d)
        => double.IsNaN(d) || d >= 2147483648.0 || d <= -2147483649.0 ? int.MinValue : (int)d;

    private static long Cvt64(double d)
        => double.IsNaN(d) || d >= 9223372036854775808.0 || d < -9223372036854775808.0 ? long.MinValue : (long)d;

    /// <summary>Writes a boxed CLR value into <paramref name="a"/>'s buffer at absolute position
    /// <paramref name="pos"/> with conversion to the array's dtype.</summary>
    public static void StoreBoxed(NDArray a, int pos, object value)
    {
        // Dispatch on dtype, not on the CLR array type (byte[] is also an sbyte[], uint[] an int[], ...).
        switch (a.DType.Storage())
        {
            case DType.Bool: ((bool[])a.Buffer)[pos] = Boxed<bool>(value); break;
            case DType.Int8: ((sbyte[])a.Buffer)[pos] = Boxed<sbyte>(value); break;
            case DType.UInt8: ((byte[])a.Buffer)[pos] = Boxed<byte>(value); break;
            case DType.Int16: ((short[])a.Buffer)[pos] = Boxed<short>(value); break;
            case DType.UInt16: ((ushort[])a.Buffer)[pos] = Boxed<ushort>(value); break;
            case DType.Int32: ((int[])a.Buffer)[pos] = Boxed<int>(value); break;
            case DType.UInt32: ((uint[])a.Buffer)[pos] = Boxed<uint>(value); break;
            case DType.Int64: ((long[])a.Buffer)[pos] = Boxed<long>(value); break;
            case DType.UInt64: ((ulong[])a.Buffer)[pos] = Boxed<ulong>(value); break;
            case DType.Float16: ((Half[])a.Buffer)[pos] = Boxed<Half>(value); break;
            case DType.Float32: ((float[])a.Buffer)[pos] = Boxed<float>(value); break;
            case DType.Float64: ((double[])a.Buffer)[pos] = Boxed<double>(value); break;
            case DType.Complex64: ((Complex[])a.Buffer)[pos] = Cx.RoundC64(Boxed<Complex>(value)); break;
            case DType.Complex128: ((Complex[])a.Buffer)[pos] = Boxed<Complex>(value); break;
            default: throw new NDNotSupportedException("unsupported buffer");
        }
    }

    private static TD Boxed<TD>(object v) where TD : unmanaged => v switch
    {
        bool x => Conv<byte, TD>(x ? (byte)1 : (byte)0),
        sbyte x => Conv<sbyte, TD>(x),
        byte x => Conv<byte, TD>(x),
        short x => Conv<short, TD>(x),
        ushort x => Conv<ushort, TD>(x),
        int x => Conv<int, TD>(x),
        uint x => Conv<uint, TD>(x),
        long x => Conv<long, TD>(x),
        ulong x => Conv<ulong, TD>(x),
        Half x => Conv<Half, TD>(x),
        float x => Conv<float, TD>(x),
        double x => Conv<double, TD>(x),
        Complex x => Conv<Complex, TD>(x),
        BigInteger x => x >= long.MinValue && x <= long.MaxValue ? Conv<long, TD>((long)x)
            : x > 0 && x <= ulong.MaxValue ? Conv<ulong, TD>((ulong)x)
            : throw new NDOverflowException($"Python int too large to convert to C long: {x}"),
        _ => throw new NDTypeException($"cannot convert {v.GetType().Name} to a number"),
    };
}
