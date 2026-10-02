// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp;

/// <summary>Reduction engine: folds an array over a set of axes with a binary kind.
/// Strategy per output element: either a direct strided run (reduced axes coalesce into one
/// (length, stride) pair — the overwhelmingly common case) or a precomputed offset block.
/// Float additions over a contiguous run use numpy's pairwise algorithm so sums of large float32
/// arrays carry the same (small) rounding error numpy's do.</summary>
internal static class Red
{
    // ------------------------------------------------------------------ axes handling

    public static int[] NormalizeAxes(int[]? axes, int ndim)
    {
        if (axes is null) return Enumerable.Range(0, ndim).ToArray();
        var r = axes.Select(a => np.NormalizeAxis(a, ndim)).ToArray();
        if (r.Distinct().Count() != r.Length) throw new NDValueException("duplicate value in 'axis'");
        return r.OrderBy(x => x).ToArray();
    }

    public static int[] OutShape(int[] shape, int[] axes, bool keepdims)
    {
        var l = new List<int>();
        for (int i = 0; i < shape.Length; i++)
        {
            if (axes.Contains(i)) { if (keepdims) l.Add(1); }
            else l.Add(shape[i]);
        }
        return l.ToArray();
    }

    // ------------------------------------------------------------------ fold with a numeric kind

    /// <summary>Fold over <paramref name="axes"/> with kind <typeparamref name="TK"/> in a's own dtype.
    /// <paramref name="identity"/> seeds empty reductions (null → error, like numpy's max/min).</summary>
    public static NDArray Fold<TK>(NDArray a, int[] axes, bool keepdims, object? identity, string opName)
        where TK : struct, INumBinary
    {
        if (a.DType.IsComplex())
            throw new NDNotSupportedException("this reduction is not implemented for complex arrays");
        if (a.DType == DType.Bool)
            return Fold<TK>(a.CastTo(DType.UInt8), axes, keepdims, identity is null ? null : Convert.ToByte(identity is bool b ? (b ? 1 : 0) : identity), opName).CastTo(DType.Bool);
        var outShape = OutShape(a.Shape, axes, keepdims);
        var result = new NDArray(a.DType, Array.CreateInstance(a.DType.ClrType(), NDArray.SizeOf(outShape)), outShape);
        int blockSize = 1;
        foreach (var ax in axes) blockSize *= a.Shape[ax];
        if (blockSize == 0 && result.Size > 0 && identity is null)
            throw new NDValueException($"zero-size array to reduction operation {opName} which has no identity");
        switch (a.DType)
        {
            case DType.Int8: FoldT<sbyte, TK>(a, axes, (sbyte[])result.Buffer, identity); break;
            case DType.UInt8: FoldT<byte, TK>(a, axes, (byte[])result.Buffer, identity); break;
            case DType.Int16: FoldT<short, TK>(a, axes, (short[])result.Buffer, identity); break;
            case DType.UInt16: FoldT<ushort, TK>(a, axes, (ushort[])result.Buffer, identity); break;
            case DType.Int32: FoldT<int, TK>(a, axes, (int[])result.Buffer, identity); break;
            case DType.UInt32: FoldT<uint, TK>(a, axes, (uint[])result.Buffer, identity); break;
            case DType.Int64: FoldT<long, TK>(a, axes, (long[])result.Buffer, identity); break;
            case DType.UInt64: FoldT<ulong, TK>(a, axes, (ulong[])result.Buffer, identity); break;
            case DType.Float16: FoldT<Half, TK>(a, axes, (Half[])result.Buffer, identity); break;
            case DType.Float32: FoldT<float, TK>(a, axes, (float[])result.Buffer, identity); break;
            default: FoldT<double, TK>(a, axes, (double[])result.Buffer, identity); break;
        }
        return result;
    }

    private static T IdentityOf<T>(object? identity) where T : unmanaged, INumber<T>
        => identity is null ? T.Zero : T.CreateTruncating(Convert.ToDouble(identity is bool b ? (b ? 1 : 0) : identity));

    private static void FoldT<T, TK>(NDArray a, int[] axes, T[] o, object? identity)
        where T : unmanaged, INumber<T> where TK : struct, INumBinary
    {
        var x = (T[])a.Buffer;
        var keptDims = Enumerable.Range(0, a.Ndim).Where(i => !axes.Contains(i)).ToArray();
        var keptShape = keptDims.Select(i => a.Shape[i]).ToArray();
        var keptStrides = keptDims.Select(i => a.Strides[i]).ToArray();
        var redShape = axes.Select(i => a.Shape[i]).ToArray();
        var redStrides = axes.Select(i => a.Strides[i]).ToArray();
        T id = IdentityOf<T>(identity);
        bool hasId = identity is not null;

        // Coalesce the reduced dims into runs. A single run → direct loop; otherwise offset block.
        var rw = new RowWalker(redShape, redStrides);
        bool single = true;
        {
            // Single iff the walker has no outer rows: InnerLen == product of sizes.
            long total = 1;
            foreach (var s in redShape) total *= s;
            single = rw.InnerLen == total || total == 0;
        }
        int[]? block = null;
        if (!single)
        {
            var list = new List<int>();
            var w2 = new RowWalker(redShape, redStrides);
            do
            {
                for (int i = 0; i < w2.InnerLen; i++) list.Add(w2.Off0 + i * w2.InnerStride0);
            } while (w2.Next());
            block = list.ToArray();
        }

        var kw = new RowWalker(keptShape, keptStrides);
        int k = 0;
        long count = 1;
        foreach (var s in keptShape) count *= s;
        if (count == 0) return;
        do
        {
            for (int j = 0; j < kw.InnerLen; j++)
            {
                int basePos = a.Offset + kw.Off0 + j * kw.InnerStride0;
                T acc;
                if (single)
                {
                    int n = rw.InnerLen, st = rw.InnerStride0;
                    if (n == 0) acc = id;
                    else if (typeof(TK) == typeof(AddK) && st == 1) acc = PairwiseAdd(x, basePos, n);
                    else
                    {
                        acc = x[basePos];
                        for (int i = 1; i < n; i++) acc = TK.Apply(acc, x[basePos + i * st]);
                        if (hasId && typeof(TK) == typeof(AddK)) { /* sum seed is zero: already exact */ }
                    }
                }
                else
                {
                    acc = x[basePos + block![0]];
                    for (int i = 1; i < block.Length; i++) acc = TK.Apply(acc, x[basePos + block[i]]);
                }
                o[k++] = acc;
            }
        } while (kw.Next());
    }

    /// <summary>numpy's pairwise summation (blocks of 8 accumulators below 128 elements, recursive
    /// halving above) — keeps error growth logarithmic and reproduces numpy's float rounding.</summary>
    private static T PairwiseAdd<T>(T[] x, int start, int n) where T : unmanaged, INumber<T>
    {
        if (n < 8)
        {
            T res = x[start];
            for (int i = 1; i < n; i++) res += x[start + i];
            return res;
        }
        if (n <= 128)
        {
            T r0 = x[start], r1 = x[start + 1], r2 = x[start + 2], r3 = x[start + 3];
            T r4 = x[start + 4], r5 = x[start + 5], r6 = x[start + 6], r7 = x[start + 7];
            int i = 8;
            for (; i < n - (n % 8); i += 8)
            {
                r0 += x[start + i]; r1 += x[start + i + 1]; r2 += x[start + i + 2]; r3 += x[start + i + 3];
                r4 += x[start + i + 4]; r5 += x[start + i + 5]; r6 += x[start + i + 6]; r7 += x[start + i + 7];
            }
            T res = ((r0 + r1) + (r2 + r3)) + ((r4 + r5) + (r6 + r7));
            for (; i < n; i++) res += x[start + i];
            return res;
        }
        int n2 = n / 2;
        n2 -= n2 % 8;
        return PairwiseAdd(x, start, n2) + PairwiseAdd(x, start + n2, n - n2);
    }

    // ------------------------------------------------------------------ argmin / argmax

    public static NDArray ArgBest(NDArray a, int? axis, bool keepdims, bool max)
    {
        if (a.DType == DType.Bool) a = a.CastTo(DType.UInt8);
        NDArray src = a;
        int[] axes;
        if (axis is null) { src = np.Ravel(a); axes = new[] { 0 }; }
        else axes = new[] { np.NormalizeAxis(axis.Value, a.Ndim) };
        int ax = axes[0];
        if (src.Shape[ax] == 0)
            throw new NDValueException($"attempt to get {(max ? "argmax" : "argmin")} of an empty sequence");
        var outShape = axis is null ? (keepdims ? Enumerable.Repeat(1, a.Ndim).ToArray() : Array.Empty<int>()) : OutShape(src.Shape, axes, keepdims);
        var result = new NDArray(DType.Int64, new long[NDArray.SizeOf(outShape)], outShape);
        switch (src.DType)
        {
            case DType.Int8: ArgT<sbyte>(src, ax, max, (long[])result.Buffer); break;
            case DType.UInt8: ArgT<byte>(src, ax, max, (long[])result.Buffer); break;
            case DType.Int16: ArgT<short>(src, ax, max, (long[])result.Buffer); break;
            case DType.UInt16: ArgT<ushort>(src, ax, max, (long[])result.Buffer); break;
            case DType.Int32: ArgT<int>(src, ax, max, (long[])result.Buffer); break;
            case DType.UInt32: ArgT<uint>(src, ax, max, (long[])result.Buffer); break;
            case DType.Int64: ArgT<long>(src, ax, max, (long[])result.Buffer); break;
            case DType.UInt64: ArgT<ulong>(src, ax, max, (long[])result.Buffer); break;
            case DType.Float16: ArgT<Half>(src, ax, max, (long[])result.Buffer); break;
            case DType.Float32: ArgT<float>(src, ax, max, (long[])result.Buffer); break;
            default: ArgT<double>(src, ax, max, (long[])result.Buffer); break;
        }
        return result;
    }

    private static void ArgT<T>(NDArray a, int axis, bool max, long[] o) where T : unmanaged, INumber<T>
    {
        var x = (T[])a.Buffer;
        var keptDims = Enumerable.Range(0, a.Ndim).Where(i => i != axis).ToArray();
        var kw = new RowWalker(keptDims.Select(i => a.Shape[i]).ToArray(), keptDims.Select(i => a.Strides[i]).ToArray());
        int n = a.Shape[axis], st = a.Strides[axis];
        int k = 0;
        do
        {
            for (int j = 0; j < kw.InnerLen; j++)
            {
                int bp = a.Offset + kw.Off0 + j * kw.InnerStride0;
                T best = x[bp];
                int bi = 0;
                if (!T.IsNaN(best))
                    for (int i = 1; i < n; i++)
                    {
                        T v = x[bp + i * st];
                        if (T.IsNaN(v)) { bi = i; break; }
                        if (max ? v > best : v < best) { best = v; bi = i; }
                    }
                o[k++] = bi;
            }
        } while (kw.Next());
    }

    // ------------------------------------------------------------------ cumulative

    public static NDArray Cumulate<TK>(NDArray a, int? axis, DType? dtype) where TK : struct, INumBinary
    {
        var rt = dtype ?? DTypes.SumResult(a.DType);
        NDArray src = a.CastTo(rt);
        int ax;
        if (axis is null) { src = np.Ravel(src); ax = 0; }
        else ax = np.NormalizeAxis(axis.Value, a.Ndim);
        var result = new NDArray(rt, Array.CreateInstance(rt.ClrType(), src.Size), (int[])src.Shape.Clone());
        if (src.Size == 0) return result;
        switch (rt)
        {
            case DType.Int8: CumT<sbyte, TK>(src, ax, result); break;
            case DType.UInt8: CumT<byte, TK>(src, ax, result); break;
            case DType.Int16: CumT<short, TK>(src, ax, result); break;
            case DType.UInt16: CumT<ushort, TK>(src, ax, result); break;
            case DType.Int32: CumT<int, TK>(src, ax, result); break;
            case DType.UInt32: CumT<uint, TK>(src, ax, result); break;
            case DType.Int64: CumT<long, TK>(src, ax, result); break;
            case DType.UInt64: CumT<ulong, TK>(src, ax, result); break;
            case DType.Float16: CumT<Half, TK>(src, ax, result); break;
            case DType.Float32: CumT<float, TK>(src, ax, result); break;
            case DType.Float64: CumT<double, TK>(src, ax, result); break;
            default: throw new NDNotSupportedException($"cumulative op on {rt}");
        }
        return result;
    }

    private static void CumT<T, TK>(NDArray a, int axis, NDArray r) where T : unmanaged, INumber<T> where TK : struct, INumBinary
    {
        var x = (T[])a.Buffer;
        var o = (T[])r.Buffer;
        var keptDims = Enumerable.Range(0, a.Ndim).Where(i => i != axis).ToArray();
        var ks = keptDims.Select(i => a.Shape[i]).ToArray();
        var kw = new RowWalker(ks, keptDims.Select(i => a.Strides[i]).ToArray(), keptDims.Select(i => r.Strides[i]).ToArray());
        int n = a.Shape[axis], sa = a.Strides[axis], so = r.Strides[axis];
        do
        {
            for (int j = 0; j < kw.InnerLen; j++)
            {
                int pa = a.Offset + kw.Off0 + j * kw.InnerStride0;
                int po = kw.Off1 + j * kw.InnerStride1;
                T acc = x[pa];
                o[po] = acc;
                for (int i = 1; i < n; i++)
                {
                    acc = TK.Apply(acc, x[pa + i * sa]);
                    o[po + i * so] = acc;
                }
            }
        } while (kw.Next());
    }
}

public static partial class np
{
    // ================================================================ sum / prod / min / max

    private static DType ReductionType(NDArray a, DType? dtype) => dtype ?? DTypes.SumResult(a.DType);

    public static NDArray Sum(NDArray a, int[]? axis = null, bool keepdims = false, DType? dtype = null)
        => a.DType.IsComplex()
            ? Cx.FromParts(Sum(Real(a), axis, keepdims), Sum(Imag(a), axis, keepdims), a.DType)
            : Red.Fold<AddK>(a.CastTo(ReductionType(a, dtype)), Red.NormalizeAxes(axis, a.Ndim), keepdims, 0L, "add");

    public static NDArray Prod(NDArray a, int[]? axis = null, bool keepdims = false, DType? dtype = null)
        => Red.Fold<MulK>(a.CastTo(ReductionType(a, dtype)), Red.NormalizeAxes(axis, a.Ndim), keepdims, 1L, "multiply");

    public static NDArray Max(NDArray a, int[]? axis = null, bool keepdims = false)
        => Red.Fold<MaxK>(a, Red.NormalizeAxes(axis, a.Ndim), keepdims, null, "maximum");

    public static NDArray Min(NDArray a, int[]? axis = null, bool keepdims = false)
        => Red.Fold<MinK>(a, Red.NormalizeAxes(axis, a.Ndim), keepdims, null, "minimum");

    /// <summary>Peak-to-peak (max − min).</summary>
    public static NDArray Ptp(NDArray a, int[]? axis = null, bool keepdims = false)
        => Subtract(Max(a, axis, keepdims), Min(a, axis, keepdims));

    public static NDArray Any(NDArray a, int[]? axis = null, bool keepdims = false)
        => Red.Fold<MaxK>(a.CastTo(DType.Bool).CastTo(DType.UInt8), Red.NormalizeAxes(axis, a.Ndim), keepdims, 0L, "logical_or").CastTo(DType.Bool);

    public static NDArray All(NDArray a, int[]? axis = null, bool keepdims = false)
        => Red.Fold<MinK>(a.CastTo(DType.Bool).CastTo(DType.UInt8), Red.NormalizeAxes(axis, a.Ndim), keepdims, 1L, "logical_and").CastTo(DType.Bool);

    public static NDArray ArgMax(NDArray a, int? axis = null, bool keepdims = false) => Red.ArgBest(a, axis, keepdims, true);
    public static NDArray ArgMin(NDArray a, int? axis = null, bool keepdims = false) => Red.ArgBest(a, axis, keepdims, false);

    public static NDArray CumSum(NDArray a, int? axis = null, DType? dtype = null) => Red.Cumulate<AddK>(a, axis, dtype);
    public static NDArray CumProd(NDArray a, int? axis = null, DType? dtype = null) => Red.Cumulate<MulK>(a, axis, dtype);

    // ================================================================ mean / var / std

    private static NDArray CountOf(NDArray a, int[] axes, DType dt)
    {
        long n = 1;
        foreach (var ax in axes) n *= a.Shape[ax];
        return NDArray.Scalar((double)n).CastTo(dt);
    }

    public static NDArray Mean(NDArray a, int[]? axis = null, bool keepdims = false, DType? dtype = null)
    {
        var axes = Red.NormalizeAxes(axis, a.Ndim);
        if (a.DType.IsComplex())
            return Cx.FromParts(Mean(Real(a), axis, keepdims), Mean(Imag(a), axis, keepdims), a.DType);
        var dt = dtype ?? DTypes.MeanResult(a.DType);
        var s = Red.Fold<AddK>(a.CastTo(dt), axes, keepdims, 0L, "add");
        return Ew.Float<DivF>(s, CountOf(a, axes, dt), dt);
    }

    /// <summary>numpy <c>var</c> computed exactly as numpy does: mean, centered, squared, summed, divided by n − ddof.</summary>
    public static NDArray Var(NDArray a, int[]? axis = null, bool keepdims = false, int ddof = 0)
    {
        var axes = Red.NormalizeAxes(axis, a.Ndim);
        var dt = DTypes.MeanResult(a.DType);
        var af = a.CastTo(dt);
        var n = CountOf(a, axes, dt);
        var mean = Ew.Float<DivF>(Red.Fold<AddK>(af, axes, true, 0L, "add"), n, dt);
        var x = Subtract(af, mean);
        x = Multiply(x, x);
        var s = Red.Fold<AddK>(x, axes, keepdims, 0L, "add");
        double denom = Math.Max(0, (double)(a.Size == 0 ? 0 : CountOfLong(a, axes)) - ddof);
        return Ew.Float<DivF>(s, NDArray.Scalar(denom).CastTo(dt), dt);
    }

    private static long CountOfLong(NDArray a, int[] axes)
    {
        long n = 1;
        foreach (var ax in axes) n *= a.Shape[ax];
        return n;
    }

    public static NDArray Std(NDArray a, int[]? axis = null, bool keepdims = false, int ddof = 0)
        => Sqrt(Var(a, axis, keepdims, ddof));

    // ================================================================ counting / searching

    public static NDArray CountNonzero(NDArray a, int[]? axis = null, bool keepdims = false)
        => Sum(NotEqual(a, NDArray.WeakScalar(0L)), axis, keepdims);

    /// <summary>numpy <c>nonzero</c>: one int64 index array per dimension, C-order.</summary>
    public static NDArray[] NonZero(NDArray a)
    {
        var b = a.CastTo(DType.Bool);
        if (b.Ndim == 0) b = Reshape(b, 1);
        var mask = (bool[])Cast.ToBuffer(b, DType.Bool);
        int count = 0;
        foreach (var v in mask) if (v) count++;
        var outs = Enumerable.Range(0, b.Ndim).Select(_ => new long[count]).ToArray();
        var idx = new int[b.Ndim];
        int k = 0;
        for (int flat = 0; flat < mask.Length; flat++)
        {
            if (mask[flat])
            {
                for (int d = 0; d < b.Ndim; d++) outs[d][k] = idx[d];
                k++;
            }
            for (int d = b.Ndim - 1; d >= 0; d--)
            {
                if (++idx[d] < b.Shape[d]) break;
                idx[d] = 0;
            }
        }
        return outs.Select(o => NDArray.FromArray(o, count)).ToArray();
    }
}
