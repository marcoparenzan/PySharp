// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp;

/// <summary><c>numpy.fft</c>. Power-of-two lengths use an iterative radix-2 transform, other lengths
/// Bluestein's algorithm; twiddle factors come from exact quadrant/octant reduction so values such as
/// <c>cos(pi/2)</c> are exactly 0 (as in pocketfft) instead of 6e-17.</summary>
public static class Fft
{
    /// <summary>cos and sin of 2*pi*k/n with octant symmetry (exact zeros, ones and sqrt(1/2)).</summary>
    private static Complex Twiddle(long k, long n)
    {
        long m = ((k % n) + n) % n;
        long four = 4 * m;
        int q = (int)(four / n);
        long r = four - q * n;            // remainder in [0, n): angle within the quadrant is (pi/2) * r / n
        double c, s;
        if (r == 0) { c = 1.0; s = 0.0; }
        else if (2 * r == n) { c = s = Math.Sqrt(0.5); }
        else if (2 * r < n)
        {
            double phi = Math.PI / 2 * ((double)r / n);
            c = Math.Cos(phi); s = Math.Sin(phi);
        }
        else
        {
            double phi = Math.PI / 2 * ((double)(n - r) / n);
            c = Math.Sin(phi); s = Math.Cos(phi);
        }
        return q switch
        {
            0 => new Complex(c, s),
            1 => new Complex(-s, c),
            2 => new Complex(-c, -s),
            _ => new Complex(s, -c),
        };
    }

    private static bool IsPow2(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>In-place forward DFT (sign -1) of a power-of-two length.</summary>
    private static void Radix2(Complex[] x)
    {
        int n = x.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (x[i], x[j]) = (x[j], x[i]);
        }
        var w = new Complex[Math.Max(n / 2, 1)];
        for (int j = 0; j < n / 2; j++) { var t = Twiddle(j, n); w[j] = new Complex(t.Real, -t.Imaginary); }
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len / 2, step = n / len;
            for (int i = 0; i < n; i += len)
                for (int j = 0; j < half; j++)
                {
                    var u = x[i + j];
                    var v = x[i + j + half] * w[j * step];
                    x[i + j] = u + v;
                    x[i + j + half] = u - v;
                }
        }
    }

    /// <summary>Forward DFT of any length, in place.</summary>
    private static void Forward(Complex[] x)
    {
        int n = x.Length;
        if (n <= 1) return;
        if (IsPow2(n)) { Radix2(x); return; }
        // Bluestein: X[k] = c[k] * sum_j (x[j] c[j]) conj(c[k-j]),  c[j] = exp(-i*pi*j^2/n)
        int m = 1;
        while (m < 2 * n - 1) m <<= 1;
        var chirp = new Complex[n];
        for (int k = 0; k < n; k++)
        {
            var t = Twiddle((long)k * k % (2L * n), 2L * n);
            chirp[k] = new Complex(t.Real, -t.Imaginary);
        }
        var a = new Complex[m];
        var b = new Complex[m];
        for (int k = 0; k < n; k++) a[k] = x[k] * chirp[k];
        b[0] = Complex.Conjugate(chirp[0]);
        for (int k = 1; k < n; k++) b[k] = b[m - k] = Complex.Conjugate(chirp[k]);
        Radix2(a);
        Radix2(b);
        for (int k = 0; k < m; k++) a[k] *= b[k];
        // inverse via conjugation
        for (int k = 0; k < m; k++) a[k] = Complex.Conjugate(a[k]);
        Radix2(a);
        for (int k = 0; k < n; k++) x[k] = chirp[k] * Complex.Conjugate(a[k]) / m;
    }

    /// <summary>The transform of real data is Hermitian: numpy computes only half of it with a real
    /// transform, so the DC (and Nyquist) terms are exactly real and the upper half mirrors the lower.</summary>
    private static void MakeHermitian(Complex[] x)
    {
        int n = x.Length;
        x[0] = new Complex(x[0].Real, 0.0);
        if (n % 2 == 0 && n > 1) x[n / 2] = new Complex(x[n / 2].Real, 0.0);
        for (int i = 1; i <= (n - 1) / 2; i++)
            x[n - i] = new Complex(x[i].Real, -x[i].Imaginary + 0.0);
        for (int i = 0; i < n; i++) x[i] = new Complex(x[i].Real, x[i].Imaginary + 0.0); // -0.0 -> +0.0
    }

    private static void Transform(Complex[] x, bool inverse)
    {
        if (!inverse) { Forward(x); return; }
        for (int i = 0; i < x.Length; i++) x[i] = Complex.Conjugate(x[i]);
        Forward(x);
        for (int i = 0; i < x.Length; i++) x[i] = Complex.Conjugate(x[i]);
    }

    private static DType OutputType(NDArray a) => a.DType is DType.Float32 or DType.Float16 or DType.Complex64
        or DType.Int8 or DType.UInt8 or DType.Int16 or DType.UInt16 or DType.Bool ? DType.Complex64 : DType.Complex128;

    private static double Scale(string? norm, int n, bool inverse)
    {
        switch (norm)
        {
            case null or "backward": return inverse ? 1.0 / n : 1.0;
            case "ortho": return 1.0 / Math.Sqrt(n);
            case "forward": return inverse ? 1.0 : 1.0 / n;
            default: throw new NDValueException($"Invalid norm value {norm}; should be \"backward\", \"ortho\" or \"forward\".");
        }
    }

    /// <summary>Crops or zero-pads <paramref name="a"/> to <paramref name="n"/> along <paramref name="axis"/>.</summary>
    private static NDArray Resize(NDArray a, int n, int axis)
    {
        int cur = a.Shape[axis];
        if (cur == n) return a;
        var idx = Enumerable.Repeat<NDIndex>(Slice.All, a.Ndim).ToArray();
        if (n < cur)
        {
            idx[axis] = new Slice(0, n);
            return a.Get(idx);
        }
        var shape = (int[])a.Shape.Clone();
        shape[axis] = n;
        var z = np.Zeros(shape, a.DType);
        idx[axis] = new Slice(0, cur);
        z.Put(a, idx);
        return z;
    }

    private static NDArray Along(NDArray input, int? n, int axis, string? norm, bool inverse)
    {
        if (input.Ndim == 0) throw new NDValueException("Invalid number of FFT data points (0) specified.");
        int ax = np.NormalizeAxis(axis, input.Ndim);
        var outType = OutputType(input);
        var a = input.CastTo(outType);
        int len = n ?? a.Shape[ax];
        if (len < 1) throw new NDValueException($"Invalid number of FFT data points ({len}) specified.");
        a = Resize(a, len, ax);
        var moved = np.MoveAxis(a, ax, -1).Copy();
        var buf = (Complex[])moved.Buffer;
        double scale = Scale(norm, len, inverse);
        bool realInput = !input.DType.IsComplex() && !inverse;
        var line = new Complex[len];
        for (int s = 0; s < buf.Length; s += len)
        {
            Array.Copy(buf, s, line, 0, len);
            Transform(line, inverse);
            if (realInput) MakeHermitian(line);
            for (int i = 0; i < len; i++) buf[s + i] = scale == 1.0 ? line[i] : line[i] * scale;
        }
        if (outType == DType.Complex64) Cx.RoundC64(buf);
        return np.MoveAxis(moved, -1, ax).Copy();
    }

    public static NDArray FFT(NDArray a, int? n = null, int axis = -1, string? norm = null) => Along(a, n, axis, norm, false);
    public static NDArray IFFT(NDArray a, int? n = null, int axis = -1, string? norm = null) => Along(a, n, axis, norm, true);

    private static NDArray Multi(NDArray a, int[]? s, int[]? axes, string? norm, bool inverse, int defaultAxes)
    {
        int[] ax = axes ?? (defaultAxes > 0
            ? Enumerable.Range(a.Ndim - defaultAxes, defaultAxes).ToArray()
            : Enumerable.Range(0, a.Ndim).ToArray());
        if (s is not null && s.Length != ax.Length)
            throw new NDValueException("Shape and axes have different lengths.");
        var r = a;
        for (int i = ax.Length - 1; i >= 0; i--)
            r = Along(r, s?[i], ax[i], norm, inverse);
        return r;
    }

    public static NDArray FFT2(NDArray a, int[]? s = null, int[]? axes = null, string? norm = null) => Multi(a, s, axes ?? new[] { -2, -1 }, norm, false, 0);
    public static NDArray IFFT2(NDArray a, int[]? s = null, int[]? axes = null, string? norm = null) => Multi(a, s, axes ?? new[] { -2, -1 }, norm, true, 0);
    public static NDArray FFTN(NDArray a, int[]? s = null, int[]? axes = null, string? norm = null) => Multi(a, s, axes, norm, false, 0);
    public static NDArray IFFTN(NDArray a, int[]? s = null, int[]? axes = null, string? norm = null) => Multi(a, s, axes, norm, true, 0);

    /// <summary>numpy <c>rfft</c>: the first n/2+1 coefficients of the transform of real input.</summary>
    public static NDArray RFFT(NDArray a, int? n = null, int axis = -1, string? norm = null)
    {
        if (a.DType.IsComplex()) throw new NDTypeException("rfft requires real input");
        var full = Along(a, n, axis, norm, false); // real input: already exactly Hermitian (see MakeHermitian)
        int ax = np.NormalizeAxis(axis, a.Ndim);
        int keep = full.Shape[ax] / 2 + 1;
        var idx = Enumerable.Repeat<NDIndex>(Slice.All, full.Ndim).ToArray();
        idx[ax] = new Slice(0, keep);
        return full.Get(idx).Copy();
    }

    /// <summary>numpy <c>irfft</c>: real signal from a Hermitian half-spectrum.</summary>
    public static NDArray IRFFT(NDArray a, int? n = null, int axis = -1, string? norm = null)
    {
        int ax = np.NormalizeAxis(axis, a.Ndim);
        int m = a.Shape[ax];
        int len = n ?? 2 * (m - 1);
        if (len < 1) throw new NDValueException($"Invalid number of FFT data points ({len}) specified.");
        var outType = OutputType(a);
        var half = Resize(a.CastTo(outType), len / 2 + 1, ax);
        var moved = np.MoveAxis(half, ax, -1).Copy();
        var src = (Complex[])moved.Buffer;
        int hl = len / 2 + 1;
        int lines = src.Length / hl;
        var result = new double[lines * len];
        var line = new Complex[len];
        double scale = Scale(norm, len, true);
        for (int l = 0; l < lines; l++)
        {
            for (int k = 0; k < hl; k++) line[k] = src[l * hl + k];
            line[0] = new Complex(line[0].Real, 0);
            if (len % 2 == 0) line[len / 2] = new Complex(line[len / 2].Real, 0);
            for (int k = hl; k < len; k++) line[k] = Complex.Conjugate(line[len - k]);
            Transform(line, true);
            for (int k = 0; k < len; k++) result[l * len + k] = line[k].Real * scale;
        }
        var shape = moved.Shape[..^1].Concat(new[] { len }).ToArray();
        var r = new NDArray(DType.Float64, result, shape);
        var real = outType == DType.Complex64 ? r.AsType(DType.Float32) : r;
        return np.MoveAxis(real, -1, ax).Copy();
    }

    public static NDArray FFTShift(NDArray a, int[]? axes = null)
    {
        var ax = axes ?? Enumerable.Range(0, a.Ndim).ToArray();
        var r = a;
        foreach (var x in ax) r = np.Roll(r, a.Shape[np.NormalizeAxis(x, a.Ndim)] / 2, x);
        return r;
    }

    public static NDArray IFFTShift(NDArray a, int[]? axes = null)
    {
        var ax = axes ?? Enumerable.Range(0, a.Ndim).ToArray();
        var r = a;
        foreach (var x in ax) r = np.Roll(r, -(a.Shape[np.NormalizeAxis(x, a.Ndim)] / 2), x);
        return r;
    }

    /// <summary>numpy <c>fftfreq(n, d)</c>.</summary>
    public static NDArray FFTFreq(int n, double d = 1.0)
    {
        if (n <= 0) throw new NDValueException("n should be an integer greater than 0");
        double val = 1.0 / (n * d);
        var r = new double[n];
        int pos = (n - 1) / 2 + 1;
        for (int i = 0; i < pos; i++) r[i] = i * val;
        for (int i = pos; i < n; i++) r[i] = (-(n / 2) + (i - pos)) * val;
        return NDArray.FromArray(r, n);
    }

    public static NDArray RFFTFreq(int n, double d = 1.0)
    {
        if (n <= 0) throw new NDValueException("n should be an integer greater than 0");
        double val = 1.0 / (n * d);
        var r = new double[n / 2 + 1];
        for (int i = 0; i < r.Length; i++) r[i] = i * val;
        return NDArray.FromArray(r, r.Length);
    }
}
