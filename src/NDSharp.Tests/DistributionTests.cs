// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Random;

namespace NDSharp.Tests;

/// <summary>exponential, gamma and Poisson draws agree with numpy 2.5.3's <c>default_rng(7)</c> stream, value by value.</summary>
public class DistributionTests
{
    private static Generator Seven() => Generator.DefaultRng(new[] { new BigInteger(7) });

    [Fact]
    public void Standard_exponential_matches_numpy()
    {
        var x = Seven().StandardExponential(new[] { 3 }).ToArray<double>();
        Assert.Equal(new[] { 0.70752926, 1.02520335, 0.56854866 }, x.Select(v => Math.Round(v, 8)).ToArray());
    }

    [Fact]
    public void Poisson_matches_numpy_for_small_and_large_means()
    {
        var g = Seven();
        g.StandardExponential(new[] { 3 });
        Assert.Equal(new long[] { 3, 4, 3, 5, 1 }, g.Poisson(3.2, new[] { 5 }).ToArray<long>());
        Assert.Equal(new long[] { 29, 32, 30, 33, 28 }, g.Poisson(30, new[] { 5 }).ToArray<long>());
    }

    [Fact]
    public void Gamma_matches_numpy_above_and_below_shape_one()
    {
        var g = Seven();
        g.StandardExponential(new[] { 3 }); g.Poisson(3.2, new[] { 5 }); g.Poisson(30, new[] { 5 });
        Assert.Equal(new[] { 9.26690657, 4.57973758, 5.2511162 }, g.Gamma(2.0, 3.0, new[] { 3 }).ToArray<double>().Select(v => Math.Round(v, 8)).ToArray());
        Assert.Equal(new[] { 0.44070926, 0.00837145, 0.25789464 }, g.Gamma(0.5, 1.0, new[] { 3 }).ToArray<double>().Select(v => Math.Round(v, 8)).ToArray());
    }

    [Fact]
    public void Poisson_of_zero_is_zero_and_exponential_scales()
    {
        Assert.All(Seven().Poisson(0, new[] { 4 }).ToArray<long>(), v => Assert.Equal(0, v));
        var a = Seven().StandardExponential(new[] { 5 }).ToArray<double>();
        var b = Seven().Exponential(2.5, new[] { 5 }).ToArray<double>();
        for (int i = 0; i < 5; i++) Assert.Equal(a[i] * 2.5, b[i]);
    }
}
