// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Image;
using OpenCvSharp;
using PySharpLib.Importing;
using PySharpLib.Numpy;
using PySharpLib.Runtime;

namespace PySharpLib.Cv2;

/// <summary>Opt-in registration of the <c>cv2</c> module (a Python binding over NDSharp.Image). Register
/// <c>numpy</c> too — cv2 images are numpy arrays.</summary>
public static class Cv2Registration
{
    public static void Register(Importer importer)
    {
        importer.RegisterBuiltin("cv2", _ => Cv2Module.Create());
    }
}

/// <summary>Python-facing argument conversions shared by the cv2 functions.</summary>
internal static class A
{
    public static NDArray Image(object o)
        => Conv.TryUnwrap(o) ?? (o is PyList or PyTuple ? Conv.FromSequence(o, null) : throw PyErr.TypeError($"Expected Ptr<cv::UMat> for argument 'src' (got {PyOps.TypeName(o)})"));

    public static NDArray? ImageOpt(object? o) => o is null or PyNone ? null : Image(o);

    private static double Num(object o)
        => PyOps.AsDouble(Conv.TryUnwrap(o) is { Ndim: 0 } nd ? Conv.Scalarize(nd) : o);

    public static double[] Doubles(object o) => o switch
    {
        PyTuple t => t.Items.Select(Num).ToArray(),
        PyList l => l.Items.Select(Num).ToArray(),
        _ when Conv.TryUnwrap(o) is { } nd => nd.Ndim == 0 ? new[] { nd.GetDouble(0) } : nd.AsType(DType.Float64).ToArray<double>(),
        _ => new[] { Num(o) },
    };

    /// <summary>cv2 scalars: a number, or a tuple/list/array of up to four numbers.</summary>
    public static double[]? Scalar(object? o) => o is null or PyNone ? null : Doubles(o);

    public static (int, int) Pair(object o)
    {
        var d = Doubles(o);
        if (d.Length != 2) throw PyErr.TypeError("Can't parse a 2-element tuple of numbers");
        return ((int)d[0], (int)d[1]);
    }

    public static (int, int)? PairOpt(object? o) => o is null or PyNone ? null : Pair(o);

    public static (int, int)[] Points(object o)
    {
        var nd = Conv.TryUnwrap(o) ?? Conv.FromSequence(o, null);
        var flat = nd.AsType(DType.Float64).ToArray<double>();
        var pts = new (int, int)[flat.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = ((int)flat[2 * i], (int)flat[2 * i + 1]);
        return pts;
    }

    public static IReadOnlyList<(int, int)[]> Polygons(object o)
    {
        // A list of (N,1,2)/(N,2) arrays, or a single such array.
        if (o is PyList or PyTuple && ((o as PyList)?.Items.FirstOrDefault() ?? (o as PyTuple)?.Items.FirstOrDefault()) is PyList or PyTuple or PyInstance
            && !(Conv.TryUnwrap(o) is not null))
        {
            var items = o is PyList l ? l.Items : ((PyTuple)o).Items.ToList();
            // [[x, y], ...] (a single polygon given as nested lists) vs [poly, poly]
            if (items.Count > 0 && items[0] is PyList or PyTuple && Doubles(items[0]).Length == 2 && !(items[0] is PyList { Items: [PyList or PyTuple, ..] }))
                return new[] { Points(o) };
            return items.Select(Points).ToList();
        }
        var nd = Conv.TryUnwrap(o) ?? Conv.FromSequence(o, null);
        if (nd.Ndim == 3 && nd.Shape[1] != 1) return Enumerable.Range(0, nd.Shape[0]).Select(i => Points(Conv.Wrap(nd.Get(i)))).ToList();
        return new[] { Points(Conv.Wrap(nd)) };
    }

    public static object Wrap(NDArray a) => Conv.Wrap(a);
}

internal static partial class Cv2Module
{
    public static readonly PyClass ErrorClass = new("error", new List<PyClass> { PyErr.Exception });

    private static PyBuiltinFunction Fn(string name, BuiltinFn fn) => new(name, (interp, a, kw) =>
    {
        try { return fn(interp, a, kw); }
        catch (NDException ex) { throw Native.Translate(ex); }
        catch (OpenCVException ex) { throw PyErr.Raise(ErrorClass, ex.Message); }
    });

    public static PyModule Create()
    {
        var m = new PyModule("cv2");
        m.Dict["__version__"] = "4.11.0-pysharp (NDSharp.Image / OpenCvSharp)";
        m.Dict["error"] = ErrorClass;
        foreach (var (name, value) in Cv2Constants.All)
            m.Dict[name] = value is long l ? new BigInteger(l) : value;
        void Def(string name, BuiltinFn fn) => m.Dict[name] = Fn(name, fn);
        RegisterBasics(Def);
        RegisterFilters(Def);
        RegisterFeatures(m, Def);
        return m;
    }

    private static object Opt(Args p, int i, object dflt) => p.Has(i) ? p[i]! : dflt;

    private static void RegisterBasics(Action<string, BuiltinFn> Def)
    {
        // ------------------------------------------------------------ I/O
        Def("imread", (i, a, k) =>
        {
            var p = new Args("imread", i, a, k, "filename", "flags");
            var img = Cv.ImRead((string)p.Required(0), p.Int(1, 1));
            return img is null ? PyNone.Instance : A.Wrap(img);
        });
        Def("imwrite", (i, a, k) =>
        {
            var p = new Args("imwrite", i, a, k, "filename", "img", "params");
            int[]? prms = p.Has(2) ? A.Doubles(p[2]!).Select(x => (int)x).ToArray() : null;
            return Cv.ImWrite((string)p.Required(0), A.Image(p.Required(1)), prms);
        });
        Def("imencode", (i, a, k) =>
        {
            var p = new Args("imencode", i, a, k, "ext", "img", "params");
            int[]? prms = p.Has(2) ? A.Doubles(p[2]!).Select(x => (int)x).ToArray() : null;
            var (ok, buf) = Cv.ImEncode((string)p.Required(0), A.Image(p.Required(1)), prms);
            return new PyTuple(new object[] { ok, A.Wrap(buf) });
        });
        Def("imdecode", (i, a, k) =>
        {
            var p = new Args("imdecode", i, a, k, "buf", "flags");
            var img = Cv.ImDecode(A.Image(p.Required(0)), p.Int(1, 1));
            return img is null ? PyNone.Instance : A.Wrap(img);
        });

        // ------------------------------------------------------------ color & geometry
        Def("cvtColor", (i, a, k) =>
        {
            var p = new Args("cvtColor", i, a, k, "src", "code", "dst", "dstCn");
            return A.Wrap(Cv.CvtColor(A.Image(p.Required(0)), p.Int(1, 0), p.Int(3, 0)));
        });
        Def("resize", (i, a, k) =>
        {
            var p = new Args("resize", i, a, k, "src", "dsize", "dst", "fx", "fy", "interpolation");
            var dsize = p.Has(1) ? A.Pair(p[1]!) : (0, 0);
            return A.Wrap(Cv.Resize(A.Image(p.Required(0)), dsize, p.Double(3, 0), p.Double(4, 0), p.Int(5, 1)));
        });
        Def("flip", (i, a, k) =>
        {
            var p = new Args("flip", i, a, k, "src", "flipCode", "dst");
            return A.Wrap(Cv.Flip(A.Image(p.Required(0)), p.Int(1, 0)));
        });
        Def("split", (i, a, k) =>
        {
            var p = new Args("split", i, a, k, "m", "mv");
            return new PyTuple(Cv.Split(A.Image(p.Required(0))).Select(x => A.Wrap(x)).ToArray());
        });
        Def("merge", (i, a, k) =>
        {
            var p = new Args("merge", i, a, k, "mv", "dst");
            var planes = PyOps.Iterate(i, p.Required(0)).Select(A.Image).ToList();
            return A.Wrap(Cv.Merge(planes));
        });
        Def("copyMakeBorder", (i, a, k) =>
        {
            var p = new Args("copyMakeBorder", i, a, k, "src", "top", "bottom", "left", "right", "borderType", "dst", "value");
            return A.Wrap(Cv.CopyMakeBorder(A.Image(p.Required(0)), p.Int(1, 0), p.Int(2, 0), p.Int(3, 0), p.Int(4, 0), p.Int(5, 0), A.Scalar(p[7])));
        });

        // ------------------------------------------------------------ arithmetic
        (NDArray?, double[]?) Operand(object o)
            => Conv.TryUnwrap(o) is { Ndim: >= 2 } nd ? (nd, null)
                : o is PyList or PyTuple && A.Doubles(o).Length > 4 ? (A.Image(o), null)
                : (null, A.Doubles(o));
        void Binary(string name, Func<NDArray?, double[]?, NDArray?, double[]?, int, NDArray> f) => Def(name, (i, a, k) =>
        {
            var p = new Args(name, i, a, k, "src1", "src2", "dst", "mask", "dtype");
            var (a1, s1) = Operand(p.Required(0));
            var (a2, s2) = Operand(p.Required(1));
            return A.Wrap(f(a1, s1, a2, s2, p.Int(4, -1)));
        });
        Binary("add", Cv.Add);
        Binary("subtract", Cv.Subtract);
        Binary("absdiff", (a1, s1, a2, s2, _) => Cv.AbsDiff(a1, s1, a2, s2));
        foreach (var (name, f) in new (string, Func<NDArray?, double[]?, NDArray?, double[]?, double, int, NDArray>)[] { ("multiply", Cv.Multiply), ("divide", Cv.Divide) })
        {
            var fn = f;
            var nm = name;
            Def(nm, (i, a, k) =>
            {
                var p = new Args(nm, i, a, k, "src1", "src2", "dst", "scale", "dtype");
                var (a1, s1) = Operand(p.Required(0));
                var (a2, s2) = Operand(p.Required(1));
                return A.Wrap(fn(a1, s1, a2, s2, p.Double(3, 1), p.Int(4, -1)));
            });
        }
        Def("addWeighted", (i, a, k) =>
        {
            var p = new Args("addWeighted", i, a, k, "src1", "alpha", "src2", "beta", "gamma", "dst", "dtype");
            return A.Wrap(Cv.AddWeighted(A.Image(p.Required(0)), p.Double(1, 0), A.Image(p.Required(2)), p.Double(3, 0), p.Double(4, 0), p.Int(6, -1)));
        });
        foreach (var (name, f) in new (string, Func<NDArray, NDArray, NDArray?, NDArray>)[] { ("bitwise_and", Cv.BitwiseAnd), ("bitwise_or", Cv.BitwiseOr), ("bitwise_xor", Cv.BitwiseXor) })
        {
            var fn = f;
            var nm = name;
            Def(nm, (i, a, k) =>
            {
                var p = new Args(nm, i, a, k, "src1", "src2", "dst", "mask");
                return A.Wrap(fn(A.Image(p.Required(0)), A.Image(p.Required(1)), A.ImageOpt(p[3])));
            });
        }
        Def("bitwise_not", (i, a, k) =>
        {
            var p = new Args("bitwise_not", i, a, k, "src", "dst", "mask");
            return A.Wrap(Cv.BitwiseNot(A.Image(p.Required(0))));
        });
        Def("normalize", (i, a, k) =>
        {
            var p = new Args("normalize", i, a, k, "src", "dst", "alpha", "beta", "norm_type", "dtype", "mask");
            return A.Wrap(Cv.Normalize(A.Image(p.Required(0)), p.Double(2, 1), p.Double(3, 0), p.Int(4, 4), p.Int(5, -1)));
        });

        // ------------------------------------------------------------ thresholding & morphology
        Def("threshold", (i, a, k) =>
        {
            var p = new Args("threshold", i, a, k, "src", "thresh", "maxval", "type");
            var (t, dst) = Cv.Threshold(A.Image(p.Required(0)), p.Double(1, 0), p.Double(2, 0), p.Int(3, 0));
            return new PyTuple(new object[] { t, A.Wrap(dst) });
        });
        Def("getStructuringElement", (i, a, k) =>
        {
            var p = new Args("getStructuringElement", i, a, k, "shape", "ksize", "anchor");
            return A.Wrap(Cv.GetStructuringElement(p.Int(0, 0), A.Pair(p.Required(1)), A.PairOpt(p[2])));
        });
        Def("erode", (i, a, k) =>
        {
            var p = new Args("erode", i, a, k, "src", "kernel", "dst", "anchor", "iterations", "borderType", "borderValue");
            return A.Wrap(Cv.Erode(A.Image(p.Required(0)), A.ImageOpt(p[1]), A.PairOpt(p[3]), p.Int(4, 1), p.Int(5, 0), A.Scalar(p[6])));
        });
        Def("dilate", (i, a, k) =>
        {
            var p = new Args("dilate", i, a, k, "src", "kernel", "dst", "anchor", "iterations", "borderType", "borderValue");
            return A.Wrap(Cv.Dilate(A.Image(p.Required(0)), A.ImageOpt(p[1]), A.PairOpt(p[3]), p.Int(4, 1), p.Int(5, 0), A.Scalar(p[6])));
        });
        Def("morphologyEx", (i, a, k) =>
        {
            var p = new Args("morphologyEx", i, a, k, "src", "op", "kernel", "dst", "anchor", "iterations", "borderType", "borderValue");
            return A.Wrap(Cv.MorphologyEx(A.Image(p.Required(0)), p.Int(1, 0), A.ImageOpt(p[2]), A.PairOpt(p[4]), p.Int(5, 1), p.Int(6, 0), A.Scalar(p[7])));
        });

        // ------------------------------------------------------------ flood fill & components
        Def("floodFill", (i, a, k) =>
        {
            var p = new Args("floodFill", i, a, k, "image", "mask", "seedPoint", "newVal", "loDiff", "upDiff", "flags");
            var image = A.Image(p.Required(0));
            var mask = A.ImageOpt(p[1]);
            var (area, r) = Cv.FloodFill(image, mask, A.Pair(p.Required(2)), A.Doubles(p.Required(3)), A.Scalar(p[4]), A.Scalar(p[5]), p.Int(6, 4));
            return new PyTuple(new object[]
            {
                new BigInteger(area), p.Required(0), p[1] is null or PyNone ? PyNone.Instance : p[1]!,
                new PyTuple(new object[] { new BigInteger(r.x), new BigInteger(r.y), new BigInteger(r.w), new BigInteger(r.h) }),
            });
        });
        Def("connectedComponents", (i, a, k) =>
        {
            var p = new Args("connectedComponents", i, a, k, "image", "labels", "connectivity", "ltype");
            var (n, labels) = Cv.ConnectedComponents(A.Image(p.Required(0)), p.Int(2, 8), p.Int(3, 4));
            return new PyTuple(new object[] { new BigInteger(n), A.Wrap(labels) });
        });
        Def("connectedComponentsWithStats", (i, a, k) =>
        {
            var p = new Args("connectedComponentsWithStats", i, a, k, "image", "labels", "stats", "centroids", "connectivity", "ltype");
            var (n, labels, stats, cent) = Cv.ConnectedComponentsWithStats(A.Image(p.Required(0)), p.Int(4, 8), p.Int(5, 4));
            return new PyTuple(new object[] { new BigInteger(n), A.Wrap(labels), A.Wrap(stats), A.Wrap(cent) });
        });

        // ------------------------------------------------------------ drawing (in place)
        Def("rectangle", (i, a, k) =>
        {
            var p = new Args("rectangle", i, a, k, "img", "pt1", "pt2", "color", "thickness", "lineType", "shift");
            Cv.Rectangle(A.Image(p.Required(0)), A.Pair(p.Required(1)), A.Pair(p.Required(2)), A.Doubles(p.Required(3)), p.Int(4, 1), p.Int(5, 8), p.Int(6, 0));
            return p[0]!;
        });
        Def("circle", (i, a, k) =>
        {
            var p = new Args("circle", i, a, k, "img", "center", "radius", "color", "thickness", "lineType", "shift");
            Cv.Circle(A.Image(p.Required(0)), A.Pair(p.Required(1)), p.Int(2, 0), A.Doubles(p.Required(3)), p.Int(4, 1), p.Int(5, 8), p.Int(6, 0));
            return p[0]!;
        });
        Def("line", (i, a, k) =>
        {
            var p = new Args("line", i, a, k, "img", "pt1", "pt2", "color", "thickness", "lineType", "shift");
            Cv.Line(A.Image(p.Required(0)), A.Pair(p.Required(1)), A.Pair(p.Required(2)), A.Doubles(p.Required(3)), p.Int(4, 1), p.Int(5, 8), p.Int(6, 0));
            return p[0]!;
        });
        Def("arrowedLine", (i, a, k) =>
        {
            var p = new Args("arrowedLine", i, a, k, "img", "pt1", "pt2", "color", "thickness", "line_type", "shift", "tipLength");
            Cv.ArrowedLine(A.Image(p.Required(0)), A.Pair(p.Required(1)), A.Pair(p.Required(2)), A.Doubles(p.Required(3)), p.Int(4, 1), p.Int(5, 8), p.Int(6, 0), p.Double(7, 0.1));
            return p[0]!;
        });
        Def("ellipse", (i, a, k) =>
        {
            var p = new Args("ellipse", i, a, k, "img", "center", "axes", "angle", "startAngle", "endAngle", "color", "thickness", "lineType", "shift");
            Cv.Ellipse(A.Image(p.Required(0)), A.Pair(p.Required(1)), A.Pair(p.Required(2)), p.Double(3, 0), p.Double(4, 0), p.Double(5, 360), A.Doubles(p.Required(6)), p.Int(7, 1), p.Int(8, 8), p.Int(9, 0));
            return p[0]!;
        });
        Def("putText", (i, a, k) =>
        {
            var p = new Args("putText", i, a, k, "img", "text", "org", "fontFace", "fontScale", "color", "thickness", "lineType", "bottomLeftOrigin");
            Cv.PutText(A.Image(p.Required(0)), (string)p.Required(1), A.Pair(p.Required(2)), p.Int(3, 0), p.Double(4, 1), A.Doubles(p.Required(5)), p.Int(6, 1), p.Int(7, 8), p.Bool(8, false));
            return p[0]!;
        });
        Def("fillPoly", (i, a, k) =>
        {
            var p = new Args("fillPoly", i, a, k, "img", "pts", "color", "lineType", "shift", "offset");
            Cv.FillPoly(A.Image(p.Required(0)), A.Polygons(p.Required(1)), A.Doubles(p.Required(2)), p.Int(3, 8));
            return p[0]!;
        });
        Def("polylines", (i, a, k) =>
        {
            var p = new Args("polylines", i, a, k, "img", "pts", "isClosed", "color", "thickness", "lineType", "shift");
            Cv.Polylines(A.Image(p.Required(0)), A.Polygons(p.Required(1)), p.Bool(2, false), A.Doubles(p.Required(3)), p.Int(4, 1), p.Int(5, 8));
            return p[0]!;
        });
        Def("drawMarker", (i, a, k) =>
        {
            var p = new Args("drawMarker", i, a, k, "img", "position", "color", "markerType", "markerSize", "thickness", "line_type");
            Cv.DrawMarker(A.Image(p.Required(0)), A.Pair(p.Required(1)), A.Doubles(p.Required(2)), p.Int(3, 0), p.Int(4, 20), p.Int(5, 1), p.Int(6, 8));
            return p[0]!;
        });
    }
}
