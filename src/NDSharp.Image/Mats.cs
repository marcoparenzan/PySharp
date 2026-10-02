// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Runtime.InteropServices;
using OpenCvSharp;

namespace NDSharp.Image;

/// <summary>Conversions between NDSharp arrays and OpenCV <see cref="Mat"/>s. An array of shape
/// (H, W) is a one-channel image, (H, W, C) a C-channel one — the same convention as cv2's numpy
/// arrays. Data is copied in both directions (a Mat never aliases NDSharp memory).</summary>
public static class Mats
{
    private static MatType MatTypeOf(DType d, int channels)
    {
        int depth = d switch
        {
            DType.UInt8 => MatType.CV_8U,
            DType.Int8 => MatType.CV_8S,
            DType.UInt16 => MatType.CV_16U,
            DType.Int16 => MatType.CV_16S,
            DType.Int32 => MatType.CV_32S,
            DType.Float32 => MatType.CV_32F,
            DType.Float64 => MatType.CV_64F,
            _ => throw new NDTypeException($"OpenCV does not support arrays of dtype {d.Name()}"),
        };
        return MatType.MakeType(depth, channels);
    }

    private static DType DTypeOfDepth(int depth) => depth switch
    {
        MatType.CV_8U => DType.UInt8,
        MatType.CV_8S => DType.Int8,
        MatType.CV_16U => DType.UInt16,
        MatType.CV_16S => DType.Int16,
        MatType.CV_32S => DType.Int32,
        MatType.CV_32F => DType.Float32,
        MatType.CV_64F => DType.Float64,
        _ => throw new NDNotSupportedException($"unsupported OpenCV depth {depth}"),
    };

    /// <summary>NDArray → Mat (copy). bool arrays are treated as uint8.</summary>
    public static unsafe Mat ToMat(NDArray a)
    {
        if (a.DType == DType.Bool) a = a.AsType(DType.UInt8);
        if (a.Ndim == 1) a = np.Reshape(a, a.Shape[0], 1); // OpenCV's numpy bridge reads a vector (N,) as N x 1
        if (a.Ndim < 2 || a.Ndim > 3)
            throw new NDValueException($"expected a 1-D vector, a 2-D (H, W) or 3-D (H, W, C) array, got {a.Ndim}-D");
        int rows = a.Shape[0], cols = a.Shape[1], ch = a.Ndim == 3 ? a.Shape[2] : 1;
        var type = MatTypeOf(a.DType, ch);
        var mat = new Mat(rows, cols, type);
        if (a.Size == 0) return mat;
        var src = a.IsContiguous && a.Offset == 0 ? a : a.Copy();
        int bytes = a.Size * a.DType.ItemSize();
        var dst = new Span<byte>((void*)mat.Data, bytes);
        var srcBytes = src.DType switch
        {
            DType.UInt8 => MemoryMarshal.AsBytes(((byte[])src.Buffer).AsSpan()),
            DType.Int8 => MemoryMarshal.AsBytes(((sbyte[])src.Buffer).AsSpan()),
            DType.UInt16 => MemoryMarshal.AsBytes(((ushort[])src.Buffer).AsSpan()),
            DType.Int16 => MemoryMarshal.AsBytes(((short[])src.Buffer).AsSpan()),
            DType.Int32 => MemoryMarshal.AsBytes(((int[])src.Buffer).AsSpan()),
            DType.Float32 => MemoryMarshal.AsBytes(((float[])src.Buffer).AsSpan()),
            _ => MemoryMarshal.AsBytes(((double[])src.Buffer).AsSpan()),
        };
        srcBytes[..bytes].CopyTo(dst);
        return mat;
    }

    /// <summary>Mat → NDArray (copy): (H, W) for one channel, (H, W, C) otherwise.</summary>
    public static unsafe NDArray FromMat(Mat m)
    {
        using var cont = m.IsContinuous() ? null : m.Clone();
        var mat = cont ?? m;
        int rows = mat.Rows, cols = mat.Cols, ch = mat.Channels();
        var dtype = DTypeOfDepth(mat.Depth());
        var shape = ch == 1 ? new[] { rows, cols } : new[] { rows, cols, ch };
        var buf = Array.CreateInstance(dtype.ClrType(), NDArray.SizeOf(shape));
        int bytes = buf.Length * dtype.ItemSize();
        if (bytes > 0)
        {
            var src = new ReadOnlySpan<byte>((void*)mat.Data, bytes);
            var dst = MemoryMarshal.AsBytes(CastSpan(buf));
            src.CopyTo(dst);
        }
        return new NDArray(dtype, buf, shape);
    }

    private static Span<byte> CastSpan(Array a) => a switch
    {
        byte[] x => x,
        sbyte[] x => MemoryMarshal.AsBytes(x.AsSpan()),
        ushort[] x => MemoryMarshal.AsBytes(x.AsSpan()),
        short[] x => MemoryMarshal.AsBytes(x.AsSpan()),
        int[] x => MemoryMarshal.AsBytes(x.AsSpan()),
        float[] x => MemoryMarshal.AsBytes(x.AsSpan()),
        double[] x => MemoryMarshal.AsBytes(x.AsSpan()),
        _ => throw new NDNotSupportedException("unsupported buffer"),
    };

    /// <summary>Up to four numbers as an OpenCV Scalar (a single number fills channel 0 only, as in cv2).</summary>
    public static Scalar ToScalar(double[]? v) => v is null || v.Length == 0 ? new Scalar(0, 0, 0, 0)
        : new Scalar(v[0], v.Length > 1 ? v[1] : 0, v.Length > 2 ? v[2] : 0, v.Length > 3 ? v[3] : 0);
}
