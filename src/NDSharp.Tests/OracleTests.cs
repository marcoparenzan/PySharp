// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;

namespace NDSharp.Tests;

/// <summary>Hosts the generated oracle cases (OracleCases.g.cs): each asserts that NDSharp's repr
/// equals what real numpy prints for the same computation.</summary>
public partial class OracleTests
{
    private static void Check(string expected, NDArray actual) => Assert.Equal(expected, ArrayFormat.Repr(actual));

    // Builders mirroring the Python helpers in gen_ndsharp_tests.py.
    private static NDArray A(params double[] v) => NDArray.FromArray(v);
    private static NDArray L(params long[] v) => NDArray.FromArray(v);
    private static NDArray U8(params byte[] v) => NDArray.FromArray(v);
    private static NDArray I8(params sbyte[] v) => NDArray.FromArray(v);
    private static NDArray F32(params float[] v) => NDArray.FromArray(v);
    private static NDArray B(params bool[] v) => NDArray.FromArray(v);
    private static NDArray LM(long[,] v) => NDArray.FromMultiDim(v);
    private static NDArray M_(double[,] v) => NDArray.FromMultiDim(v);
    private static NDArray M6() => np.Reshape(np.Arange(0L, 6L), 2, 3);

    // Weak (Python-number) scalars.
    private static NDArray W(long v) => NDArray.WeakScalar(v);
    private static NDArray W(double v) => NDArray.WeakScalar(v);
}
