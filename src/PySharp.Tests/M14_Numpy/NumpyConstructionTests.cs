// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 2: real construction (`np.array`,
/// `np.zeros`/`ones`/`full`/`empty`, `np.arange`, `np.linspace`, `np.eye`/`identity`, `.copy()`).
/// Every array here is genuinely built and shape/value-checked via `str()`, not just
/// smoke-tested for "did it not crash".</summary>
public class NumpyConstructionTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Array_from_a_flat_1D_list_of_ints_infers_int64_and_a_list_of_floats_infers_float64()
        => Assert.Equal("(3,)\nint64\n[1 2 3]\nfloat64\n[1. 2. 3.]", Run("""
            import numpy as np
            a = np.array([1, 2, 3])
            print(a.shape)
            print(a.dtype.name)
            print(str(a))
            f = np.array([1.0, 2.0, 3.0])
            print(f.dtype.name)
            print(str(f))
            """));

    [Fact]
    public void Array_from_a_nested_2D_list_infers_shape_recursively()
        => Assert.Equal("(2, 2)\n[[1 2]\n [3 4]]", Run("""
            import numpy as np
            b = np.array([[1, 2], [3, 4]])
            print(b.shape)
            print(str(b))
            """));

    [Fact]
    public void Array_from_a_bare_scalar_produces_a_real_0D_array()
        => Assert.Equal("0\n5.", Run("""
            import numpy as np
            s = np.array(5.0)
            print(s.ndim)
            print(str(s))
            """));

    [Fact]
    public void Array_from_an_empty_list_has_shape_zero_and_size_zero()
        => Assert.Equal("(0,)\n0", Run("""
            import numpy as np
            e = np.array([])
            print(e.shape)
            print(e.size)
            """));

    [Fact]
    public void Array_rejects_a_ragged_nested_list_with_a_real_ValueError()
        => Assert.Equal("True", Run("""
            import numpy as np
            try:
                np.array([[1, 2], [3]])
                print(False)
            except ValueError:
                print(True)
            """));

    [Fact]
    public void Array_rejects_a_list_mixing_scalars_and_sequences_at_the_same_level()
        => Assert.Equal("True", Run("""
            import numpy as np
            try:
                np.array([1, [2, 3]])
                print(False)
            except ValueError:
                print(True)
            """));

    [Fact]
    public void Zeros_and_ones_accept_an_int_or_a_tuple_shape()
        => Assert.Equal("[0. 0. 0.]\n[[0. 0.]\n [0. 0.]]\n[[1. 1. 1.]\n [1. 1. 1.]]", Run("""
            import numpy as np
            print(str(np.zeros(3)))
            print(str(np.zeros((2, 2))))
            print(str(np.ones((2, 3))))
            """));

    [Fact]
    public void Full_fills_every_element_with_the_given_value_and_empty_has_the_right_shape()
        => Assert.Equal("[[7. 7.]\n [7. 7.]]\n(2,)", Run("""
            import numpy as np
            print(str(np.full((2, 2), 7.0)))
            print(np.empty(2).shape)
            """));

    [Fact]
    public void Arange_supports_stop_only_start_stop_and_start_stop_step_including_a_negative_step()
        => Assert.Equal("[0. 1. 2. 3. 4.]\n[1. 2. 3. 4.]\n[5. 4. 3. 2. 1.]\n[0. 0.25 0.5 0.75]", Run("""
            import numpy as np
            print(str(np.arange(5)))
            print(str(np.arange(1, 5)))
            print(str(np.arange(5, 0, -1)))
            print(str(np.arange(0, 1, 0.25)))
            """));

    [Fact]
    public void Linspace_honors_num_and_endpoint_including_the_num_equals_one_edge_case()
        => Assert.Equal("[0. 0.25 0.5 0.75 1.]\n[0. 3.3333333333333335 6.666666666666667]\n[5.]", Run("""
            import numpy as np
            print(str(np.linspace(0, 1, 5)))
            print(str(np.linspace(0, 10, 3, endpoint=False)))
            print(str(np.linspace(5, 5, 1)))
            """));

    [Fact]
    public void Eye_and_identity_place_ones_on_the_real_diagonal_including_a_rectangular_eye()
        => Assert.Equal(
            "[[1. 0. 0.]\n [0. 1. 0.]\n [0. 0. 1.]]\n[[1. 0. 0.]\n [0. 1. 0.]]\n[[1. 0.]\n [0. 1.]]",
            Run("""
            import numpy as np
            print(str(np.eye(3)))
            print(str(np.eye(2, 3)))
            print(str(np.identity(2)))
            """));

    [Fact]
    public void Copy_method_and_module_level_copy_both_produce_an_independent_equal_valued_array()
        => Assert.Equal("[1. 2. 3.]\n[1. 2. 3.]\nFalse", Run("""
            import numpy as np
            orig = np.array([1.0, 2.0, 3.0])
            dup = orig.copy()
            dup_module = np.copy(orig)
            print(str(dup))
            print(str(dup_module))
            print(orig is dup)
            """));
}
