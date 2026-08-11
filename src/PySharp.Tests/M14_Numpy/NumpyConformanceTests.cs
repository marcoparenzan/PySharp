// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 12.5: a conformance sweep of snippets adapted
/// from real numpy's own "quickstart" tutorial (https://numpy.org/doc/stable/user/quickstart.html),
/// run end-to-end as one cohesive group rather than the phase-by-phase unit coverage elsewhere in
/// this directory. One deliberate deviation from the tutorial's own literal output: `np.arange`
/// without an explicit `dtype=` stays `float64` in this shim (documented in NUMPY_PLAN.md Phase
/// 9.2 — real numpy infers `int64` there), so the first snippet passes `dtype=np.int64` explicitly
/// to match the tutorial's own shown values.</summary>
public class NumpyConformanceTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Quickstart_basic_array_attributes()
        => Assert.Equal(
            "[[0 1 2 3 4]\n [5 6 7 8 9]\n [10 11 12 13 14]]\n(3, 5)\n2\nint64\n15\nndarray", Run("""
            import numpy as np
            a = np.arange(15, dtype=np.int64).reshape(3, 5)
            print(str(a))
            print(a.shape)
            print(a.ndim)
            print(a.dtype.name)
            print(a.size)
            print(type(a).__name__)
            """));

    [Fact]
    public void Quickstart_basic_operations_subtraction_power_and_comparison()
        => Assert.Equal("[20. 29. 38. 47.]\n[0. 1. 4. 9.]\n[True True False False]", Run("""
            import numpy as np
            a = np.array([20.0, 30.0, 40.0, 50.0])
            b = np.arange(4.0)
            print(str(a - b))
            print(str(b ** 2))
            print(str(a < 35))
            """));

    [Fact]
    public void Quickstart_matrix_product_via_at_versus_elementwise_product_via_star()
        => Assert.Equal("[[5. 4.]\n [3. 4.]]\n[[2. 0.]\n [0. 4.]]", Run("""
            import numpy as np
            A = np.array([[1.0, 1.0], [0.0, 1.0]])
            B = np.array([[2.0, 0.0], [3.0, 4.0]])
            print(str(A @ B))
            print(str(A * B))
            """));

    [Fact]
    public void Quickstart_universal_functions_exp_and_sqrt()
        => Assert.Equal("[1. 2.718281828459045 7.38905609893065]\n[0. 1. 1.4142135623730951]", Run("""
            import numpy as np
            b = np.arange(3.0)
            print(str(np.exp(b)))
            print(str(np.sqrt(b)))
            """));

    [Fact]
    public void Quickstart_shape_manipulation_ravel_reshape_and_transpose()
        => Assert.Equal(
            "[3. 7. 3. 4. 1. 4. 2. 2. 7. 2. 4. 9.]\n"
            + "[[3. 7.]\n [3. 4.]\n [1. 4.]\n [2. 2.]\n [7. 2.]\n [4. 9.]]\n"
            + "[[3. 1. 7.]\n [7. 4. 2.]\n [3. 2. 4.]\n [4. 2. 9.]]\n(4, 3)", Run("""
            import numpy as np
            a = np.array([[3.0, 7.0, 3.0, 4.0], [1.0, 4.0, 2.0, 2.0], [7.0, 2.0, 4.0, 9.0]])
            print(str(a.ravel()))
            print(str(a.reshape(6, 2)))
            print(str(a.T))
            print(a.T.shape)
            """));

    [Fact]
    public void Quickstart_stacking_arrays_vstack_and_hstack()
        => Assert.Equal("[[1. 2.]\n [3. 4.]\n [5. 6.]\n [7. 8.]]\n[[1. 2. 5. 6.]\n [3. 4. 7. 8.]]", Run("""
            import numpy as np
            a = np.array([[1.0, 2.0], [3.0, 4.0]])
            b = np.array([[5.0, 6.0], [7.0, 8.0]])
            print(str(np.vstack((a, b))))
            print(str(np.hstack((a, b))))
            """));

    [Fact]
    public void Quickstart_iterating_over_a_2D_array_yields_one_row_at_a_time()
        => Assert.Equal("[0 1 2 3 4]\n[5 6 7 8 9]\n[10 11 12 13 14]", Run("""
            import numpy as np
            a = np.arange(15, dtype=np.int64).reshape(3, 5)
            for row in a:
                print(str(row))
            """));

    [Fact]
    public void Quickstart_boolean_mask_indexing_idiom_selects_even_values()
        => Assert.Equal("[0. 2. 4. 6. 8.]", Run("""
            import numpy as np
            data = np.arange(10.0)
            print(str(data[data % 2 == 0]))
            """));
}
