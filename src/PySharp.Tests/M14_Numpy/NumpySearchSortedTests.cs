// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy.searchsorted and ndarray.searchsorted (NDSharp, on the MathemaSharp kernels); values worked out by hand, not compared with CPython/NumPy.</summary>
[Collection("NumpyReductionKernels")]
public class NumpySearchSortedTests
{
    static string Run(string code)
    {
        var output = new StringWriter();
        Py.NewEngine(output).Run("import numpy as np\na = np.array([1, 3, 5, 7, 9])\n" + code);
        return output.ToString().Trim();
    }

    [Fact]
    public void SidesAndShapes()
    {
        Assert.Equal("[0, 1, 2, 5]", Run("print(np.searchsorted(a, [0, 3, 4, 10]).tolist())"));
        Assert.Equal("[0, 2, 2, 5]", Run("print(np.searchsorted(a, [0, 3, 4, 10], side='right').tolist())"));
        Assert.Equal("[[1, 2], [4, 5]]", Run("print(np.searchsorted(a, np.array([[2, 4], [8, 10]])).tolist())"));
        Assert.Equal("1", Run("print(a.searchsorted(2))"));
    }

    [Fact]
    public void SorterNanAndErrors()
    {
        Assert.Equal("[0, 2, 5]", Run("u = np.array([9.0, 1.0, 7.0, 3.0, 5.0])\nprint(np.searchsorted(u, [0.5, 3.5, 11.0], sorter=np.argsort(u)).tolist())"));
        Assert.Equal("2", Run("print(np.searchsorted(np.array([1.0, 2.0, np.nan]), np.nan))"));   // NaN sorts last, so it goes before the existing NaN on the left side
        Assert.Contains("ValueError", Record.Exception(() => Run("np.searchsorted(a, 3, side='middle')"))!.ToString());
    }
}
