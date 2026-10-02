// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp;

/// <summary>An n-dimensional, typed, strided array with numpy semantics.
///
/// Storage is a plain CLR array (<c>double[]</c>, <c>byte[]</c>, ... — see <see cref="DTypes.ClrType"/>)
/// shared by every view of the same data. <see cref="Shape"/>, <see cref="Strides"/> (in
/// <em>elements</em>, not bytes) and <see cref="Offset"/> describe which elements of the buffer this
/// particular array sees, so slicing, transposition, reshaping and broadcasting are views, exactly
/// as in numpy. <see cref="Base"/> is the array that owns the buffer (numpy's <c>.base</c>), or
/// <c>null</c> when this array owns it.
///
/// A <em>weak scalar</em> (<see cref="IsWeakScalar"/>) is a 0-d array standing for a plain Python
/// number: under numpy 2's NEP 50 promotion rules it adapts to the other operand's dtype instead of
/// forcing one (<c>uint8_array + 1</c> stays <c>uint8</c>). It exists only so bindings can pass
/// language-level numbers into operators; every operation returns ordinary, non-weak arrays.</summary>
public sealed partial class NDArray
{
    public DType DType { get; }
    public Array Buffer { get; }
    public int[] Shape { get; }
    public int[] Strides { get; }
    public int Offset { get; }
    public NDArray? Base { get; }
    public bool IsWeakScalar { get; private init; }

    /// <summary>Host slot for a language binding to cache its own wrapper object for this array
    /// (so that e.g. Python <c>view.base is arr</c> keeps identity). NDSharp never reads it.</summary>
    public object? Tag { get; set; }

    public int Ndim => Shape.Length;
    public int Size { get; }

    /// <summary>A fresh array that owns <paramref name="buffer"/>: C-contiguous strides, offset 0.</summary>
    public NDArray(DType dtype, Array buffer, int[] shape)
        : this(dtype, buffer, shape, ComputeStrides(shape), 0, null)
    {
    }

    /// <summary>A view: explicit <paramref name="strides"/>/<paramref name="offset"/> into someone
    /// else's <paramref name="buffer"/>; <paramref name="base_"/> keeps the owner reachable.</summary>
    public NDArray(DType dtype, Array buffer, int[] shape, int[] strides, int offset, NDArray? base_)
    {
        if (buffer.GetType().GetElementType() != dtype.ClrType())
            throw new NDTypeException($"buffer of {buffer.GetType().GetElementType()?.Name} cannot back a {dtype.Name()} array");
        DType = dtype;
        Buffer = buffer;
        Shape = shape;
        Strides = strides;
        Offset = offset;
        Base = base_;
        long size = 1;
        foreach (var d in shape)
        {
            if (d < 0) throw new NDValueException("negative dimensions are not allowed");
            size *= d;
        }
        if (size > int.MaxValue) throw new NDValueException("array is too big");
        Size = (int)size;
    }

    /// <summary>C-order (row-major) strides, in elements.</summary>
    public static int[] ComputeStrides(int[] shape)
    {
        var strides = new int[shape.Length];
        int acc = 1;
        for (int i = shape.Length - 1; i >= 0; i--)
        {
            strides[i] = acc;
            acc *= Math.Max(shape[i], 1);
        }
        return strides;
    }

    public static int SizeOf(int[] shape)
    {
        long s = 1;
        foreach (var d in shape) s *= d;
        if (s > int.MaxValue) throw new NDValueException("array is too big");
        return (int)s;
    }

    /// <summary>A view over this array's buffer with a different shape/strides/offset (offset is
    /// absolute in the shared buffer).</summary>
    public NDArray ViewWith(int[] shape, int[] strides, int offset)
        => new(DType, Buffer, shape, strides, offset, Base ?? this);

    /// <summary>Same data, but flagged as a weak (Python-number-like) scalar. Only meaningful for 0-d arrays.</summary>
    internal NDArray AsWeak() => new(DType, Buffer, Shape, Strides, Offset, Base) { IsWeakScalar = true };

    /// <summary>True when the elements are laid out as one dense C-order run starting at
    /// <see cref="Offset"/> (a contiguous slice such as <c>a[2:]</c> still qualifies).</summary>
    public bool IsContiguous
    {
        get
        {
            int expected = 1;
            for (int i = Shape.Length - 1; i >= 0; i--)
            {
                if (Shape[i] == 1) continue;
                if (Strides[i] != expected) return false;
                expected *= Shape[i];
            }
            return true;
        }
    }

    /// <summary>The backing array as <typeparamref name="T"/>[]; <typeparamref name="T"/> must be
    /// this array's CLR element type.</summary>
    public T[] Data<T>() => Buffer as T[]
        ?? throw new NDTypeException($"array of dtype {DType.Name()} is not backed by {typeof(T).Name}[]");

    // ------------------------------------------------------------------ element access (boxed)

    /// <summary>The element at absolute-from-<see cref="Offset"/> position <paramref name="offset"/>
    /// (a dot product of an index with <see cref="Strides"/>), boxed as its CLR type.</summary>
    public object GetAt(int offset) => Buffer.GetValue(Offset + offset)!;

    /// <summary>Reads the element at a full multi-index, boxed as its CLR type.</summary>
    public object this[params int[] index] => GetAt(OffsetOf(index));

    public int OffsetOf(int[] index)
    {
        if (index.Length != Ndim)
            throw new NDIndexException($"index has {index.Length} dimensions, array has {Ndim}");
        int off = 0;
        for (int i = 0; i < index.Length; i++)
        {
            int ix = index[i] < 0 ? index[i] + Shape[i] : index[i];
            if (ix < 0 || ix >= Shape[i])
                throw new NDIndexException($"index {index[i]} is out of bounds for axis {i} with size {Shape[i]}");
            off += ix * Strides[i];
        }
        return off;
    }

    /// <summary>Writes one element, converting <paramref name="value"/> (any CLR numeric or bool)
    /// to this array's dtype with numpy's casting rules.</summary>
    public void SetAt(int offset, object value) => Cast.StoreBoxed(this, Offset + offset, value);

    public void Set(object value, params int[] index) => SetAt(OffsetOf(index), value);

    /// <summary>The value as a <see cref="double"/> (bool → 0/1).</summary>
    public double GetDouble(int offset)
    {
        // Dispatch on dtype, never on the CLR array type: byte[] is also an sbyte[] (and uint[] an
        // int[], ulong[] a long[]) as far as the CLR's array covariance is concerned.
        int p = Offset + offset;
        return DType switch
        {
            DType.Float64 => ((double[])Buffer)[p],
            DType.Float32 => ((float[])Buffer)[p],
            DType.Float16 => (double)((Half[])Buffer)[p],
            DType.Int64 => ((long[])Buffer)[p],
            DType.UInt64 => ((ulong[])Buffer)[p],
            DType.Int32 => ((int[])Buffer)[p],
            DType.UInt32 => ((uint[])Buffer)[p],
            DType.Int16 => ((short[])Buffer)[p],
            DType.UInt16 => ((ushort[])Buffer)[p],
            DType.Int8 => ((sbyte[])Buffer)[p],
            DType.UInt8 => ((byte[])Buffer)[p],
            DType.Bool => ((bool[])Buffer)[p] ? 1.0 : 0.0,
            DType.Complex64 or DType.Complex128 => ((System.Numerics.Complex[])Buffer)[p].Real,
            _ => throw new NDNotSupportedException("unsupported buffer"),
        };
    }

    // ------------------------------------------------------------------ copies and casts

    /// <summary>A fresh, independent, C-contiguous copy (never a view).</summary>
    public NDArray Copy() => Materialize(DType);

    /// <summary>numpy <c>astype</c>: a converted copy (always a copy).</summary>
    public NDArray AsType(DType dtype) => Materialize(dtype);

    /// <summary>The same elements converted to <paramref name="dtype"/>, or this very array when no
    /// conversion is needed (callers must not mutate the result).</summary>
    internal NDArray CastTo(DType dtype) => dtype == DType ? this : Materialize(dtype);

    private NDArray Materialize(DType dtype)
        => new(dtype, Cast.ToBuffer(this, dtype), (int[])Shape.Clone());

    /// <summary>The elements in C-order as a flat <typeparamref name="T"/>[] (converting dtype if
    /// needed). Always a copy.</summary>
    public T[] ToArray<T>() where T : unmanaged
        => (T[])Cast.ToBuffer(this, DTypes.FromClrType(typeof(T)));

    /// <summary>The single element of a size-1 array, boxed.</summary>
    public object ToScalar()
    {
        if (Size != 1) throw new NDValueException("can only convert an array of size 1 to a scalar");
        return GetAt(0);
    }

    // ------------------------------------------------------------------ factories

    /// <summary>Wraps (does not copy) a CLR array as a C-contiguous array of the given shape.</summary>
    public static NDArray FromArray<T>(T[] data, params int[] shape) where T : unmanaged
    {
        if (shape.Length == 0)
            shape = new[] { data.Length };
        if (SizeOf(shape) != data.Length)
            throw new NDValueException($"cannot wrap {data.Length} elements as shape ({string.Join(", ", shape)})");
        return new NDArray(DTypes.FromClrType(typeof(T)), data, shape);
    }

    /// <summary>Wraps a rectangular .NET array (<c>T[]</c>, <c>T[,]</c>, <c>T[,,]</c>, ...) by copying it.</summary>
    public static NDArray FromMultiDim(Array array)
    {
        var dtype = DTypes.FromClrType(array.GetType().GetElementType()!);
        var shape = new int[array.Rank];
        for (int i = 0; i < shape.Length; i++) shape[i] = array.GetLength(i);
        var buf = Array.CreateInstance(dtype.ClrType(), SizeOf(shape));
        int k = 0;
        foreach (var v in array) buf.SetValue(v, k++);
        return new NDArray(dtype, buf, shape);
    }

    public static NDArray Scalar(bool v) => new(DType.Bool, new[] { v }, Array.Empty<int>());
    public static NDArray Scalar(long v) => new(DType.Int64, new[] { v }, Array.Empty<int>());
    public static NDArray Scalar(double v) => new(DType.Float64, new[] { v }, Array.Empty<int>());

    /// <summary>A 0-d array holding <paramref name="value"/> in the given dtype.</summary>
    public static NDArray Scalar(object value, DType dtype)
    {
        var a = new NDArray(dtype, Array.CreateInstance(dtype.ClrType(), 1), Array.Empty<int>());
        a.SetAt(0, value);
        return a;
    }

    /// <summary>Weak scalar from a Python-style bool.</summary>
    public static NDArray WeakScalar(bool v) => Scalar(v).AsWeak();
    /// <summary>Weak scalar from a Python-style int (stored as int64, or uint64 when it only fits there).</summary>
    public static NDArray WeakScalar(long v) => Scalar(v).AsWeak();
    public static NDArray WeakScalar(ulong v) => new NDArray(DType.UInt64, new[] { v }, Array.Empty<int>()).AsWeak();
    /// <summary>Weak scalar from a Python-style complex.</summary>
    public static NDArray WeakScalar(System.Numerics.Complex v) => new NDArray(DType.Complex128, new[] { v }, Array.Empty<int>()).AsWeak();
    /// <summary>Weak scalar from a Python-style float.</summary>
    public static NDArray WeakScalar(double v) => Scalar(v).AsWeak();

    /// <summary>Weak scalar from an arbitrary-size integer; throws <see cref="NDOverflowException"/>
    /// beyond the uint64/int64 range, as numpy does.</summary>
    public static NDArray WeakScalar(BigInteger v)
    {
        if (v >= long.MinValue && v <= long.MaxValue) return WeakScalar((long)v);
        if (v > 0 && v <= ulong.MaxValue) return WeakScalar((ulong)v);
        throw new NDOverflowException($"Python int too large to convert to C long: {v}");
    }

    public override string ToString() => ArrayFormat.Str(this);
}
