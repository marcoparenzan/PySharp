// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Runtime;
using TorchSharp;
using static TorchSharp.torch;
using Tensor = TorchSharp.torch.Tensor;
using F = TorchSharp.torch.nn.functional;

namespace PySharpLib.Torch;

/// <summary>torch.nn.functional (the libtorch kernels through TorchSharp) and torch.nn.init.</summary>
internal static class TorchFunctional
{
    private static Tensor X(this Args p, int i) => TC.ToTensor(p.Required(i));
    private static Tensor? XOpt(this Args p, int i) => p.Has(i) ? TC.ToTensor(p[i]!) : null;
    private static object R(Tensor t) => TC.Wrap(t);

    private static long[] Pair(object? o, long dflt, int n = 2)
    {
        if (o is null or PyNone) return Enumerable.Repeat(dflt, n).ToArray();
        var v = TC.ToLongs(o);
        return v.Length == 1 ? Enumerable.Repeat(v[0], n).ToArray() : v;
    }

    private static torch.nn.Reduction Red(Args p, int i)
    {
        string s = p.Has(i) ? (string)p[i]! : "mean";
        return s switch
        {
            "mean" => torch.nn.Reduction.Mean, "sum" => torch.nn.Reduction.Sum, "none" => torch.nn.Reduction.None,
            _ => throw PyErr.ValueError($"{s} is not a valid value for reduction"),
        };
    }

    public static PyModule Create()
    {
        var m = new PyModule("torch._F");
        void Def(string name, string[] ps, Func<Args, object> fn)
            => m.Dict[name] = new PyBuiltinFunction(name, (i, a, kw) =>
            {
                var f = Ops.Fn(name, (ii, aa, kk) => { var r = fn(new Args(name, ii, aa, kk, ps)); GradFn.Tag(r, name); return r; });
                return f.Fn(i, a, kw);
            });

        // activations
        Def("relu", new[] { "input", "inplace" }, p => R(F.relu(p.X(0))));
        Def("relu6", new[] { "input", "inplace" }, p => R(F.relu6(p.X(0))));
        Def("leaky_relu", new[] { "input", "negative_slope", "inplace" }, p => R(F.leaky_relu(p.X(0), p.Double(1, 0.01))));
        Def("elu", new[] { "input", "alpha", "inplace" }, p => R(F.elu(p.X(0), p.Double(1, 1.0))));
        Def("selu", new[] { "input", "inplace" }, p => R(F.selu(p.X(0))));
        Def("silu", new[] { "input", "inplace" }, p => R(F.silu(p.X(0))));
        Def("gelu", new[] { "input", "approximate" }, p => R(F.gelu(p.X(0), p.Has(1) && (string)p[1]! == "tanh" ? TorchSharp.Modules.GELU.Approximate.tanh : TorchSharp.Modules.GELU.Approximate.none)));
        Def("sigmoid", new[] { "input" }, p => R(p.X(0).sigmoid()));
        Def("tanh", new[] { "input" }, p => R(p.X(0).tanh()));
        Def("softplus", new[] { "input", "beta", "threshold" }, p => R(F.softplus(p.X(0), p.Double(1, 1.0), p.Double(2, 20.0))));
        Def("hardtanh", new[] { "input", "min_val", "max_val" }, p => R(p.X(0).clamp(p.Double(1, -1.0), p.Double(2, 1.0))));
        Def("softmax", new[] { "input", "dim", "dtype" }, p => R(torch.special.softmax(p.X(0), p.Has(1) ? TC.ToLong(p[1]!) : -1, p.Has(2) ? TC.ToDType(p[2]) : null)));
        Def("log_softmax", new[] { "input", "dim", "dtype" }, p => R(torch.special.log_softmax(p.X(0), p.Has(1) ? TC.ToLong(p[1]!) : -1, p.Has(2) ? TC.ToDType(p[2]) : null)));
        Def("softsign", new[] { "input" }, p => { var x = p.X(0); return R(x / (x.abs() + 1)); });

        // linear / conv / pool
        Def("linear", new[] { "input", "weight", "bias" }, p => R(F.linear(p.X(0), p.X(1), p.XOpt(2))));
        Def("conv2d", new[] { "input", "weight", "bias", "stride", "padding", "dilation", "groups" }, p =>
        {
            var w = p.X(1); long[] stride = Pair(p[3], 1), dil = Pair(p[5], 1), pad;
            if (p.Has(4) && p[4] is string ps)
                pad = ps == "valid" ? new long[] { 0, 0 } : new[] { dil[0] * (w.shape[2] - 1) / 2, dil[1] * (w.shape[3] - 1) / 2 };
            else pad = Pair(p[4], 0);
            return R(F.conv2d(p.X(0), w, p.XOpt(2), stride, pad, dil, p.Has(6) ? TC.ToLong(p[6]!) : 1));
        });
        Def("conv1d", new[] { "input", "weight", "bias", "stride", "padding", "dilation", "groups" }, p =>
            R(F.conv1d(p.X(0), p.X(1), p.XOpt(2), Pair(p[3], 1, 1)[0], Pair(p[4], 0, 1)[0], Pair(p[5], 1, 1)[0], p.Has(6) ? TC.ToLong(p[6]!) : 1)));
        Def("conv_transpose2d", new[] { "input", "weight", "bias", "stride", "padding", "output_padding", "groups", "dilation" }, p =>
            R(F.conv_transpose2d(p.X(0), p.X(1), p.XOpt(2), Pair(p[3], 1), Pair(p[4], 0), Pair(p[5], 0), Pair(p[7], 1), p.Has(6) ? TC.ToLong(p[6]!) : 1)));
        Def("max_pool2d", new[] { "input", "kernel_size", "stride", "padding", "dilation", "ceil_mode", "return_indices" }, p =>
        {
            var k = Pair(p[1], 1);
            var s = p.Has(2) ? Pair(p[2], 1) : k;
            if (p.Bool(6, false))
            {
                var (v, idx) = F.max_pool2d_with_indices(p.X(0), k, s, Pair(p[3], 0), Pair(p[4], 1), p.Bool(5, false));
                return new PyTuple(new object[] { R(v), R(idx) });
            }
            return R(F.max_pool2d(p.X(0), k, s, Pair(p[3], 0), Pair(p[4], 1), p.Bool(5, false)));
        });
        Def("avg_pool2d", new[] { "input", "kernel_size", "stride", "padding", "ceil_mode", "count_include_pad", "divisor_override" }, p =>
        {
            var k = Pair(p[1], 1);
            return R(F.avg_pool2d(p.X(0), k, p.Has(2) ? Pair(p[2], 1) : k, Pair(p[3], 0), p.Bool(4, false), p.Bool(5, true), p.Has(6) ? TC.ToLong(p[6]!) : null));
        });
        Def("adaptive_avg_pool2d", new[] { "input", "output_size" }, p => R(F.adaptive_avg_pool2d(p.X(0), Pair(p[1], 1))));
        Def("adaptive_max_pool2d", new[] { "input", "output_size" }, p => R(F.adaptive_max_pool2d(p.X(0), Pair(p[1], 1))));
        Def("adaptive_avg_pool1d", new[] { "input", "output_size" }, p => R(F.adaptive_avg_pool1d(p.X(0), Pair(p[1], 1, 1)[0])));

        // normalization / regularization
        Def("batch_norm", new[] { "input", "running_mean", "running_var", "weight", "bias", "training", "momentum", "eps" }, p =>
            R(F.batch_norm(p.X(0), p.XOpt(1)!, p.XOpt(2)!, p.XOpt(3), p.XOpt(4), p.Bool(5, false), p.Double(6, 0.1), p.Double(7, 1e-5))));
        Def("layer_norm", new[] { "input", "normalized_shape", "weight", "bias", "eps" }, p =>
            R(F.layer_norm(p.X(0), TC.ToLongs(p.Required(1)), p.XOpt(2), p.XOpt(3), p.Double(4, 1e-5))));
        Def("group_norm", new[] { "input", "num_groups", "weight", "bias", "eps" }, p =>
            R(F.group_norm(p.X(0), TC.ToLong(p.Required(1)), p.XOpt(2), p.XOpt(3), p.Double(4, 1e-5))));
        Def("dropout", new[] { "input", "p", "training", "inplace" }, p => R(F.dropout(p.X(0), p.Double(1, 0.5), p.Bool(2, true))));
        Def("normalize", new[] { "input", "p", "dim", "eps" }, p => R(F.normalize(p.X(0), p.Double(1, 2.0), p.Has(2) ? TC.ToLong(p[2]!) : 1, p.Double(3, 1e-12))));
        Def("pad", new[] { "input", "pad", "mode", "value" }, p =>
        {
            string mode = p.Has(2) ? (string)p[2]! : "constant";
            var pm = mode switch { "reflect" => PaddingModes.Reflect, "replicate" => PaddingModes.Replicate, "circular" => PaddingModes.Circular, _ => PaddingModes.Constant };
            return R(F.pad(p.X(0), TC.ToLongs(p.Required(1)), pm, p.Double(3, 0.0)));
        });
        Def("interpolate", new[] { "input", "size", "scale_factor", "mode", "align_corners", "recompute_scale_factor", "antialias" }, p =>
        {
            string mode = p.Has(3) ? (string)p[3]! : "nearest";
            var im = mode switch
            {
                "bilinear" => InterpolationMode.Bilinear, "bicubic" => InterpolationMode.Bicubic, "linear" => InterpolationMode.Linear,
                "trilinear" => InterpolationMode.Trilinear, "area" => InterpolationMode.Area, "nearest-exact" => InterpolationMode.NearestExact,
                _ => InterpolationMode.Nearest,
            };
            long[]? size = p.Has(1) ? Pair(p[1], 1, (int)p.X(0).Dimensions - 2) : null;
            double[]? sf = null;
            if (p.Has(2))
            {
                if (p[2] is PyList or PyTuple)
                {
                    IEnumerable<object> seq = p[2] is PyList l ? l.Items : ((PyTuple)p[2]!).Items;
                    sf = seq.Select(v => TC.ToDouble(v)).ToArray();
                }
                else sf = Enumerable.Repeat(TC.ToDouble(p[2]!), (int)p.X(0).Dimensions - 2).ToArray();
            }
            return R(F.interpolate(p.X(0), size, sf, im, p.Has(4) ? p.Bool(4, false) : null, p.Bool(5, false), p.Bool(6, false)));
        });
        Def("one_hot", new[] { "input", "num_classes" }, p => R(F.one_hot(p.X(0), p.Has(1) ? TC.ToLong(p[1]!) : -1)));
        Def("unfold", new[] { "input", "kernel_size", "dilation", "padding", "stride" }, p =>
            R(F.unfold(p.X(0), Pair(p[1], 1).ToValueTuple2(), Pair(p[2], 1).ToValueTuple2(), Pair(p[3], 0).ToValueTuple2(), Pair(p[4], 1).ToValueTuple2())));
        Def("cosine_similarity", new[] { "x1", "x2", "dim", "eps" }, p => R(F.cosine_similarity(p.X(0), p.X(1), p.Has(2) ? TC.ToLong(p[2]!) : 1, p.Double(3, 1e-8))));

        // losses
        Def("mse_loss", new[] { "input", "target", "size_average", "reduce", "reduction" }, p =>
        {
            var x = p.X(0); var t = TC.Operand(x, p.Required(1));
            return R(F.mse_loss(x, t, Red(p, 4)));
        });
        Def("l1_loss", new[] { "input", "target", "size_average", "reduce", "reduction" }, p => R(F.l1_loss(p.X(0), p.X(1), Red(p, 4))));
        Def("smooth_l1_loss", new[] { "input", "target", "size_average", "reduce", "reduction", "beta" }, p => R(F.smooth_l1_loss(p.X(0), p.X(1), Red(p, 4), p.Double(5, 1.0))));
        Def("huber_loss", new[] { "input", "target", "reduction", "delta" }, p => R(F.huber_loss(p.X(0), p.X(1), p.Double(3, 1.0), Red(p, 2))));
        Def("cross_entropy", new[] { "input", "target", "weight", "size_average", "ignore_index", "reduce", "reduction", "label_smoothing" }, p =>
            R(F.cross_entropy(p.X(0), p.X(1), p.XOpt(2), p.Has(4) ? TC.ToLong(p[4]!) : -100, Red(p, 6), p.Double(7, 0.0))));
        Def("nll_loss", new[] { "input", "target", "weight", "size_average", "ignore_index", "reduce", "reduction" }, p =>
            R(F.nll_loss(p.X(0), p.X(1), p.XOpt(2), Red(p, 6))));
        Def("binary_cross_entropy", new[] { "input", "target", "weight", "size_average", "reduce", "reduction" }, p =>
            R(F.binary_cross_entropy(p.X(0), p.X(1), p.XOpt(2), Red(p, 5))));
        Def("binary_cross_entropy_with_logits", new[] { "input", "target", "weight", "size_average", "reduce", "reduction", "pos_weight" }, p =>
            R(F.binary_cross_entropy_with_logits(p.X(0), p.X(1), p.XOpt(2), Red(p, 5), p.XOpt(6))));
        Def("kl_div", new[] { "input", "target", "size_average", "reduce", "reduction", "log_target" }, p => R(F.kl_div(p.X(0), p.X(1), p.Bool(5, false), Red(p, 4))));
        Def("embedding", new[] { "input", "weight", "padding_idx" }, p =>
        {
            var idx = p.X(0); var w = p.X(1);
            return R(w.index_select(0, idx.reshape(-1)).reshape(idx.shape.Concat(new[] { w.shape[1] }).ToArray()));
        });
        return m;
    }

    private static (long, long) ToValueTuple2(this long[] v) => (v[0], v[1]);

    // ------------------------------------------------------------------------------------------ nn.init

    private static torch.nn.init.NonlinearityType Nl(string s) => s.ToLowerInvariant() switch
    {
        "relu" => torch.nn.init.NonlinearityType.ReLU, "leaky_relu" => torch.nn.init.NonlinearityType.LeakyReLU,
        "tanh" => torch.nn.init.NonlinearityType.Tanh, "sigmoid" => torch.nn.init.NonlinearityType.Sigmoid,
        "linear" => torch.nn.init.NonlinearityType.Linear, "conv2d" => torch.nn.init.NonlinearityType.Conv2D,
        "conv1d" => torch.nn.init.NonlinearityType.Conv1D, "selu" => torch.nn.init.NonlinearityType.ReLU,
        _ => throw PyErr.ValueError($"Unsupported nonlinearity {s}"),
    };

    public static PyModule CreateInit()
    {
        var m = new PyModule("torch.nn.init");
        void Def(string name, string[] ps, Func<Args, object> fn)
            => m.Dict[name] = Ops.Fn(name, (i, a, kw) => { using var _ = torch.no_grad(); return fn(new Args(name, i, a, kw, ps)); });
        object Self(Args p) => p.Required(0);
        Def("calculate_gain", new[] { "nonlinearity", "param" }, p => torch.nn.init.calculate_gain(Nl((string)p.Required(0)), p.Double(1, 0.01)));
        Def("xavier_uniform_", new[] { "tensor", "gain", "generator" }, p => { torch.nn.init.xavier_uniform_(p.X(0), p.Double(1, 1.0)); return Self(p); });
        Def("xavier_normal_", new[] { "tensor", "gain", "generator" }, p => { torch.nn.init.xavier_normal_(p.X(0), p.Double(1, 1.0)); return Self(p); });
        Def("kaiming_uniform_", new[] { "tensor", "a", "mode", "nonlinearity", "generator" }, p =>
        {
            torch.nn.init.kaiming_uniform_(p.X(0), p.Double(1, 0.0), p.Has(2) && (string)p[2]! == "fan_out" ? torch.nn.init.FanInOut.FanOut : torch.nn.init.FanInOut.FanIn, Nl(p.Has(3) ? (string)p[3]! : "leaky_relu"));
            return Self(p);
        });
        Def("kaiming_normal_", new[] { "tensor", "a", "mode", "nonlinearity", "generator" }, p =>
        {
            torch.nn.init.kaiming_normal_(p.X(0), p.Double(1, 0.0), p.Has(2) && (string)p[2]! == "fan_out" ? torch.nn.init.FanInOut.FanOut : torch.nn.init.FanInOut.FanIn, Nl(p.Has(3) ? (string)p[3]! : "leaky_relu"));
            return Self(p);
        });
        Def("normal_", new[] { "tensor", "mean", "std", "generator" }, p => { p.X(0).normal_(p.Double(1, 0.0), p.Double(2, 1.0)); return Self(p); });
        Def("uniform_", new[] { "tensor", "a", "b", "generator" }, p => { p.X(0).uniform_(p.Double(1, 0.0), p.Double(2, 1.0)); return Self(p); });
        Def("trunc_normal_", new[] { "tensor", "mean", "std", "a", "b", "generator" }, p => { torch.nn.init.trunc_normal_(p.X(0), p.Double(1, 0.0), p.Double(2, 1.0), p.Double(3, -2.0), p.Double(4, 2.0)); return Self(p); });
        Def("zeros_", new[] { "tensor" }, p => { using var _ = torch.no_grad(); p.X(0).zero_(); return Self(p); });
        Def("ones_", new[] { "tensor" }, p => { using var _ = torch.no_grad(); p.X(0).fill_(1); return Self(p); });
        Def("constant_", new[] { "tensor", "val" }, p => { using var _ = torch.no_grad(); p.X(0).fill_(p.Double(1, 0.0)); return Self(p); });
        Def("eye_", new[] { "tensor" }, p => { using var _ = torch.no_grad(); var t = p.X(0); t.copy_(torch.eye(t.shape[0], t.shape[1])); return Self(p); });
        return m;
    }
}
