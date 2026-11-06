// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy's nan* functions (NDSharp, on the MathemaSharp kernels). Values worked out by hand from NumPy's documented rules; not compared with CPython/NumPy (not installed here).</summary>
[Collection("NumpyReductionKernels")]
public class NumpyNanFunctionsTests
{
    static string Run(string code)
    {
        var output = new StringWriter();
        Py.NewEngine(output).Run("import numpy as np\na = np.array([[1.0, np.nan, 3.0], [4.0, 5.0, np.nan]])\n" + code);
        return output.ToString().Trim();
    }

    [Theory]
    [InlineData("np.nansum(a)", "13.0")]
    [InlineData("np.nanprod(a)", "60.0")]
    [InlineData("np.nanmean(a)", "3.25")]
    [InlineData("np.nanmax(a)", "5.0")]
    [InlineData("np.nanmin(a)", "1.0")]
    [InlineData("np.nanmedian(a)", "3.5")]
    [InlineData("np.nanvar(a)", "2.1875")]          // values 1 3 4 5: mean 3.25, squared deviations add up to 8.75
    [InlineData("np.nanvar(a, ddof=1)", "2.9166666666666665")]
    [InlineData("np.nanquantile(a, 0.25)", "2.5")]       // linear: position 0.75 between 1 and 3
    [InlineData("np.nanpercentile(a, 50, method='hazen')", "3.5")]
    [InlineData("np.nanargmax(a)", "4")]
    [InlineData("np.nanargmin(a)", "0")]
    public void ScalarResults(string expression, string expected)
        => Assert.Equal(expected, Run($"print({expression})"));

    [Fact]
    public void AxesShapesAndCumulativeFunctions()
    {
        Assert.Equal("[5.0, 5.0, 3.0]", Run("print(np.nansum(a, axis=0).tolist())"));
        Assert.Equal("[2.0, 4.5]", Run("print(np.nanmean(a, axis=1).tolist())"));
        Assert.Equal("[2, 1]", Run("print(np.nanargmax(a, axis=1).tolist())"));
        Assert.Equal("[1.0, 1.0, 4.0, 8.0, 13.0, 13.0]", Run("print(np.nancumsum(a.reshape(-1)).tolist())"));
        Assert.Equal("[1.0, 1.0, 3.0, 12.0, 60.0, 60.0]", Run("print(np.nancumprod(a.reshape(-1)).tolist())"));
    }

    [Fact]
    public void AllNanSlices()
    {
        Assert.Equal("0.0 1.0 nan nan nan", Run("b = np.array([np.nan, np.nan])\nprint(np.nansum(b), np.nanprod(b), np.nanmean(b), np.nanmax(b), np.nanmedian(b))"));
        Assert.Contains("ValueError", Record.Exception(() => Run("np.nanargmax(np.array([np.nan, np.nan]))"))!.ToString());
    }

    [Fact]
    public void IntegersBehaveLikeThePlainFunctions()
        => Assert.Equal("6 2.0", Run("c = np.array([1, 2, 3])\nprint(np.nansum(c), np.nanmean(c))"));
}
