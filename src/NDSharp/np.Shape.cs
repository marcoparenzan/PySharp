// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp;

public static partial class np
{
    public static int NormalizeAxis(int axis, int ndim)
    {
        int r = axis < 0 ? axis + ndim : axis;
        if (r < 0 || r >= ndim) throw new NDAxisException(axis, ndim);
        return r;
    }

    // ================================================================ reshape family

    /// <summary>numpy <c>reshape</c>: one dimension may be -1 (inferred). A view when the array is
    /// contiguous, otherwise a copy — as numpy does when no view can express the new shape.</summary>
    public static NDArray Reshape(NDArray a, params int[] newShape)
    {
        int unknown = -1;
        long known = 1;
        for (int i = 0; i < newShape.Length; i++)
        {
            if (newShape[i] == -1)
            {
                if (unknown >= 0) throw new NDValueException("can only specify one unknown dimension");
                unknown = i;
            }
            else if (newShape[i] < 0)
                throw new NDValueException("negative dimensions not allowed");
            else known *= newShape[i];
        }
        var resolved = (int[])newShape.Clone();
        if (unknown >= 0)
        {
            if (known == 0 || a.Size % known != 0)
                throw new NDValueException($"cannot reshape array of size {a.Size} into shape {Broadcasting.ShapeText(newShape)}");
            resolved[unknown] = (int)(a.Size / known);
        }
        if (NDArray.SizeOf(resolved) != a.Size)
            throw new NDValueException($"cannot reshape array of size {a.Size} into shape {Broadcasting.ShapeText(newShape)}");
        if (a.IsContiguous)
            return a.ViewWith(resolved, NDArray.ComputeStrides(resolved), a.Offset);
        return new NDArray(a.DType, Cast.ToBuffer(a, a.DType), resolved);
    }

    public static NDArray Ravel(NDArray a) => Reshape(a, a.Size);

    /// <summary>numpy <c>flatten</c>: always a copy.</summary>
    public static NDArray Flatten(NDArray a) => Ravel(a).Copy();

    public static NDArray Transpose(NDArray a, int[]? axes = null)
    {
        int n = a.Ndim;
        int[] perm = axes is null ? Enumerable.Range(0, n).Reverse().ToArray() : axes.Select(x => NormalizeAxis(x, n)).ToArray();
        if (perm.Length != n || perm.Distinct().Count() != n)
            throw new NDValueException("axes don't match array");
        return a.ViewWith(perm.Select(p => a.Shape[p]).ToArray(), perm.Select(p => a.Strides[p]).ToArray(), a.Offset);
    }

    public static NDArray SwapAxes(NDArray a, int axis1, int axis2)
    {
        int n = a.Ndim;
        var perm = Enumerable.Range(0, n).ToArray();
        (perm[NormalizeAxis(axis1, n)], perm[NormalizeAxis(axis2, n)]) = (perm[NormalizeAxis(axis2, n)], perm[NormalizeAxis(axis1, n)]);
        return Transpose(a, perm);
    }

    public static NDArray MoveAxis(NDArray a, int source, int destination)
    {
        int n = a.Ndim;
        int s = NormalizeAxis(source, n), d = NormalizeAxis(destination, n);
        var order = Enumerable.Range(0, n).Where(i => i != s).ToList();
        order.Insert(d, s);
        return Transpose(a, order.ToArray());
    }

    public static NDArray ExpandDims(NDArray a, int axis)
    {
        int nn = a.Ndim + 1;
        int ax = NormalizeAxis(axis, nn);
        var shape = a.Shape.ToList();
        var strides = a.Strides.ToList();
        shape.Insert(ax, 1);
        strides.Insert(ax, 0);
        return a.ViewWith(shape.ToArray(), strides.ToArray(), a.Offset);
    }

    public static NDArray Squeeze(NDArray a, int? axis = null)
    {
        var shape = new List<int>();
        var strides = new List<int>();
        int? ax = axis is int x ? NormalizeAxis(x, a.Ndim) : null;
        if (ax is int k && a.Shape[k] != 1)
            throw new NDValueException("cannot select an axis to squeeze out which has size not equal to one");
        for (int i = 0; i < a.Ndim; i++)
        {
            bool drop = a.Shape[i] == 1 && (ax is null || ax == i);
            if (drop) continue;
            shape.Add(a.Shape[i]);
            strides.Add(a.Strides[i]);
        }
        return a.ViewWith(shape.ToArray(), strides.ToArray(), a.Offset);
    }

    public static NDArray BroadcastTo(NDArray a, int[] shape) => Broadcasting.BroadcastTo(a, shape);

    public static NDArray AtLeast1d(NDArray a) => a.Ndim >= 1 ? a : Reshape(a, 1);
    public static NDArray AtLeast2d(NDArray a) => a.Ndim >= 2 ? a : a.Ndim == 1 ? ExpandDims(a, 0) : Reshape(a, 1, 1);

    // ================================================================ joining

    public static NDArray Concatenate(IReadOnlyList<NDArray> arrays, int axis = 0)
    {
        if (arrays.Count == 0) throw new NDValueException("need at least one array to concatenate");
        int ndim = arrays[0].Ndim;
        if (ndim == 0) throw new NDValueException("zero-dimensional arrays cannot be concatenated");
        int ax = NormalizeAxis(axis, ndim);
        var rt = arrays[0].DType;
        foreach (var arr in arrays)
        {
            if (arr.Ndim != ndim)
                throw new NDValueException(
                    $"all the input array dimensions except for the concatenation axis must match exactly, but along dimension 0, the array at index 0 has {ndim} dimension(s) and the array at index {arrays.ToList().IndexOf(arr)} has {arr.Ndim} dimension(s)");
            for (int d = 0; d < ndim; d++)
                if (d != ax && arr.Shape[d] != arrays[0].Shape[d])
                    throw new NDValueException(
                        $"all the input array dimensions except for the concatenation axis must match exactly, but along dimension {d}, the array at index 0 has size {arrays[0].Shape[d]} and the array at index {arrays.ToList().IndexOf(arr)} has size {arr.Shape[d]}");
            rt = DTypes.Promote(rt, arr.DType);
        }
        var outShape = (int[])arrays[0].Shape.Clone();
        outShape[ax] = arrays.Sum(x => x.Shape[ax]);
        var result = New(rt, outShape);
        int pos = 0;
        foreach (var arr in arrays)
        {
            var shape = (int[])outShape.Clone();
            shape[ax] = arr.Shape[ax];
            var target = result.ViewWith(shape, result.Strides, result.Offset + pos * result.Strides[ax]);
            Assign.Copy(target, arr, Casting.Unsafe);
            pos += arr.Shape[ax];
        }
        return result;
    }

    public static NDArray Stack(IReadOnlyList<NDArray> arrays, int axis = 0)
    {
        if (arrays.Count == 0) throw new NDValueException("need at least one array to stack");
        foreach (var a in arrays)
            if (!a.Shape.SequenceEqual(arrays[0].Shape))
                throw new NDValueException("all input arrays must have the same shape");
        int ax = NormalizeAxis(axis, arrays[0].Ndim + 1);
        return Concatenate(arrays.Select(a => ExpandDims(a, ax)).ToList(), ax);
    }

    public static NDArray VStack(IReadOnlyList<NDArray> arrays) => Concatenate(arrays.Select(AtLeast2d).ToList(), 0);

    public static NDArray HStack(IReadOnlyList<NDArray> arrays)
    {
        var arrs = arrays.Select(AtLeast1d).ToList();
        return Concatenate(arrs, arrs[0].Ndim == 1 ? 0 : 1);
    }

    public static NDArray DStack(IReadOnlyList<NDArray> arrays)
    {
        var arrs = arrays.Select(a => a.Ndim switch
        {
            0 => Reshape(a, 1, 1, 1),
            1 => Reshape(a, 1, a.Size, 1),
            2 => ExpandDims(a, 2),
            _ => a,
        }).ToList();
        return Concatenate(arrs, 2);
    }

    // ================================================================ flipping / rolling / tiling

    public static NDArray Flip(NDArray a, params int[]? axes)
    {
        var ax = axes is null || axes.Length == 0 ? Enumerable.Range(0, a.Ndim).ToArray() : axes.Select(x => NormalizeAxis(x, a.Ndim)).ToArray();
        var strides = (int[])a.Strides.Clone();
        int off = a.Offset;
        foreach (var k in ax)
        {
            if (a.Shape[k] > 0) off += (a.Shape[k] - 1) * strides[k];
            strides[k] = -strides[k];
        }
        return a.ViewWith((int[])a.Shape.Clone(), strides, off);
    }

    public static NDArray Fliplr(NDArray a)
    {
        if (a.Ndim < 2) throw new NDValueException("Input must be >= 2-d.");
        return Flip(a, 1);
    }

    public static NDArray Flipud(NDArray a)
    {
        if (a.Ndim < 1) throw new NDValueException("Input must be >= 1-d.");
        return Flip(a, 0);
    }

    /// <summary>numpy <c>roll</c> along one axis (or, with <paramref name="axis"/> null, on the flattened array).</summary>
    public static NDArray Roll(NDArray a, int shift, int? axis = null)
    {
        if (axis is null)
            return Reshape(Roll(Ravel(a).Copy(), shift, 0), a.Shape);
        int ax = NormalizeAxis(axis.Value, a.Ndim);
        int n = a.Shape[ax];
        var result = New(a.DType, a.Shape);
        if (n == 0) return result;
        int s = ((shift % n) + n) % n;
        if (s == 0) { Assign.Copy(result, a, Casting.Unsafe); return result; }
        NDArray Part(NDArray x, int start, int len)
        {
            var shape = (int[])x.Shape.Clone();
            shape[ax] = len;
            return x.ViewWith(shape, x.Strides, x.Offset + start * x.Strides[ax]);
        }
        Assign.Copy(Part(result, s, n - s), Part(a, 0, n - s), Casting.Unsafe);
        Assign.Copy(Part(result, 0, s), Part(a, n - s, s), Casting.Unsafe);
        return result;
    }

    public static NDArray Tile(NDArray a, params int[] reps)
    {
        int nd = Math.Max(a.Ndim, reps.Length);
        var shape = Enumerable.Repeat(1, nd - a.Ndim).Concat(a.Shape).ToArray();
        var r = Enumerable.Repeat(1, nd - reps.Length).Concat(reps).ToArray();
        // [r0, s0, r1, s1, ...] broadcast of [1, s0, 1, s1, ...], then merge pairs.
        var inter = new int[nd * 2];
        var src = new int[nd * 2];
        for (int i = 0; i < nd; i++)
        {
            inter[2 * i] = r[i]; inter[2 * i + 1] = shape[i];
            src[2 * i] = 1; src[2 * i + 1] = shape[i];
        }
        var v = Broadcasting.BroadcastTo(Reshape(a, src), inter).Copy();
        return Reshape(v, Enumerable.Range(0, nd).Select(i => r[i] * shape[i]).ToArray());
    }

    public static NDArray Repeat(NDArray a, int repeats, int? axis = null)
    {
        if (axis is null) return Repeat(Ravel(a), repeats, 0);
        int ax = NormalizeAxis(axis.Value, a.Ndim);
        // Insert a new axis after ax, broadcast it to `repeats`, merge.
        var withAxis = ExpandDims(a, ax + 1);
        var shape = (int[])withAxis.Shape.Clone();
        shape[ax + 1] = repeats;
        var b = Broadcasting.BroadcastTo(withAxis, shape).Copy();
        var outShape = a.Shape.ToArray();
        outShape[ax] *= repeats;
        return Reshape(b, outShape);
    }

    /// <summary>numpy <c>pad</c> for the index-mapped modes <c>edge</c>, <c>reflect</c>, <c>symmetric</c> and <c>wrap</c>, axis by axis
    /// (the extension is periodic for reflect/symmetric/wrap, so pads wider than the array behave like numpy's repeated passes).</summary>
    public static NDArray PadIndexed(NDArray a, (int before, int after)[] widths, string mode)
    {
        if (widths.Length == 1 && a.Ndim > 1) widths = Enumerable.Repeat(widths[0], a.Ndim).ToArray();
        if (widths.Length != a.Ndim) throw new NDValueException("operands could not be broadcast together with remapped shapes [original->remapped]");
        var result = a;
        for (int ax = 0; ax < a.Ndim; ax++)
        {
            int n = a.Shape[ax], before = widths[ax].before, after = widths[ax].after;
            if (before < 0 || after < 0) throw new NDValueException("index can't contain negative values");
            if (before == 0 && after == 0) continue;
            if (n == 0) throw new NDValueException($"can't extend empty axis {ax} using modes other than 'constant' or 'empty'");
            var map = new long[n + before + after];
            for (int j = 0; j < map.Length; j++)
            {
                long src = j - before;
                map[j] = mode switch
                {
                    "edge" => Math.Clamp(src, 0, n - 1),
                    "wrap" => ((src % n) + n) % n,
                    "symmetric" => SymIndex(src, n),
                    "reflect" => n == 1 ? 0 : RefIndex(src, n),
                    _ => throw new NDValueException($"mode '{mode}' is not supported"),
                };
            }
            var idx = new NDIndex[a.Ndim];
            for (int d = 0; d < a.Ndim; d++) idx[d] = d == ax ? NDArray.FromArray(map, map.Length) : new Slice();
            result = result.Get(idx).Copy();
        }
        return result;

        static long SymIndex(long src, int n) { long m = ((src % (2L * n)) + 2L * n) % (2L * n); return m < n ? m : 2L * n - 1 - m; }
        static long RefIndex(long src, int n) { long p = 2L * (n - 1); long m = ((src % p) + p) % p; return m < n ? m : p - m; }
    }

    /// <summary>numpy <c>pad</c> with <c>mode='constant'</c> (value 0 by default).</summary>
    public static NDArray PadConstant(NDArray a, (int before, int after)[] widths, object? value = null)
    {
        if (widths.Length == 1 && a.Ndim > 1) widths = Enumerable.Repeat(widths[0], a.Ndim).ToArray();
        if (widths.Length != a.Ndim) throw new NDValueException("operands could not be broadcast together with remapped shapes [original->remapped]");
        var shape = new int[a.Ndim];
        for (int i = 0; i < a.Ndim; i++)
        {
            if (widths[i].before < 0 || widths[i].after < 0) throw new NDValueException("index can't contain negative values");
            shape[i] = a.Shape[i] + widths[i].before + widths[i].after;
        }
        var result = value is null ? New(a.DType, shape) : Full(shape, value, a.DType);
        var inner = result.ViewWith((int[])a.Shape.Clone(), result.Strides,
            result.Offset + Enumerable.Range(0, a.Ndim).Sum(i => widths[i].before * result.Strides[i]));
        Assign.Copy(inner, a, Casting.Unsafe);
        return result;
    }
}
