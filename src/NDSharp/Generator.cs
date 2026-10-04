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

    /// <summary>numpy's ziggurat <c>wi_double</c> table, recovered bit-exactly from numpy's own output (a table recomputed from the layer areas differs in the last bit).</summary>
    private static readonly ulong[] WiBits =
    {
        0x3CCF493B7815D979UL, 0x3C8B8D0BE3FDF6C6UL, 0x3C9250AF3C2C5BB4UL, 0x3C957CB938443B61UL,
        0x3C9801FCE82FA70CUL, 0x3C9A230C2E4CD0BCUL, 0x3C9C004D2F3861F7UL, 0x3C9DAC2F5A747274UL,
        0x3C9F32482D4CD5C3UL, 0x3CA04D32278EBBADUL, 0x3CA0F5053B025D43UL, 0x3CA192A697413677UL,
        0x3CA227A28F7A1AF5UL, 0x3CA2B52E3863D880UL, 0x3CA33C3FC05791F5UL, 0x3CA3BD9EC1A2B12FUL,
        0x3CA439EF8DFF9B55UL, 0x3CA4B1BB363DFEA7UL, 0x3CA52575621AD374UL, 0x3CA59580A707CE96UL,
        0x3CA60231CFD97EEAUL, 0x3CA66BD261A37C3DUL, 0x3CA6D2A292000570UL, 0x3CA736DAD346F8A6UL,
        0x3CA798AD10B32A77UL, 0x3CA7F845AD46F543UL, 0x3CA855CC53430A77UL, 0x3CA8B1649E7B769AUL,
        0x3CA90B2EA94ECF98UL, 0x3CA96347822C1EEAUL, 0x3CA9B9C98E38C546UL, 0x3CAA0ECCDCA4A72CUL,
        0x3CAA62676D77CD59UL, 0x3CAAB4AD6E101630UL, 0x3CAB05B16D136C9CUL, 0x3CAB558487427A29UL,
        0x3CABA4368E529F3AUL, 0x3CABF1D62ABF8232UL, 0x3CAC3E70F9594EF3UL, 0x3CAC8A13A5323B61UL,
        0x3CACD4C9FE72268BUL, 0x3CAD1E9F0E80B748UL, 0x3CAD679D29E41F10UL, 0x3CADAFCE0023B8C3UL,
        0x3CADF73AA9F17653UL, 0x3CAE3DEBB5D2EDFEUL, 0x3CAE83E9337A6F00UL, 0x3CAEC93ABDF982CEUL,
        0x3CAF0DE784F06226UL, 0x3CAF51F654D8F688UL, 0x3CAF956D9E87D7AEUL, 0x3CAFD8537DFA2EACUL,
        0x3CB00D56E04234ECUL, 0x3CB02E40F5398F9AUL, 0x3CB04EEA9E16A5FCUL, 0x3CB06F565B72A010UL,
        0x3CB08F869071F40BUL, 0x3CB0AF7D84BC6113UL, 0x3CB0CF3D664BCC7FUL, 0x3CB0EEC84B16086BUL,
        0x3CB10E20329515EEUL, 0x3CB12D4707310FBEUL, 0x3CB14C3E9F8E9141UL, 0x3CB16B08BFC4201EUL,
        0x3CB189A71A78DA34UL, 0x3CB1A81B51EE6D88UL, 0x3CB1C666F8F82ACBUL, 0x3CB1E48B93E0D42EUL,
        0x3CB2028A9940A09FUL, 0x3CB2206572C4C6E9UL, 0x3CB23E1D7DE9C31FUL, 0x3CB25BB40CA96BFBUL,
        0x3CB2792A661DD37FUL, 0x3CB29681C719D71BUL, 0x3CB2B3BB62B82EDAUL, 0x3CB2D0D862E1B853UL,
        0x3CB2EDD9E8CBA98EUL, 0x3CB30AC10D6E48D7UL, 0x3CB3278EE1F4B930UL, 0x3CB3444470265EA1UL,
        0x3CB360E2BACA52D5UL, 0x3CB37D6ABE05586AUL, 0x3CB399DD6FB2B264UL, 0x3CB3B63BBFB83D03UL,
        0x3CB3D28698561DE0UL, 0x3CB3EEBEDE725A83UL, 0x3CB40AE571E09E74UL, 0x3CB426FB2DA6745DUL,
        0x3CB44300E83C30A4UL, 0x3CB45EF773CAC75DUL, 0x3CB47ADF9E66C336UL, 0x3CB496BA32488F2FUL,
        0x3CB4B287F602415DUL, 0x3CB4CE49ACB311DCUL, 0x3CB4EA001638A605UL, 0x3CB505ABEF5E5562UL,
        0x3CB5214DF20A8B5AUL, 0x3CB53CE6D56A664FUL, 0x3CB558774E1BB2C8UL, 0x3CB574000E555F78UL,
        0x3CB58F81C60E8514UL, 0x3CB5AAFD23241B59UL, 0x3CB5C672D17D733DUL, 0x3CB5E1E37B2F8CD3UL,
        0x3CB5FD4FC89F5E38UL, 0x3CB618B860A31FC3UL, 0x3CB6341DE8A2B0A2UL, 0x3CB64F8104B7260BUL,
        0x3CB66AE257C99672UL, 0x3CB6864283B13137UL, 0x3CB6A1A22950B2B1UL, 0x3CB6BD01E8B343BBUL,
        0x3CB6D8626128D352UL, 0x3CB6F3C43161F854UL, 0x3CB70F27F78B68EBUL, 0x3CB72A8E516914C6UL,
        0x3CB745F7DC70EEDCUL, 0x3CB7616535E5731FUL, 0x3CB77CD6FAEFF449UL, 0x3CB7984DC8BABD93UL,
        0x3CB7B3CA3C8B1409UL, 0x3CB7CF4CF3DB22FBUL, 0x3CB7EAD68C73DEE7UL, 0x3CB80667A486EA1FUL,
        0x3CB82200DAC88676UL, 0x3CB83DA2CE899F15UL, 0x3CB8594E1FD1F5BDUL, 0x3CB875036F7A7EC5UL,
        0x3CB890C35F47F72DUL, 0x3CB8AC8E9205C043UL, 0x3CB8C865ABA10C9CUL, 0x3CB8E44951446A27UL,
        0x3CB9003A2973B58FUL, 0x3CB91C38DC288347UL, 0x3CB9384612EF0AFCUL, 0x3CB954627903A28AUL,
        0x3CB9708EBB70D5EEUL, 0x3CB98CCB892E2A31UL, 0x3CB9A919933F99BFUL, 0x3CB9C5798CD5D92CUL,
        0x3CB9E1EC2B6F7411UL, 0x3CB9FE7226FAD24AUL, 0x3CBA1B0C39F93692UL, 0x3CBA37BB21A2C85BUL,
        0x3CBA547F9E0BBB88UL, 0x3CBA715A724AA9A4UL, 0x3CBA8E4C64A0313DUL, 0x3CBAAB563E9FF108UL,
        0x3CBAC878CD5AF5CEUL, 0x3CBAE5B4E18BB336UL, 0x3CBB030B4FC3A11AUL, 0x3CBB207CF09A985BUL,
        0x3CBB3E0AA0E00C00UL, 0x3CBB5BB541CE3D03UL, 0x3CBB797DB93F8927UL, 0x3CBB9764F1E5F73CUL,
        0x3CBBB56BDB85256EUL, 0x3CBBD3936B2EC0A2UL, 0x3CBBF1DC9B81AE83UL, 0x3CBC10486CEC16A0UL,
        0x3CBC2ED7E5F07A2DUL, 0x3CBC4D8C136E0D1CUL, 0x3CBC6C6608EC8705UL, 0x3CBC8B66E0EBA617UL,
        0x3CBCAA8FBD36A2ABUL, 0x3CBCC9E1C73BD690UL, 0x3CBCE95E3068E037UL, 0x3CBD0906328B8F6EUL,
        0x3CBD28DB1037EF20UL, 0x3CBD48DE1533C647UL, 0x3CBD691096E7F123UL, 0x3CBD8973F4D7FBA5UL,
        0x3CBDAA0999206E70UL, 0x3CBDCAD2F8FC490EUL, 0x3CBDEBD195522E37UL, 0x3CBE0D06FB49D21CUL,
        0x3CBE2E74C4EA46F6UL, 0x3CBE501C99C1D188UL, 0x3CBE72002F97FE25UL, 0x3CBE94214B2ABF0AUL,
        0x3CBEB681C0F76F08UL, 0x3CBED9237610A73AUL, 0x3CBEFC086101ECA9UL, 0x3CBF1F328AC25321UL,
        0x3CBF42A40FB74D6DUL, 0x3CBF665F20C90168UL, 0x3CBF8A6604899782UL, 0x3CBFAEBB187122BFUL,
        0x3CBFD360D22FE785UL, 0x3CBFF859C118F60BUL, 0x3CC00ED447D3A075UL, 0x3CC021A8028FC947UL,
        0x3CC034A983A902ABUL, 0x3CC047DA4E3EF5C7UL, 0x3CC05B3BF6ADB37EUL, 0x3CC06ED023A72668UL,
        0x3CC082988F632E17UL, 0x3CC0969708E8A254UL, 0x3CC0AACD7571C0C4UL, 0x3CC0BF3DD1EED448UL,
        0x3CC0D3EA34AA3D30UL, 0x3CC0E8D4CF116593UL, 0x3CC0FDFFEFA69FB6UL, 0x3CC1136E04207041UL,
        0x3CC129219BBB5D35UL, 0x3CC13F1D69C4096DUL, 0x3CC1556448602E3BUL, 0x3CC16BF93B9DEEF3UL,
        0x3CC182DF74D21261UL, 0x3CC19A1A564EEBACUL, 0x3CC1B1AD777F2F8EUL, 0x3CC1C99CA971A694UL,
        0x3CC1E1EBFBE4AE39UL, 0x3CC1FA9FC2E2D901UL, 0x3CC213BC9D04CC81UL, 0x3CC22D477A6FD3EEUL,
        0x3CC24745A4AC9C24UL, 0x3CC261BCC77658E0UL, 0x3CC27CB2FAA8592EUL, 0x3CC2982ECD770E78UL,
        0x3CC2B437532A0A52UL, 0x3CC2D0D43196DB97UL, 0x3CC2EE0DB1A978F5UL, 0x3CC30BECD256AEEEUL,
        0x3CC32A7B5E68A4A3UL, 0x3CC349C405AE12A3UL, 0x3CC369D27A33A840UL, 0x3CC38AB39256410AUL,
        0x3CC3AC7570AE88FAUL, 0x3CC3CF27B31704A6UL, 0x3CC3F2DBAA60F475UL, 0x3CC417A49CB9E5DAUL,
        0x3CC43D9815545E94UL, 0x3CC464CE44A73A15UL, 0x3CC48D62759C43BCUL, 0x3CC4B7739D6B5A27UL,
        0x3CC4E3250DCD8902UL, 0x3CC5109F53E9AC41UL, 0x3CC54011523A7E42UL, 0x3CC571B1A94AE41BUL,
        0x3CC5A5C08B718DD9UL, 0x3CC5DC8A243AD0FEUL, 0x3CC61669CF861E4CUL, 0x3CC653CE7B006AEAUL,
        0x3CC69540BE9FE5C3UL, 0x3CC6DB6B8D09E232UL, 0x3CC72728F05F7A34UL, 0x3CC7799556090673UL,
        0x3CC7D42DF4D6CE8CUL, 0x3CC839030529F234UL, 0x3CC8AB0FBFAA7C14UL, 0x3CC92EE0946F4496UL,
        0x3CC9CBEE014057ABUL, 0x3CCA8FDC7894775AUL, 0x3CCB981F3878FDB1UL, 0x3CCD3BB48209AD33UL,
    };

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
        for (int i = 0; i < 256; i++) Wi[i] = BitConverter.UInt64BitsToDouble(WiBits[i]);
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
