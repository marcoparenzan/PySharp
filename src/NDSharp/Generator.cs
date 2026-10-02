// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace NDSharp.Random;

/// <summary>numpy's <c>Generator</c> (<c>np.random.default_rng</c>): the same algorithms as numpy
/// (PCG64 bit stream, Lemire bounded integers, ziggurat normals, Floyd's sampling) so that a given
/// seed produces numpy's exact values.</summary>
public sealed class Generator
{
    private readonly PCG64 _bits;

    public Generator(PCG64 bits) => _bits = bits;

    /// <summary>numpy <c>default_rng(seed)</c>; a null seed draws OS entropy.</summary>
    public static Generator DefaultRng(IReadOnlyList<BigInteger>? seed = null) => new(new PCG64(new SeedSequence(seed)));

    // ================================================================ raw draws

    public double NextDouble() => _bits.NextDouble();

    private static int[] Shape(int[]? size) => size ?? Array.Empty<int>();

    /// <summary>numpy <c>random(size)</c>: float64 in [0, 1).</summary>
    public NDArray Random(int[]? size = null)
    {
        var shape = Shape(size);
        var buf = new double[NDArray.SizeOf(shape)];
        for (int i = 0; i < buf.Length; i++) buf[i] = _bits.NextDouble();
        return new NDArray(DType.Float64, buf, shape);
    }

    public NDArray Uniform(double low, double high, int[]? size = null)
    {
        var shape = Shape(size);
        var buf = new double[NDArray.SizeOf(shape)];
        double range = high - low;
        for (int i = 0; i < buf.Length; i++) buf[i] = low + range * _bits.NextDouble();
        return new NDArray(DType.Float64, buf, shape);
    }

    public NDArray StandardNormal(int[]? size = null) => Normal(0.0, 1.0, size);

    /// <summary>numpy <c>normal(loc, scale, size)</c> with array-valued parameters (broadcast against each other
    /// and <paramref name="size"/>); each element is <c>loc + scale * z</c> with z drawn in C order.</summary>
    public NDArray Normal(NDArray loc, NDArray scale, int[]? size = null)
    {
        var shape = size ?? Broadcasting.Shape(loc.Shape, scale.Shape);
        var z = StandardNormal(shape);
        return np.Add(loc.AsType(DType.Float64), np.Multiply(scale.AsType(DType.Float64), z));
    }

    /// <summary>numpy <c>uniform(low, high, size)</c> with array-valued bounds: <c>low + (high - low) * u</c>.</summary>
    public NDArray Uniform(NDArray low, NDArray high, int[]? size = null)
    {
        var l = low.AsType(DType.Float64);
        var range = np.Subtract(high.AsType(DType.Float64), l);
        var shape = size ?? Broadcasting.Shape(l.Shape, range.Shape);
        return np.Add(l, np.Multiply(range, Random(shape)));
    }

    public NDArray Normal(double loc, double scale, int[]? size = null)
    {
        var shape = Shape(size);
        var buf = new double[NDArray.SizeOf(shape)];
        for (int i = 0; i < buf.Length; i++) buf[i] = loc + scale * StandardNormalDouble();
        return new NDArray(DType.Float64, buf, shape);
    }

    // ================================================================ ziggurat normal

    private const double ZigR = 3.6541528853610087963519472518;
    private const double ZigInvR = 0.27366123732975827203338247596;
    private static readonly ulong[] Ki = new ulong[256];
    private static readonly double[] Wi = new double[256];
    private static readonly double[] Fi = new double[256];

    static Generator()
    {
        // Strip area consistent with R: v = r*f(r) + integral_r^inf f, f(x)=exp(-x^2/2) (Marsaglia-Tsang use
        // the 12-digit 0.00492867323399; numpy's tables were generated from the exact value).
        double ZigV = ZigR * Math.Exp(-0.5 * ZigR * ZigR) + Math.Sqrt(Math.PI / 2.0) * Erfc(ZigR / Math.Sqrt(2.0));
        // Marsaglia–Tsang layers for 256 strips, scaled to the 52-bit integers numpy draws.
        var x = new double[256];
        x[255] = ZigR;
        for (int i = 254; i >= 1; i--)
            x[i] = Math.Sqrt(-2.0 * Math.Log(ZigV / x[i + 1] + Math.Exp(-0.5 * x[i + 1] * x[i + 1])));
        x[0] = ZigV / Math.Exp(-0.5 * ZigR * ZigR);
        const double two52 = 4503599627370496.0;
        Ki[0] = (ulong)(ZigR / x[0] * two52);
        Ki[1] = 0;
        for (int i = 2; i < 256; i++) Ki[i] = (ulong)(x[i - 1] / x[i] * two52);
        for (int i = 0; i < 256; i++) Wi[i] = x[i] / two52;
        Fi[0] = 1.0;
        for (int i = 1; i < 256; i++) Fi[i] = Math.Exp(-0.5 * x[i] * x[i]);
    }

    /// <summary>erfc(x) for x &gt; 1 by its continued fraction (accurate to double precision).</summary>
    private static double Erfc(double x)
    {
        double f = x;
        for (int k = 200; k >= 1; k--)
            f = x + (k / 2.0) / f;
        return Math.Exp(-x * x) / Math.Sqrt(Math.PI) / f;
    }

    private double StandardNormalDouble()
    {
        while (true)
        {
            ulong r = _bits.Next64();
            int idx = (int)(r & 0xff);
            r >>= 8;
            int sign = (int)(r & 0x1);
            ulong rabs = (r >> 1) & 0x000fffffffffffffUL;
            double x = rabs * Wi[idx];
            if (sign != 0) x = -x;
            if (rabs < Ki[idx]) return x;
            if (idx == 0)
            {
                while (true)
                {
                    double xx = -ZigInvR * Math.Log(1.0 - _bits.NextDouble());
                    double yy = -Math.Log(1.0 - _bits.NextDouble());
                    if (yy + yy > xx * xx)
                        return ((rabs >> 8) & 0x1) != 0 ? -(ZigR + xx) : ZigR + xx;
                }
            }
            if ((Fi[idx - 1] - Fi[idx]) * _bits.NextDouble() + Fi[idx] < Math.Exp(-0.5 * x * x))
                return x;
        }
    }

    // ================================================================ bounded integers

    /// <summary>Lemire's nearly-divisionless bounded 32-bit integer in [0, rng] (numpy's
    /// <c>buffered_bounded_lemire_uint32</c> with the 32-bit stream).</summary>
    private uint BoundedLemire32(uint rng)
    {
        uint rngExcl = rng + 1;
        ulong m = (ulong)_bits.Next32() * rngExcl;
        uint leftover = (uint)m;
        if (leftover < rngExcl)
        {
            uint threshold = (uint.MaxValue - rng) % rngExcl;
            while (leftover < threshold)
            {
                m = (ulong)_bits.Next32() * rngExcl;
                leftover = (uint)m;
            }
        }
        return (uint)(m >> 32);
    }

    private ulong BoundedLemire64(ulong rng)
    {
        ulong rngExcl = rng + 1;
        UInt128 m = (UInt128)_bits.Next64() * rngExcl;
        ulong leftover = (ulong)m;
        if (leftover < rngExcl)
        {
            ulong threshold = (ulong.MaxValue - rng) % rngExcl;
            while (leftover < threshold)
            {
                m = (UInt128)_bits.Next64() * rngExcl;
                leftover = (ulong)m;
            }
        }
        return (ulong)(m >> 64);
    }

    /// <summary>numpy's <c>buffered_uint8/16</c>: slices of one 32-bit draw (3 more bytes / 1 more half-word).</summary>
    private uint BufferedPiece(int bits, ref int bcnt, ref uint buf)
    {
        if (bcnt == 0)
        {
            buf = _bits.Next32();
            bcnt = bits == 8 ? 3 : 1;
        }
        else
        {
            buf >>= bits;
            bcnt--;
        }
        return buf & (bits == 8 ? 0xFFu : 0xFFFFu);
    }

    /// <summary>numpy's <c>buffered_bounded_lemire_uint8/16</c> (or the unbounded draw when rng is the full range).</summary>
    private uint BoundedSmall(uint rng, int bits, ref int bcnt, ref uint buf)
    {
        uint max = bits == 8 ? 0xFFu : 0xFFFFu;
        if (rng == 0) return 0;
        if (rng == max) return BufferedPiece(bits, ref bcnt, ref buf);
        uint rngExcl = rng + 1;
        uint m = BufferedPiece(bits, ref bcnt, ref buf) * rngExcl;
        uint leftover = m & max;
        if (leftover < rngExcl)
        {
            uint threshold = (max - rng) % rngExcl;
            while (leftover < threshold)
            {
                m = BufferedPiece(bits, ref bcnt, ref buf) * rngExcl;
                leftover = m & max;
            }
        }
        return m >> bits;
    }

    /// <summary>A value in [0, rng] (numpy's <c>random_bounded_uint64</c> / the fill loops).</summary>
    private ulong Bounded(ulong rng)
    {
        if (rng == 0) return 0;
        if (rng <= 0xFFFFFFFFUL)
            return rng == 0xFFFFFFFFUL ? _bits.Next32() : BoundedLemire32((uint)rng);
        if (rng == ulong.MaxValue) return _bits.Next64();
        return BoundedLemire64(rng);
    }

    /// <summary>numpy <c>random_interval(max)</c>: masked rejection, used by shuffle.</summary>
    private ulong Interval(ulong max)
    {
        if (max == 0) return 0;
        ulong mask = max;
        mask |= mask >> 1; mask |= mask >> 2; mask |= mask >> 4; mask |= mask >> 8; mask |= mask >> 16; mask |= mask >> 32;
        ulong value;
        if (max <= 0xFFFFFFFFUL)
            while ((value = _bits.Next32() & mask) > max) { }
        else
            while ((value = _bits.Next64() & mask) > max) { }
        return value;
    }

    /// <summary>numpy <c>integers(low, high, size, dtype, endpoint)</c> for scalar bounds.</summary>
    public NDArray Integers(long low, long high, int[]? size = null, DType dtype = DType.Int64, bool endpoint = false)
    {
        if (!dtype.IsInteger()) throw new NDTypeException($"Unsupported dtype {dtype.Name()} for integers");
        if (!endpoint)
        {
            if (high <= low) throw new NDValueException("low >= high");
            high -= 1;
        }
        else if (high < low) throw new NDValueException("low > high");
        ulong rng = unchecked((ulong)(high - low));
        var shape = Shape(size);
        int n = NDArray.SizeOf(shape);
        var raw = new long[n];
        if (dtype.ItemSize() == 1 && rng <= 0xFF)
        {
            // numpy's uint8 fill: four bytes per 32-bit draw (buffered), Lemire on 8 bits.
            uint buf = 0; int bcnt = 0;
            for (int i = 0; i < n; i++)
                raw[i] = unchecked(low + (long)BoundedSmall((uint)rng, 8, ref bcnt, ref buf));
        }
        else if (dtype.ItemSize() == 2 && rng <= 0xFFFF)
        {
            uint buf = 0; int bcnt = 0;
            for (int i = 0; i < n; i++)
                raw[i] = unchecked(low + (long)BoundedSmall((uint)rng, 16, ref bcnt, ref buf));
        }
        else
            for (int i = 0; i < n; i++)
                raw[i] = unchecked(low + (long)Bounded(rng));
        var result = NDArray.FromArray(raw, shape);
        return dtype == DType.Int64 ? result : result.AsType(dtype);
    }

    /// <summary>numpy <c>multivariate_normal</c> (method 'svd'): rows ~ N(mean, cov). Reproduces numpy's draws when
    /// the SVD's vector signs agree with LAPACK's (see NOTEBOOKS_PLAN.md known divergences).</summary>
    public NDArray MultivariateNormal(NDArray mean, NDArray cov, int[]? size = null)
    {
        if (mean.Ndim != 1) throw new NDValueException("mean must be 1 dimensional");
        int n = mean.Shape[0];
        if (cov.Ndim != 2 || cov.Shape[0] != n || cov.Shape[1] != n) throw new NDValueException("mean and cov must have same length");
        var finalShape = (size ?? Array.Empty<int>()).Concat(new[] { n }).ToArray();
        var z = np.Reshape(StandardNormal(finalShape), -1, n);
        var svd = np.Svd(cov.AsType(DType.Float64));
        var transform = np.Multiply(np.ExpandDims(np.Sqrt(svd.S), 1), svd.Vt);
        var x = np.Add(np.MatMul(z, transform), mean.AsType(DType.Float64));
        return np.Reshape(x, finalShape);
    }

    // ================================================================ shuffling & sampling

    /// <summary>Fisher–Yates. numpy's <c>shuffle</c>/<c>permutation</c> draw j with <c>random_interval</c>
    /// (masked rejection); the shuffle inside <c>choice(replace=False)</c> uses Lemire's bounded draw.</summary>
    private void ShuffleInts(long[] data, bool lemire = false)
    {
        for (int i = data.Length - 1; i > 0; i--)
        {
            int j = (int)(lemire ? Bounded((ulong)i) : Interval((ulong)i));
            (data[i], data[j]) = (data[j], data[i]);
        }
    }

    /// <summary>numpy <c>shuffle</c> along axis 0, in place.</summary>
    public void Shuffle(NDArray x)
    {
        if (x.Ndim == 0) throw new NDTypeException("shuffle() requires an array with at least one dimension");
        int n = x.Shape[0];
        if (x.Ndim == 1)
        {
            var copy = x.Copy();
            var idx = new long[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            // Swap elements exactly as numpy's _shuffle_raw does, on a scratch copy of the values.
            var vals = copy;
            for (int i = n - 1; i > 0; i--)
            {
                int j = (int)Interval((ulong)i);
                var a = vals.Get(i).Copy();
                var b = vals.Get(j).Copy();
                vals.Put(b, i);
                vals.Put(a, j);
            }
            Assign.Copy(x, vals, Casting.Unsafe);
            return;
        }
        // N-d: shuffle whole sub-arrays along axis 0.
        var order = new long[n];
        for (int i = 0; i < n; i++) order[i] = i;
        ShuffleInts(order);
        var shuffled = x.Get(NDArray.FromArray(order)).Copy();
        Assign.Copy(x, shuffled, Casting.Unsafe);
    }

    /// <summary>numpy <c>permutation(n)</c>.</summary>
    public NDArray Permutation(long n)
    {
        var data = new long[n];
        for (long i = 0; i < n; i++) data[i] = i;
        ShuffleInts(data);
        return NDArray.FromArray(data);
    }

    /// <summary>numpy <c>permutation(x)</c> for an array: a shuffled copy along axis 0.</summary>
    public NDArray Permutation(NDArray x)
    {
        if (x.Ndim == 1)
        {
            var c = x.Copy();
            Shuffle(c);
            return c;
        }
        var idx = new long[x.Shape[0]];
        for (int i = 0; i < idx.Length; i++) idx[i] = i;
        ShuffleInts(idx);
        return x.Get(NDArray.FromArray(idx)).Copy();
    }

    /// <summary>numpy <c>choice</c> over indices <c>[0, popSize)</c>, uniform; the caller maps indices to values.</summary>
    public long[] ChooseIndices(long popSize, int count, bool replace, bool shuffle = true)
    {
        if (popSize <= 0) throw new NDValueException("a must be a positive integer unless no samples are taken");
        var idx = new long[count];
        if (replace)
        {
            for (int i = 0; i < count; i++) idx[i] = (long)Bounded((ulong)(popSize - 1));
            return idx;
        }
        if (count > popSize) throw new NDValueException("Cannot take a larger sample than population when 'replace=False'");
        if (popSize > 10000 && count > popSize / 50)
        {
            // Tail shuffle: partial Fisher–Yates over the whole population.
            var all = new long[popSize];
            for (long i = 0; i < popSize; i++) all[i] = i;
            for (long i = popSize - 1; i >= popSize - count; i--)
            {
                long j = (long)Bounded((ulong)i);
                (all[i], all[j]) = (all[j], all[i]);
            }
            Array.Copy(all, popSize - count, idx, 0, count);
            return idx;
        }
        // Floyd's algorithm with numpy's open-addressing hash set.
        ulong setSize = (ulong)(1.2 * count);
        ulong mask = GenMask(setSize);
        setSize = 1 + mask;
        var hashSet = new ulong[setSize];
        Array.Fill(hashSet, ulong.MaxValue);
        for (long j = popSize - count; j < popSize; j++)
        {
            ulong val = Bounded((ulong)j);
            ulong loc = val & mask;
            while (hashSet[loc] != ulong.MaxValue && hashSet[loc] != val) loc = (loc + 1) & mask;
            if (hashSet[loc] == ulong.MaxValue)
            {
                hashSet[loc] = val;
                idx[j - popSize + count] = (long)val;
            }
            else
            {
                loc = (ulong)j & mask;
                while (hashSet[loc] != ulong.MaxValue) loc = (loc + 1) & mask;
                hashSet[loc] = (ulong)j;
                idx[j - popSize + count] = j;
            }
        }
        if (shuffle) ShuffleInts(idx, lemire: true);
        return idx;
    }

    private static ulong GenMask(ulong max)
    {
        ulong mask = max;
        mask |= mask >> 1; mask |= mask >> 2; mask |= mask >> 4; mask |= mask >> 8; mask |= mask >> 16; mask |= mask >> 32;
        return mask;
    }
}
