// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using MathemaSharp.Kernels;
using NDSharp;

namespace PySharpLib.Numpy;

/// <summary>Routes the plain float64 calls of numpy's reductions and scans to the shared lane kernels of <c>MathemaSharp.Kernels</c>.
/// Anything else (other dtypes, <c>dtype=</c>, <c>out=</c>, several axes, empty or non-contiguous arrays, a degenerate <c>ddof</c>) is left to NDSharp, unchanged.
/// NumPy defaults chosen here: NaN propagates everywhere; <c>var</c>/<c>std</c> divide by n − ddof with ddof = 0; sums use numpy's pairwise algorithm and variance its two-pass one,
/// so the results are bit-identical to NDSharp's.</summary>
internal static class NumpyReductions
{
    /// <summary>Turns the kernel path off (tests compare both paths bit by bit).</summary>
    internal static bool Enabled { get; set; } = true;

    static long kernelCalls;
    /// <summary>Calls answered by the kernels since the process started.</summary>
    internal static long KernelCalls => Interlocked.Read(ref kernelCalls);

    delegate void Kernel(double[] source, int start, Lane lane, double[] result);

    internal static bool TrySum(Args p, out object result) => Reduce(p, 1, 2, 3, 4, out result, (s, o, l, r) => Reductions.Sum<double>(s.AsSpan(o), l, r, NanPolicy.Propagate, SumMethod.Pairwise));
    internal static bool TryProd(Args p, out object result) => Reduce(p, 1, 2, 3, 4, out result, (s, o, l, r) => Reductions.Product<double>(s.AsSpan(o), l, r));
    internal static bool TryMax(Args p, out object result) => Reduce(p, 1, -1, 2, 3, out result, (s, o, l, r) => Reductions.Max<double>(s.AsSpan(o), l, r, Span<int>.Empty, NanPolicy.Propagate));
    internal static bool TryMin(Args p, out object result) => Reduce(p, 1, -1, 2, 3, out result, (s, o, l, r) => Reductions.Min<double>(s.AsSpan(o), l, r, Span<int>.Empty, NanPolicy.Propagate));
    internal static bool TryMean(Args p, out object result) => Reduce(p, 1, 2, 3, 4, out result, (s, o, l, r) => Reductions.Mean<double>(s.AsSpan(o), l, r, NanPolicy.Propagate, SumMethod.Pairwise));
    internal static bool TryVar(Args p, out object result) => Spread(p, sqrt: false, out result);
    internal static bool TryStd(Args p, out object result) => Spread(p, sqrt: true, out result);

    internal static bool TryCumSum(Args p, out object result) => Cumulative(p, ScanOp.Sum, out result);
    internal static bool TryCumProd(Args p, out object result) => Cumulative(p, ScanOp.Product, out result);

    /// <summary>A non-empty float64 array stored C-contiguously (any offset): the only layout these kernels read.</summary>
    static bool Plain(NDArray a, out double[] buffer, out int offset, out int[] shape)
    {
        buffer = []; offset = 0; shape = [];
        if (!Enabled || a.DType != DType.Float64 || a.Size == 0) return false;
        var view = a.AsArrayView<double>();
        shape = view.Shape.ToArray();
        long expected = 1;
        for (int axis = shape.Length - 1; axis >= 0; axis--)
        {
            if (shape[axis] != 1 && view.Strides[axis] != expected) return false;
            expected *= shape[axis];
        }
        buffer = view.Buffer;
        offset = view.Offset;
        return true;
    }

    /// <summary>The single axis (normalised) a call reduces over, or -1 for <c>None</c>; false when the call names several axes or an invalid one.</summary>
    static bool SingleAxis(int[]? axes, int rank, out int axis)
    {
        axis = -1;
        if (axes is null) return true;
        if (axes.Length != 1) return false;
        axis = axes[0] < 0 ? axes[0] + rank : axes[0];
        return (uint)axis < (uint)rank;
    }

    static NDArray Wrap(double[] values, int[] shape)
    {
        var strides = new int[shape.Length];
        int run = 1;
        for (int i = shape.Length - 1; i >= 0; i--) { strides[i] = run; run *= Math.Max(shape[i], 1); }
        return NDArray.FromArrayView(new ArrayView<double>(values, shape, strides), DType.Float64);
    }

    static int[] ReducedShape(int[] shape, int axis, bool keepdims)
    {
        if (axis < 0) return keepdims ? Enumerable.Repeat(1, shape.Length).ToArray() : [];
        if (keepdims) { var r = (int[])shape.Clone(); r[axis] = 1; return r; }
        return shape.Where((_, i) => i != axis).ToArray();
    }

    static Lane LaneOf(int[] shape, int axis, int size) => axis < 0 ? Lane.Flat(size) : Lane.ForRowMajorAxis(shape, axis);

    static bool Reduce(Args p, int axisIdx, int dtypeIdx, int outIdx, int keepIdx, out object result, Kernel kernel)
    {
        result = null!;
        if (!Enabled || p.Has(outIdx) || (dtypeIdx >= 0 && p.Has(dtypeIdx))) return false;
        var a = p.ND(0);
        if (!Plain(a, out var buffer, out int offset, out var shape) || !SingleAxis(p.Axes(axisIdx), shape.Length, out int axis)) return false;
        var lane = LaneOf(shape, axis, a.Size);
        var values = new double[lane.Count];
        kernel(buffer, offset, lane, values);
        Interlocked.Increment(ref kernelCalls);
        result = Conv.Result(Wrap(values, ReducedShape(shape, axis, p.Bool(keepIdx, false))));
        return true;
    }

    static bool Spread(Args p, bool sqrt, out object result)
    {
        result = null!;
        // std/var(a, axis, dtype, out, ddof, keepdims)
        if (!Enabled || p.Has(2) || p.Has(3)) return false;
        var a = p.ND(0);
        int ddof = p.Int(4, 0);
        if (ddof < 0 || !Plain(a, out var buffer, out int offset, out var shape) || !SingleAxis(p.Axes(1), shape.Length, out int axis)) return false;
        var lane = LaneOf(shape, axis, a.Size);
        if (lane.Length - ddof <= 0) return false;   // numpy warns and returns NaN or inf: left to NDSharp
        var values = new double[lane.Count];
        if (sqrt) Reductions.StandardDeviation<double>(buffer.AsSpan(offset), lane, values, ddof, NanPolicy.Propagate, VarianceMethod.TwoPass);
        else Reductions.Variance<double>(buffer.AsSpan(offset), lane, values, ddof, NanPolicy.Propagate, VarianceMethod.TwoPass);
        Interlocked.Increment(ref kernelCalls);
        result = Conv.Result(Wrap(values, ReducedShape(shape, axis, p.Bool(5, false))));
        return true;
    }

    static bool Cumulative(Args p, ScanOp op, out object result)
    {
        result = null!;
        // cumsum/cumprod(a, axis, dtype, out)
        if (!Enabled || p.Has(2) || p.Has(3)) return false;
        var a = p.ND(0);
        if (!Plain(a, out var buffer, out int offset, out var shape)) return false;
        int? requested = p.Axis(1);
        int axis = -1;
        if (requested is int r)
        {
            axis = r < 0 ? r + shape.Length : r;
            if ((uint)axis >= (uint)shape.Length) return false;
        }
        var lane = LaneOf(shape, axis, a.Size);
        var values = new double[a.Size];
        Scans.Scan<double>(buffer.AsSpan(offset), lane, values, op, ScanDirection.Forward, NanPolicy.Propagate);
        Interlocked.Increment(ref kernelCalls);
        result = Conv.Result(Wrap(values, axis < 0 ? [a.Size] : shape));
        return true;
    }
}
