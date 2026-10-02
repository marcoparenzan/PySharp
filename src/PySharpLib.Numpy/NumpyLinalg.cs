// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary><c>numpy.linalg</c>.</summary>
internal static class NumpyLinalg
{
    public static PyModule Create()
    {
        var m = new PyModule("numpy.linalg");
        void Def(string name, BuiltinFn fn) => m.Dict[name] = Native.Fn(name, fn);

        m.Dict["norm"] = NumpyFunctions.All["norm"];
        m.Dict["LinAlgError"] = NumpyErrors.LinAlgError;
        Def("inv", (i, a, k) => Conv.Wrap(np.Inv(new Args("inv", i, a, k, "a").ND(0))));
        Def("solve", (i, a, k) =>
        {
            var p = new Args("solve", i, a, k, "a", "b");
            return Conv.Wrap(np.Solve(p.ND(0), p.ND(1)));
        });
        Def("det", (i, a, k) => Conv.Result(np.Det(new Args("det", i, a, k, "a").ND(0))));
        Def("slogdet", (i, a, k) =>
        {
            var (sign, logdet) = np.SlogDet(new Args("slogdet", i, a, k, "a").ND(0));
            return new PyTuple(new[] { Conv.Result(sign), Conv.Result(logdet) });
        });
        Def("eigh", (i, a, k) =>
        {
            var p = new Args("eigh", i, a, k, "a", "UPLO");
            var (w, v) = np.Eigh(p.ND(0));
            return new PyTuple(new object[] { Conv.Wrap(w), Conv.Wrap(v) });
        });
        Def("eigvalsh", (i, a, k) => Conv.Wrap(np.EigvalsH(new Args("eigvalsh", i, a, k, "a", "UPLO").ND(0))));
        Def("svd", (i, a, k) =>
        {
            var p = new Args("svd", i, a, k, "a", "full_matrices", "compute_uv", "hermitian");
            var r = np.Svd(p.ND(0), p.Bool(1, true));
            if (!p.Bool(2, true)) return Conv.Wrap(r.S);
            return new PyTuple(new object[] { Conv.Wrap(r.U), Conv.Wrap(r.S), Conv.Wrap(r.Vt) });
        });
        Def("lstsq", (i, a, k) =>
        {
            var p = new Args("lstsq", i, a, k, "a", "b", "rcond");
            double? rcond = p.Has(2) ? p.Double(2, 0) : null;
            var (x, res, rank, s) = np.LstSq(p.ND(0), p.ND(1), rcond);
            return new PyTuple(new object[] { Conv.Wrap(x), Conv.Wrap(res), new BigInteger(rank), Conv.Wrap(s) });
        });
        Def("matrix_rank", (i, a, k) =>
        {
            var p = new Args("matrix_rank", i, a, k, "A", "tol", "hermitian");
            double? tol = p.Has(1) ? p.Double(1, 0) : null;
            return new BigInteger(np.MatrixRank(p.ND(0), tol));
        });
        Def("pinv", (i, a, k) =>
        {
            var p = new Args("pinv", i, a, k, "a", "rcond", "hermitian");
            return Conv.Wrap(np.Pinv(p.ND(0), p.Has(1) ? p.Double(1, 0) : null));
        });
        return m;
    }
}
