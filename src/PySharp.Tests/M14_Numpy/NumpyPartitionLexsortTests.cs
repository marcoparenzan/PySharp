// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy.partition, argpartition and lexsort (NDSharp, on the MathemaSharp sorting kernels). partition and argpartition guarantee only the kth element and the
/// before/after property; here the rest comes out sorted. Values worked out by hand, not compared with CPython/NumPy.</summary>
[Collection("NumpyReductionKernels")]
public class NumpyPartitionLexsortTests
{
    static string Run(string code)
    {
        var output = new StringWriter();
        Py.NewEngine(output).Run("import numpy as np\n" + code);
        return output.ToString().Trim();
    }

    [Fact]
    public void PartitionKeepsTheKthElementInPlace()
    {
        Assert.Equal("3.0", Run("a = np.array([9.0, 3.0, 8.0, 5.0, 1.0])\nprint(float(np.partition(a, 1)[1]))"));   // the second smallest
        Assert.Equal("True", Run("a = np.array([9.0, 3.0, 8.0, 5.0, 1.0])\np = np.partition(a, 2)\nprint(bool(p[2] == 5.0 and (p[:2] <= 5.0).all() and (p[3:] >= 5.0).all()))"));
        Assert.Equal("True", Run("a = np.array([9, 3, 8, 5, 1])\nprint(bool(np.partition(a, -1)[-1] == 9))"));
        Assert.Equal("[1, 2, 3, 4, 5, 6]", Run("print(np.partition(np.array([[6, 5, 4], [3, 2, 1]]), 2, axis=None).tolist())"));   // axis=None flattens
        Assert.Equal("(3, 4)", Run("print(np.partition(np.arange(12).reshape(3, 4)[:, ::-1], 2, axis=1).shape)"));
        Assert.Equal("True", Run("print(bool(np.argpartition(np.array([9, 3, 8, 5, 1]), 2)[2] == 3))"));
    }

    [Fact]
    public void PartitionErrors()
    {
        Assert.Contains("ValueError", Record.Exception(() => Run("np.partition(np.array([1, 2, 3]), 3)"))!.ToString());
        Assert.Contains("ValueError", Record.Exception(() => Run("np.argpartition(np.array([1, 2, 3]), -4)"))!.ToString());
    }

    [Fact]
    public void LexsortLikeTheNumpyDocumentation()
    {
        // keys (a, b): sort by b (the last key), ties by a
        Assert.Equal("[2, 4, 6, 5, 3, 1, 0]", Run("a = np.array([1, 5, 1, 4, 3, 4, 4])\nb = np.array([9, 4, 0, 4, 0, 2, 1])\nprint(np.lexsort((a, b)).tolist())"));
        Assert.Equal("[[1, 2, 0], [2, 1, 0]]", Run("k1 = np.array([[2, 1, 1], [0, 5, 5]])\nk2 = np.array([[0, 0, 0], [9, 8, 7]])\nprint(np.lexsort((k1, k2)).tolist())"));
    }
}
