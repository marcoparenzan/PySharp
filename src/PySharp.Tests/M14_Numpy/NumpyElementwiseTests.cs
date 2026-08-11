// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 4: elementwise arithmetic, real scalar and
/// shape broadcasting (stride-0 iteration over the broadcast shape — see
/// NumpyBroadcastShapeTests.cs for the broadcasting rule itself), unary ops, and `+=`'s
/// documented non-aliasing simplification.</summary>
public class NumpyElementwiseTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Same_shape_arrays_add_subtract_multiply_and_divide_elementwise()
        => Assert.Equal("[11. 22. 33.]\n[-9. -18. -27.]\n[10. 40. 90.]\n[10. 10. 10.]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0])
            b = np.array([10.0, 20.0, 30.0])
            print(str(a + b))
            print(str(a - b))
            print(str(a * b))
            print(str(b / a))
            """));

    [Fact]
    public void A_real_scalar_broadcasts_from_either_side_for_every_arithmetic_operator()
        => Assert.Equal("[3. 4. 5.]\n[3. 4. 5.]\n[0. 1. 2.]\n[0. -1. -2.]\n[2. 4. 6.]\n[2. 4. 6.]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0])
            print(str(a + 2))
            print(str(2 + a))
            print(str(a - 1))
            print(str(1 - a))
            print(str(a * 2))
            print(str(2 * a))
            """));

    [Fact]
    public void Ndarray_dunders_are_really_reachable_through_the_plain_operators()
        => Assert.Equal("True\nTrue", Run("""
            import numpy as np
            a = np.array([1.0])
            print(hasattr(a, "__add__"))
            print((a + a).shape == (1,))
            """));

    [Fact]
    public void Row_shape_broadcasts_over_every_row_of_a_2D_array()
        => Assert.Equal("[[11. 22. 33.]\n [14. 25. 36.]]", Run("""
            import numpy as np
            m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            row = np.array([10.0, 20.0, 30.0])
            print(str(m + row))
            """));

    [Fact]
    public void A_column_shape_and_a_row_shape_broadcast_together_into_a_real_2D_grid()
        => Assert.Equal("[[11. 21. 31.]\n [12. 22. 32.]]", Run("""
            import numpy as np
            col = np.array([[1.0], [2.0]])
            wide = np.array([[10.0, 20.0, 30.0]])
            print(str(col + wide))
            """));

    [Fact]
    public void Incompatible_shapes_raise_a_real_ValueError()
        => Assert.Equal("True", Run("""
            import numpy as np
            try:
                np.array([1.0, 2.0, 3.0]) + np.array([1.0, 2.0])
                print(False)
            except ValueError:
                print(True)
            """));

    [Fact]
    public void Unary_negation_plus_and_abs_all_work_elementwise()
        => Assert.Equal("[-1. -2. -3.]\n[1. 2. 3.]\n[1. 2. 3.]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0])
            print(str(-a))
            print(str(+a))
            print(str(abs(np.array([-1.0, 2.0, -3.0]))))
            """));

    [Fact]
    public void Power_works_elementwise_and_with_a_scalar_exponent_or_base()
        => Assert.Equal("[1. 4. 9.]\n[2. 3.]\n[2. 4. 8.]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0])
            print(str(a ** 2))
            print(str(np.array([4.0, 9.0]) ** 0.5))
            print(str(2 ** a))
            """));

    [Fact]
    public void Augmented_assignment_rebinds_to_a_new_array_rather_than_mutating_the_aliased_original()
        => Assert.Equal("[2. 3. 4.]\n[1. 2. 3.]", Run("""
            import numpy as np
            x = np.array([1.0, 2.0, 3.0])
            y = x
            x += 1
            print(str(x))
            print(str(y))
            """));
}
