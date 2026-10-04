// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp;

/// <summary>A Python-style slice <c>start:stop:step</c> (any part optional).</summary>
public readonly record struct Slice(int? Start = null, int? Stop = null, int? Step = null)
{
    public static readonly Slice All = new();

    /// <summary>Python's <c>slice.indices(length)</c> plus the resulting element count.</summary>
    public (int start, int stop, int step, int count) Indices(int length)
    {
        int step = Step ?? 1;
        if (step == 0) throw new NDValueException("slice step cannot be zero");
        int start, stop;
        if (step > 0)
        {
            start = Start is int s ? (s < 0 ? Math.Max(s + length, 0) : Math.Min(s, length)) : 0;
            stop = Stop is int e ? (e < 0 ? Math.Max(e + length, 0) : Math.Min(e, length)) : length;
        }
        else
        {
            start = Start is int s ? (s < 0 ? Math.Max(s + length, -1) : Math.Min(s, length - 1)) : length - 1;
            stop = Stop is int e ? (e < 0 ? Math.Max(e + length, -1) : Math.Min(e, length - 1)) : -1;
        }
        int count = step > 0
            ? (stop > start ? (stop - start + step - 1) / step : 0)
            : (start > stop ? (start - stop + (-step) - 1) / (-step) : 0);
        return (start, stop, step, count);
    }
}

/// <summary>One component of an index expression: an integer, a <see cref="Slice"/>, a new axis,
/// an ellipsis, or an integer/boolean index array.</summary>
public readonly struct NDIndex
{
    public enum Kinds { Int, Slice, NewAxis, Ellipsis, Array }

    public Kinds Kind { get; }
    public int Value { get; }
    public Slice Slice { get; }
    public NDArray? Array { get; }

    private NDIndex(Kinds kind, int value = 0, Slice slice = default, NDArray? array = null)
    {
        Kind = kind;
        Value = value;
        Slice = slice;
        Array = array;
    }

    public static NDIndex NewAxis => new(Kinds.NewAxis);
    public static NDIndex Ellipsis => new(Kinds.Ellipsis);

    public static implicit operator NDIndex(int i) => new(Kinds.Int, i);
    public static implicit operator NDIndex(Slice s) => new(Kinds.Slice, 0, s);
    public static implicit operator NDIndex(NDArray a) => new(Kinds.Array, 0, default, a);
}

public sealed partial class NDArray
{
    /// <summary>numpy <c>a[index]</c>: a view for basic indexing (ints, slices, new axes, ellipsis);
    /// a copy when any index is an array (advanced indexing).</summary>
    public NDArray Get(params NDIndex[] index) => Indexer.Get(this, index);

    /// <summary>The absolute buffer positions (C-order) of the elements <c>a[index]</c> selects, and the shape of
    /// that selection — what <c>ufunc.at</c> needs to update repeated indices in place.</summary>
    public int[] IndexPositions(NDIndex[] index, out int[] shape) => Indexer.Positions(this, index, out shape);

    /// <summary>numpy <c>a[index] = value</c> (value broadcast to the indexed region, converted to this dtype).</summary>
    public void Put(NDArray value, params NDIndex[] index) => Indexer.Set(this, index, value);
}

internal static class Indexer
{
    private sealed class Axis
    {
        public int Size;
        public int Stride;
    }

    private sealed class Adv
    {
        public NDArray Indices = null!; // int64 indices (already wrapped & checked at use)
        public int SourceSize;          // size of the indexed source axis
        public int SourceStride;
        public int SourceAxis;
        public int ItemPosition;        // position in the expanded item list (for adjacency)
    }

    // ------------------------------------------------------------------ resolution

    private sealed class Plan
    {
        public int BaseOffset;
        public List<Axis> Dims = new();
        public List<Adv> Advanced = new();
    }

    private static List<NDIndex> Expand(NDArray a, NDIndex[] index)
    {
        int consumed = 0;
        bool hasEllipsis = false;
        foreach (var it in index)
        {
            switch (it.Kind)
            {
                case NDIndex.Kinds.Ellipsis:
                    if (hasEllipsis) throw new NDIndexException("an index can only have a single ellipsis ('...')");
                    hasEllipsis = true;
                    break;
                case NDIndex.Kinds.NewAxis: break;
                case NDIndex.Kinds.Array:
                    consumed += it.Array!.DType == DType.Bool ? Math.Max(it.Array.Ndim, 0) : 1;
                    break;
                default: consumed++; break;
            }
        }
        if (consumed > a.Ndim)
            throw new NDIndexException(
                $"too many indices for array: array is {a.Ndim}-dimensional, but {consumed} were indexed");
        var result = new List<NDIndex>();
        foreach (var it in index)
        {
            if (it.Kind == NDIndex.Kinds.Ellipsis)
                for (int i = 0; i < a.Ndim - consumed; i++) result.Add(new Slice());
            else
                result.Add(it);
        }
        if (!hasEllipsis)
            while (consumed++ < a.Ndim) result.Add(new Slice());
        return result;
    }

    private static Plan Resolve(NDArray a, NDIndex[] index)
    {
        var items = Expand(a, index);
        bool anyArray = items.Any(x => x.Kind == NDIndex.Kinds.Array);
        var plan = new Plan { BaseOffset = a.Offset };
        int src = 0;
        for (int pos = 0; pos < items.Count; pos++)
        {
            var it = items[pos];
            switch (it.Kind)
            {
                case NDIndex.Kinds.NewAxis:
                    plan.Dims.Add(new Axis { Size = 1, Stride = 0 });
                    break;
                case NDIndex.Kinds.Slice:
                {
                    var (start, _, step, count) = it.Slice.Indices(a.Shape[src]);
                    plan.BaseOffset += start * a.Strides[src];
                    plan.Dims.Add(new Axis { Size = count, Stride = step * a.Strides[src] });
                    src++;
                    break;
                }
                case NDIndex.Kinds.Int when !anyArray:
                {
                    int size = a.Shape[src];
                    int ix = it.Value < 0 ? it.Value + size : it.Value;
                    if (ix < 0 || ix >= size)
                        throw new NDIndexException($"index {it.Value} is out of bounds for axis {src} with size {size}");
                    plan.BaseOffset += ix * a.Strides[src];
                    src++;
                    break;
                }
                case NDIndex.Kinds.Int: // integer scalars take part in advanced indexing when arrays are present
                {
                    var adv = new Adv
                    {
                        Indices = NDArray.Scalar((long)it.Value),
                        SourceSize = a.Shape[src], SourceStride = a.Strides[src], SourceAxis = src, ItemPosition = pos,
                    };
                    plan.Advanced.Add(adv);
                    src++;
                    break;
                }
                case NDIndex.Kinds.Array:
                {
                    var arr = it.Array!;
                    if (arr.DType == DType.Bool)
                    {
                        for (int d = 0; d < arr.Ndim; d++)
                            if (arr.Shape[d] != a.Shape[src + d])
                                throw new NDIndexException(
                                    $"boolean index did not match indexed array along axis {src + d}; size of axis is {a.Shape[src + d]} but size of corresponding boolean axis is {arr.Shape[d]}");
                        var nz = np.NonZero(arr);
                        for (int d = 0; d < arr.Ndim; d++)
                            plan.Advanced.Add(new Adv
                            {
                                Indices = nz[d], SourceSize = a.Shape[src + d], SourceStride = a.Strides[src + d],
                                SourceAxis = src + d, ItemPosition = pos,
                            });
                        src += arr.Ndim;
                    }
                    else if (arr.DType.IsInteger())
                    {
                        plan.Advanced.Add(new Adv
                        {
                            Indices = arr.CastTo(DType.Int64), SourceSize = a.Shape[src], SourceStride = a.Strides[src],
                            SourceAxis = src, ItemPosition = pos,
                        });
                        src++;
                    }
                    else
                        throw new NDIndexException("arrays used as indices must be of integer (or boolean) type");
                    break;
                }
            }
        }
        // Dimensions of the *view* are the Slice/NewAxis dims; advanced axes are not part of Dims.
        return plan;
    }

    public static int[] Positions(NDArray a, NDIndex[] index, out int[] shape)
    {
        var plan = Resolve(a, index);
        if (plan.Advanced.Count > 0)
        {
            var (sh, offsets) = AdvancedOffsets(a, index, plan);
            shape = sh;
            return offsets;
        }
        shape = plan.Dims.Select(d => d.Size).ToArray();
        var list = new List<int>();
        if (NDArray.SizeOf(shape) == 0) return Array.Empty<int>();
        var w = new RowWalker(shape, plan.Dims.Select(d => d.Stride).ToArray());
        do
        {
            for (int i = 0; i < w.InnerLen; i++) list.Add(plan.BaseOffset + w.Off0 + i * w.InnerStride0);
        } while (w.Next());
        return list.ToArray();
    }

    // ------------------------------------------------------------------ get

    public static NDArray Get(NDArray a, NDIndex[] index)
    {
        var plan = Resolve(a, index);
        if (plan.Advanced.Count == 0)
            return a.ViewWith(plan.Dims.Select(d => d.Size).ToArray(), plan.Dims.Select(d => d.Stride).ToArray(), plan.BaseOffset);

        var (shape, offsets) = AdvancedOffsets(a, index, plan);
        var result = new NDArray(a.DType, Array.CreateInstance(a.DType.ClrType(), offsets.Length), shape);
        Gather(a, offsets, result);
        return result;
    }

    // ------------------------------------------------------------------ set

    public static void Set(NDArray a, NDIndex[] index, NDArray value)
    {
        var plan = Resolve(a, index);
        if (plan.Advanced.Count == 0)
        {
            var view = a.ViewWith(plan.Dims.Select(d => d.Size).ToArray(), plan.Dims.Select(d => d.Stride).ToArray(), plan.BaseOffset);
            Assign.Copy(view, value, Casting.Unsafe);
            return;
        }
        var (shape, offsets) = AdvancedOffsets(a, index, plan);
        var v = Broadcasting.BroadcastTo(value, shape).CastTo(a.DType);
        var flat = v.IsContiguous && v.Strides.Length == shape.Length ? v : v.Copy();
        Scatter(a, offsets, flat);
    }

    // ------------------------------------------------------------------ advanced indexing core

    /// <summary>Computes the result shape and, for every result element in C-order, the absolute
    /// buffer position it reads from (numpy's advanced-indexing rules, including where the
    /// broadcast index dimensions land: in place if the index arrays are adjacent, first otherwise).</summary>
    private static (int[] shape, int[] offsets) AdvancedOffsets(NDArray a, NDIndex[] index, Plan plan)
    {
        var advShape = Broadcasting.Shape(plan.Advanced.Select(x => x.Indices.Shape).ToArray());
        int bSize = NDArray.SizeOf(advShape);

        // Offsets contributed by the advanced indices, one per element of the broadcast shape.
        var advOffset = new int[bSize];
        foreach (var adv in plan.Advanced)
        {
            var idx = Broadcasting.BroadcastTo(adv.Indices, advShape);
            var vals = idx.ToArray<long>();
            for (int i = 0; i < bSize; i++)
            {
                long ix = vals[i] < 0 ? vals[i] + adv.SourceSize : vals[i];
                if (ix < 0 || ix >= adv.SourceSize)
                    throw new NDIndexException(
                        $"index {vals[i]} is out of bounds for axis {adv.SourceAxis} with size {adv.SourceSize}");
                advOffset[i] += (int)ix * adv.SourceStride;
            }
        }

        // Are the advanced items one contiguous run in the (expanded) item list?
        var positions = plan.Advanced.Select(x => x.ItemPosition).Distinct().OrderBy(x => x).ToList();
        bool adjacent = positions.Count == 0 || positions[^1] - positions[0] + 1 == positions.Count;

        // Split the non-advanced dims into those before/after the advanced block (when adjacent).
        // Dims were appended in item order; recover how many precede the first advanced item.
        int preCount = 0;
        if (adjacent)
        {
            int first = positions[0];
            var items = Expand(a, index);
            for (int p = 0; p < first; p++)
                if (items[p].Kind is NDIndex.Kinds.Slice or NDIndex.Kinds.NewAxis) preCount++;
        }

        var pre = plan.Dims.Take(adjacent ? preCount : 0).ToList();
        var post = plan.Dims.Skip(adjacent ? preCount : 0).ToList();

        int[] preOff = DimOffsets(pre);
        int[] postOff = DimOffsets(post);

        var shape = pre.Select(d => d.Size).Concat(advShape).Concat(post.Select(d => d.Size)).ToArray();
        var offsets = new int[preOff.Length * bSize * postOff.Length];
        int k = 0;
        foreach (var po in preOff)
            for (int b = 0; b < bSize; b++)
            {
                int bo = plan.BaseOffset + po + advOffset[b];
                foreach (var qo in postOff)
                    offsets[k++] = bo + qo;
            }
        return (shape, offsets);
    }

    private static int[] DimOffsets(List<Axis> dims)
    {
        var result = new List<int> { 0 };
        foreach (var d in dims)
        {
            var next = new List<int>(result.Count * Math.Max(d.Size, 0));
            foreach (var o in result)
                for (int i = 0; i < d.Size; i++)
                    next.Add(o + i * d.Stride);
            result = next;
        }
        return result.ToArray();
    }

    private static void Gather(NDArray a, int[] offsets, NDArray result)
    {
        switch (a.DType.Storage())
        {
            case DType.Bool: GatherT((bool[])a.Buffer, offsets, (bool[])result.Buffer); break;
            case DType.Int8: GatherT((sbyte[])a.Buffer, offsets, (sbyte[])result.Buffer); break;
            case DType.UInt8: GatherT((byte[])a.Buffer, offsets, (byte[])result.Buffer); break;
            case DType.Int16: GatherT((short[])a.Buffer, offsets, (short[])result.Buffer); break;
            case DType.UInt16: GatherT((ushort[])a.Buffer, offsets, (ushort[])result.Buffer); break;
            case DType.Int32: GatherT((int[])a.Buffer, offsets, (int[])result.Buffer); break;
            case DType.UInt32: GatherT((uint[])a.Buffer, offsets, (uint[])result.Buffer); break;
            case DType.Int64: GatherT((long[])a.Buffer, offsets, (long[])result.Buffer); break;
            case DType.UInt64: GatherT((ulong[])a.Buffer, offsets, (ulong[])result.Buffer); break;
            case DType.Float16: GatherT((Half[])a.Buffer, offsets, (Half[])result.Buffer); break;
            case DType.Float32: GatherT((float[])a.Buffer, offsets, (float[])result.Buffer); break;
            case DType.Complex64:
            case DType.Complex128: GatherT((System.Numerics.Complex[])a.Buffer, offsets, (System.Numerics.Complex[])result.Buffer); break;
            default: GatherT((double[])a.Buffer, offsets, (double[])result.Buffer); break;
        }
    }

    private static void GatherT<T>(T[] src, int[] offsets, T[] dst)
    {
        for (int i = 0; i < offsets.Length; i++) dst[i] = src[offsets[i]];
    }

    private static void Scatter(NDArray a, int[] offsets, NDArray values)
    {
        int vo = values.Offset;
        switch (a.DType.Storage())
        {
            case DType.Bool: ScatterT((bool[])a.Buffer, offsets, (bool[])values.Buffer, vo); break;
            case DType.Int8: ScatterT((sbyte[])a.Buffer, offsets, (sbyte[])values.Buffer, vo); break;
            case DType.UInt8: ScatterT((byte[])a.Buffer, offsets, (byte[])values.Buffer, vo); break;
            case DType.Int16: ScatterT((short[])a.Buffer, offsets, (short[])values.Buffer, vo); break;
            case DType.UInt16: ScatterT((ushort[])a.Buffer, offsets, (ushort[])values.Buffer, vo); break;
            case DType.Int32: ScatterT((int[])a.Buffer, offsets, (int[])values.Buffer, vo); break;
            case DType.UInt32: ScatterT((uint[])a.Buffer, offsets, (uint[])values.Buffer, vo); break;
            case DType.Int64: ScatterT((long[])a.Buffer, offsets, (long[])values.Buffer, vo); break;
            case DType.UInt64: ScatterT((ulong[])a.Buffer, offsets, (ulong[])values.Buffer, vo); break;
            case DType.Float16: ScatterT((Half[])a.Buffer, offsets, (Half[])values.Buffer, vo); break;
            case DType.Float32: ScatterT((float[])a.Buffer, offsets, (float[])values.Buffer, vo); break;
            case DType.Complex64:
            case DType.Complex128: ScatterT((System.Numerics.Complex[])a.Buffer, offsets, (System.Numerics.Complex[])values.Buffer, vo); break;
            default: ScatterT((double[])a.Buffer, offsets, (double[])values.Buffer, vo); break;
        }
    }

    private static void ScatterT<T>(T[] dst, int[] offsets, T[] src, int so)
    {
        for (int i = 0; i < offsets.Length; i++) dst[offsets[i]] = src[so + i];
    }
}
