// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace NDSharp.Image;

/// <summary>A detected feature point (cv2.KeyPoint).</summary>
public sealed record KeyPoint2(float X, float Y, float Size, float Angle = -1, float Response = 0, int Octave = 0, int ClassId = -1);

/// <summary>A descriptor match (cv2.DMatch).</summary>
public readonly record struct DMatch2(int QueryIdx, int TrainIdx, int ImgIdx, float Distance);

public static partial class Cv
{
    internal static KeyPoint To(KeyPoint2 k) => new(k.X, k.Y, k.Size, k.Angle, k.Response, k.Octave, k.ClassId);
    internal static KeyPoint2 From(KeyPoint k) => new(k.Pt.X, k.Pt.Y, k.Size, k.Angle, k.Response, k.Octave, k.ClassId);
    private static DMatch To(DMatch2 m) => new(m.QueryIdx, m.TrainIdx, m.ImgIdx, m.Distance);
    private static DMatch2 From(DMatch m) => new(m.QueryIdx, m.TrainIdx, m.ImgIdx, m.Distance);

    // ================================================================ SIFT & matching

    /// <summary>cv2.SIFT_create.</summary>
    public sealed class Sift : IDisposable
    {
        private readonly SIFT _sift;

        public Sift(int nFeatures = 0, int nOctaveLayers = 3, double contrastThreshold = 0.04, double edgeThreshold = 10, double sigma = 1.6)
            => _sift = SIFT.Create(nFeatures, nOctaveLayers, contrastThreshold, edgeThreshold, sigma);

        public (KeyPoint2[] keypoints, NDArray descriptors) DetectAndCompute(NDArray image, NDArray? mask = null)
        {
            using var img = Mats.ToMat(image);
            using var mk = Opt(mask);
            using var desc = new Mat();
            _sift.DetectAndCompute(img, mk, out var kps, desc);
            return (kps.Select(From).ToArray(), desc.Empty() ? np.Zeros(new[] { 0, 128 }, DType.Float32) : Mats.FromMat(desc));
        }

        public KeyPoint2[] Detect(NDArray image, NDArray? mask = null)
        {
            using var img = Mats.ToMat(image);
            using var mk = Opt(mask);
            return _sift.Detect(img, mk).Select(From).ToArray();
        }

        public (KeyPoint2[] keypoints, NDArray descriptors) Compute(NDArray image, KeyPoint2[] keypoints)
        {
            using var img = Mats.ToMat(image);
            using var desc = new Mat();
            var kps = keypoints.Select(To).ToArray();
            _sift.Compute(img, ref kps, desc);
            return (kps.Select(From).ToArray(), Mats.FromMat(desc));
        }

        public void Dispose() => _sift.Dispose();
    }

    /// <summary>cv2.BFMatcher.</summary>
    public sealed class BfMatcher : IDisposable
    {
        private readonly BFMatcher _bf;

        public BfMatcher(int normType = (int)NormTypes.L2, bool crossCheck = false)
            => _bf = new BFMatcher((NormTypes)normType, crossCheck);

        public DMatch2[] Match(NDArray query, NDArray train, NDArray? mask = null)
        {
            using var q = Mats.ToMat(query);
            using var t = Mats.ToMat(train);
            using var mk = Opt(mask);
            return _bf.Match(q, t, mk).Select(From).ToArray();
        }

        public DMatch2[][] KnnMatch(NDArray query, NDArray train, int k, NDArray? mask = null)
        {
            using var q = Mats.ToMat(query);
            using var t = Mats.ToMat(train);
            using var mk = Opt(mask);
            return _bf.KnnMatch(q, t, k, mk).Select(r => r.Select(From).ToArray()).ToArray();
        }

        public void Dispose() => _bf.Dispose();
    }

    public static NDArray DrawKeypoints(NDArray image, IReadOnlyList<KeyPoint2> keypoints, double[]? color = null, int flags = 0)
    {
        using var img = Mats.ToMat(image);
        using var d = new Mat();
        Cv2.DrawKeypoints(img, keypoints.Select(To), d, color is null ? Scalar.All(-1) : Mats.ToScalar(color), (DrawMatchesFlags)flags);
        return Mats.FromMat(d);
    }

    public static NDArray DrawMatches(NDArray img1, IReadOnlyList<KeyPoint2> kp1, NDArray img2, IReadOnlyList<KeyPoint2> kp2,
        IReadOnlyList<DMatch2> matches, double[]? matchColor = null, double[]? singlePointColor = null, byte[]? matchesMask = null, int flags = 0)
    {
        using var a = Mats.ToMat(img1);
        using var b = Mats.ToMat(img2);
        using var d = new Mat();
        Cv2.DrawMatches(a, kp1.Select(To), b, kp2.Select(To), matches.Select(To), d,
            matchColor is null ? Scalar.All(-1) : Mats.ToScalar(matchColor),
            singlePointColor is null ? Scalar.All(-1) : Mats.ToScalar(singlePointColor),
            matchesMask, (DrawMatchesFlags)flags);
        return Mats.FromMat(d);
    }

    // ================================================================ optical flow & stereo

    public static NDArray CalcOpticalFlowFarneback(NDArray prev, NDArray next, double pyrScale, int levels, int winSize, int iterations, int polyN, double polySigma, int flags = 0)
    {
        using var p = Mats.ToMat(prev);
        using var n = Mats.ToMat(next);
        using var flow = new Mat();
        Cv2.CalcOpticalFlowFarneback(p, n, flow, pyrScale, levels, winSize, iterations, polyN, polySigma, (OpticalFlowFlags)flags);
        return Mats.FromMat(flow);
    }

    /// <summary>cv2.calcOpticalFlowPyrLK: (nextPts (N,1,2) float32, status (N,1) uint8, err (N,1) float32).</summary>
    public static (NDArray nextPts, NDArray status, NDArray err) CalcOpticalFlowPyrLK(NDArray prev, NDArray next, NDArray prevPts, NDArray? nextPts,
        (int w, int h) winSize, int maxLevel, int critType, int critMaxCount, double critEps, int flags = 0, double minEigThreshold = 1e-4)
    {
        using var p = Mats.ToMat(prev);
        using var n = Mats.ToMat(next);
        using var pp = Mats.ToMat(prevPts.AsType(DType.Float32));
        using var np2 = nextPts is null ? new Mat() : Mats.ToMat(nextPts.AsType(DType.Float32));
        using var status = new Mat();
        using var err = new Mat();
        Cv2.CalcOpticalFlowPyrLK(p, n, pp, np2, status, err, S(winSize), maxLevel, new TermCriteria((CriteriaTypes)critType, critMaxCount, critEps), (OpticalFlowFlags)flags, minEigThreshold);
        return (Mats.FromMat(np2), Mats.FromMat(status), Mats.FromMat(err));
    }

    /// <summary>cv2.StereoBM_create.</summary>
    public sealed class StereoBlockMatcher : IDisposable
    {
        private readonly StereoBM _bm;

        public StereoBlockMatcher(int numDisparities = 0, int blockSize = 21) => _bm = StereoBM.Create(numDisparities, blockSize);

        /// <summary>Disparity map as int16 fixed point (divide by 16).</summary>
        public NDArray Compute(NDArray left, NDArray right)
        {
            using var l = Mats.ToMat(left);
            using var r = Mats.ToMat(right);
            using var d = new Mat();
            _bm.Compute(l, r, d);
            return Mats.FromMat(d);
        }

        /// <summary>Reads/writes the OpenCV property named <paramref name="name"/> (BlockSize, NumDisparities, Disp12MaxDiff, ...).</summary>
        public object? Get(string name) => typeof(StereoBM).GetProperty(name)?.GetValue(_bm) ?? throw new NDValueException($"unknown StereoBM property {name}");
        public void Set(string name, int value)
        {
            var prop = typeof(StereoBM).GetProperty(name) ?? throw new NDValueException($"unknown StereoBM property {name}");
            prop.SetValue(_bm, Convert.ChangeType(value, prop.PropertyType));
        }

        public void Dispose() => _bm.Dispose();
    }

    /// <summary>cv2.Stitcher_create.</summary>
    public sealed class ImageStitcher : IDisposable
    {
        private readonly Stitcher _st;
        public ImageStitcher(int mode = 0) => _st = Stitcher.Create((Stitcher.Mode)mode);

        public (int status, NDArray? pano) Stitch(IReadOnlyList<NDArray> images)
        {
            var mats = images.Select(Mats.ToMat).ToArray();
            try
            {
                using var pano = new Mat();
                var status = _st.Stitch(mats, pano);
                return ((int)status, status == Stitcher.Status.OK ? Mats.FromMat(pano) : null);
            }
            finally { foreach (var m in mats) m.Dispose(); }
        }

        public void Dispose() => _st.Dispose();
    }

    /// <summary>cv2.CascadeClassifier.</summary>
    public sealed class Cascade : IDisposable
    {
        private readonly CascadeClassifier _c;
        public Cascade(string path) => _c = new CascadeClassifier(path);
        public bool Empty => _c.Empty();

        /// <summary>(N, 4) int32 rectangles (x, y, w, h).</summary>
        public NDArray DetectMultiScale(NDArray image, double scaleFactor = 1.1, int minNeighbors = 3, int flags = 0, (int w, int h)? minSize = null, (int w, int h)? maxSize = null)
        {
            using var img = Mats.ToMat(image);
            var rects = _c.DetectMultiScale(img, scaleFactor, minNeighbors, (HaarDetectionTypes)flags, minSize is { } a ? S(a) : null, maxSize is { } b ? S(b) : null);
            var data = new int[rects.Length * 4];
            for (int i = 0; i < rects.Length; i++) { data[4 * i] = rects[i].X; data[4 * i + 1] = rects[i].Y; data[4 * i + 2] = rects[i].Width; data[4 * i + 3] = rects[i].Height; }
            return NDArray.FromArray(data, rects.Length, 4);
        }

        public void Dispose() => _c.Dispose();
    }

    // ================================================================ calib3d

    private static (NDArray, NDArray?) WithMask(Func<Mat> compute, Mat mask)
    {
        using var m = compute();
        return (Mats.FromMat(m), mask.Empty() ? null : Mats.FromMat(mask));
    }

    /// <summary>cv2.findHomography: (H, mask) — H is null when estimation fails.</summary>
    public static (NDArray? h, NDArray? mask) FindHomography(NDArray src, NDArray dst, int method = 0, double ransacReprojThreshold = 3, int maxIters = 2000, double confidence = 0.995)
    {
        using var s = Mats.ToMat(src.AsType(DType.Float32));
        using var d = Mats.ToMat(dst.AsType(DType.Float32));
        using var mask = new Mat();
        using var h = Cv2.FindHomography(s, d, (HomographyMethods)method, ransacReprojThreshold, mask, maxIters, confidence);
        return (h.Empty() ? null : Mats.FromMat(h), mask.Empty() ? null : Mats.FromMat(mask));
    }

    public static (NDArray? e, NDArray? mask) FindEssentialMat(NDArray p1, NDArray p2, NDArray cameraMatrix, int method = (int)EssentialMatMethod.Ransac, double prob = 0.999, double threshold = 1.0)
    {
        using var a = Mats.ToMat(p1);
        using var b = Mats.ToMat(p2);
        using var k = Mats.ToMat(cameraMatrix);
        using var mask = new Mat();
        using var e = Cv2.FindEssentialMat(a, b, k, (EssentialMatMethod)method, prob, threshold, mask);
        return (e.Empty() ? null : Mats.FromMat(e), mask.Empty() ? null : Mats.FromMat(mask));
    }

    public static (int inliers, NDArray r, NDArray t, NDArray? mask) RecoverPose(NDArray e, NDArray p1, NDArray p2, NDArray cameraMatrix, NDArray? mask = null)
    {
        using var em = Mats.ToMat(e);
        using var a = Mats.ToMat(p1);
        using var b = Mats.ToMat(p2);
        using var k = Mats.ToMat(cameraMatrix);
        using var r = new Mat();
        using var t = new Mat();
        using var mk = mask is null ? new Mat() : Mats.ToMat(mask);
        int n = Cv2.RecoverPose(em, a, b, k, r, t, mk);
        return (n, Mats.FromMat(r), Mats.FromMat(t), mk.Empty() ? null : Mats.FromMat(mk));
    }

    public static (NDArray? f, NDArray? mask) FindFundamentalMat(NDArray p1, NDArray p2, int method = (int)FundamentalMatMethods.Ransac, double ransacReprojThreshold = 3, double confidence = 0.99)
    {
        using var a = Mats.ToMat(p1);
        using var b = Mats.ToMat(p2);
        using var mask = new Mat();
        using var f = Cv2.FindFundamentalMat(a, b, (FundamentalMatMethods)method, ransacReprojThreshold, confidence, mask);
        return (f.Empty() ? null : Mats.FromMat(f), mask.Empty() ? null : Mats.FromMat(mask));
    }

    /// <summary>cv2.Rodrigues: rotation vector ↔ matrix (and the Jacobian).</summary>
    public static (NDArray dst, NDArray jacobian) Rodrigues(NDArray src)
    {
        using var s = Mats.ToMat(src);
        using var d = new Mat();
        using var j = new Mat();
        Cv2.Rodrigues(s, d, j);
        return (Mats.FromMat(d), Mats.FromMat(j));
    }

    public static NDArray TriangulatePoints(NDArray proj1, NDArray proj2, NDArray pts1, NDArray pts2)
    {
        using var a = Mats.ToMat(proj1);
        using var b = Mats.ToMat(proj2);
        using var p1 = Mats.ToMat(pts1);
        using var p2 = Mats.ToMat(pts2);
        using var r = new Mat();
        Cv2.TriangulatePoints(a, b, p1, p2, r);
        return Mats.FromMat(r);
    }

    public sealed record Calibration(double Rms, NDArray CameraMatrix, NDArray DistCoeffs, NDArray[] Rvecs, NDArray[] Tvecs);

    public static Calibration CalibrateCamera(IReadOnlyList<NDArray> objectPoints, IReadOnlyList<NDArray> imagePoints, (int w, int h) imageSize,
        NDArray? cameraMatrix = null, NDArray? distCoeffs = null, int flags = 0, int critType = 3, int critMaxCount = 30, double critEps = double.Epsilon)
    {
        var obj = objectPoints.Select(o => Mats.ToMat(o.AsType(DType.Float32))).ToArray();
        var img = imagePoints.Select(o => Mats.ToMat(o.AsType(DType.Float32))).ToArray();
        using var k = cameraMatrix is null ? new Mat() : Mats.ToMat(cameraMatrix);
        using var dc = distCoeffs is null ? new Mat() : Mats.ToMat(distCoeffs);
        try
        {
            var crit = new TermCriteria((CriteriaTypes)critType, critMaxCount, critEps == double.Epsilon ? 2.220446049250313e-16 : critEps);
            double rms = Cv2.CalibrateCamera(obj, img, S(imageSize), k, dc, out var rvecs, out var tvecs, (CalibrationFlags)flags, crit);
            return new Calibration(rms, Mats.FromMat(k), Mats.FromMat(dc), rvecs.Select(Mats.FromMat).ToArray(), tvecs.Select(Mats.FromMat).ToArray());
        }
        finally { foreach (var m in obj.Concat(img)) m.Dispose(); }
    }

    /// <summary>cv2.findChessboardCorners: (found, (N,1,2) float32 corners).</summary>
    public static (bool found, NDArray corners) FindChessboardCorners(NDArray image, (int w, int h) patternSize, int flags = (int)ChessboardFlags.AdaptiveThresh | (int)ChessboardFlags.NormalizeImage)
    {
        using var img = Mats.ToMat(image);
        using var corners = new Mat();
        bool found = Cv2.FindChessboardCorners(img, S(patternSize), corners, (ChessboardFlags)flags);
        return (found, corners.Empty() ? np.Zeros(new[] { 0, 1, 2 }, DType.Float32) : Mats.FromMat(corners));
    }

    public static NDArray DrawChessboardCorners(NDArray image, (int w, int h) patternSize, NDArray corners, bool found)
        => Draw(image, m => { using var c = Mats.ToMat(corners.AsType(DType.Float32)); Cv2.DrawChessboardCorners(m, S(patternSize), c, found); });

    public static (NDArray imagePoints, NDArray? jacobian) ProjectPoints(NDArray objectPoints, NDArray rvec, NDArray tvec, NDArray cameraMatrix, NDArray? distCoeffs, double aspectRatio = 0)
    {
        using var o = Mats.ToMat(objectPoints);
        using var r = Mats.ToMat(rvec);
        using var t = Mats.ToMat(tvec);
        using var k = Mats.ToMat(cameraMatrix);
        using var dc = distCoeffs is null ? new Mat() : Mats.ToMat(distCoeffs);
        using var ip = new Mat();
        using var jac = new Mat();
        Cv2.ProjectPoints(o, r, t, k, dc, ip, jac, aspectRatio);
        return (Mats.FromMat(ip), jac.Empty() ? null : Mats.FromMat(jac));
    }

    public static NDArray UndistortPoints(NDArray src, NDArray cameraMatrix, NDArray? distCoeffs, NDArray? r = null, NDArray? p = null)
    {
        using var s = Mats.ToMat(src);
        using var k = Mats.ToMat(cameraMatrix);
        using var dc = distCoeffs is null ? new Mat() : Mats.ToMat(distCoeffs);
        using var rr = Opt(r);
        using var pp = Opt(p);
        using var d = new Mat();
        Cv2.UndistortPoints(s, d, k, dc, rr, pp);
        return Mats.FromMat(d);
    }
}
