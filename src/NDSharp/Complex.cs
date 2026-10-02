// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp;

/// <summary>Complex-number engines. Complex arrays are stored as <see cref="Complex"/> (double parts);
/// <c>complex64</c> rounds both parts to single precision after every operation so its values (and
/// printed digits) are those of a true single-precision complex.</summary>
internal static class Cx
{
    public static Complex RoundC64(Complex c) => new((float)c.Real, (float)c.Imaginary);

    public static void RoundC64(Complex[] buf)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = RoundC64(buf[i]);
    }

    private static NDArray Alloc(DType d, int[] shape)
        => new(d, new Complex[NDArray.SizeOf(shape)], (int[])shape.Clone());

    /// <summary>Elementwise binary operation in complex dtype <paramref name="rt"/> with broadcasting.</summary>
    public static NDArray Binary(NDArray a, NDArray b, DType rt, Func<Complex, Complex, Complex> f)
    {
        var shape = Broadcasting.Shape(a.Shape, b.Shape);
        var ac = a.CastTo(rt);
        var bc = b.CastTo(rt);
        var sa = Broadcasting.StridesFor(ac, shape);
        var sb = Broadcasting.StridesFor(bc, shape);
        var result = Alloc(rt, shape);
        var o = (Complex[])result.Buffer;
        var x = (Complex[])ac.Buffer;
        var y = (Complex[])bc.Buffer;
        var w = new RowWalker(shape, sa, sb);
        int k = 0;
        do
        {
            int p = ac.Offset + w.Off0, q = bc.Offset + w.Off1, n = w.InnerLen, s0 = w.InnerStride0, s1 = w.InnerStride1;
            for (int i = 0; i < n; i++) o[k + i] = f(x[p + i * s0], y[q + i * s1]);
            k += n;
        } while (w.Next());
        if (rt == DType.Complex64) RoundC64(o);
        return result;
    }

    public static NDArray Compare(NDArray a, NDArray b, DType ct, Func<Complex, Complex, bool> f)
    {
        var shape = Broadcasting.Shape(a.Shape, b.Shape);
        var ac = a.CastTo(ct);
        var bc = b.CastTo(ct);
        var sa = Broadcasting.StridesFor(ac, shape);
        var sb = Broadcasting.StridesFor(bc, shape);
        var o = new bool[NDArray.SizeOf(shape)];
        var x = (Complex[])ac.Buffer;
        var y = (Complex[])bc.Buffer;
        var w = new RowWalker(shape, sa, sb);
        int k = 0;
        do
        {
            int p = ac.Offset + w.Off0, q = bc.Offset + w.Off1, n = w.InnerLen, s0 = w.InnerStride0, s1 = w.InnerStride1;
            for (int i = 0; i < n; i++) o[k + i] = f(x[p + i * s0], y[q + i * s1]);
            k += n;
        } while (w.Next());
        return new NDArray(DType.Bool, o, shape);
    }

    /// <summary>complex → complex, same dtype.</summary>
    public static NDArray Unary(NDArray a, Func<Complex, Complex> f)
    {
        var result = Alloc(a.DType, a.Shape);
        var o = (Complex[])result.Buffer;
        var x = (Complex[])a.Buffer;
        var w = new RowWalker(a.Shape, a.Strides);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, n = w.InnerLen, s0 = w.InnerStride0;
            for (int i = 0; i < n; i++) o[k + i] = f(x[p + i * s0]);
            k += n;
        } while (w.Next());
        if (a.DType == DType.Complex64) RoundC64(o);
        return result;
    }

    /// <summary>complex → real (abs, real part, imaginary part, angle), in the real dtype of the parts.</summary>
    public static NDArray ToReal(NDArray a, Func<Complex, double> f)
    {
        var rd = a.DType.RealPart();
        var o = new double[a.Size];
        var x = (Complex[])a.Buffer;
        var w = new RowWalker(a.Shape, a.Strides);
        int k = 0;
        do
        {
            int p = a.Offset + w.Off0, n = w.InnerLen, s0 = w.InnerStride0;
            for (int i = 0; i < n; i++) o[k + i] = f(x[p + i * s0]);
            k += n;
        } while (w.Next());
        var r = new NDArray(DType.Float64, o, (int[])a.Shape.Clone());
        return rd == DType.Float64 ? r : r.AsType(rd);
    }

    /// <summary>Builds a complex array from real and imaginary parts (broadcast together).</summary>
    public static NDArray FromParts(NDArray re, NDArray im, DType complexType)
    {
        var shape = Broadcasting.Shape(re.Shape, im.Shape);
        var r = Broadcasting.BroadcastTo(re.AsType(DType.Float64), shape).Copy().ToArray<double>();
        var i = Broadcasting.BroadcastTo(im.AsType(DType.Float64), shape).Copy().ToArray<double>();
        var o = new Complex[r.Length];
        for (int k = 0; k < o.Length; k++) o[k] = new Complex(r[k], i[k]);
        if (complexType == DType.Complex64) RoundC64(o);
        return new NDArray(complexType, o, shape);
    }
}

public static partial class np
{
    private static readonly Func<Complex, Complex, Complex> CxAdd = (x, y) => x + y;

    /// <summary>numpy <c>real</c>: the real part (a copy; the argument itself for real dtypes).</summary>
    public static NDArray Real(NDArray a) => a.DType.IsComplex() ? Cx.ToReal(a, c => c.Real) : a;

    /// <summary>numpy <c>imag</c>: the imaginary part (zeros for real dtypes).</summary>
    public static NDArray Imag(NDArray a) => a.DType.IsComplex() ? Cx.ToReal(a, c => c.Imaginary) : ZerosLike(a);

    public static NDArray Conj(NDArray a) => a.DType.IsComplex() ? Cx.Unary(a, Complex.Conjugate) : a.Copy();

    /// <summary>numpy <c>angle</c> (radians, or degrees with <paramref name="deg"/>).</summary>
    public static NDArray Angle(NDArray a, bool deg = false)
    {
        var r = a.DType.IsComplex()
            ? Cx.ToReal(a, c => Math.Atan2(c.Imaginary, c.Real))
            : Arctan2(ZerosLike(a, DTypes.FloatResult(a.DType)), a.CastTo(DTypes.FloatResult(a.DType)));
        return deg ? Degrees(r) : r;
    }

    /// <summary>numpy <c>complex(re, im)</c>-style construction from real and imaginary arrays.</summary>
    public static NDArray MakeComplex(NDArray re, NDArray im)
        => Cx.FromParts(re, im, DTypes.Promote(re.DType.IsFloat() ? re.DType : DType.Float64, im.DType.IsFloat() ? im.DType : DType.Float64) == DType.Float32 ? DType.Complex64 : DType.Complex128);
}
