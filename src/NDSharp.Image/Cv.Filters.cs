// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using OpenCvSharp;

namespace NDSharp.Image;

public static partial class Cv
{
    private static Point? Anchor((int x, int y)? a) => a is { } p ? new Point(p.x, p.y) : null;

    // ================================================================ linear filters

    /// <summary>cv2.filter2D (correlation). <paramref name="ddepth"/> is an OpenCV depth (-1 = same as source).</summary>
    public static NDArray Filter2D(NDArray src, int ddepth, NDArray kernel, (int x, int y)? anchor = null, double delta = 0, int borderType = (int)BorderTypes.Reflect101)
        => Run(src, (s, d) => { using var k = Mats.ToMat(kernel); Cv2.Filter2D(s, d, ddepth, k, Anchor(anchor), delta, (BorderTypes)borderType); });

    public static NDArray GaussianBlur(NDArray src, (int w, int h) ksize, double sigmaX, double sigmaY = 0, int borderType = (int)BorderTypes.Reflect101)
        => Run(src, (s, d) => Cv2.GaussianBlur(s, d, S(ksize), sigmaX, sigmaY, (BorderTypes)borderType));

    /// <summary>cv2.getGaussianKernel: a (ksize, 1) column; ktype is CV_32F or CV_64F (default).</summary>
    public static NDArray GetGaussianKernel(int ksize, double sigma, int ktype = MatType.CV_64F)
    {
        using var m = Cv2.GetGaussianKernel(ksize, sigma, ktype);
        return Mats.FromMat(m);
    }

    public static NDArray BoxFilter(NDArray src, int ddepth, (int w, int h) ksize, (int x, int y)? anchor = null, bool normalize = true, int borderType = (int)BorderTypes.Reflect101)
        => Run(src, (s, d) => Cv2.BoxFilter(s, d, ddepth, S(ksize), Anchor(anchor), normalize, (BorderTypes)borderType));

    public static NDArray Blur(NDArray src, (int w, int h) ksize, (int x, int y)? anchor = null, int borderType = (int)BorderTypes.Reflect101)
        => Run(src, (s, d) => Cv2.Blur(s, d, S(ksize), Anchor(anchor), (BorderTypes)borderType));

    public static NDArray MedianBlur(NDArray src, int ksize)
        => Run(src, (s, d) => Cv2.MedianBlur(s, d, ksize));

    public static NDArray BilateralFilter(NDArray src, int d, double sigmaColor, double sigmaSpace, int borderType = (int)BorderTypes.Reflect101)
        => Run(src, (s, dst) => Cv2.BilateralFilter(s, dst, d, sigmaColor, sigmaSpace, (BorderTypes)borderType));

    // ================================================================ derivatives & edges

    public static NDArray Sobel(NDArray src, int ddepth, int dx, int dy, int ksize = 3, double scale = 1, double delta = 0, int borderType = (int)BorderTypes.Reflect101)
        => Run(src, (s, d) => Cv2.Sobel(s, d, ddepth, dx, dy, ksize, scale, delta, (BorderTypes)borderType));

    public static NDArray Scharr(NDArray src, int ddepth, int dx, int dy, double scale = 1, double delta = 0, int borderType = (int)BorderTypes.Reflect101)
        => Run(src, (s, d) => Cv2.Scharr(s, d, ddepth, dx, dy, scale, delta, (BorderTypes)borderType));

    public static NDArray Laplacian(NDArray src, int ddepth, int ksize = 1, double scale = 1, double delta = 0, int borderType = (int)BorderTypes.Reflect101)
        => Run(src, (s, d) => Cv2.Laplacian(s, d, ddepth, ksize, scale, delta, (BorderTypes)borderType));

    public static NDArray Canny(NDArray src, double threshold1, double threshold2, int apertureSize = 3, bool l2Gradient = false)
        => Run(src, (s, d) => Cv2.Canny(s, d, threshold1, threshold2, apertureSize, l2Gradient));

    public static NDArray PyrDown(NDArray src, (int w, int h)? dstSize = null, int borderType = (int)BorderTypes.Reflect101)
        => Run(src, (s, d) => Cv2.PyrDown(s, d, dstSize is { } z ? S(z) : null, (BorderTypes)borderType));

    public static NDArray PyrUp(NDArray src, (int w, int h)? dstSize = null, int borderType = (int)BorderTypes.Default)
        => Run(src, (s, d) => Cv2.PyrUp(s, d, dstSize is { } z ? S(z) : null, (BorderTypes)borderType));

    public static NDArray GetGaborKernel((int w, int h) ksize, double sigma, double theta, double lambd, double gamma, double psi = Math.PI * 0.5, int ktype = MatType.CV_64F)
    {
        using var m = Cv2.GetGaborKernel(S(ksize), sigma, theta, lambd, gamma, psi, ktype);
        return Mats.FromMat(m);
    }

    public static (NDArray magnitude, NDArray angle) CartToPolar(NDArray x, NDArray y, bool angleInDegrees = false)
    {
        using var mx = Mats.ToMat(x);
        using var my = Mats.ToMat(y);
        using var mag = new Mat();
        using var ang = new Mat();
        Cv2.CartToPolar(mx, my, mag, ang, angleInDegrees);
        return (Mats.FromMat(mag), Mats.FromMat(ang));
    }

    public static NDArray Dct(NDArray src, int flags = 0) => Run(src, (s, d) => Cv2.Dct(s, d, (DctFlags)flags));
    public static NDArray Idct(NDArray src, int flags = 0) => Run(src, (s, d) => Cv2.Idct(s, d, (DctFlags)flags));

    public static NDArray DistanceTransform(NDArray src, int distanceType, int maskSize, int dstType = MatType.CV_32F)
        => Run(src, (s, d) => Cv2.DistanceTransform(s, d, (DistanceTypes)distanceType, (DistanceTransformMasks)maskSize, dstType));

    // ================================================================ geometry

    private static Mat? Opt(NDArray? a) => a is null ? null : Mats.ToMat(a);

    public static NDArray WarpAffine(NDArray src, NDArray m, (int w, int h) dsize, int flags = (int)InterpolationFlags.Linear, int borderMode = (int)BorderTypes.Constant, double[]? borderValue = null)
        => Run(src, (s, d) => { using var mm = Mats.ToMat(m); Cv2.WarpAffine(s, d, mm, S(dsize), (InterpolationFlags)flags, (BorderTypes)borderMode, Mats.ToScalar(borderValue)); });

    public static NDArray WarpPerspective(NDArray src, NDArray m, (int w, int h) dsize, int flags = (int)InterpolationFlags.Linear, int borderMode = (int)BorderTypes.Constant, double[]? borderValue = null)
        => Run(src, (s, d) => { using var mm = Mats.ToMat(m); Cv2.WarpPerspective(s, d, mm, S(dsize), (InterpolationFlags)flags, (BorderTypes)borderMode, Mats.ToScalar(borderValue)); });

    public static NDArray GetRotationMatrix2D((double x, double y) center, double angle, double scale)
    {
        using var m = Cv2.GetRotationMatrix2D(new Point2f((float)center.x, (float)center.y), angle, scale);
        return Mats.FromMat(m);
    }

    private static Point2f[] Pts2f(NDArray a)
    {
        var f = a.AsType(DType.Float32).ToArray<float>();
        return Enumerable.Range(0, f.Length / 2).Select(i => new Point2f(f[2 * i], f[2 * i + 1])).ToArray();
    }

    public static NDArray GetAffineTransform(NDArray src, NDArray dst)
    {
        using var m = Cv2.GetAffineTransform(Pts2f(src), Pts2f(dst));
        return Mats.FromMat(m);
    }

    public static NDArray GetPerspectiveTransform(NDArray src, NDArray dst)
    {
        using var m = Cv2.GetPerspectiveTransform(Pts2f(src), Pts2f(dst));
        return Mats.FromMat(m);
    }

    public static NDArray InvertAffineTransform(NDArray m)
        => Run(m, (s, d) => Cv2.InvertAffineTransform(s, d));

    public static NDArray PerspectiveTransform(NDArray src, NDArray m)
        => Run2(src, m, (s, mm, d) => Cv2.PerspectiveTransform(s, d, mm));

    // ================================================================ moments & contours

    /// <summary>cv2.moments as an ordered name → value map (m00.., mu20.., nu20..).</summary>
    public static IReadOnlyList<(string name, double value)> Moments(NDArray array, bool binaryImage = false)
    {
        using var m = Mats.ToMat(array);
        var mo = Cv2.Moments(m, binaryImage);
        return new (string, double)[]
        {
            ("m00", mo.M00), ("m10", mo.M10), ("m01", mo.M01), ("m20", mo.M20), ("m11", mo.M11), ("m02", mo.M02),
            ("m30", mo.M30), ("m21", mo.M21), ("m12", mo.M12), ("m03", mo.M03),
            ("mu20", mo.Mu20), ("mu11", mo.Mu11), ("mu02", mo.Mu02), ("mu30", mo.Mu30), ("mu21", mo.Mu21), ("mu12", mo.Mu12), ("mu03", mo.Mu03),
            ("nu20", mo.Nu20), ("nu11", mo.Nu11), ("nu02", mo.Nu02), ("nu30", mo.Nu30), ("nu21", mo.Nu21), ("nu12", mo.Nu12), ("nu03", mo.Nu03),
        };
    }

    /// <summary>cv2.HuMoments of a moments map: a (7, 1) float64 array (same operation order as OpenCV).</summary>
    public static NDArray HuMoments(IReadOnlyDictionary<string, double> m)
    {
        double V(string k) => m.TryGetValue(k, out var v) ? v : 0.0;
        double n20 = V("nu20"), n11 = V("nu11"), n02 = V("nu02"), n30 = V("nu30"), n21 = V("nu21"), n12 = V("nu12"), n03 = V("nu03");
        double t0 = n30 + n12, t1 = n21 + n03;
        double q0 = t0 * t0, q1 = t1 * t1;
        double n4 = 4 * n11, sum = n20 + n02, d = n20 - n02;
        var hu = new double[7];
        hu[0] = sum;
        hu[1] = d * d + n4 * n11;
        hu[3] = q0 + q1;
        hu[5] = d * (q0 - q1) + n4 * t0 * t1;
        t0 *= q0 - 3 * q1;
        t1 *= 3 * q0 - q1;
        q0 = n30 - 3 * n12;
        q1 = 3 * n21 - n03;
        hu[2] = q0 * q0 + q1 * q1;
        hu[4] = q0 * t0 + q1 * t1;
        hu[6] = q1 * t0 - q0 * t1;
        return NDArray.FromArray(hu, 7, 1);
    }

    /// <summary>cv2.findContours: contours as (N, 1, 2) int32 arrays and the (1, N, 4) hierarchy.</summary>
    public static (NDArray[] contours, NDArray hierarchy) FindContours(NDArray image, int mode, int method, (int x, int y)? offset = null)
    {
        using var s = Mats.ToMat(image);
        Cv2.FindContours(s, out var contours, out var hierarchy, (RetrievalModes)mode, (ContourApproximationModes)method, offset is { } o ? new Point(o.x, o.y) : null);
        var arrays = contours.Select(c =>
        {
            var data = new int[c.Length * 2];
            for (int i = 0; i < c.Length; i++) { data[2 * i] = c[i].X; data[2 * i + 1] = c[i].Y; }
            return NDArray.FromArray(data, c.Length, 1, 2);
        }).ToArray();
        var h = new int[hierarchy.Length * 4];
        for (int i = 0; i < hierarchy.Length; i++)
        {
            h[4 * i] = hierarchy[i].Next; h[4 * i + 1] = hierarchy[i].Previous; h[4 * i + 2] = hierarchy[i].Child; h[4 * i + 3] = hierarchy[i].Parent;
        }
        return (arrays, NDArray.FromArray(h, 1, hierarchy.Length, 4));
    }

    public static double ContourArea(NDArray contour, bool oriented = false)
    {
        var pts = Pts2f(contour);
        return Cv2.ContourArea(pts, oriented);
    }

    public static double ArcLength(NDArray curve, bool closed)
        => Cv2.ArcLength(Pts2f(curve), closed);

    // ================================================================ corners & lines

    public static NDArray CornerHarris(NDArray src, int blockSize, int ksize, double k, int borderType = (int)BorderTypes.Default)
        => Run(src, (s, d) => Cv2.CornerHarris(s, d, blockSize, ksize, k, (BorderTypes)borderType));

    public static NDArray CornerMinEigenVal(NDArray src, int blockSize, int ksize = 3, int borderType = (int)BorderTypes.Default)
        => Run(src, (s, d) => Cv2.CornerMinEigenVal(s, d, blockSize, ksize, (BorderTypes)borderType));

    /// <summary>cv2.goodFeaturesToTrack: (N, 1, 2) float32 corners.</summary>
    public static NDArray GoodFeaturesToTrack(NDArray image, int maxCorners, double qualityLevel, double minDistance, NDArray? mask = null, int blockSize = 3, bool useHarris = false, double k = 0.04)
    {
        using var s = Mats.ToMat(image);
        using var mk = Opt(mask);
        var pts = Cv2.GoodFeaturesToTrack(s, maxCorners, qualityLevel, minDistance, mk ?? new Mat(), blockSize, useHarris, k);
        return PointsToArray(pts);
    }

    private static NDArray PointsToArray(Point2f[] pts)
    {
        var data = new float[pts.Length * 2];
        for (int i = 0; i < pts.Length; i++) { data[2 * i] = pts[i].X; data[2 * i + 1] = pts[i].Y; }
        return NDArray.FromArray(data, pts.Length, 1, 2);
    }

    public static NDArray CornerSubPix(NDArray image, NDArray corners, (int w, int h) winSize, (int w, int h) zeroZone, int criteriaType, int maxCount, double epsilon)
    {
        using var s = Mats.ToMat(image);
        var refined = Cv2.CornerSubPix(s, Pts2f(corners), S(winSize), S(zeroZone), new TermCriteria((CriteriaTypes)criteriaType, maxCount, epsilon));
        return PointsToArray(refined);
    }

    /// <summary>cv2.HoughLines: (N, 1, 2) float32 (rho, theta), or an empty array.</summary>
    public static NDArray HoughLines(NDArray image, double rho, double theta, int threshold, double srn = 0, double stn = 0)
    {
        using var s = Mats.ToMat(image);
        var lines = Cv2.HoughLines(s, rho, theta, threshold, srn, stn);
        var data = new float[lines.Length * 2];
        for (int i = 0; i < lines.Length; i++) { data[2 * i] = lines[i].Rho; data[2 * i + 1] = lines[i].Theta; }
        return NDArray.FromArray(data, lines.Length, 1, 2);
    }

    /// <summary>cv2.HoughLinesP: (N, 1, 4) int32 segments.</summary>
    public static NDArray HoughLinesP(NDArray image, double rho, double theta, int threshold, double minLineLength = 0, double maxLineGap = 0)
    {
        using var s = Mats.ToMat(image);
        var lines = Cv2.HoughLinesP(s, rho, theta, threshold, minLineLength, maxLineGap);
        var data = new int[lines.Length * 4];
        for (int i = 0; i < lines.Length; i++)
        {
            data[4 * i] = lines[i].P1.X; data[4 * i + 1] = lines[i].P1.Y; data[4 * i + 2] = lines[i].P2.X; data[4 * i + 3] = lines[i].P2.Y;
        }
        return NDArray.FromArray(data, lines.Length, 1, 4);
    }

    /// <summary>cv2.HoughCircles: (1, N, 3) float32 (x, y, r) or null when nothing is found.</summary>
    public static NDArray? HoughCircles(NDArray image, int method, double dp, double minDist, double param1 = 100, double param2 = 100, int minRadius = 0, int maxRadius = 0)
    {
        using var s = Mats.ToMat(image);
        var circles = Cv2.HoughCircles(s, (HoughModes)method, dp, minDist, param1, param2, minRadius, maxRadius);
        if (circles.Length == 0) return null;
        var data = new float[circles.Length * 3];
        for (int i = 0; i < circles.Length; i++) { data[3 * i] = circles[i].Center.X; data[3 * i + 1] = circles[i].Center.Y; data[3 * i + 2] = circles[i].Radius; }
        return NDArray.FromArray(data, 1, circles.Length, 3);
    }
}
