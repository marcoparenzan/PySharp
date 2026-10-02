// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp;

public static partial class np
{
    public const double pi = Math.PI;
    public const double e = Math.E;
    public const double inf = double.PositiveInfinity;
    public const double nan = double.NaN;

    private static NDArray New(DType dtype, int[] shape)
        => new(dtype, Array.CreateInstance(dtype.ClrType(), NDArray.SizeOf(shape)), (int[])shape.Clone());

    public static NDArray Zeros(int[] shape, DType dtype = DType.Float64) => New(dtype, shape);

    /// <summary>numpy <c>empty</c>: here deterministic zeros (numpy's contents are undefined).</summary>
    public static NDArray Empty(int[] shape, DType dtype = DType.Float64) => New(dtype, shape);

    public static NDArray Ones(int[] shape, DType dtype = DType.Float64) => Full(shape, 1.0, dtype);

    public static NDArray Full(int[] shape, object value, DType? dtype = null)
    {
        DType dt = dtype ?? value switch
        {
            bool => DType.Bool,
            double or float or Half => DType.Float64,
            ulong => DType.UInt64,
            _ => DType.Int64,
        };
        var a = New(dt, shape);
        if (a.Size > 0)
        {
            var one = NDArray.Scalar(value, dt);
            Fill(a, one);
        }
        return a;
    }

    /// <summary>Sets every element of <paramref name="a"/> (honoring views) to the scalar held by <paramref name="scalar"/>.</summary>
    internal static void Fill(NDArray a, NDArray scalar)
        => Assign.Copy(a, scalar, Casting.Unsafe);

    public static NDArray ZerosLike(NDArray a, DType? dtype = null) => Zeros(a.Shape, dtype ?? a.DType);
    public static NDArray OnesLike(NDArray a, DType? dtype = null) => Ones(a.Shape, dtype ?? a.DType);
    public static NDArray EmptyLike(NDArray a, DType? dtype = null) => Empty(a.Shape, dtype ?? a.DType);
    public static NDArray FullLike(NDArray a, object value, DType? dtype = null) => Full(a.Shape, value, dtype ?? a.DType);

    public static NDArray Eye(int n, int? m = null, int k = 0, DType dtype = DType.Float64)
    {
        int cols = m ?? n;
        var a = New(dtype, new[] { n, cols });
        var one = NDArray.Scalar(1L, dtype);
        for (int i = 0; i < n; i++)
        {
            int j = i + k;
            if (j >= 0 && j < cols)
                Assign.Copy(a.ViewWith(Array.Empty<int>(), Array.Empty<int>(), a.Offset + i * cols + j), one, Casting.Unsafe);
        }
        return a;
    }

    public static NDArray Identity(int n, DType dtype = DType.Float64) => Eye(n, null, 0, dtype);

    /// <summary>numpy <c>arange(start, stop, step)</c>. The dtype is int64 when every argument is an
    /// integer, float64 otherwise, unless given.</summary>
    public static NDArray Arange(double start, double stop, double step, DType? dtype = null, bool allInteger = false)
    {
        if (step == 0) throw new NDValueException("Maximum allowed size exceeded");
        long count = (long)Math.Ceiling((stop - start) / step);
        if (count < 0) count = 0;
        DType dt = dtype ?? (allInteger ? DType.Int64 : DType.Float64);
        var a = New(dt, new[] { (int)count });
        if (dt.IsFloat() && dt != DType.Float64 || !dt.IsFloat())
        {
            // Compute in float64 then convert — exact for the integer ranges arange is used with.
            var tmp = new double[count];
            for (long i = 0; i < count; i++) tmp[i] = start + i * step;
            return NDArray.FromArray(tmp, (int)count).AsType(dt);
        }
        var buf = (double[])a.Buffer;
        for (long i = 0; i < count; i++) buf[i] = start + i * step;
        return a;
    }

    public static NDArray Arange(long start, long stop, long step = 1, DType? dtype = null)
    {
        if (step == 0) throw new NDValueException("Maximum allowed size exceeded");
        return Arange((double)start, (double)stop, (double)step, dtype ?? DType.Int64, true);
    }

    public static NDArray Linspace(double start, double stop, int num = 50, bool endpoint = true, DType? dtype = null)
    {
        if (num < 0) throw new NDValueException($"Number of samples, {num}, must be non-negative.");
        var buf = new double[num];
        int div = endpoint ? num - 1 : num;
        if (num == 1) buf[0] = start;
        else if (div > 0)
        {
            double step = (stop - start) / div;
            for (int i = 0; i < num; i++) buf[i] = start + i * step;
            if (endpoint) buf[num - 1] = stop;
        }
        var r = NDArray.FromArray(buf, num);
        return dtype is DType dt && dt != DType.Float64 ? r.AsType(dt) : r;
    }

    /// <summary>numpy <c>meshgrid</c> (default <c>indexing='xy'</c>, or <c>'ij'</c>).</summary>
    public static NDArray[] Meshgrid(NDArray[] xi, bool indexingXY = true)
    {
        int n = xi.Length;
        var outShape = new int[n];
        for (int i = 0; i < n; i++) outShape[i] = xi[i].Size;
        if (indexingXY && n >= 2) (outShape[0], outShape[1]) = (outShape[1], outShape[0]);
        var result = new NDArray[n];
        for (int i = 0; i < n; i++)
        {
            int axis = i;
            if (indexingXY && n >= 2 && i < 2) axis = 1 - i;
            var shape = Enumerable.Repeat(1, n).ToArray();
            shape[axis] = xi[i].Size;
            var v = Reshape(xi[i], shape);
            result[i] = Broadcasting.BroadcastTo(v, outShape).Copy();
        }
        return result;
    }

    /// <summary>numpy <c>diag</c>: extract a diagonal from a 2-D array, or build a 2-D array from a 1-D one.</summary>
    public static NDArray Diag(NDArray v, int k = 0)
    {
        if (v.Ndim == 1)
        {
            int n = v.Size + Math.Abs(k);
            var a = New(v.DType, new[] { n, n });
            for (int i = 0; i < v.Size; i++)
            {
                int r = k >= 0 ? i : i - k, c = k >= 0 ? i + k : i;
                Assign.Copy(a.ViewWith(Array.Empty<int>(), Array.Empty<int>(), a.Offset + r * n + c),
                    v.ViewWith(Array.Empty<int>(), Array.Empty<int>(), v.Offset + i * v.Strides[0]), Casting.Unsafe);
            }
            return a;
        }
        if (v.Ndim == 2) return Diagonal(v, k);
        throw new NDValueException("Input must be 1- or 2-d.");
    }

    /// <summary>numpy <c>diagonal</c> of a 2-D array (a view, as in numpy; read-only in spirit).</summary>
    public static NDArray Diagonal(NDArray a, int offset = 0)
    {
        if (a.Ndim != 2) throw new NDValueException("diag requires an array of at least two dimensions");
        int rows = a.Shape[0], cols = a.Shape[1];
        int r0 = offset >= 0 ? 0 : -offset, c0 = offset >= 0 ? offset : 0;
        int len = Math.Max(0, Math.Min(rows - r0, cols - c0));
        return a.ViewWith(new[] { len }, new[] { a.Strides[0] + a.Strides[1] }, a.Offset + r0 * a.Strides[0] + c0 * a.Strides[1]);
    }

    public static void FillDiagonal(NDArray a, NDArray value)
    {
        if (a.Ndim < 2) throw new NDValueException("array must be at least 2-d");
        int n = a.Shape.Min();
        int step = a.Strides.Sum();
        var diag = a.ViewWith(new[] { n }, new[] { step }, a.Offset);
        Assign.Copy(diag, value, Casting.Unsafe);
    }
}
