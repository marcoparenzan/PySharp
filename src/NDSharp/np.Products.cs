// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp;

public static partial class np
{
    // ================================================================ products

    /// <summary>numpy <c>matmul</c> / <c>@</c>: 1-D operands are promoted to a row/column and the
    /// extra axis dropped again; leading (batch) dimensions broadcast.</summary>
    public static NDArray MatMul(NDArray a, NDArray b)
    {
        if (a.Ndim == 0 || b.Ndim == 0)
            throw new NDValueException("matmul: Input operand does not have enough dimensions (has 0, gufunc core with signature (n?,k),(k,m?)->(n?,m?) requires 1)");
        var rt = DTypes.Promote(a.DType, b.DType);
        if (rt == DType.Bool) return MatMul(a.CastTo(DType.UInt8), b.CastTo(DType.UInt8)).CastTo(DType.Bool);
        bool a1 = a.Ndim == 1, b1 = b.Ndim == 1;
        var a2 = a1 ? ExpandDims(a, 0) : a;
        var b2 = b1 ? ExpandDims(b, 1) : b;
        int m = a2.Shape[^2], k = a2.Shape[^1], k2 = b2.Shape[^2], n = b2.Shape[^1];
        if (k != k2)
            throw new NDValueException(
                $"matmul: Input operand 1 has a mismatch in its core dimension 0, with gufunc signature (n?,k),(k,m?)->(n?,m?) (size {k2} is different from {k})");
        var batchA = a2.Shape[..^2];
        var batchB = b2.Shape[..^2];
        var batch = Broadcasting.Shape(batchA, batchB);
        var ac = a2.CastTo(rt);
        var bc = b2.CastTo(rt);
        var sa = Broadcasting.StridesFor(ac.ViewWith(batchA, ac.Strides[..^2], ac.Offset), batch);
        var sb = Broadcasting.StridesFor(bc.ViewWith(batchB, bc.Strides[..^2], bc.Offset), batch);
        var outShape = batch.Concat(new[] { m, n }).ToArray();
        var result = New(rt, outShape);
        switch (rt)
        {
            case DType.Int8: MatMulT<sbyte>(ac, bc, sa, sb, batch, m, k, n, (sbyte[])result.Buffer); break;
            case DType.UInt8: MatMulT<byte>(ac, bc, sa, sb, batch, m, k, n, (byte[])result.Buffer); break;
            case DType.Int16: MatMulT<short>(ac, bc, sa, sb, batch, m, k, n, (short[])result.Buffer); break;
            case DType.UInt16: MatMulT<ushort>(ac, bc, sa, sb, batch, m, k, n, (ushort[])result.Buffer); break;
            case DType.Int32: MatMulT<int>(ac, bc, sa, sb, batch, m, k, n, (int[])result.Buffer); break;
            case DType.UInt32: MatMulT<uint>(ac, bc, sa, sb, batch, m, k, n, (uint[])result.Buffer); break;
            case DType.Int64: MatMulT<long>(ac, bc, sa, sb, batch, m, k, n, (long[])result.Buffer); break;
            case DType.UInt64: MatMulT<ulong>(ac, bc, sa, sb, batch, m, k, n, (ulong[])result.Buffer); break;
            case DType.Float16: MatMulT<Half>(ac, bc, sa, sb, batch, m, k, n, (Half[])result.Buffer); break;
            case DType.Float32: MatMulT<float>(ac, bc, sa, sb, batch, m, k, n, (float[])result.Buffer); break;
            case DType.Complex64:
            case DType.Complex128:
                MatMulT<System.Numerics.Complex>(ac, bc, sa, sb, batch, m, k, n, (System.Numerics.Complex[])result.Buffer);
                if (rt == DType.Complex64) Cx.RoundC64((System.Numerics.Complex[])result.Buffer);
                break;
            default: MatMulT<double>(ac, bc, sa, sb, batch, m, k, n, (double[])result.Buffer); break;
        }
        // Drop the synthesized axes.
        if (a1 && b1) return Reshape(result, batch);
        if (a1) return Reshape(result, batch.Concat(new[] { n }).ToArray());
        if (b1) return Reshape(result, batch.Concat(new[] { m }).ToArray());
        return result;
    }

    private static void MatMulT<T>(NDArray a, NDArray b, int[] sa, int[] sb, int[] batch, int m, int k, int n, T[] o)
        where T : unmanaged, INumberBase<T>
    {
        var x = (T[])a.Buffer;
        var y = (T[])b.Buffer;
        int ar = a.Strides[^2], ac = a.Strides[^1], br = b.Strides[^2], bcs = b.Strides[^1];
        var w = new RowWalker(batch, sa, sb);
        int ob = 0;
        int mn = m * n;
        do
        {
            for (int bi = 0; bi < w.InnerLen; bi++)
            {
                int pa = a.Offset + w.Off0 + bi * w.InnerStride0;
                int pb = b.Offset + w.Off1 + bi * w.InnerStride1;
                for (int i = 0; i < m; i++)
                {
                    int orow = ob + i * n;
                    for (int kk = 0; kk < k; kk++)
                    {
                        T av = x[pa + i * ar + kk * ac];
                        int prow = pb + kk * br;
                        if (bcs == 1)
                            for (int j = 0; j < n; j++) o[orow + j] += av * y[prow + j];
                        else
                            for (int j = 0; j < n; j++) o[orow + j] += av * y[prow + j * bcs];
                    }
                }
                ob += mn;
            }
        } while (w.Next());
    }

    /// <summary>numpy <c>dot</c>: scalars multiply, 1-D/2-D operands use matmul. (N-D × N-D "sum over
    /// last/second-to-last" is not implemented.)</summary>
    public static NDArray Dot(NDArray a, NDArray b)
    {
        if (a.Ndim == 0 || b.Ndim == 0) return Multiply(a, b);
        if (a.Ndim <= 2 && b.Ndim <= 2) return MatMul(a, b);
        if (b.Ndim == 1) return MatMul(a, b);
        throw new NDNotSupportedException("dot of N-D arrays (N > 2) with N-D arrays is not implemented");
    }

    public static NDArray Outer(NDArray a, NDArray b) => Multiply(Reshape(a, a.Size, 1), Reshape(b, 1, b.Size));

    public static NDArray Trace(NDArray a, int offset = 0) => Sum(Diagonal(a, offset));

    public static NDArray Inner(NDArray a, NDArray b)
    {
        if (a.Ndim == 0 || b.Ndim == 0) return Multiply(a, b);
        if (a.Ndim <= 2 && b.Ndim <= 2) return MatMul(a, Transpose(b));
        throw new NDNotSupportedException("inner of N-D arrays is not implemented");
    }

    // ================================================================ norm

    /// <summary>numpy <c>linalg.norm</c> with <c>ord=None</c> (2-norm of the flattened array / Frobenius),
    /// optionally along axes.</summary>
    public static NDArray Norm(NDArray x, int[]? axis = null, bool keepdims = false)
    {
        var f = x.DType.IsFloat() ? x : x.CastTo(DType.Float64);
        var sq = Multiply(f, f);
        return Sqrt(Sum(sq, axis, keepdims));
    }

    public static NDArray Norm(NDArray x, double ord, int[]? axis = null, bool keepdims = false)
    {
        var f = x.DType.IsFloat() ? x : x.CastTo(DType.Float64);
        var ab = Abs(f);
        if (ord == 1) return Sum(ab, axis, keepdims);
        if (double.IsPositiveInfinity(ord)) return Max(ab, axis, keepdims);
        if (double.IsNegativeInfinity(ord)) return Min(ab, axis, keepdims);
        if (ord == 0) return Sum(NotEqual(f, NDArray.WeakScalar(0L)).AsType(f.DType), axis, keepdims);
        if (ord == 2) return Norm(x, axis, keepdims);
        var p = NDArray.WeakScalar(ord);
        return Power(Sum(Power(ab, p), axis, keepdims), NDArray.WeakScalar(1.0 / ord));
    }
}
