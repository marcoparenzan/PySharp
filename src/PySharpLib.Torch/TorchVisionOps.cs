// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;
using TorchSharp;
using static TorchSharp.torch;
using Tensor = TorchSharp.torch.Tensor;

namespace PySharpLib.Torch;

/// <summary>The two compiled operators torchvision's detection models need that libtorch does not have: <c>nms</c> and <c>roi_align</c>
/// (forward only). The arithmetic follows torchvision's CPU kernels operation for operation, in the tensor's own precision.</summary>
internal static class TorchVisionOps
{
    public static void Install(PyModule m)
    {
        m.Dict["_vision_nms"] = Ops.Fn("_vision_nms", (i, a, kw) =>
            TC.Wrap(Nms(TC.Unwrap(a[0]), TC.Unwrap(a[1]), TC.ToDouble(a[2]))));
        m.Dict["_vision_roi_align"] = Ops.Fn("_vision_roi_align", (i, a, kw) =>
            TC.Wrap(RoiAlign(TC.Unwrap(a[0]), TC.Unwrap(a[1]), TC.ToDouble(a[2]), TC.ToLong(a[3]), TC.ToLong(a[4]), TC.ToLong(a[5]), a[6] is true)));
    }

    private static Tensor FromArray<T>(T[] data, long[] shape, ScalarType st) where T : unmanaged
    {
        var t = torch.empty(shape, st);
        if (data.Length > 0) System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan()).CopyTo(t.bytes);
        return t;
    }

    // ------------------------------------------------------------------------------------------ nms

    private static Tensor Nms(Tensor boxes, Tensor scores, double iouThreshold)
    {
        if (boxes.numel() == 0) return torch.empty(new long[] { 0 }, ScalarType.Int64);
        if (boxes.dtype == ScalarType.Float64) return NmsT(boxes.contiguous().ToArr<double>(), scores, (double)iouThreshold);
        var f = boxes.dtype == ScalarType.Float32 ? boxes : boxes.to(ScalarType.Float32);
        return NmsT(f.contiguous().ToArr<float>(), scores, (float)iouThreshold);
    }

    private static Tensor NmsT<T>(T[] b, Tensor scores, T thr) where T : unmanaged, IFloatingPointIeee754<T>
    {
        int n = b.Length / 4;
        // like the kernel: order = scores.sort(descending)
        var (_, idx) = scores.sort(0, true);
        var order = idx.contiguous().ToArr<long>();
        var x1 = new T[n]; var y1 = new T[n]; var x2 = new T[n]; var y2 = new T[n]; var areas = new T[n];
        for (int k = 0; k < n; k++)
        {
            x1[k] = b[4 * k]; y1[k] = b[4 * k + 1]; x2[k] = b[4 * k + 2]; y2[k] = b[4 * k + 3];
            areas[k] = (x2[k] - x1[k]) * (y2[k] - y1[k]);
        }
        var suppressed = new bool[n];
        var keep = new List<long>();
        for (int ii = 0; ii < n; ii++)
        {
            long i = order[ii];
            if (suppressed[i]) continue;
            keep.Add(i);
            T ix1 = x1[i], iy1 = y1[i], ix2 = x2[i], iy2 = y2[i], iarea = areas[i];
            for (int jj = ii + 1; jj < n; jj++)
            {
                long j = order[jj];
                if (suppressed[j]) continue;
                T xx1 = T.Max(ix1, x1[j]), yy1 = T.Max(iy1, y1[j]), xx2 = T.Min(ix2, x2[j]), yy2 = T.Min(iy2, y2[j]);
                T w = T.Max(T.Zero, xx2 - xx1), h = T.Max(T.Zero, yy2 - yy1);
                T inter = w * h;
                T ovr = inter / (iarea + areas[j] - inter);
                if (ovr > thr) suppressed[j] = true;
            }
        }
        return FromArray(keep.ToArray(), new long[] { keep.Count }, ScalarType.Int64);
    }

    // ------------------------------------------------------------------------------------------ roi_align

    private static Tensor RoiAlign(Tensor input, Tensor rois, double spatialScale, long pooledH, long pooledW, long samplingRatio, bool aligned)
    {
        var dt = input.dtype;
        if (dt == ScalarType.Float64)
            return RoiAlignT(input.contiguous().ToArr<double>(), input.shape, rois.to(ScalarType.Float64).contiguous().ToArr<double>(), (int)rois.shape[0],
                spatialScale, pooledH, pooledW, samplingRatio, aligned, ScalarType.Float64);
        var f = dt == ScalarType.Float32 ? input : input.to(ScalarType.Float32);
        var res = RoiAlignT(f.contiguous().ToArr<float>(), f.shape, rois.to(ScalarType.Float32).contiguous().ToArr<float>(), (int)rois.shape[0],
            (float)spatialScale, pooledH, pooledW, samplingRatio, aligned, ScalarType.Float32);
        return dt == ScalarType.Float32 ? res : res.to(dt);
    }

    private static Tensor RoiAlignT<T>(T[] inp, long[] shape, T[] rois, int numRois, T spatialScale, long ph, long pw, long samplingRatio, bool aligned, ScalarType st)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int channels = (int)shape[1], height = (int)shape[2], width = (int)shape[3];
        int pooledH = (int)ph, pooledW = (int)pw;
        var output = new T[(long)numRois * channels * pooledH * pooledW];
        T half = T.CreateTruncating(0.5), one = T.One;
        T offset = aligned ? half : T.Zero;

        Parallel.For(0, numRois, n =>
        {
            long rbase = n * 5L;
            int batch = int.CreateTruncating(rois[rbase]);
            T roiStartW = rois[rbase + 1] * spatialScale - offset, roiStartH = rois[rbase + 2] * spatialScale - offset;
            T roiEndW = rois[rbase + 3] * spatialScale - offset, roiEndH = rois[rbase + 4] * spatialScale - offset;
            T roiWidth = roiEndW - roiStartW, roiHeight = roiEndH - roiStartH;
            if (!aligned) { roiWidth = T.Max(roiWidth, one); roiHeight = T.Max(roiHeight, one); }
            T binH = roiHeight / T.CreateTruncating(pooledH), binW = roiWidth / T.CreateTruncating(pooledW);
            int gridH = samplingRatio > 0 ? (int)samplingRatio : int.CreateTruncating(T.Ceiling(roiHeight / T.CreateTruncating(pooledH)));
            int gridW = samplingRatio > 0 ? (int)samplingRatio : int.CreateTruncating(T.Ceiling(roiWidth / T.CreateTruncating(pooledW)));
            T count = T.Max(T.CreateTruncating(gridH * gridW), one);
            for (int c = 0; c < channels; c++)
            {
                long plane = ((long)batch * channels + c) * height * width;
                for (int py = 0; py < pooledH; py++)
                    for (int px = 0; px < pooledW; px++)
                    {
                        T sum = T.Zero;
                        for (int iy = 0; iy < gridH; iy++)
                        {
                            T y = roiStartH + T.CreateTruncating(py) * binH + (T.CreateTruncating(iy) + half) * binH / T.CreateTruncating(gridH);
                            for (int ix = 0; ix < gridW; ix++)
                            {
                                T x = roiStartW + T.CreateTruncating(px) * binW + (T.CreateTruncating(ix) + half) * binW / T.CreateTruncating(gridW);
                                sum += Bilinear(inp, plane, height, width, y, x);
                            }
                        }
                        output[(((long)n * channels + c) * pooledH + py) * pooledW + px] = sum / count;
                    }
            }
        });
        return FromArray(output, new long[] { numRois, channels, pooledH, pooledW }, st);
    }

    private static T Bilinear<T>(T[] inp, long plane, int height, int width, T y, T x) where T : unmanaged, IFloatingPointIeee754<T>
    {
        T one = T.One, zero = T.Zero;
        if (y < -one || y > T.CreateTruncating(height) || x < -one || x > T.CreateTruncating(width)) return zero;
        if (y <= zero) y = zero;
        if (x <= zero) x = zero;
        int yLow = int.CreateTruncating(y), xLow = int.CreateTruncating(x), yHigh, xHigh;
        if (yLow >= height - 1) { yHigh = yLow = height - 1; y = T.CreateTruncating(yLow); } else yHigh = yLow + 1;
        if (xLow >= width - 1) { xHigh = xLow = width - 1; x = T.CreateTruncating(xLow); } else xHigh = xLow + 1;
        T ly = y - T.CreateTruncating(yLow), lx = x - T.CreateTruncating(xLow), hy = one - ly, hx = one - lx;
        T w1 = hy * hx, w2 = hy * lx, w3 = ly * hx, w4 = ly * lx;
        T v1 = inp[plane + yLow * width + xLow], v2 = inp[plane + yLow * width + xHigh];
        T v3 = inp[plane + yHigh * width + xLow], v4 = inp[plane + yHigh * width + xHigh];
        return w1 * v1 + w2 * v2 + w3 * v3 + w4 * v4;
    }
}
