// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy.quantile and numpy.percentile accept every <c>method</c> (NDSharp runs them on the MathemaSharp kernels); values worked out by hand
/// from the Hyndman-Fan definitions for [3, 1, 4, 2] at q = 0.25. Not compared with CPython/NumPy (not installed here).</summary>
[Collection("NumpyReductionKernels")]
public class NumpyQuantileMethodTests
{
    static string Run(string code)
    {
        var output = new StringWriter();
        Py.NewEngine(output).Run("import numpy as np\na = np.array([3.0, 1.0, 4.0, 2.0])\n" + code);
        return output.ToString().Trim();
    }

    [Theory]
    [InlineData("inverted_cdf", "1.0")]
    [InlineData("averaged_inverted_cdf", "1.5")]
    [InlineData("interpolated_inverted_cdf", "1.0")]
    [InlineData("hazen", "1.5")]
    [InlineData("weibull", "1.25")]
    [InlineData("linear", "1.75")]
    [InlineData("median_unbiased", "1.4166666666666667")]
    [InlineData("normal_unbiased", "1.4375")]
    [InlineData("lower", "1.0")]
    [InlineData("higher", "2.0")]
    [InlineData("nearest", "2.0")]
    [InlineData("midpoint", "1.5")]
    public void QuantileMethods(string method, string expected)
        => Assert.Equal(double.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(Run($"print(float(np.quantile(a, 0.25, method='{method}')))"), System.Globalization.CultureInfo.InvariantCulture), 12);

    [Fact]
    public void PercentileTakesTheSameMethods()
        => Assert.Equal("1.5", Run("print(float(np.percentile(a, 25, method='hazen')))"));

    [Fact]
    public void ProbabilityAxisComesFirstAndAxesWork()
        => Assert.Equal("(3, 2)", Run("b = np.array([[1.0, 10.0], [2.0, 20.0], [3.0, 30.0]])\nprint(np.quantile(b, [0.0, 0.5, 1.0], axis=0, method='lower').shape)"));

    [Fact]
    public void UnknownMethodIsAValueError()
        => Assert.Contains("ValueError", Record.Exception(() => Run("np.quantile(a, 0.5, method='bogus')"))!.ToString());
}
