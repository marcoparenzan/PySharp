// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 10: basic linear algebra. `dot`/`matmul`/`@`
/// share one `MatMul` core covering 1-D/2-D operands only (real numpy's own N-D "stacked" matmul is
/// out of this v1 shim's scope, documented and deferred as Phase 10.4); `trace`/`diagonal`/
/// `linalg.norm` are real, verified against known real numpy output.</summary>
public class NumpyLinalgTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Dot_and_matmul_operator_on_two_1D_arrays_return_a_real_scalar_not_a_0D_array()
        => Assert.Equal("32.0\n32.0\nfloat\n32\nint", Run("""
            import numpy as np
            v1 = np.array([1.0, 2.0, 3.0])
            v2 = np.array([4.0, 5.0, 6.0])
            print(np.dot(v1, v2))
            print(v1 @ v2)
            print(type(v1 @ v2).__name__)
            i1 = np.array([1, 2, 3])
            i2 = np.array([4, 5, 6])
            print(i1 @ i2)
            print(type(i1 @ i2).__name__)
            """));

    [Fact]
    public void Matmul_on_two_2D_arrays_is_a_real_matrix_product_via_operator_and_both_module_functions()
        => Assert.Equal(
            "[[19. 22.]\n [43. 50.]]\n[[19. 22.]\n [43. 50.]]\n[[19. 22.]\n [43. 50.]]\n[[4. 5.]\n [10. 11.]]", Run("""
            import numpy as np
            m1 = np.array([[1.0, 2.0], [3.0, 4.0]])
            m2 = np.array([[5.0, 6.0], [7.0, 8.0]])
            print(str(m1 @ m2))
            print(str(np.matmul(m1, m2)))
            print(str(np.dot(m1, m2)))
            a = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            b = np.array([[1.0, 0.0], [0.0, 1.0], [1.0, 1.0]])
            print(str(a @ b))
            """));

    [Fact]
    public void Matmul_promotes_a_1D_operand_by_a_temporary_axis_and_drops_it_again_from_the_result()
        => Assert.Equal("[7. 10.]\n(2,)\n[5. 11.]\n(2,)", Run("""
            import numpy as np
            row = np.array([1.0, 2.0])
            mat = np.array([[1.0, 2.0], [3.0, 4.0]])
            print(str(row @ mat))
            print((row @ mat).shape)
            col = np.array([1.0, 2.0])
            print(str(mat @ col))
            print((mat @ col).shape)
            """));

    [Fact]
    public void Matmul_rejects_a_shape_mismatch_with_a_real_ValueError()
        => Assert.Equal("True", Run("""
            import numpy as np
            try:
                np.array([1.0, 2.0]) @ np.array([1.0, 2.0, 3.0])
                print(False)
            except ValueError:
                print(True)
            """));

    [Fact]
    public void Trace_and_diagonal_read_along_the_main_or_an_offset_diagonal()
        => Assert.Equal("16.0\n[1. 5. 10.]\n[2. 6.]\n[4. 8.]", Run("""
            import numpy as np
            sq = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0], [7.0, 8.0, 10.0]])
            print(np.trace(sq))
            print(str(np.diagonal(sq)))
            print(str(np.diagonal(sq, offset=1)))
            print(str(np.diagonal(sq, offset=-1)))
            """));

    [Fact]
    public void Linalg_norm_is_the_2norm_for_a_vector_and_the_Frobenius_norm_for_a_matrix_reachable_both_ways()
        => Assert.Equal("5.0\n5.477225575051661\n5.0", Run("""
            import numpy as np
            print(np.linalg.norm(np.array([3.0, 4.0])))
            print(np.linalg.norm(np.array([[1.0, 2.0], [3.0, 4.0]])))
            import numpy.linalg as la
            print(la.norm(np.array([3.0, 4.0])))
            """));
}
