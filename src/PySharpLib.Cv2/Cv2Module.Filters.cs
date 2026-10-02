// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Image;
using PySharpLib.Numpy;
using PySharpLib.Runtime;

namespace PySharpLib.Cv2;

internal static partial class Cv2Module
{
    private static object Tuple(params object[] items) => new PyTuple(items);
    private static object Big(int v) => new BigInteger(v);

    private static void RegisterFilters(Action<string, BuiltinFn> Def)
    {
        // ------------------------------------------------------------ filters
        Def("filter2D", (i, a, k) =>
        {
            var p = new Args("filter2D", i, a, k, "src", "ddepth", "kernel", "dst", "anchor", "delta", "borderType");
            return A.Wrap(Cv.Filter2D(A.Image(p.Required(0)), p.Int(1, -1), A.Image(p.Required(2)), A.PairOpt(p[4]), p.Double(5, 0), p.Int(6, 4)));
        });
        Def("GaussianBlur", (i, a, k) =>
        {
            var p = new Args("GaussianBlur", i, a, k, "src", "ksize", "sigmaX", "dst", "sigmaY", "borderType");
            return A.Wrap(Cv.GaussianBlur(A.Image(p.Required(0)), A.Pair(p.Required(1)), p.Double(2, 0), p.Double(4, 0), p.Int(5, 4)));
        });
        Def("getGaussianKernel", (i, a, k) =>
        {
            var p = new Args("getGaussianKernel", i, a, k, "ksize", "sigma", "ktype");
            return A.Wrap(Cv.GetGaussianKernel(p.Int(0, 0), p.Double(1, 0), p.Int(2, 6)));
        });
        Def("boxFilter", (i, a, k) =>
        {
            var p = new Args("boxFilter", i, a, k, "src", "ddepth", "ksize", "dst", "anchor", "normalize", "borderType");
            return A.Wrap(Cv.BoxFilter(A.Image(p.Required(0)), p.Int(1, -1), A.Pair(p.Required(2)), A.PairOpt(p[4]), p.Bool(5, true), p.Int(6, 4)));
        });
        Def("blur", (i, a, k) =>
        {
            var p = new Args("blur", i, a, k, "src", "ksize", "dst", "anchor", "borderType");
            return A.Wrap(Cv.Blur(A.Image(p.Required(0)), A.Pair(p.Required(1)), A.PairOpt(p[3]), p.Int(4, 4)));
        });
        Def("medianBlur", (i, a, k) =>
        {
            var p = new Args("medianBlur", i, a, k, "src", "ksize", "dst");
            return A.Wrap(Cv.MedianBlur(A.Image(p.Required(0)), p.Int(1, 3)));
        });
        Def("bilateralFilter", (i, a, k) =>
        {
            var p = new Args("bilateralFilter", i, a, k, "src", "d", "sigmaColor", "sigmaSpace", "dst", "borderType");
            return A.Wrap(Cv.BilateralFilter(A.Image(p.Required(0)), p.Int(1, 0), p.Double(2, 0), p.Double(3, 0), p.Int(5, 4)));
        });

        // ------------------------------------------------------------ derivatives & edges
        Def("Sobel", (i, a, k) =>
        {
            var p = new Args("Sobel", i, a, k, "src", "ddepth", "dx", "dy", "dst", "ksize", "scale", "delta", "borderType");
            return A.Wrap(Cv.Sobel(A.Image(p.Required(0)), p.Int(1, -1), p.Int(2, 0), p.Int(3, 0), p.Int(5, 3), p.Double(6, 1), p.Double(7, 0), p.Int(8, 4)));
        });
        Def("Scharr", (i, a, k) =>
        {
            var p = new Args("Scharr", i, a, k, "src", "ddepth", "dx", "dy", "dst", "scale", "delta", "borderType");
            return A.Wrap(Cv.Scharr(A.Image(p.Required(0)), p.Int(1, -1), p.Int(2, 0), p.Int(3, 0), p.Double(5, 1), p.Double(6, 0), p.Int(7, 4)));
        });
        Def("Laplacian", (i, a, k) =>
        {
            var p = new Args("Laplacian", i, a, k, "src", "ddepth", "dst", "ksize", "scale", "delta", "borderType");
            return A.Wrap(Cv.Laplacian(A.Image(p.Required(0)), p.Int(1, -1), p.Int(3, 1), p.Double(4, 1), p.Double(5, 0), p.Int(6, 4)));
        });
        Def("Canny", (i, a, k) =>
        {
            var p = new Args("Canny", i, a, k, "image", "threshold1", "threshold2", "edges", "apertureSize", "L2gradient");
            return A.Wrap(Cv.Canny(A.Image(p.Required(0)), p.Double(1, 0), p.Double(2, 0), p.Int(4, 3), p.Bool(5, false)));
        });
        Def("pyrDown", (i, a, k) =>
        {
            var p = new Args("pyrDown", i, a, k, "src", "dst", "dstsize", "borderType");
            return A.Wrap(Cv.PyrDown(A.Image(p.Required(0)), A.PairOpt(p[2]), p.Int(3, 4)));
        });
        Def("pyrUp", (i, a, k) =>
        {
            var p = new Args("pyrUp", i, a, k, "src", "dst", "dstsize", "borderType");
            return A.Wrap(Cv.PyrUp(A.Image(p.Required(0)), A.PairOpt(p[2]), p.Int(3, 4)));
        });
        Def("getGaborKernel", (i, a, k) =>
        {
            var p = new Args("getGaborKernel", i, a, k, "ksize", "sigma", "theta", "lambd", "gamma", "psi", "ktype");
            return A.Wrap(Cv.GetGaborKernel(A.Pair(p.Required(0)), p.Double(1, 0), p.Double(2, 0), p.Double(3, 0), p.Double(4, 0), p.Double(5, Math.PI * 0.5), p.Int(6, 6)));
        });
        Def("cartToPolar", (i, a, k) =>
        {
            var p = new Args("cartToPolar", i, a, k, "x", "y", "magnitude", "angle", "angleInDegrees");
            var (mag, ang) = Cv.CartToPolar(A.Image(p.Required(0)), A.Image(p.Required(1)), p.Bool(4, false));
            return Tuple(A.Wrap(mag), A.Wrap(ang));
        });
        Def("dct", (i, a, k) =>
        {
            var p = new Args("dct", i, a, k, "src", "dst", "flags");
            return A.Wrap(Cv.Dct(A.Image(p.Required(0)), p.Int(2, 0)));
        });
        Def("idct", (i, a, k) =>
        {
            var p = new Args("idct", i, a, k, "src", "dst", "flags");
            return A.Wrap(Cv.Idct(A.Image(p.Required(0)), p.Int(2, 0)));
        });
        Def("distanceTransform", (i, a, k) =>
        {
            var p = new Args("distanceTransform", i, a, k, "src", "distanceType", "maskSize", "dst", "dstType");
            return A.Wrap(Cv.DistanceTransform(A.Image(p.Required(0)), p.Int(1, 2), p.Int(2, 3), p.Int(4, 5)));
        });

        // ------------------------------------------------------------ geometry
        Def("warpAffine", (i, a, k) =>
        {
            var p = new Args("warpAffine", i, a, k, "src", "M", "dsize", "dst", "flags", "borderMode", "borderValue");
            return A.Wrap(Cv.WarpAffine(A.Image(p.Required(0)), A.Image(p.Required(1)), A.Pair(p.Required(2)), p.Int(4, 1), p.Int(5, 0), A.Scalar(p[6])));
        });
        Def("warpPerspective", (i, a, k) =>
        {
            var p = new Args("warpPerspective", i, a, k, "src", "M", "dsize", "dst", "flags", "borderMode", "borderValue");
            return A.Wrap(Cv.WarpPerspective(A.Image(p.Required(0)), A.Image(p.Required(1)), A.Pair(p.Required(2)), p.Int(4, 1), p.Int(5, 0), A.Scalar(p[6])));
        });
        Def("getRotationMatrix2D", (i, a, k) =>
        {
            var p = new Args("getRotationMatrix2D", i, a, k, "center", "angle", "scale");
            var c = A.Doubles(p.Required(0));
            return A.Wrap(Cv.GetRotationMatrix2D((c[0], c[1]), p.Double(1, 0), p.Double(2, 1)));
        });
        Def("getAffineTransform", (i, a, k) =>
        {
            var p = new Args("getAffineTransform", i, a, k, "src", "dst");
            return A.Wrap(Cv.GetAffineTransform(A.Image(p.Required(0)), A.Image(p.Required(1))));
        });
        Def("getPerspectiveTransform", (i, a, k) =>
        {
            var p = new Args("getPerspectiveTransform", i, a, k, "src", "dst", "solveMethod");
            return A.Wrap(Cv.GetPerspectiveTransform(A.Image(p.Required(0)), A.Image(p.Required(1))));
        });
        Def("invertAffineTransform", (i, a, k) =>
        {
            var p = new Args("invertAffineTransform", i, a, k, "M", "iM");
            return A.Wrap(Cv.InvertAffineTransform(A.Image(p.Required(0))));
        });
        Def("perspectiveTransform", (i, a, k) =>
        {
            var p = new Args("perspectiveTransform", i, a, k, "src", "m", "dst");
            return A.Wrap(Cv.PerspectiveTransform(A.Image(p.Required(0)), A.Image(p.Required(1))));
        });

        // ------------------------------------------------------------ moments & contours
        Def("moments", (i, a, k) =>
        {
            var p = new Args("moments", i, a, k, "array", "binaryImage");
            var d = new PyDict();
            foreach (var (name, value) in Cv.Moments(A.Image(p.Required(0)), p.Bool(1, false)))
                d[name] = value;
            return d;
        });
        Def("HuMoments", (i, a, k) =>
        {
            var p = new Args("HuMoments", i, a, k, "m", "hu");
            var d = new Dictionary<string, double>();
            if (p.Required(0) is PyDict pd)
                foreach (var (key, value) in pd.Entries)
                    d[(string)key] = PyOps.AsDouble(value);
            return A.Wrap(Cv.HuMoments(d));
        });
        Def("findContours", (i, a, k) =>
        {
            var p = new Args("findContours", i, a, k, "image", "mode", "method", "contours", "hierarchy", "offset");
            var (contours, hierarchy) = Cv.FindContours(A.Image(p.Required(0)), p.Int(1, 0), p.Int(2, 1), A.PairOpt(p[5]));
            return Tuple(new PyTuple(contours.Select(c => A.Wrap(c)).ToArray()), contours.Length == 0 ? PyNone.Instance : A.Wrap(hierarchy));
        });
        Def("contourArea", (i, a, k) =>
        {
            var p = new Args("contourArea", i, a, k, "contour", "oriented");
            return Cv.ContourArea(A.Image(p.Required(0)), p.Bool(1, false));
        });
        Def("arcLength", (i, a, k) =>
        {
            var p = new Args("arcLength", i, a, k, "curve", "closed");
            return Cv.ArcLength(A.Image(p.Required(0)), p.Bool(1, false));
        });

        // ------------------------------------------------------------ corners & lines
        Def("cornerHarris", (i, a, k) =>
        {
            var p = new Args("cornerHarris", i, a, k, "src", "blockSize", "ksize", "k", "dst", "borderType");
            return A.Wrap(Cv.CornerHarris(A.Image(p.Required(0)), p.Int(1, 2), p.Int(2, 3), p.Double(3, 0.04), p.Int(5, 4)));
        });
        Def("cornerMinEigenVal", (i, a, k) =>
        {
            var p = new Args("cornerMinEigenVal", i, a, k, "src", "blockSize", "dst", "ksize", "borderType");
            return A.Wrap(Cv.CornerMinEigenVal(A.Image(p.Required(0)), p.Int(1, 3), p.Int(3, 3), p.Int(4, 4)));
        });
        Def("goodFeaturesToTrack", (i, a, k) =>
        {
            var p = new Args("goodFeaturesToTrack", i, a, k, "image", "maxCorners", "qualityLevel", "minDistance", "corners", "mask", "blockSize", "useHarrisDetector", "k");
            var r = Cv.GoodFeaturesToTrack(A.Image(p.Required(0)), p.Int(1, 0), p.Double(2, 0), p.Double(3, 0), A.ImageOpt(p[5]), p.Int(6, 3), p.Bool(7, false), p.Double(8, 0.04));
            return r.Size == 0 ? PyNone.Instance : A.Wrap(r);
        });
        Def("cornerSubPix", (i, a, k) =>
        {
            var p = new Args("cornerSubPix", i, a, k, "image", "corners", "winSize", "zeroZone", "criteria");
            var c = A.Doubles(p.Required(4));
            return A.Wrap(Cv.CornerSubPix(A.Image(p.Required(0)), A.Image(p.Required(1)), A.Pair(p.Required(2)), A.Pair(p.Required(3)), (int)c[0], (int)c[1], c[2]));
        });
        Def("HoughLines", (i, a, k) =>
        {
            var p = new Args("HoughLines", i, a, k, "image", "rho", "theta", "threshold", "lines", "srn", "stn", "min_theta", "max_theta");
            var r = Cv.HoughLines(A.Image(p.Required(0)), p.Double(1, 1), p.Double(2, Math.PI / 180), p.Int(3, 100), p.Double(5, 0), p.Double(6, 0));
            return r.Size == 0 ? PyNone.Instance : A.Wrap(r);
        });
        Def("HoughLinesP", (i, a, k) =>
        {
            var p = new Args("HoughLinesP", i, a, k, "image", "rho", "theta", "threshold", "lines", "minLineLength", "maxLineGap");
            var r = Cv.HoughLinesP(A.Image(p.Required(0)), p.Double(1, 1), p.Double(2, Math.PI / 180), p.Int(3, 100), p.Double(5, 0), p.Double(6, 0));
            return r.Size == 0 ? PyNone.Instance : A.Wrap(r);
        });
        Def("HoughCircles", (i, a, k) =>
        {
            var p = new Args("HoughCircles", i, a, k, "image", "method", "dp", "minDist", "circles", "param1", "param2", "minRadius", "maxRadius");
            var r = Cv.HoughCircles(A.Image(p.Required(0)), p.Int(1, 3), p.Double(2, 1), p.Double(3, 1), p.Double(5, 100), p.Double(6, 100), p.Int(7, 0), p.Int(8, 0));
            return r is null ? PyNone.Instance : A.Wrap(r);
        });
    }
}
