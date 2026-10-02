// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text.Json;
using NDSharp;
using NDSharp.Random;

namespace NDSharp.Tests;

/// <summary>Bit-exactness of <c>default_rng</c> against numpy 2.5.3 (Fixtures/rng_cases.json):
/// integers and index draws must match exactly, floats to the last bit (normals to 1e-12, since the
/// ziggurat tables are recomputed rather than copied).</summary>
public class RngTests
{
    private static readonly JsonElement Root =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rng_cases.json"))).RootElement;

    public static IEnumerable<object[]> Seeds => Root.GetProperty("seeds").EnumerateObject().Select(p => new object[] { p.Name });

    private static Generator Make(string name)
    {
        var s = Root.GetProperty("seeds").GetProperty(name);
        IReadOnlyList<BigInteger> entropy = s.ValueKind == JsonValueKind.Array
            ? s.EnumerateArray().Select(e => BigInteger.Parse(e.GetRawText())).ToList()
            : new[] { BigInteger.Parse(s.GetRawText()) };
        return Generator.DefaultRng(entropy);
    }

    private static JsonElement Case(string seed, string key) => Root.GetProperty("cases").GetProperty(seed).GetProperty(key);
    private static double[] Doubles(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();
    private static long[] Longs(JsonElement e) => e.EnumerateArray().Select(x => x.GetInt64()).ToArray();

    [Theory, MemberData(nameof(Seeds))]
    public void Random_matches_numpy_bit_for_bit(string s)
        => Assert.Equal(Doubles(Case(s, "random")), Make(s).Random(new[] { 6 }).ToArray<double>());

    [Theory, MemberData(nameof(Seeds))]
    public void Uniform_matches(string s)
        => Assert.Equal(Doubles(Case(s, "uniform")), Make(s).Uniform(-1, 1, new[] { 6 }).ToArray<double>());

    [Theory, MemberData(nameof(Seeds))]
    public void Normal_matches(string s)
    {
        var all = Make(s).Normal(0, 1, new[] { 2000 }).ToArray<double>();
        var expected = Doubles(Case(s, "normal"));
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - all[i * 100]) < 1e-12, $"normal[{i * 100}] expected {expected[i]:R} got {all[i * 100]:R}");
        var big = Make(s).Normal(0, 1, new[] { 100000 }).ToArray<double>().Sum();
        Assert.True(Math.Abs(Case(s, "normal_sum").GetDouble() - big) < 1e-8);
        var loc = Make(s).Normal(10, 2, new[] { 5 }).ToArray<double>();
        var el = Doubles(Case(s, "normal_loc"));
        for (int i = 0; i < 5; i++) Assert.True(Math.Abs(el[i] - loc[i]) < 1e-12);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Integers_match(string s)
    {
        Assert.Equal(Longs(Case(s, "int_small")), Make(s).Integers(0, 10, new[] { 30 }).ToArray<long>());
        Assert.Equal(Longs(Case(s, "int_range")), Make(s).Integers(5, 100, new[] { 10 }).ToArray<long>());
        Assert.Equal(Longs(Case(s, "int_big")), Make(s).Integers(0, 1L << 40, new[] { 5 }).ToArray<long>());
        Assert.Equal(Longs(Case(s, "int_huge")), Make(s).Integers(0, 1L << 62, new[] { 3 }).ToArray<long>());
        Assert.Equal(Longs(Case(s, "int_endpoint")), Make(s).Integers(1, 6, new[] { 12 }, DType.Int64, true).ToArray<long>());
        Assert.Equal(Longs(Case(s, "int_i32")), Make(s).Integers(0, 1000, new[] { 8 }, DType.Int32).ToArray<long>());
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Choice_and_permutation_match(string s)
    {
        Assert.Equal(Longs(Case(s, "choice_rep")), Make(s).ChooseIndices(10, 8, true));
        Assert.Equal(Longs(Case(s, "choice_norep")), Make(s).ChooseIndices(10, 5, false));
        Assert.Equal(Longs(Case(s, "choice_norep_big")), Make(s).ChooseIndices(100000, 3000, false).Take(10).ToArray());
        Assert.Equal(Longs(Case(s, "perm")), Make(s).Permutation(10).ToArray<long>());
        var a = np.Arange(0L, 10L);
        Make(s).Shuffle(a);
        Assert.Equal(Longs(Case(s, "shuffle")), a.ToArray<long>());
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Interleaved_32_and_64_bit_draws_share_the_buffered_stream(string s)
    {
        var g = Make(s);
        var m = Case(s, "mixed").EnumerateArray().ToArray();
        Assert.Equal(m[0].GetInt64(), g.Integers(0, 6).ToScalarLong());
        Assert.Equal(m[1].GetDouble(), g.NextDouble());
        Assert.Equal(m[2].GetInt64(), g.Integers(0, 6).ToScalarLong());
        Assert.True(Math.Abs(m[3].GetDouble() - g.Normal(0, 1).ToScalarDouble()) < 1e-12);
        Assert.Equal(m[4].GetInt64(), g.Integers(0, 100).ToScalarLong());
    }
}

internal static class RngTestExtensions
{
    public static long ToScalarLong(this NDArray a) => a.ToArray<long>()[0];
    public static double ToScalarDouble(this NDArray a) => a.ToArray<double>()[0];
}
