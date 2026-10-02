// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Text;
using PySharpLib.Runtime;
using TorchSharp;
using static TorchSharp.torch;
using Tensor = TorchSharp.torch.Tensor;

namespace PySharpLib.Torch;

/// <summary>A port of <c>torch._tensor_str</c>: the same widths, precision, scientific-mode and line-wrapping rules as the real repr.</summary>
internal static class TorchFormat
{
    private static int Precision = 4, Threshold = 1000, EdgeItems = 3, LineWidth = 80;
    private static bool? SciMode;

    public static void SetOptions(Dictionary<string, object>? kw)
    {
        if (kw is null) return;
        if (kw.TryGetValue("precision", out var p)) Precision = (int)TC.ToLong(p);
        if (kw.TryGetValue("threshold", out var t)) Threshold = (int)TC.ToLong(t);
        if (kw.TryGetValue("edgeitems", out var e)) EdgeItems = (int)TC.ToLong(e);
        if (kw.TryGetValue("linewidth", out var l)) LineWidth = (int)TC.ToLong(l);
        if (kw.TryGetValue("sci_mode", out var s)) SciMode = s is PyNone ? null : (bool)s;
        if (kw.TryGetValue("profile", out var pr) && pr is string prof)
        {
            if (prof == "default") { Precision = 4; Threshold = 1000; EdgeItems = 3; LineWidth = 80; }
            else if (prof == "short") { Precision = 2; Threshold = 1000; EdgeItems = 2; LineWidth = 80; }
            else if (prof == "full") { Precision = 4; Threshold = int.MaxValue; EdgeItems = 3; LineWidth = 80; }
        }
    }

    private sealed class Formatter
    {
        public bool Floating, IntMode = true, Sci;
        public int MaxWidth = 1;

        public Formatter(Tensor t)
        {
            Floating = TC.IsFloat(t.dtype);
            var flat = t.detach().reshape(-1);
            if (!Floating)
            {
                foreach (var v in Elements(flat)) MaxWidth = Math.Max(MaxWidth, Str(v, flat.dtype).Length);
                return;
            }
            var vals = Doubles(flat);
            var nz = vals.Where(v => double.IsFinite(v) && v != 0).ToArray();
            double max = 0, min = 0;
            if (nz.Length > 0) { max = nz.Max(Math.Abs); min = nz.Min(Math.Abs); }
            foreach (var v in nz) if (v != Math.Ceiling(v)) { IntMode = false; break; }
            if (IntMode)
            {
                if (max / min > 1000.0 || max > 1.0e8)
                {
                    Sci = true;
                    foreach (var v in nz) MaxWidth = Math.Max(MaxWidth, E(v).Length);
                }
                else foreach (var v in nz) MaxWidth = Math.Max(MaxWidth, v.ToString("F0", CultureInfo.InvariantCulture).Length + 1);
            }
            else
            {
                if (max / min > 1000.0 || max > 1.0e8 || min < 1.0e-4)
                {
                    Sci = true;
                    foreach (var v in nz) MaxWidth = Math.Max(MaxWidth, E(v).Length);
                }
                else foreach (var v in nz) MaxWidth = Math.Max(MaxWidth, v.ToString("F" + Precision, CultureInfo.InvariantCulture).Length);
            }
            if (SciMode is { } sm) Sci = sm;
        }

        private static string E(double v)
        {
            string s = v.ToString("0." + new string('0', Precision) + "e+00", CultureInfo.InvariantCulture);
            return s;
        }

        private static string Str(object v, ScalarType dt) => dt == ScalarType.Bool ? ((bool)v ? "True" : "False") : Convert.ToString(v, CultureInfo.InvariantCulture)!;

        public string Format(object value, ScalarType dt)
        {
            string ret;
            if (Floating)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d)) ret = "nan";
                else if (double.IsInfinity(d)) ret = d > 0 ? "inf" : "-inf";
                else if (Sci) ret = E(d).PadLeft(MaxWidth);
                else if (IntMode) ret = d.ToString("F0", CultureInfo.InvariantCulture) + ".";
                else ret = d.ToString("F" + Precision, CultureInfo.InvariantCulture);
                if (ret == "-0" || ret == "-0.") ret = ret; // keeps the sign of negative zero like torch ("-0.")
            }
            else ret = Str(value, dt);
            return ret.PadLeft(MaxWidth);
        }
    }

    private static double[] Doubles(Tensor flat) => flat.to(ScalarType.Float64).data<double>().ToArray();
    private static long[] Ints(Tensor flat) => flat.dtype == ScalarType.Bool ? flat.to(ScalarType.Int64).data<long>().ToArray() : flat.to(ScalarType.Int64).data<long>().ToArray();

    private static object[] Elements(Tensor t)
    {
        if (t.dtype == ScalarType.Bool) return t.data<bool>().ToArray().Select(v => (object)v).ToArray();
        if (TC.IsFloat(t.dtype)) return Doubles(t).Select(v => (object)v).ToArray();
        return Ints(t).Select(v => (object)v).ToArray();
    }

    private static string Vector(Tensor t, int indent, bool summarize, Formatter f)
    {
        int elementLength = f.MaxWidth + 2;
        int perLine = Math.Max(1, (LineWidth - indent) / elementLength);
        var dt = t.dtype;
        List<string> data;
        long n = t.shape[0];
        if (summarize && n > 2 * EdgeItems)
        {
            data = Elements(t.narrow(0, 0, EdgeItems).contiguous()).Select(v => f.Format(v, dt)).ToList();
            data.Add(" ...");
            data.AddRange(Elements(t.narrow(0, n - EdgeItems, EdgeItems).contiguous()).Select(v => f.Format(v, dt)));
        }
        else data = Elements(t.contiguous()).Select(v => f.Format(v, dt)).ToList();
        var lines = new List<string>();
        for (int i = 0; i < data.Count; i += perLine) lines.Add(string.Join(", ", data.Skip(i).Take(perLine)));
        return "[" + string.Join("," + "\n" + new string(' ', indent + 1), lines) + "]";
    }

    private static string Rec(Tensor t, int indent, bool summarize, Formatter f)
    {
        int dim = (int)t.Dimensions;
        if (dim == 0) return f.Format(Elements(t.reshape(1))[0], t.dtype);
        if (dim == 1) return Vector(t, indent, summarize, f);
        long n = t.shape[0];
        var slices = new List<string>();
        if (summarize && n > 2 * EdgeItems)
        {
            for (long i = 0; i < EdgeItems; i++) slices.Add(Rec(t.select(0, i), indent + 1, summarize, f));
            slices.Add("...");
            for (long i = n - EdgeItems; i < n; i++) slices.Add(Rec(t.select(0, i), indent + 1, summarize, f));
        }
        else for (long i = 0; i < n; i++) slices.Add(Rec(t.select(0, i), indent + 1, summarize, f));
        return "[" + string.Join("," + new string('\n', dim - 1) + new string(' ', indent + 1), slices) + "]";
    }

    private static Tensor Summarized(Tensor t)
    {
        if (t.Dimensions == 0) return t;
        long n = t.shape[0];
        if (n > 2 * EdgeItems)
        {
            var head = Summarized(t.narrow(0, 0, EdgeItems));
            var tail = Summarized(t.narrow(0, n - EdgeItems, EdgeItems));
            return torch.cat(new[] { head, tail }, 0);
        }
        return torch.stack(Enumerable.Range(0, (int)n).Select(i => Summarized(t.select(0, i))).ToArray(), 0);
    }

    public static string Repr(Tensor t0)
    {
        var t = t0;
        const string prefix = "tensor(";
        int indent = prefix.Length;
        var suffixes = new List<string>();
        if (t.dtype != torch.get_default_dtype() && t.dtype != ScalarType.Int64 && t.dtype != ScalarType.Bool)
            suffixes.Add("dtype=torch." + TC.DTypeName(t.dtype));
        string body;
        if (t.numel() == 0)
        {
            body = "[]";
            if (t.Dimensions != 1) suffixes.Insert(0, "size=" + "(" + string.Join(", ", t.shape) + (t.Dimensions == 1 ? "," : "") + ")");
        }
        else
        {
            var src = t.detach();
            if (src.dtype is ScalarType.Float16 or ScalarType.BFloat16) src = src.to(ScalarType.Float32);
            if (TC.IsComplex(src.dtype)) src = torch.view_as_real(src);
            bool summarize = src.numel() > Threshold;
            var f = new Formatter(summarize ? Summarized(src) : src);
            body = Rec(src, indent, summarize, f);
        }
        if (t.requires_grad)
        {
            if (!t.is_leaf) suffixes.Add("grad_fn=<" + GradFn.NameOf(t) + ">");
            else suffixes.Add("requires_grad=True");
        }
        var sb = new StringBuilder(prefix + body);
        int lastLineLen = body.Length - body.LastIndexOf('\n') + 1 + (body.Contains('\n') ? 0 : prefix.Length);
        foreach (var s in suffixes)
        {
            if (lastLineLen + s.Length + 2 > LineWidth) { sb.Append(",\n" + new string(' ', indent) + s); lastLineLen = indent + s.Length; }
            else { sb.Append(", " + s); lastLineLen += s.Length + 2; }
        }
        sb.Append(')');
        return sb.ToString();
    }
}

/// <summary>TorchSharp does not expose <c>grad_fn</c>, so a result remembers the op that produced it (for the repr and <c>.grad_fn</c>).</summary>
internal static class GradFn
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Tensor, string> Names = new();

    private static readonly Dictionary<string, string> Special = new()
    {
        ["add"] = "Add", ["sub"] = "Sub", ["mul"] = "Mul", ["div"] = "Div", ["matmul"] = "Mm", ["mm"] = "Mm", ["bmm"] = "Bmm", ["pow"] = "Pow",
        ["neg"] = "Neg", ["sum"] = "Sum", ["mean"] = "Mean", ["relu"] = "Relu", ["sigmoid"] = "Sigmoid", ["tanh"] = "Tanh", ["exp"] = "Exp", ["log"] = "Log",
        ["sqrt"] = "Sqrt", ["abs"] = "Abs", ["mse_loss"] = "MseLoss", ["cross_entropy"] = "NllLoss", ["nll_loss"] = "NllLoss", ["log_softmax"] = "LogSoftmax",
        ["softmax"] = "Softmax", ["linear"] = "Addmm", ["binary_cross_entropy_with_logits"] = "BinaryCrossEntropyWithLogits", ["l1_loss"] = "L1Loss",
        ["conv2d"] = "Convolution", ["max_pool2d"] = "MaxPool2DWithIndices", ["view"] = "View", ["reshape"] = "Reshape", ["cat"] = "Cat", ["stack"] = "Stack",
        ["transpose"] = "Transpose", ["permute"] = "Permute", ["flatten"] = "View", ["unsqueeze"] = "Unsqueeze", ["squeeze"] = "Squeeze", ["clamp"] = "Clamp",
        ["__getitem__"] = "Select", ["var"] = "Var", ["std"] = "Std", ["norm"] = "Linalg_vector_norm", ["max"] = "Max", ["min"] = "Min", ["where"] = "Where",
        ["gelu"] = "Gelu", ["leaky_relu"] = "LeakyRelu", ["dropout"] = "NativeDropout", ["layer_norm"] = "NativeLayerNorm", ["batch_norm"] = "NativeBatchNorm",
        ["to"] = "To", ["float"] = "To", ["double"] = "To", ["clone"] = "Clone",
    };

    public static void Tag(object result, string opName)
    {
        if (result is PyInstance { Native: TBox b } && b.T.requires_grad && !b.T.is_leaf)
        {
            Names.Remove(b.T);
            Names.Add(b.T, opName);
        }
    }

    public static string NameOf(Tensor t)
    {
        if (!Names.TryGetValue(t, out var n)) return "Backward0";
        string basename = Special.TryGetValue(n, out var s) ? s : string.Concat(n.Split('_').Select(w => w.Length == 0 ? w : char.ToUpper(w[0]) + w[1..]));
        return basename + "Backward0";
    }
}
