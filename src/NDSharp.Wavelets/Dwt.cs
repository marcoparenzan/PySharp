// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Wavelets;

/// <summary>A discrete wavelet's filter bank (pywt.Wavelet).</summary>
public sealed class Wavelet
{
    public string Name { get; }
    public double[] DecLo { get; }
    public double[] DecHi { get; }
    public double[] RecLo { get; }
    public double[] RecHi { get; }
    public bool Orthogonal { get; }
    public int FilterLength => DecLo.Length;

    private Wavelet(string name, double[] dl, double[] dh, double[] rl, double[] rh, bool orth)
        => (Name, DecLo, DecHi, RecLo, RecHi, Orthogonal) = (name, dl, dh, rl, rh, orth);

    public static IReadOnlyList<string> Names => WaveletFilters.All.Select(w => w.Name).ToList();

    public static Wavelet Get(string name)
    {
        foreach (var w in WaveletFilters.All)
            if (w.Name == name) return new Wavelet(w.Name, w.DecLo, w.DecHi, w.RecLo, w.RecHi, w.Orthogonal);
        throw new NDValueException($"Unknown wavelet name '{name}', check wavelist() for the list of available builtin wavelets.");
    }
}

/// <summary>pywt.dwt / idwt / dwt2 / idwt2 — a port of PyWavelets' convolution-based transforms (same extension
/// modes, same output lengths), computed in double precision.</summary>
public static class Dwt
{
    public static int CoeffLength(int inputLen, int filterLen, string mode)
        => mode == "periodization" ? (inputLen + 1) / 2 : (inputLen + filterLen - 1) / 2;

    private static int Mod(int a, int n) => ((a % n) + n) % n;

    /// <summary>Value of the signal extended beyond its ends according to a PyWavelets mode.</summary>
    private static double Ext(double[] x, int i, string mode)
    {
        int n = x.Length;
        if (i >= 0 && i < n) return x[i];
        switch (mode)
        {
            case "zero": return 0.0;
            case "constant": return i < 0 ? x[0] : x[n - 1];
            case "symmetric":
            {
                int m = Mod(i, 2 * n);
                return m < n ? x[m] : x[2 * n - 1 - m];
            }
            case "reflect":
            {
                if (n == 1) return x[0];
                int m = Mod(i, 2 * n - 2);
                return m < n ? x[m] : x[2 * n - 2 - m];
            }
            case "periodic": return x[Mod(i, n)];
            case "antisymmetric":
            {
                int q = (int)Math.Floor((double)i / n);
                int r = i - q * n;
                double v = (q & 1) == 0 ? x[r] : x[n - 1 - r];
                return (q & 1) == 0 ? v : -v;
            }
            case "antireflect":
                return i < 0 ? 2 * x[0] - Ext(x, -i, mode) : 2 * x[n - 1] - Ext(x, 2 * (n - 1) - i, mode);
            case "smooth":
                if (n == 1) return x[0];
                return i < 0 ? x[0] + i * (x[1] - x[0]) : x[n - 1] + (i - (n - 1)) * (x[n - 1] - x[n - 2]);
            default:
                throw new NDValueException($"Unknown mode '{mode}'");
        }
    }

    /// <summary>One level of the 1-D transform of a signal: (approximation, detail).</summary>
    public static (double[] cA, double[] cD) Dwt1D(double[] x, Wavelet w, string mode = "symmetric")
    {
        int f = w.FilterLength, n = x.Length;
        if (n < 1) throw new NDValueException("Invalid input data length");
        int outLen = CoeffLength(n, f, mode);
        var a = new double[outLen];
        var d = new double[outLen];
        if (mode == "periodization")
        {
            var xe = n % 2 == 1 ? x.Append(x[^1]).ToArray() : x;
            int ne = xe.Length;
            for (int k = 0; k < outLen; k++)
            {
                double sa = 0, sd = 0;
                for (int j = 0; j < f; j++)
                {
                    double v = xe[Mod(2 * k + f / 2 - j, ne)];
                    sa += w.DecLo[j] * v;
                    sd += w.DecHi[j] * v;
                }
                a[k] = sa; d[k] = sd;
            }
            return (a, d);
        }
        for (int k = 0; k < outLen; k++)
        {
            double sa = 0, sd = 0;
            for (int j = 0; j < f; j++)
            {
                double v = Ext(x, 2 * k + 1 - j, mode);
                sa += w.DecLo[j] * v;
                sd += w.DecHi[j] * v;
            }
            a[k] = sa; d[k] = sd;
        }
        return (a, d);
    }

    /// <summary>Inverse of <see cref="Dwt1D"/>; either coefficient array may be null (treated as zero).</summary>
    public static double[] Idwt1D(double[]? cA, double[]? cD, Wavelet w, string mode = "symmetric")
    {
        if (cA is null && cD is null) throw new NDValueException("At least one coefficient parameter must be specified.");
        int m = (cA ?? cD)!.Length;
        if (cA is not null && cD is not null && cA.Length != cD.Length)
            throw new NDValueException("Coefficient arrays must have the same length");
        int f = w.FilterLength;
        if (mode == "periodization")
        {
            var outp = new double[2 * m];
            for (int i = 0; i < 2 * m; i++)
            {
                double s = 0;
                for (int k = 0; k < m; k++)
                {
                    // A filter longer than the signal wraps around more than once: sum every aliased tap.
                    for (int j = Mod(i - 2 * k + f / 2 - 1, 2 * m); j < f; j += 2 * m)
                    {
                        if (cA is not null) s += cA[k] * w.RecLo[j];
                        if (cD is not null) s += cD[k] * w.RecHi[j];
                    }
                }
                outp[i] = s;
            }
            return outp;
        }
        int len = 2 * m - f + 2;
        if (len < 1) throw new NDValueException("Coefficient arrays are too short for this wavelet");
        var y = new double[len];
        for (int i = 0; i < len; i++)
        {
            double s = 0;
            for (int k = 0; k < m; k++)
            {
                int j = i + f - 2 - 2 * k;
                if (j < 0 || j >= f) continue;
                if (cA is not null) s += cA[k] * w.RecLo[j];
                if (cD is not null) s += cD[k] * w.RecHi[j];
            }
            y[i] = s;
        }
        return y;
    }

    // ------------------------------------------------------------------ N-d helpers

    private static NDArray AsFloat(NDArray a) => a.DType is DType.Float32 or DType.Float64 ? a : a.AsType(DType.Float64);

    /// <summary>pywt.dwt along <paramref name="axis"/> of an array.</summary>
    public static (NDArray cA, NDArray cD) DwtAxis(NDArray data, Wavelet w, string mode, int axis)
    {
        var src = AsFloat(data);
        int ax = np.NormalizeAxis(axis, src.Ndim);
        var moved = np.MoveAxis(src.AsType(DType.Float64), ax, -1).Copy();
        int n = moved.Shape[^1];
        int outLen = CoeffLength(n, w.FilterLength, mode);
        int lines = moved.Size / Math.Max(n, 1);
        var buf = (double[])moved.Buffer;
        var ra = new double[lines * outLen];
        var rd = new double[lines * outLen];
        var line = new double[n];
        for (int l = 0; l < lines; l++)
        {
            Array.Copy(buf, l * n, line, 0, n);
            var (a, d) = Dwt1D(line, w, mode);
            Array.Copy(a, 0, ra, l * outLen, outLen);
            Array.Copy(d, 0, rd, l * outLen, outLen);
        }
        var shape = moved.Shape[..^1].Concat(new[] { outLen }).ToArray();
        NDArray Back(double[] r)
        {
            var arr = new NDArray(DType.Float64, r, (int[])shape.Clone());
            arr = np.MoveAxis(arr, -1, ax).Copy();
            return src.DType == DType.Float32 ? arr.AsType(DType.Float32) : arr;
        }
        return (Back(ra), Back(rd));
    }

    public static NDArray IdwtAxis(NDArray? cA, NDArray? cD, Wavelet w, string mode, int axis)
    {
        var any = AsFloat((cA ?? cD)!);
        int ax = np.NormalizeAxis(axis, any.Ndim);
        NDArray? Prep(NDArray? c) => c is null ? null : np.MoveAxis(AsFloat(c).AsType(DType.Float64), ax, -1).Copy();
        var pa = Prep(cA);
        var pd = Prep(cD);
        var shape0 = (pa ?? pd)!.Shape;
        int m = shape0[^1];
        int outLen = mode == "periodization" ? 2 * m : 2 * m - w.FilterLength + 2;
        int lines = (pa ?? pd)!.Size / Math.Max(m, 1);
        var res = new double[lines * outLen];
        var la = new double[m];
        var ld = new double[m];
        for (int l = 0; l < lines; l++)
        {
            if (pa is not null) Array.Copy((double[])pa.Buffer, l * m, la, 0, m);
            if (pd is not null) Array.Copy((double[])pd.Buffer, l * m, ld, 0, m);
            var y = Idwt1D(pa is null ? null : la, pd is null ? null : ld, w, mode);
            Array.Copy(y, 0, res, l * outLen, outLen);
        }
        var arr = new NDArray(DType.Float64, res, shape0[..^1].Concat(new[] { outLen }).ToArray());
        arr = np.MoveAxis(arr, -1, ax).Copy();
        return any.DType == DType.Float32 ? arr.AsType(DType.Float32) : arr;
    }

    /// <summary>pywt.dwt2: (cA, (cH, cV, cD)) with cH = detail along axis 0, cV = detail along axis 1.</summary>
    public static (NDArray cA, (NDArray cH, NDArray cV, NDArray cD)) Dwt2(NDArray data, Wavelet w, string mode = "symmetric")
    {
        var (a0, d0) = DwtAxis(data, w, mode, -2);   // first letter = axis 0
        var (aa, ad) = DwtAxis(a0, w, mode, -1);
        var (da, dd) = DwtAxis(d0, w, mode, -1);
        return (aa, (da, ad, dd));
    }

    public static NDArray Idwt2(NDArray cA, (NDArray cH, NDArray cV, NDArray cD) details, Wavelet w, string mode = "symmetric")
    {
        var a0 = IdwtAxis(cA, details.cV, w, mode, -1);
        var d0 = IdwtAxis(details.cH, details.cD, w, mode, -1);
        return IdwtAxis(a0, d0, w, mode, -2);
    }
}
