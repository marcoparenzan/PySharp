// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp;

public static partial class np
{
    // ================================================================ sorting

    /// <summary>Orders NaN after every number (numpy's sort order); stable for equal values.</summary>
    private readonly struct NanLastComparer<T> : IComparer<T> where T : unmanaged, INumber<T>
    {
        public int Compare(T x, T y)
        {
            bool nx = T.IsNaN(x), ny = T.IsNaN(y);
            if (nx || ny) return nx == ny ? 0 : nx ? 1 : -1;
            return x < y ? -1 : x > y ? 1 : 0;
        }
    }

    private static NDArray Moved(NDArray a, int axis) => MoveAxis(a, axis, -1);

    /// <summary>numpy <c>sort</c> along <paramref name="axis"/> (default last); <c>null</c> sorts the flattened array.</summary>
    public static NDArray Sort(NDArray a, int? axis = -1)
    {
        if (axis is null) return Sort(Flatten(a), 0);
        if (a.Ndim == 0) throw new NDAxisException(axis.Value, 0);
        int ax = NormalizeAxis(axis.Value, a.Ndim);
        if (a.DType == DType.Bool) return Sort(a.AsType(DType.UInt8), ax).AsType(DType.Bool);
        var data = Moved(a, ax).Copy(); // contiguous, sorting axis last
        int n = data.Shape[^1];
        if (n > 1)
        {
            // Dispatch on dtype, never on the CLR array type (byte[] is also an sbyte[]).
            switch (data.DType)
            {
                case DType.Int8: SortLines((sbyte[])data.Buffer, n); break;
                case DType.UInt8: SortLines((byte[])data.Buffer, n); break;
                case DType.Int16: SortLines((short[])data.Buffer, n); break;
                case DType.UInt16: SortLines((ushort[])data.Buffer, n); break;
                case DType.Int32: SortLines((int[])data.Buffer, n); break;
                case DType.UInt32: SortLines((uint[])data.Buffer, n); break;
                case DType.Int64: SortLines((long[])data.Buffer, n); break;
                case DType.UInt64: SortLines((ulong[])data.Buffer, n); break;
                case DType.Float16: SortLines((Half[])data.Buffer, n); break;
                case DType.Float32: SortLines((float[])data.Buffer, n); break;
                default: SortLines((double[])data.Buffer, n); break;
            }
        }
        return MoveAxis(data, -1, ax).Copy();
    }

    private static void SortLines<T>(T[] buf, int n) where T : unmanaged, INumber<T>
    {
        var cmp = new NanLastComparer<T>();
        for (int s = 0; s < buf.Length; s += n) Array.Sort(buf, s, n, cmp);
    }

    /// <summary>numpy <c>argsort</c> (stable: equal values keep their index order; NaN last).</summary>
    public static NDArray ArgSort(NDArray a, int? axis = -1)
    {
        if (axis is null) return ArgSort(Flatten(a), 0);
        if (a.Ndim == 0) throw new NDAxisException(axis.Value, 0);
        int ax = NormalizeAxis(axis.Value, a.Ndim);
        var moved = Moved(a.DType == DType.Bool ? a.AsType(DType.UInt8) : a, ax);
        var data = moved.Copy();
        int n = data.Shape[^1];
        var result = new long[data.Size];
        switch (data.DType)
        {
            case DType.Int8: ArgSortLines((sbyte[])data.Buffer, n, result); break;
            case DType.UInt8: ArgSortLines((byte[])data.Buffer, n, result); break;
            case DType.Int16: ArgSortLines((short[])data.Buffer, n, result); break;
            case DType.UInt16: ArgSortLines((ushort[])data.Buffer, n, result); break;
            case DType.Int32: ArgSortLines((int[])data.Buffer, n, result); break;
            case DType.UInt32: ArgSortLines((uint[])data.Buffer, n, result); break;
            case DType.Int64: ArgSortLines((long[])data.Buffer, n, result); break;
            case DType.UInt64: ArgSortLines((ulong[])data.Buffer, n, result); break;
            case DType.Float16: ArgSortLines((Half[])data.Buffer, n, result); break;
            case DType.Float32: ArgSortLines((float[])data.Buffer, n, result); break;
            default: ArgSortLines((double[])data.Buffer, n, result); break;
        }
        return MoveAxis(new NDArray(DType.Int64, result, (int[])data.Shape.Clone()), -1, ax).Copy();
    }

    private static void ArgSortLines<T>(T[] buf, int n, long[] result) where T : unmanaged, INumber<T>
    {
        if (n == 0) return;
        var cmp = new NanLastComparer<T>();
        for (int s = 0; s < buf.Length; s += n)
        {
            int start = s;
            var order = Enumerable.Range(0, n).OrderBy(i => buf[start + i], cmp).ToArray();
            for (int i = 0; i < n; i++) result[s + i] = order[i];
        }
    }

    // ================================================================ sets & counting

    public sealed record UniqueResult(NDArray Values, NDArray? Index, NDArray? Inverse, NDArray? Counts);

    /// <summary>numpy <c>unique</c> of the flattened array (sorted).</summary>
    public static UniqueResult Unique(NDArray a, bool returnIndex = false, bool returnInverse = false, bool returnCounts = false)
    {
        var flat = Flatten(a);
        int n = flat.Size;
        var order = ArgSort(flat, 0).ToArray<long>();
        var sortedVals = flat.Get(NDArray.FromArray(order)).Copy();
        var keep = new List<int>();
        var counts = new List<long>();
        var inverse = new long[n];
        var eq = n > 1 ? np.Equal(sortedVals.Get(new Slice(1, null)), sortedVals.Get(new Slice(null, -1))).ToArray<bool>() : Array.Empty<bool>();
        // NaNs: numpy >= 1.21 collapses all NaNs into one entry.
        var isNan = sortedVals.DType.IsFloat() ? np.IsNan(sortedVals).ToArray<bool>() : new bool[n];
        int u = -1;
        for (int i = 0; i < n; i++)
        {
            bool newGroup = i == 0 || !(eq[i - 1] || (isNan[i] && isNan[i - 1]));
            if (newGroup)
            {
                keep.Add(i);
                counts.Add(0);
                u++;
            }
            counts[u]++;
            inverse[order[i]] = u;
        }
        var values = sortedVals.Get(NDArray.FromArray(keep.Select(i => (long)i).ToArray())).Copy();
        if (keep.Count == 0) values = np.Zeros(new[] { 0 }, a.DType);
        return new UniqueResult(
            values,
            returnIndex ? NDArray.FromArray(keep.Select(i => order[i]).ToArray()) : null,
            returnInverse ? NDArray.FromArray(inverse, a.Shape.Length == 0 ? new[] { 1 } : new[] { n }) : null,
            returnCounts ? NDArray.FromArray(counts.ToArray()) : null);
    }

    /// <summary>numpy <c>bincount</c>.</summary>
    public static NDArray BinCount(NDArray x, NDArray? weights = null, int minlength = 0)
    {
        if (x.Ndim != 1) throw new NDValueException("object too deep for desired array");
        if (!x.DType.IsInteger() && x.DType != DType.Bool)
            throw new NDTypeException("Cannot cast array data from dtype('float64') to dtype('int64') according to the rule 'safe'");
        var vals = x.ToArray<long>();
        if (vals.Any(v => v < 0)) throw new NDValueException("'list' argument must have no negative elements");
        int len = Math.Max(vals.Length == 0 ? 0 : (int)vals.Max() + 1, minlength);
        if (weights is null)
        {
            var counts = new long[len];
            foreach (var v in vals) counts[v]++;
            return NDArray.FromArray(counts, len);
        }
        var w = weights.ToArray<double>();
        var sums = new double[len];
        for (int i = 0; i < vals.Length; i++) sums[vals[i]] += w[i];
        return NDArray.FromArray(sums, len);
    }

    /// <summary>numpy <c>unravel_index</c> (C order); outputs have the shape of <paramref name="indices"/>.</summary>
    public static NDArray[] UnravelIndex(NDArray indices, int[] shape)
    {
        var idx = indices.ToArray<long>();
        long total = 1;
        foreach (var d in shape) total *= d;
        var outs = shape.Select(_ => new long[idx.Length]).ToArray();
        for (int i = 0; i < idx.Length; i++)
        {
            long v = idx[i];
            if (v < 0 || v >= total) throw new NDValueException("index " + v + " is out of bounds for array with size " + total);
            for (int d = shape.Length - 1; d >= 0; d--)
            {
                outs[d][i] = v % shape[d];
                v /= shape[d];
            }
        }
        return outs.Select(o => new NDArray(DType.Int64, o, (int[])indices.Shape.Clone())).ToArray();
    }

    // ================================================================ order statistics

    /// <summary>numpy <c>median</c>; NaN if the slice contains a NaN.</summary>
    public static NDArray Median(NDArray a, int[]? axis = null, bool keepdims = false)
    {
        var axes = Red.NormalizeAxes(axis, a.Ndim);
        var (flat, lead) = CollapseAxes(a, axes);
        int n = flat.Shape[^1];
        if (n == 0) return Full(Red.OutShape(a.Shape, axes, keepdims), double.NaN, DTypes.MeanResult(a.DType));
        var s = Sort(flat, -1);
        NDArray result;
        if (n % 2 == 1)
            result = s.Get(NDIndex.Ellipsis, n / 2);
        else
        {
            var lo = s.Get(NDIndex.Ellipsis, n / 2 - 1);
            var hi = s.Get(NDIndex.Ellipsis, n / 2);
            result = Mean(Stack(new[] { lo, hi }, -1), new[] { -1 });
        }
        result = result.DType.IsFloat() ? result.Copy() : result.AsType(DType.Float64);
        if (a.DType.IsFloat())
        {
            var hasNan = Any(IsNan(flat), new[] { -1 });
            result = Where(hasNan, NDArray.WeakScalar(double.NaN), result);
        }
        return Reshape(result, Red.OutShape(a.Shape, axes, keepdims));
    }

    /// <summary>Moves the reduced axes last and merges them into one: returns (array[..., n], leading shape).</summary>
    private static (NDArray, int[]) CollapseAxes(NDArray a, int[] axes)
    {
        var kept = Enumerable.Range(0, a.Ndim).Where(i => !axes.Contains(i)).ToArray();
        var order = kept.Concat(axes).ToArray();
        var t = Transpose(a, order).Copy();
        var lead = kept.Select(i => a.Shape[i]).ToArray();
        int n = 1;
        foreach (var ax in axes) n *= a.Shape[ax];
        return (Reshape(t, lead.Concat(new[] { n }).ToArray()), lead);
    }

    /// <summary>numpy <c>percentile</c>/<c>quantile</c> (method 'linear'); <paramref name="q"/> in [0, 100] (or [0, 1] when <paramref name="isQuantile"/>).</summary>
    public static NDArray Percentile(NDArray a, NDArray q, int[]? axis = null, bool keepdims = false, bool isQuantile = false)
    {
        var axes = Red.NormalizeAxes(axis, a.Ndim);
        var qs = q.AsType(DType.Float64);
        var qv = qs.ToArray<double>().Select(v => isQuantile ? v : v / 100.0).ToArray();
        if (qv.Any(v => v < 0 || v > 1)) throw new NDValueException(isQuantile ? "Quantiles must be in the range [0, 1]" : "Percentiles must be in the range [0, 100]");
        var (flat, lead) = CollapseAxes(a.DType.IsFloat() ? a : a.AsType(DType.Float64), axes);
        int n = flat.Shape[^1];
        var s = Sort(flat, -1);
        var parts = new List<NDArray>();
        foreach (var quantile in qv)
        {
            double vi = (n - 1) * quantile;
            long prev = (long)Math.Floor(vi);
            long next = Math.Min(prev + 1, n - 1);
            double gamma = vi - prev;
            var lo = s.Get(NDIndex.Ellipsis, (int)prev);
            var hi = s.Get(NDIndex.Ellipsis, (int)next);
            var diff = Subtract(hi, lo);
            var t = NDArray.WeakScalar(gamma);
            var lerp = Add(lo, Multiply(diff, t));
            if (gamma >= 0.5)
                lerp = Subtract(hi, Multiply(diff, NDArray.WeakScalar(1 - gamma)));
            if (a.DType.IsFloat())
                lerp = Where(Any(IsNan(flat), new[] { -1 }), NDArray.WeakScalar(double.NaN), lerp);
            parts.Add(lerp);
        }
        var stacked = Stack(parts, 0);                                   // (nq, *lead)
        var outShape = Red.OutShape(a.Shape, axes, keepdims);
        if (q.Ndim == 0) return Reshape(stacked, outShape);
        return Reshape(stacked, q.Shape.Concat(outShape).ToArray());
    }

    // ================================================================ differences

    /// <summary>numpy <c>diff</c> along <paramref name="axis"/>, <paramref name="n"/> times.</summary>
    public static NDArray Diff(NDArray a, int n = 1, int axis = -1)
    {
        if (a.Ndim == 0) throw new NDValueException("diff requires input that is at least one dimensional");
        int ax = NormalizeAxis(axis, a.Ndim);
        var r = a;
        for (int k = 0; k < n; k++)
        {
            var idxHi = Enumerable.Repeat<NDIndex>(Slice.All, a.Ndim).ToArray();
            var idxLo = Enumerable.Repeat<NDIndex>(Slice.All, a.Ndim).ToArray();
            idxHi[ax] = new Slice(1, null);
            idxLo[ax] = new Slice(null, -1);
            var hi = r.Get(idxHi);
            var lo = r.Get(idxLo);
            r = r.DType == DType.Bool ? NotEqual(hi, lo) : Subtract(hi, lo);
        }
        return r;
    }

    /// <summary>numpy <c>gradient</c> with uniform spacing (scalar per axis); central differences inside, one-sided at the edges.</summary>
    public static NDArray[] Gradient(NDArray f, double[]? spacing = null, int[]? axes = null)
    {
        var ax = axes is null ? Enumerable.Range(0, f.Ndim).ToArray() : axes.Select(x => NormalizeAxis(x, f.Ndim)).ToArray();
        var ft = f.DType.IsFloat() ? f : f.AsType(DType.Float64);
        var result = new List<NDArray>();
        for (int k = 0; k < ax.Length; k++)
        {
            double dx = spacing is null ? 1.0 : spacing.Length == 1 ? spacing[0] : spacing[k];
            int a0 = ax[k];
            int n = f.Shape[a0];
            if (n < 2) throw new NDValueException("Shape of array too small to calculate a numerical gradient, at least (edge_order + 1) elements are required.");
            NDArray Take(Slice s)
            {
                var idx = Enumerable.Repeat<NDIndex>(Slice.All, f.Ndim).ToArray();
                idx[a0] = s;
                return ft.Get(idx);
            }
            var outArr = np.Zeros(f.Shape, ft.DType);
            void Put(NDArray v, Slice s)
            {
                var idx = Enumerable.Repeat<NDIndex>(Slice.All, f.Ndim).ToArray();
                idx[a0] = s;
                outArr.Put(v, idx);
            }
            var dxs = NDArray.WeakScalar(dx);
            if (n > 2)
                Put(Divide(Subtract(Take(new Slice(2, null)), Take(new Slice(null, -2))), NDArray.WeakScalar(2.0 * dx)), new Slice(1, -1));
            Put(Divide(Subtract(Take(new Slice(1, 2)), Take(new Slice(0, 1))), dxs), new Slice(0, 1));
            Put(Divide(Subtract(Take(new Slice(-1, null)), Take(new Slice(-2, -1))), dxs), new Slice(-1, null));
            result.Add(outArr);
        }
        return result.ToArray();
    }

    /// <summary>numpy <c>gradient</c> with a coordinate array per axis (non-uniform spacing uses numpy's
    /// second-order interior formula; evenly spaced coordinates collapse to the uniform case).</summary>
    public static NDArray[] GradientCoordinates(NDArray f, IReadOnlyList<NDArray> coordinates, int[]? axes = null)
    {
        var ax = axes is null ? Enumerable.Range(0, f.Ndim).ToArray() : axes.Select(x => NormalizeAxis(x, f.Ndim)).ToArray();
        if (coordinates.Count != ax.Length)
            throw new NDValueException("spacing must either be a single scalar, or a scalar / 1d-array per axis");
        var ft = f.DType.IsFloat() ? f : f.AsType(DType.Float64);
        var result = new List<NDArray>();
        for (int k = 0; k < ax.Length; k++)
        {
            var x = coordinates[k];
            int a0 = ax[k], n = f.Shape[a0];
            if (x.Ndim != 1 || x.Size != n)
                throw new NDValueException("when 1d, distances must match the length of the corresponding dimension");
            var dxs = Diff(x.DType.IsFloat() ? x : x.AsType(DType.Float64));
            bool uniform = (bool)All(Equal(dxs, dxs.Get(0))).GetAt(0);
            if (uniform)
            {
                result.Add(Gradient(f, new[] { Convert.ToDouble(dxs.Get(0).GetAt(0)) }, new[] { a0 })[0]);
                continue;
            }
            int[] Shape1(int len) { var s = Enumerable.Repeat(1, f.Ndim).ToArray(); s[a0] = len; return s; }
            NDArray Take(Slice s) { var idx = Enumerable.Repeat<NDIndex>(Slice.All, f.Ndim).ToArray(); idx[a0] = s; return ft.Get(idx); }
            var dx1 = Reshape(dxs.Get(new Slice(0, n - 2)), Shape1(n - 2));
            var dx2 = Reshape(dxs.Get(new Slice(1, null)), Shape1(n - 2));
            var a = Negative(Divide(dx2, Multiply(dx1, Add(dx1, dx2))));
            var b = Divide(Subtract(dx2, dx1), Multiply(dx1, dx2));
            var c = Divide(dx1, Multiply(dx2, Add(dx1, dx2)));
            var outArr = np.Zeros(f.Shape, ft.DType);
            void Put(NDArray v, Slice s) { var idx = Enumerable.Repeat<NDIndex>(Slice.All, f.Ndim).ToArray(); idx[a0] = s; outArr.Put(v, idx); }
            Put(Add(Add(Multiply(a, Take(new Slice(null, -2))), Multiply(b, Take(new Slice(1, -1)))), Multiply(c, Take(new Slice(2, null)))), new Slice(1, -1));
            Put(Divide(Subtract(Take(new Slice(1, 2)), Take(new Slice(0, 1))), dxs.Get(new Slice(0, 1))), new Slice(0, 1));
            Put(Divide(Subtract(Take(new Slice(-1, null)), Take(new Slice(-2, -1))), dxs.Get(new Slice(-1, null))), new Slice(-1, null));
            result.Add(outArr);
        }
        return result.ToArray();
    }

    // ================================================================ vector algebra

    /// <summary>numpy <c>cross</c> of 3-vectors (or 2-vectors, giving the z component) along the last axis.</summary>
    public static NDArray Cross(NDArray a, NDArray b)
    {
        int la = a.Shape[^1], lb = b.Shape[^1];
        if ((la != 2 && la != 3) || (lb != 2 && lb != 3))
            throw new NDValueException("incompatible dimensions for cross product\n(dimension must be 2 or 3)");
        var rt = DTypes.Promote(a.DType, b.DType);
        NDArray C(NDArray x, int i) => x.Get(NDIndex.Ellipsis, i);
        var zero = NDArray.Scalar(0L, rt);
        NDArray A(int i) => i < la ? C(a, i) : zero;
        NDArray B(int i) => i < lb ? C(b, i) : zero;
        if (la == 2 && lb == 2)
            return Subtract(Multiply(A(0), B(1)), Multiply(A(1), B(0)));
        var x0 = Subtract(Multiply(A(1), B(2)), Multiply(A(2), B(1)));
        var x1 = Subtract(Multiply(A(2), B(0)), Multiply(A(0), B(2)));
        var x2 = Subtract(Multiply(A(0), B(1)), Multiply(A(1), B(0)));
        return Stack(new[] { x0, x1, x2 }, -1);
    }

    /// <summary>numpy <c>convolve</c> (1-D); <paramref name="mode"/> is "full", "same" or "valid".</summary>
    public static NDArray Convolve(NDArray a, NDArray v, string mode = "full")
    {
        if (a.Ndim != 1 || v.Ndim != 1) throw new NDValueException("object too deep for desired array");
        if (a.Size == 0) throw new NDValueException("v cannot be empty");
        if (v.Size == 0) throw new NDValueException("v cannot be empty");
        var rt = DTypes.Promote(a.DType, v.DType);
        if (rt == DType.Bool) rt = DType.Int8;
        int n = a.Size, m = v.Size;
        // Build the full convolution as a sum of shifted, scaled copies (exact and dtype-preserving).
        var full = np.Zeros(new[] { n + m - 1 }, rt);
        var ac = a.CastTo(rt);
        for (int j = 0; j < m; j++)
        {
            var scaled = Multiply(ac, v.CastTo(rt).Get(j));
            var target = full.Get(new Slice(j, j + n));
            Assign.Copy(target, Add(target, scaled), Casting.Unsafe);
        }
        return mode switch
        {
            "full" => full,
            "same" => full.Get(new Slice((Math.Min(n, m) - 1) / 2, (Math.Min(n, m) - 1) / 2 + Math.Max(n, m))).Copy(),
            "valid" => full.Get(new Slice(Math.Min(n, m) - 1, Math.Max(n, m))).Copy(),
            _ => throw new NDValueException("mode must be one of 'valid', 'same', or 'full'"),
        };
    }

    // ================================================================ covariance

    /// <summary>numpy <c>cov</c> (rows are variables unless <paramref name="rowvar"/> is false).</summary>
    public static NDArray Cov(NDArray m, NDArray? y = null, bool rowvar = true, bool bias = false, int? ddof = null)
    {
        int dd = ddof ?? (bias ? 0 : 1);
        var x = AtLeast2d(m.AsType(DTypes.Promote(m.DType, DType.Float64)));
        if (!rowvar && x.Shape[0] != 1) x = Transpose(x);
        if (y is not null)
        {
            var yy = AtLeast2d(y.AsType(DType.Float64));
            if (!rowvar && yy.Shape[0] != 1) yy = Transpose(yy);
            x = VStack(new[] { x, yy });
        }
        int n = x.Shape[1];
        double fact = n - dd;
        var avg = Mean(x, new[] { 1 });
        var xc = Subtract(x, ExpandDims(avg, 1));
        var c = MatMul(xc, Transpose(xc));
        // numpy multiplies by the reciprocal (c *= true_divide(1, fact)); fact is floored at 0 (-> inf).
        c = Multiply(c, NDArray.WeakScalar(1.0 / Math.Max(fact, 0.0)));
        return Squeeze(c);
    }

    /// <summary>numpy <c>corrcoef</c>.</summary>
    public static NDArray CorrCoef(NDArray x, NDArray? y = null, bool rowvar = true)
    {
        var c = Cov(x, y, rowvar);
        if (c.Ndim == 0) return Divide(c, c);
        var d = Diagonal(c);
        var std = Sqrt(d);
        c = Divide(c, ExpandDims(std, 1));
        c = Divide(c, ExpandDims(std, 0));
        return Clip(c, NDArray.WeakScalar(-1.0), NDArray.WeakScalar(1.0));
    }
}
