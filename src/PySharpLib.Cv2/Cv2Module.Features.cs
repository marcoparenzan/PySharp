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
    // Python classes for the objects cv2 hands back. Their payload lives in PyInstance.Native.
    private static readonly PyClass KeyPointClass = BuildKeyPointClass();
    private static readonly PyClass DMatchClass = BuildDMatchClass();

    private static PyInstance WrapKeyPoint(KeyPoint2 k) => new(KeyPointClass) { Native = k };
    private static PyInstance WrapDMatch(DMatch2 m) => new(DMatchClass) { Native = m };

    private static KeyPoint2 KeyPointOf(object o) => o is PyInstance { Native: KeyPoint2 k } ? k : throw PyErr.TypeError("expected a cv2.KeyPoint");
    private static DMatch2 DMatchOf(object o) => o is PyInstance { Native: DMatch2 m } ? m : throw PyErr.TypeError("expected a cv2.DMatch");

    private static List<KeyPoint2> KeyPoints(Interpretation.Interp interp, object o) => PyOps.Iterate(interp, o).Select(KeyPointOf).ToList();
    private static List<DMatch2> DMatches(Interpretation.Interp interp, object o) => PyOps.Iterate(interp, o).Select(DMatchOf).ToList();

    private static PyClass BuildKeyPointClass()
    {
        var cls = new PyClass("KeyPoint", new List<PyClass>());
        cls.Dict["__new__"] = Fn("KeyPoint.__new__", (i, a, k) =>
        {
            var p = new Args("KeyPoint", i, a.Skip(1).ToArray(), k, "x", "y", "size", "angle", "response", "octave", "class_id");
            return new PyInstance(cls) { Native = new KeyPoint2((float)p.Double(0, 0), (float)p.Double(1, 0), (float)p.Double(2, 0), (float)p.Double(3, -1), (float)p.Double(4, 0), p.Int(5, 0), p.Int(6, -1)) };
        });
        cls.Dict["__init__"] = new PyBuiltinFunction("KeyPoint.__init__", (_, _, _) => PyNone.Instance);
        PyProperty Prop(Func<KeyPoint2, object> f) => new() { Getter = new PyBuiltinFunction("<prop>", (_, a, _) => f(((KeyPoint2)((PyInstance)a[0]).Native!))) };
        cls.Dict["pt"] = Prop(k => new PyTuple(new object[] { (double)k.X, (double)k.Y }));
        cls.Dict["size"] = Prop(k => (double)k.Size);
        cls.Dict["angle"] = Prop(k => (double)k.Angle);
        cls.Dict["response"] = Prop(k => (double)k.Response);
        cls.Dict["octave"] = Prop(k => Big(k.Octave));
        cls.Dict["class_id"] = Prop(k => Big(k.ClassId));
        cls.Dict["__repr__"] = new PyBuiltinFunction("KeyPoint.__repr__", (_, a, _) =>
        {
            var k = (KeyPoint2)((PyInstance)a[0]).Native!;
            return $"<cv2.KeyPoint {k.X},{k.Y} size={k.Size}>";
        });
        return cls;
    }

    private static PyClass BuildDMatchClass()
    {
        var cls = new PyClass("DMatch", new List<PyClass>());
        cls.Dict["__new__"] = Fn("DMatch.__new__", (i, a, k) =>
        {
            var p = new Args("DMatch", i, a.Skip(1).ToArray(), k, "_queryIdx", "_trainIdx", "_imgIdx", "_distance");
            return new PyInstance(cls) { Native = new DMatch2(p.Int(0, 0), p.Int(1, 0), p.Has(3) ? p.Int(2, 0) : 0, (float)p.Double(p.Has(3) ? 3 : 2, 0)) };
        });
        cls.Dict["__init__"] = new PyBuiltinFunction("DMatch.__init__", (_, _, _) => PyNone.Instance);
        PyProperty Prop(Func<DMatch2, object> f) => new() { Getter = new PyBuiltinFunction("<prop>", (_, a, _) => f((DMatch2)((PyInstance)a[0]).Native!)) };
        cls.Dict["queryIdx"] = Prop(m => Big(m.QueryIdx));
        cls.Dict["trainIdx"] = Prop(m => Big(m.TrainIdx));
        cls.Dict["imgIdx"] = Prop(m => Big(m.ImgIdx));
        cls.Dict["distance"] = Prop(m => (double)m.Distance);
        cls.Dict["__repr__"] = new PyBuiltinFunction("DMatch.__repr__", (_, a, _) =>
        {
            var m = (DMatch2)((PyInstance)a[0]).Native!;
            return $"<cv2.DMatch {m.QueryIdx}->{m.TrainIdx} d={m.Distance}>";
        });
        return cls;
    }

    private static PyClass NewObjectClass(string name) => new(name, new List<PyClass>());

    private static NDArray? Mask(object? o) => A.ImageOpt(o);

    private static void RegisterFeatures(PyModule m, Action<string, BuiltinFn> Def)
    {
        m.Dict["KeyPoint"] = KeyPointClass;
        m.Dict["DMatch"] = DMatchClass;
        Def("KeyPoint_convert", (i, a, k) => throw PyErr.NotImplementedError("cv2.KeyPoint_convert is not implemented"));

        // ------------------------------------------------------------ SIFT
        var siftClass = NewObjectClass("SIFT");
        Cv.Sift S(object o) => (Cv.Sift)((PyInstance)o).Native!;
        siftClass.Dict["detectAndCompute"] = Fn("SIFT.detectAndCompute", (i, a, k) =>
        {
            var p = new Args("detectAndCompute", i, a.Skip(1).ToArray(), k, "image", "mask", "descriptors", "useProvidedKeypoints");
            var (kps, desc) = S(a[0]).DetectAndCompute(A.Image(p.Required(0)), Mask(p[1]));
            return Tuple(new PyTuple(kps.Select(x => (object)WrapKeyPoint(x)).ToArray()), kps.Length == 0 ? PyNone.Instance : A.Wrap(desc));
        });
        siftClass.Dict["detect"] = Fn("SIFT.detect", (i, a, k) =>
        {
            var p = new Args("detect", i, a.Skip(1).ToArray(), k, "image", "mask");
            return new PyTuple(S(a[0]).Detect(A.Image(p.Required(0)), Mask(p[1])).Select(x => (object)WrapKeyPoint(x)).ToArray());
        });
        siftClass.Dict["compute"] = Fn("SIFT.compute", (i, a, k) =>
        {
            var p = new Args("compute", i, a.Skip(1).ToArray(), k, "image", "keypoints", "descriptors");
            var (kps, desc) = S(a[0]).Compute(A.Image(p.Required(0)), KeyPoints(i, p.Required(1)).ToArray());
            return Tuple(new PyTuple(kps.Select(x => (object)WrapKeyPoint(x)).ToArray()), A.Wrap(desc));
        });
        m.Dict["SIFT"] = siftClass;
        Def("SIFT_create", (i, a, k) =>
        {
            var p = new Args("SIFT_create", i, a, k, "nfeatures", "nOctaveLayers", "contrastThreshold", "edgeThreshold", "sigma");
            return new PyInstance(siftClass) { Native = new Cv.Sift(p.Int(0, 0), p.Int(1, 3), p.Double(2, 0.04), p.Double(3, 10), p.Double(4, 1.6)) };
        });

        // ------------------------------------------------------------ BFMatcher
        var bfClass = NewObjectClass("BFMatcher");
        Cv.BfMatcher B(object o) => (Cv.BfMatcher)((PyInstance)o).Native!;
        bfClass.Dict["__new__"] = Fn("BFMatcher.__new__", (i, a, k) =>
        {
            var p = new Args("BFMatcher", i, a.Skip(1).ToArray(), k, "normType", "crossCheck");
            return new PyInstance(bfClass) { Native = new Cv.BfMatcher(p.Int(0, 4), p.Bool(1, false)) };
        });
        bfClass.Dict["__init__"] = new PyBuiltinFunction("BFMatcher.__init__", (_, _, _) => PyNone.Instance);
        bfClass.Dict["match"] = Fn("BFMatcher.match", (i, a, k) =>
        {
            var p = new Args("match", i, a.Skip(1).ToArray(), k, "queryDescriptors", "trainDescriptors", "mask");
            return new PyTuple(B(a[0]).Match(A.Image(p.Required(0)), A.Image(p.Required(1)), Mask(p[2])).Select(x => (object)WrapDMatch(x)).ToArray());
        });
        bfClass.Dict["knnMatch"] = Fn("BFMatcher.knnMatch", (i, a, k) =>
        {
            var p = new Args("knnMatch", i, a.Skip(1).ToArray(), k, "queryDescriptors", "trainDescriptors", "k", "mask", "compactResult");
            var r = B(a[0]).KnnMatch(A.Image(p.Required(0)), A.Image(p.Required(1)), p.Int(2, 1), Mask(p[3]));
            return new PyTuple(r.Select(row => (object)new PyTuple(row.Select(x => (object)WrapDMatch(x)).ToArray())).ToArray());
        });
        m.Dict["BFMatcher"] = bfClass;

        Def("drawKeypoints", (i, a, k) =>
        {
            var p = new Args("drawKeypoints", i, a, k, "image", "keypoints", "outImage", "color", "flags");
            return A.Wrap(Cv.DrawKeypoints(A.Image(p.Required(0)), KeyPoints(i, p.Required(1)), A.Scalar(p[3]), p.Int(4, 0)));
        });
        Def("drawMatches", (i, a, k) =>
        {
            var p = new Args("drawMatches", i, a, k, "img1", "keypoints1", "img2", "keypoints2", "matches1to2", "outImg", "matchColor", "singlePointColor", "matchesMask", "flags");
            byte[]? mask = p.Has(8) ? A.Doubles(p[8]!).Select(x => (byte)x).ToArray() : null;
            return A.Wrap(Cv.DrawMatches(A.Image(p.Required(0)), KeyPoints(i, p.Required(1)), A.Image(p.Required(2)), KeyPoints(i, p.Required(3)),
                DMatches(i, p.Required(4)), A.Scalar(p[6]), A.Scalar(p[7]), mask, p.Int(9, 0)));
        });

        // ------------------------------------------------------------ optical flow & stereo
        Def("calcOpticalFlowFarneback", (i, a, k) =>
        {
            var p = new Args("calcOpticalFlowFarneback", i, a, k, "prev", "next", "flow", "pyr_scale", "levels", "winsize", "iterations", "poly_n", "poly_sigma", "flags");
            return A.Wrap(Cv.CalcOpticalFlowFarneback(A.Image(p.Required(0)), A.Image(p.Required(1)), p.Double(3, 0.5), p.Int(4, 3), p.Int(5, 15), p.Int(6, 3), p.Int(7, 5), p.Double(8, 1.2), p.Int(9, 0)));
        });
        Def("calcOpticalFlowPyrLK", (i, a, k) =>
        {
            var p = new Args("calcOpticalFlowPyrLK", i, a, k, "prevImg", "nextImg", "prevPts", "nextPts", "status", "err", "winSize", "maxLevel", "criteria", "flags", "minEigThreshold");
            var win = p.Has(6) ? A.Pair(p[6]!) : (21, 21);
            var crit = p.Has(8) ? A.Doubles(p[8]!) : new double[] { 3, 30, 0.01 };
            var (next, status, err) = Cv.CalcOpticalFlowPyrLK(A.Image(p.Required(0)), A.Image(p.Required(1)), A.Image(p.Required(2)), A.ImageOpt(p[3]), win, p.Int(7, 3), (int)crit[0], (int)crit[1], crit[2], p.Int(9, 0), p.Double(10, 1e-4));
            return Tuple(A.Wrap(next), A.Wrap(status), A.Wrap(err));
        });

        var stereoClass = NewObjectClass("StereoBM");
        Cv.StereoBlockMatcher SB(object o) => (Cv.StereoBlockMatcher)((PyInstance)o).Native!;
        stereoClass.Dict["compute"] = Fn("StereoBM.compute", (i, a, k) =>
        {
            var p = new Args("compute", i, a.Skip(1).ToArray(), k, "left", "right", "disparity");
            return A.Wrap(SB(a[0]).Compute(A.Image(p.Required(0)), A.Image(p.Required(1))));
        });
        foreach (var prop in new[] { "BlockSize", "NumDisparities", "MinDisparity", "SpeckleRange", "SpeckleWindowSize", "Disp12MaxDiff", "PreFilterType", "PreFilterSize", "PreFilterCap", "TextureThreshold", "UniquenessRatio", "SmallerBlockSize" })
        {
            string name = prop;
            stereoClass.Dict["set" + name] = Fn("StereoBM.set" + name, (i, a, k) => { SB(a[0]).Set(name, Conv.ToInt(a[1], name)); return PyNone.Instance; });
            stereoClass.Dict["get" + name] = Fn("StereoBM.get" + name, (i, a, k) => Convert.ToDouble(SB(a[0]).Get(name)) is var d && d == Math.Floor(d) ? Big((int)d) : d);
        }
        m.Dict["StereoBM"] = stereoClass;
        Def("StereoBM_create", (i, a, k) =>
        {
            var p = new Args("StereoBM_create", i, a, k, "numDisparities", "blockSize");
            return new PyInstance(stereoClass) { Native = new Cv.StereoBlockMatcher(p.Int(0, 0), p.Int(1, 21)) };
        });

        // ------------------------------------------------------------ FaceDetectorYN (YuNet)
        var yuClass = NewObjectClass("FaceDetectorYN");
        Cv.FaceDetectorYuNet YU(object o) => (Cv.FaceDetectorYuNet)((PyInstance)o).Native!;
        yuClass.Dict["detect"] = Fn("FaceDetectorYN.detect", (i, a, k) =>
        {
            var p = new Args("detect", i, a.Skip(1).ToArray(), k, "image", "faces");
            var faces = YU(a[0]).Detect(A.Image(p.Required(0)));
            return Tuple(Big(1), faces is null ? PyNone.Instance : A.Wrap(faces));
        });
        yuClass.Dict["setInputSize"] = Fn("FaceDetectorYN.setInputSize", (i, a, k) => { var (w, h) = A.Pair(a[1]); YU(a[0]).SetInputSize(w, h); return PyNone.Instance; });
        yuClass.Dict["getInputSize"] = Fn("FaceDetectorYN.getInputSize", (i, a, k) => Tuple(Big(YU(a[0]).InputWidth), Big(YU(a[0]).InputHeight)));
        yuClass.Dict["setScoreThreshold"] = Fn("FaceDetectorYN.setScoreThreshold", (i, a, k) => { YU(a[0]).ScoreThreshold = (float)PyOps.AsDouble(a[1]); return PyNone.Instance; });
        yuClass.Dict["getScoreThreshold"] = Fn("FaceDetectorYN.getScoreThreshold", (i, a, k) => (double)YU(a[0]).ScoreThreshold);
        yuClass.Dict["setNMSThreshold"] = Fn("FaceDetectorYN.setNMSThreshold", (i, a, k) => { YU(a[0]).NmsThreshold = (float)PyOps.AsDouble(a[1]); return PyNone.Instance; });
        yuClass.Dict["getNMSThreshold"] = Fn("FaceDetectorYN.getNMSThreshold", (i, a, k) => (double)YU(a[0]).NmsThreshold);
        yuClass.Dict["setTopK"] = Fn("FaceDetectorYN.setTopK", (i, a, k) => { YU(a[0]).TopK = Conv.ToInt(a[1], "top_k"); return PyNone.Instance; });
        yuClass.Dict["getTopK"] = Fn("FaceDetectorYN.getTopK", (i, a, k) => Big(YU(a[0]).TopK));
        m.Dict["FaceDetectorYN"] = yuClass;
        BuiltinFn createYu = (i, a, k) =>
        {
            var p = new Args("FaceDetectorYN_create", i, a, k, "model", "config", "input_size", "score_threshold", "nms_threshold", "top_k", "backend_id", "target_id");
            var (w, h) = A.Pair(p.Required(2));
            string model = p.Required(0) is string ms ? ms : PyOps.Str(i, p.Required(0));
            return new PyInstance(yuClass) { Native = new Cv.FaceDetectorYuNet(model, w, h, (float)p.Double(3, 0.9), (float)p.Double(4, 0.3), p.Int(5, 5000)) };
        };
        Def("FaceDetectorYN_create", createYu);
        yuClass.Dict["create"] = new PyStaticMethod(Fn("FaceDetectorYN.create", createYu));

        // ------------------------------------------------------------ stitching & cascades
        var stitcherClass = NewObjectClass("Stitcher");
        stitcherClass.Dict["stitch"] = Fn("Stitcher.stitch", (i, a, k) =>
        {
            var p = new Args("stitch", i, a.Skip(1).ToArray(), k, "images", "pano");
            var images = PyOps.Iterate(i, p.Required(0)).Select(A.Image).ToList();
            var (status, pano) = ((Cv.ImageStitcher)((PyInstance)a[0]).Native!).Stitch(images);
            return Tuple(Big(status), pano is null ? PyNone.Instance : A.Wrap(pano));
        });
        m.Dict["Stitcher"] = stitcherClass;
        Def("Stitcher_create", (i, a, k) =>
        {
            var p = new Args("Stitcher_create", i, a, k, "mode");
            return new PyInstance(stitcherClass) { Native = new Cv.ImageStitcher(p.Int(0, 0)) };
        });

        var cascadeClass = NewObjectClass("CascadeClassifier");
        Cv.Cascade C(object o) => (Cv.Cascade)((PyInstance)o).Native!;
        cascadeClass.Dict["__new__"] = Fn("CascadeClassifier.__new__", (i, a, k) =>
        {
            var p = new Args("CascadeClassifier", i, a.Skip(1).ToArray(), k, "filename");
            return new PyInstance(cascadeClass) { Native = new Cv.Cascade((string)p.Required(0)) };
        });
        cascadeClass.Dict["__init__"] = new PyBuiltinFunction("CascadeClassifier.__init__", (_, _, _) => PyNone.Instance);
        cascadeClass.Dict["empty"] = Fn("CascadeClassifier.empty", (i, a, k) => C(a[0]).Empty);
        cascadeClass.Dict["detectMultiScale"] = Fn("CascadeClassifier.detectMultiScale", (i, a, k) =>
        {
            var p = new Args("detectMultiScale", i, a.Skip(1).ToArray(), k, "image", "scaleFactor", "minNeighbors", "flags", "minSize", "maxSize");
            var r = C(a[0]).DetectMultiScale(A.Image(p.Required(0)), p.Double(1, 1.1), p.Int(2, 3), p.Int(3, 0), A.PairOpt(p[4]), A.PairOpt(p[5]));
            return r.Shape[0] == 0 ? PyTuple.Empty : A.Wrap(r);
        });
        m.Dict["CascadeClassifier"] = cascadeClass;
        var data = new PyModule("cv2.data");
        data.Dict["haarcascades"] = Path.Combine(AppContext.BaseDirectory, "data", "haarcascades") + Path.DirectorySeparatorChar;
        m.Dict["data"] = data;

        // ------------------------------------------------------------ calib3d
        (NDArray, NDArray) Pts(Args p) => (A.Image(p.Required(0)), A.Image(p.Required(1)));
        Def("findHomography", (i, a, k) =>
        {
            var p = new Args("findHomography", i, a, k, "srcPoints", "dstPoints", "method", "ransacReprojThreshold", "mask", "maxIters", "confidence");
            var (s, d) = Pts(p);
            var (h, mask) = Cv.FindHomography(s, d, p.Int(2, 0), p.Double(3, 3), p.Int(5, 2000), p.Double(6, 0.995));
            return Tuple(h is null ? PyNone.Instance : A.Wrap(h), mask is null ? PyNone.Instance : A.Wrap(mask));
        });
        Def("findEssentialMat", (i, a, k) =>
        {
            var p = new Args("findEssentialMat", i, a, k, "points1", "points2", "cameraMatrix", "method", "prob", "threshold", "maxIters", "mask");
            var (e, mask) = Cv.FindEssentialMat(A.Image(p.Required(0)), A.Image(p.Required(1)), A.Image(p.Required(2)), p.Int(3, 8), p.Double(4, 0.999), p.Double(5, 1.0));
            return Tuple(e is null ? PyNone.Instance : A.Wrap(e), mask is null ? PyNone.Instance : A.Wrap(mask));
        });
        Def("recoverPose", (i, a, k) =>
        {
            var p = new Args("recoverPose", i, a, k, "E", "points1", "points2", "cameraMatrix", "R", "t", "mask");
            var (n, r, t, mask) = Cv.RecoverPose(A.Image(p.Required(0)), A.Image(p.Required(1)), A.Image(p.Required(2)), A.Image(p.Required(3)), A.ImageOpt(p[6]));
            return Tuple(Big(n), A.Wrap(r), A.Wrap(t), mask is null ? PyNone.Instance : A.Wrap(mask));
        });
        Def("findFundamentalMat", (i, a, k) =>
        {
            var p = new Args("findFundamentalMat", i, a, k, "points1", "points2", "method", "ransacReprojThreshold", "confidence", "maxIters", "mask");
            var (f, mask) = Cv.FindFundamentalMat(A.Image(p.Required(0)), A.Image(p.Required(1)), p.Int(2, 8), p.Double(3, 3), p.Double(4, 0.99));
            return Tuple(f is null ? PyNone.Instance : A.Wrap(f), mask is null ? PyNone.Instance : A.Wrap(mask));
        });
        Def("Rodrigues", (i, a, k) =>
        {
            var p = new Args("Rodrigues", i, a, k, "src", "dst", "jacobian");
            var (d, j) = Cv.Rodrigues(A.Image(p.Required(0)));
            return Tuple(A.Wrap(d), A.Wrap(j));
        });
        Def("triangulatePoints", (i, a, k) =>
        {
            var p = new Args("triangulatePoints", i, a, k, "projMatr1", "projMatr2", "projPoints1", "projPoints2", "points4D");
            return A.Wrap(Cv.TriangulatePoints(A.Image(p.Required(0)), A.Image(p.Required(1)), A.Image(p.Required(2)), A.Image(p.Required(3))));
        });
        Def("calibrateCamera", (i, a, k) =>
        {
            var p = new Args("calibrateCamera", i, a, k, "objectPoints", "imagePoints", "imageSize", "cameraMatrix", "distCoeffs", "rvecs", "tvecs", "flags", "criteria");
            var crit = p.Has(8) ? A.Doubles(p[8]!) : new double[] { 3, 30, double.Epsilon };
            var r = Cv.CalibrateCamera(PyOps.Iterate(i, p.Required(0)).Select(A.Image).ToList(), PyOps.Iterate(i, p.Required(1)).Select(A.Image).ToList(),
                A.Pair(p.Required(2)), A.ImageOpt(p[3]), A.ImageOpt(p[4]), p.Int(7, 0), (int)crit[0], (int)crit[1], crit[2]);
            return Tuple(r.Rms, A.Wrap(r.CameraMatrix), A.Wrap(r.DistCoeffs),
                new PyTuple(r.Rvecs.Select(x => A.Wrap(x)).ToArray()), new PyTuple(r.Tvecs.Select(x => A.Wrap(x)).ToArray()));
        });
        Def("findChessboardCorners", (i, a, k) =>
        {
            var p = new Args("findChessboardCorners", i, a, k, "image", "patternSize", "corners", "flags");
            var (found, corners) = Cv.FindChessboardCorners(A.Image(p.Required(0)), A.Pair(p.Required(1)), p.Int(3, 3));
            return Tuple(found, found ? A.Wrap(corners) : PyNone.Instance);
        });
        Def("drawChessboardCorners", (i, a, k) =>
        {
            var p = new Args("drawChessboardCorners", i, a, k, "image", "patternSize", "corners", "patternWasFound");
            Cv.DrawChessboardCorners(A.Image(p.Required(0)), A.Pair(p.Required(1)), A.Image(p.Required(2)), p.Bool(3, false));
            return p[0]!;
        });
        Def("projectPoints", (i, a, k) =>
        {
            var p = new Args("projectPoints", i, a, k, "objectPoints", "rvec", "tvec", "cameraMatrix", "distCoeffs", "imagePoints", "jacobian", "aspectRatio");
            var (ip, jac) = Cv.ProjectPoints(A.Image(p.Required(0)), A.Image(p.Required(1)), A.Image(p.Required(2)), A.Image(p.Required(3)), A.ImageOpt(p[4]), p.Double(7, 0));
            return Tuple(A.Wrap(ip), jac is null ? PyNone.Instance : A.Wrap(jac));
        });
        Def("undistortPoints", (i, a, k) =>
        {
            var p = new Args("undistortPoints", i, a, k, "src", "cameraMatrix", "distCoeffs", "dst", "R", "P", "criteria");
            return A.Wrap(Cv.UndistortPoints(A.Image(p.Required(0)), A.Image(p.Required(1)), A.ImageOpt(p[2]), A.ImageOpt(p[4]), A.ImageOpt(p[5])));
        });
    }
}
