// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary><c>numpy.fft</c> over NDSharp's Fft.</summary>
internal static class NumpyFft
{
    public static PyModule Create()
    {
        var m = new PyModule("numpy.fft");
        void Def(string name, BuiltinFn fn) => m.Dict[name] = Native.Fn(name, fn);
        static string? Norm(Args p, int i) => p.Has(i) ? (string)p[i]! : null;

        void OneD(string name, Func<NDArray, int?, int, string?, NDArray> f) => Def(name, (i, a, k) =>
        {
            var p = new Args(name, i, a, k, "a", "n", "axis", "norm", "out");
            return Conv.Wrap(f(p.ND(0), p.IntOrNull(1), p.Int(2, -1), Norm(p, 3)));
        });
        void Multi(string name, Func<NDArray, int[]?, int[]?, string?, NDArray> f) => Def(name, (i, a, k) =>
        {
            var p = new Args(name, i, a, k, "a", "s", "axes", "norm", "out");
            return Conv.Wrap(f(p.ND(0), p.Has(1) ? Conv.ToShape(p[1]!) : null, p.Axes(2), Norm(p, 3)));
        });

        OneD("fft", Fft.FFT);
        OneD("ifft", Fft.IFFT);
        OneD("rfft", Fft.RFFT);
        OneD("irfft", Fft.IRFFT);
        Multi("fft2", Fft.FFT2);
        Multi("ifft2", Fft.IFFT2);
        Multi("fftn", Fft.FFTN);
        Multi("ifftn", Fft.IFFTN);
        Def("fftshift", (i, a, k) =>
        {
            var p = new Args("fftshift", i, a, k, "x", "axes");
            return Conv.Wrap(Fft.FFTShift(p.ND(0), p.Axes(1)));
        });
        Def("ifftshift", (i, a, k) =>
        {
            var p = new Args("ifftshift", i, a, k, "x", "axes");
            return Conv.Wrap(Fft.IFFTShift(p.ND(0), p.Axes(1)));
        });
        Def("fftfreq", (i, a, k) =>
        {
            var p = new Args("fftfreq", i, a, k, "n", "d");
            return Conv.Wrap(Fft.FFTFreq(p.Int(0, 0), p.Double(1, 1.0)));
        });
        Def("rfftfreq", (i, a, k) =>
        {
            var p = new Args("rfftfreq", i, a, k, "n", "d");
            return Conv.Wrap(Fft.RFFTFreq(p.Int(0, 0), p.Double(1, 1.0)));
        });
        return m;
    }
}
