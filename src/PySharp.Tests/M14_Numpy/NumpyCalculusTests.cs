// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy.trapezoid / trapz, numpy.histogram and numpy.gradient (NDSharp, on the MathemaSharp kernels). Values worked out by hand from the definitions and
/// NumPy's documented rules; not compared with CPython/NumPy (not installed here).</summary>
[Collection("NumpyReductionKernels")]
public class NumpyCalculusTests
{
    static string Run(string code)
    {
        var output = new StringWriter();
        Py.NewEngine(output).Run("import numpy as np\n" + code);
        return output.ToString().Trim();
    }

    [Fact]
    public void TrapezoidAndItsOlderName()
    {
        Assert.Equal("4.5", Run("print(float(np.trapezoid([0.0, 1.0, 2.0, 3.0])))"));
        Assert.Equal("9.0", Run("print(float(np.trapz([0.0, 1.0, 2.0, 3.0], dx=2.0)))"));
        Assert.Equal("6.0", Run("print(float(np.trapezoid([1.0, 1.0, 1.0, 1.0], [0.0, 1.0, 3.0, 6.0])))"));
        Assert.Equal("[4.0, 10.0]", Run("print(np.trapezoid(np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]]), axis=1).tolist())"));
        Assert.Equal("4.5", Run("print(float(np.trapezoid(np.array([0, 1, 2, 3]))))"));   // integers are integrated as float64
    }

    [Fact]
    public void HistogramWithBinsRangeAndEdges()
    {
        Assert.Equal("[0, 2, 1] [0.0, 1.0, 2.0, 3.0]", Run("h, e = np.histogram([1.0, 2.0, 1.0], bins=[0, 1, 2, 3])\nprint(h.tolist(), e.tolist())"));
        Assert.Equal("[2, 3] [1.0, 3.0, 5.0]", Run("h, e = np.histogram([1, 2, 3, 4, 5], bins=2)\nprint(h.tolist(), e.tolist())"));
        Assert.Equal("[2, 0, 3]", Run("h, e = np.histogram([0.1, 0.2, 2.5, 2.6, 2.7, 9.0], bins=3, range=(0.0, 3.0))\nprint(h.tolist())"));
        Assert.Equal("[0.25, 0.5, 0.125]", Run("h, e = np.histogram([0.5, 1.5, 1.5, 2.5], bins=[0.0, 1.0, 2.0, 4.0], density=True)\nprint(h.tolist())"));
        Assert.Equal("[0, 0, 0, 0] [0.0, 0.25, 0.5, 0.75, 1.0]", Run("h, e = np.histogram(np.array([]), bins=4)\nprint(h.tolist(), e.tolist())"));
    }

    [Fact]
    public void HistogramErrors()
    {
        Assert.Contains("ValueError", Record.Exception(() => Run("np.histogram([1.0, 2.0], bins=0)"))!.ToString());
        Assert.Contains("ValueError", Record.Exception(() => Run("np.histogram([1.0, float('nan')], bins=3)"))!.ToString());
        Assert.Contains("ValueError", Record.Exception(() => Run("np.histogram([1.0], bins=[2.0, 1.0])"))!.ToString());
        Assert.Contains("NotImplementedError", Record.Exception(() => Run("np.histogram([1.0], bins=2, weights=[1.0])"))!.ToString());
    }

    [Fact]
    public void GradientUniformAndWithCoordinates()
    {
        Assert.Equal("[3.0, 4.0, 6.0, 7.0]", Run("print(np.gradient(np.array([1.0, 4.0, 9.0, 16.0])).tolist())"));
        Assert.Equal("[1.0, 2.0, 6.0, 9.0]", Run("x = np.array([0.0, 1.0, 3.0, 6.0])\nprint(np.gradient(x * x, x).tolist())"));
    }
}
