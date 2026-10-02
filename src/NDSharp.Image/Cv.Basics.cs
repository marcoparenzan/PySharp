// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using OpenCvSharp;

namespace NDSharp.Image;

/// <summary>cv2-shaped image operations on <see cref="NDArray"/> (OpenCV through OpenCvSharp). Enum-like
/// parameters are plain ints holding OpenCV's own constant values (<c>cv2.COLOR_BGR2GRAY</c> = 6, ...), so
/// a language binding can pass cv2 constants straight through. Pixel data is BGR, like OpenCV.</summary>
public static partial class Cv
{
    private static Point P((int x, int y) p) => new(p.x, p.y);
    private static Size S((int w, int h) s) => new(s.w, s.h);

    /// <summary>Runs <paramref name="f"/> on a Mat copy of <paramref name="src"/> and returns the Mat it produces.</summary>
    private static NDArray Run(NDArray src, Action<Mat, Mat> f)
    {
        using var s = Mats.ToMat(src);
        using var d = new Mat();
        f(s, d);
        return Mats.FromMat(d);
    }

    private static NDArray Run2(NDArray a, NDArray b, Action<Mat, Mat, Mat> f)
    {
        using var ma = Mats.ToMat(a);
        using var mb = Mats.ToMat(b);
        using var d = new Mat();
        f(ma, mb, d);
        return Mats.FromMat(d);
    }

    /// <summary>OpenCV's in-place drawing: draws on a Mat copy, then writes the pixels back into <paramref name="img"/>.</summary>
    private static NDArray Draw(NDArray img, Action<Mat> f)
    {
        using var m = Mats.ToMat(img);
        f(m);
        Assign.Copy(img, Mats.FromMat(m), Casting.Unsafe);
        return img;
    }

    // ================================================================ image I/O

    /// <summary>cv2.imread: null when the file cannot be read.</summary>
    public static NDArray? ImRead(string path, int flags = (int)ImreadModes.Color)
    {
        using var m = Cv2.ImRead(path, (ImreadModes)flags);
        return m.Empty() ? null : Mats.FromMat(m);
    }

    public static bool ImWrite(string path, NDArray img, int[]? parameters = null)
    {
        using var m = Mats.ToMat(img);
        return Cv2.ImWrite(path, m, parameters ?? Array.Empty<int>());
    }

    /// <summary>cv2.imencode: (ok, 1-D uint8 buffer).</summary>
    public static (bool ok, NDArray buf) ImEncode(string ext, NDArray img, int[]? parameters = null)
    {
        using var m = Mats.ToMat(img);
        bool ok = Cv2.ImEncode(ext, m, out var bytes, parameters ?? Array.Empty<int>());
        return (ok, NDArray.FromArray(bytes, bytes.Length));
    }

    /// <summary>cv2.imdecode of a 1-D uint8 buffer; null when the data is not an image.</summary>
    public static NDArray? ImDecode(NDArray buf, int flags = (int)ImreadModes.Color)
    {
        var bytes = buf.ToArray<byte>();
        using var m = Cv2.ImDecode(bytes, (ImreadModes)flags);
        return m.Empty() ? null : Mats.FromMat(m);
    }

    // ================================================================ color & geometry

    public static NDArray CvtColor(NDArray src, int code, int dstCn = 0)
        => Run(src, (s, d) => Cv2.CvtColor(s, d, (ColorConversionCodes)code, dstCn));

    /// <summary>cv2.resize: <paramref name="dsize"/> is (width, height); when (0, 0) the scale factors are used.</summary>
    public static NDArray Resize(NDArray src, (int w, int h) dsize, double fx = 0, double fy = 0, int interpolation = (int)InterpolationFlags.Linear)
        => Run(src, (s, d) => Cv2.Resize(s, d, S(dsize), fx, fy, (InterpolationFlags)interpolation));

    public static NDArray Flip(NDArray src, int flipCode)
        => Run(src, (s, d) => Cv2.Flip(s, d, (FlipMode)flipCode));

    public static NDArray[] Split(NDArray src)
    {
        using var m = Mats.ToMat(src);
        Cv2.Split(m, out var planes);
        try { return planes.Select(Mats.FromMat).ToArray(); }
        finally { foreach (var p in planes) p.Dispose(); }
    }

    public static NDArray Merge(IReadOnlyList<NDArray> planes)
    {
        var mats = planes.Select(Mats.ToMat).ToArray();
        try
        {
            using var d = new Mat();
            Cv2.Merge(mats, d);
            return Mats.FromMat(d);
        }
        finally { foreach (var m in mats) m.Dispose(); }
    }

    public static NDArray CopyMakeBorder(NDArray src, int top, int bottom, int left, int right, int borderType, double[]? value = null)
        => Run(src, (s, d) => Cv2.CopyMakeBorder(s, d, top, bottom, left, right, (BorderTypes)borderType, Mats.ToScalar(value)));

    // ================================================================ per-element arithmetic (saturating, like cv2)

    private static InputArray Operand(NDArray? arr, double[]? scalar, out Mat? owned)
    {
        if (arr is not null) { owned = Mats.ToMat(arr); return owned; }
        owned = null;
        return InputArray.Create(Mats.ToScalar(scalar));
    }

    private static NDArray Arith(NDArray? a, double[]? sa, NDArray? b, double[]? sb, Action<InputArray, InputArray, Mat> f)
    {
        var x = Operand(a, sa, out var ma);
        var y = Operand(b, sb, out var mb);
        try
        {
            using var d = new Mat();
            f(x, y, d);
            return Mats.FromMat(d);
        }
        finally { ma?.Dispose(); mb?.Dispose(); }
    }

    public static NDArray Add(NDArray? a, double[]? sa, NDArray? b, double[]? sb, int dtype = -1)
        => Arith(a, sa, b, sb, (x, y, d) => Cv2.Add(x, y, d, null, dtype));
    public static NDArray Subtract(NDArray? a, double[]? sa, NDArray? b, double[]? sb, int dtype = -1)
        => Arith(a, sa, b, sb, (x, y, d) => Cv2.Subtract(x, y, d, null, dtype));
    public static NDArray Multiply(NDArray? a, double[]? sa, NDArray? b, double[]? sb, double scale = 1, int dtype = -1)
        => Arith(a, sa, b, sb, (x, y, d) => Cv2.Multiply(x, y, d, scale, dtype));
    public static NDArray Divide(NDArray? a, double[]? sa, NDArray? b, double[]? sb, double scale = 1, int dtype = -1)
        => Arith(a, sa, b, sb, (x, y, d) => Cv2.Divide(x, y, d, scale, dtype));
    public static NDArray AbsDiff(NDArray? a, double[]? sa, NDArray? b, double[]? sb)
        => Arith(a, sa, b, sb, (x, y, d) => Cv2.Absdiff(x, y, d));

    public static NDArray AddWeighted(NDArray a, double alpha, NDArray b, double beta, double gamma, int dtype = -1)
        => Run2(a, b, (x, y, d) => Cv2.AddWeighted(x, alpha, y, beta, gamma, d, dtype));

    public static NDArray BitwiseAnd(NDArray a, NDArray b, NDArray? mask = null)
        => Run2(a, b, (x, y, d) => { using var mk = mask is null ? null : Mats.ToMat(mask); Cv2.BitwiseAnd(x, y, d, mk); });
    public static NDArray BitwiseOr(NDArray a, NDArray b, NDArray? mask = null)
        => Run2(a, b, (x, y, d) => { using var mk = mask is null ? null : Mats.ToMat(mask); Cv2.BitwiseOr(x, y, d, mk); });
    public static NDArray BitwiseXor(NDArray a, NDArray b, NDArray? mask = null)
        => Run2(a, b, (x, y, d) => { using var mk = mask is null ? null : Mats.ToMat(mask); Cv2.BitwiseXor(x, y, d, mk); });
    public static NDArray BitwiseNot(NDArray a) => Run(a, (s, d) => Cv2.BitwiseNot(s, d));

    public static NDArray Normalize(NDArray src, double alpha = 1, double beta = 0, int normType = (int)NormTypes.L2, int dtype = -1)
        => Run(src, (s, d) => Cv2.Normalize(s, d, alpha, beta, (NormTypes)normType, dtype));

    // ================================================================ thresholding & morphology

    /// <summary>cv2.threshold: (the threshold actually used, result image).</summary>
    public static (double retval, NDArray dst) Threshold(NDArray src, double thresh, double maxval, int type)
    {
        using var s = Mats.ToMat(src);
        using var d = new Mat();
        double t = Cv2.Threshold(s, d, thresh, maxval, (ThresholdTypes)type);
        return (t, Mats.FromMat(d));
    }

    public static NDArray GetStructuringElement(int shape, (int w, int h) ksize, (int x, int y)? anchor = null)
    {
        using var m = Cv2.GetStructuringElement((MorphShapes)shape, S(ksize), anchor is { } a ? P(a) : new Point(-1, -1));
        return Mats.FromMat(m);
    }

    private static Mat? Kernel(NDArray? element) => element is null ? null : Mats.ToMat(element);

    public static NDArray Erode(NDArray src, NDArray? kernel = null, (int x, int y)? anchor = null, int iterations = 1, int borderType = (int)BorderTypes.Constant, double[]? borderValue = null)
        => Run(src, (s, d) => { using var k = Kernel(kernel); Cv2.Erode(s, d, k, anchor is { } a ? P(a) : null, iterations, (BorderTypes)borderType, borderValue is null ? null : Mats.ToScalar(borderValue)); });

    public static NDArray Dilate(NDArray src, NDArray? kernel = null, (int x, int y)? anchor = null, int iterations = 1, int borderType = (int)BorderTypes.Constant, double[]? borderValue = null)
        => Run(src, (s, d) => { using var k = Kernel(kernel); Cv2.Dilate(s, d, k, anchor is { } a ? P(a) : null, iterations, (BorderTypes)borderType, borderValue is null ? null : Mats.ToScalar(borderValue)); });

    public static NDArray MorphologyEx(NDArray src, int op, NDArray? kernel = null, (int x, int y)? anchor = null, int iterations = 1, int borderType = (int)BorderTypes.Constant, double[]? borderValue = null)
        => Run(src, (s, d) => { using var k = Kernel(kernel); Cv2.MorphologyEx(s, d, (MorphTypes)op, k, anchor is { } a ? P(a) : null, iterations, (BorderTypes)borderType, borderValue is null ? null : Mats.ToScalar(borderValue)); });

    // ================================================================ flood fill & connected components

    /// <summary>cv2.floodFill: fills <paramref name="image"/> in place (and <paramref name="mask"/>, when given);
    /// returns (area, bounding rect x, y, w, h).</summary>
    public static (int retval, (int x, int y, int w, int h) rect) FloodFill(NDArray image, NDArray? mask, (int x, int y) seed, double[] newVal,
        double[]? loDiff = null, double[]? upDiff = null, int flags = 4)
    {
        using var im = Mats.ToMat(image);
        using var mk = mask is null ? new Mat() : Mats.ToMat(mask);
        // OpenCvSharp forces the mask fill value to 255 when FLOODFILL_MASK_ONLY (1 << 17) is set, ignoring
        // the value in bits 8-15 that OpenCV honours. MASK_ONLY only means "leave the image untouched", so
        // run without it on the Mat copy and simply do not write the image back.
        const int MaskOnly = 1 << 17;
        bool maskOnly = (flags & MaskOnly) != 0;
        int area = Cv2.FloodFill(im, mk, P(seed), Mats.ToScalar(newVal), out var rect, Mats.ToScalar(loDiff), Mats.ToScalar(upDiff), (FloodFillFlags)(flags & ~MaskOnly));
        if (!maskOnly) Assign.Copy(image, Mats.FromMat(im), Casting.Unsafe);
        if (mask is not null) Assign.Copy(mask, Mats.FromMat(mk), Casting.Unsafe);
        return (area, (rect.X, rect.Y, rect.Width, rect.Height));
    }

    public static (int count, NDArray labels) ConnectedComponents(NDArray image, int connectivity = 8, int ltype = MatType.CV_32S)
    {
        using var s = Mats.ToMat(image);
        using var labels = new Mat();
        int n = Cv2.ConnectedComponents(s, labels, (PixelConnectivity)connectivity, ltype);
        return (n, Mats.FromMat(labels));
    }

    public static (int count, NDArray labels, NDArray stats, NDArray centroids) ConnectedComponentsWithStats(NDArray image, int connectivity = 8, int ltype = MatType.CV_32S)
    {
        using var s = Mats.ToMat(image);
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int n = Cv2.ConnectedComponentsWithStats(s, labels, stats, centroids, (PixelConnectivity)connectivity, ltype);
        return (n, Mats.FromMat(labels), Mats.FromMat(stats), Mats.FromMat(centroids));
    }

    // ================================================================ drawing (in place, returns the image)

    public static NDArray Rectangle(NDArray img, (int x, int y) p1, (int x, int y) p2, double[] color, int thickness = 1, int lineType = 8, int shift = 0)
        => Draw(img, m => Cv2.Rectangle(m, P(p1), P(p2), Mats.ToScalar(color), thickness, (LineTypes)lineType, shift));

    public static NDArray Circle(NDArray img, (int x, int y) center, int radius, double[] color, int thickness = 1, int lineType = 8, int shift = 0)
        => Draw(img, m => Cv2.Circle(m, P(center), radius, Mats.ToScalar(color), thickness, (LineTypes)lineType, shift));

    public static NDArray Line(NDArray img, (int x, int y) p1, (int x, int y) p2, double[] color, int thickness = 1, int lineType = 8, int shift = 0)
        => Draw(img, m => Cv2.Line(m, P(p1), P(p2), Mats.ToScalar(color), thickness, (LineTypes)lineType, shift));

    public static NDArray ArrowedLine(NDArray img, (int x, int y) p1, (int x, int y) p2, double[] color, int thickness = 1, int lineType = 8, int shift = 0, double tipLength = 0.1)
        => Draw(img, m => Cv2.ArrowedLine(m, P(p1), P(p2), Mats.ToScalar(color), thickness, (LineTypes)lineType, shift, tipLength));

    public static NDArray Ellipse(NDArray img, (int x, int y) center, (int w, int h) axes, double angle, double startAngle, double endAngle, double[] color, int thickness = 1, int lineType = 8, int shift = 0)
        => Draw(img, m => Cv2.Ellipse(m, P(center), S(axes), angle, startAngle, endAngle, Mats.ToScalar(color), thickness, (LineTypes)lineType, shift));

    public static NDArray PutText(NDArray img, string text, (int x, int y) org, int fontFace, double fontScale, double[] color, int thickness = 1, int lineType = 8, bool bottomLeftOrigin = false)
        => Draw(img, m => Cv2.PutText(m, text, P(org), (HersheyFonts)fontFace, fontScale, Mats.ToScalar(color), thickness, (LineTypes)lineType, bottomLeftOrigin));

    public static NDArray FillPoly(NDArray img, IReadOnlyList<(int x, int y)[]> polygons, double[] color, int lineType = 8)
        => Draw(img, m => Cv2.FillPoly(m, polygons.Select(p => p.Select(P)), Mats.ToScalar(color), (LineTypes)lineType));

    public static NDArray Polylines(NDArray img, IReadOnlyList<(int x, int y)[]> polygons, bool isClosed, double[] color, int thickness = 1, int lineType = 8)
        => Draw(img, m => Cv2.Polylines(m, polygons.Select(p => p.Select(P)), isClosed, Mats.ToScalar(color), thickness, (LineTypes)lineType));

    public static NDArray DrawMarker(NDArray img, (int x, int y) position, double[] color, int markerType = 0, int markerSize = 20, int thickness = 1, int lineType = 8)
        => Draw(img, m => Cv2.DrawMarker(m, P(position), Mats.ToScalar(color), (MarkerTypes)markerType, markerSize, thickness, (LineTypes)lineType));
}
