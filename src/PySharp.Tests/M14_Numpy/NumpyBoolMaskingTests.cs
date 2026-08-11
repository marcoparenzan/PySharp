// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 5: real `bool` dtype, comparisons producing
/// bool arrays (not a collapsed Python bool — this needed a real `Interp.cs` core change, see
/// `Interp.CompareRaw`), logical `&amp; | ^ ~`, `.any()`/`.all()`, boolean-mask read/write, and
/// `np.where`.</summary>
public class NumpyBoolMaskingTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void A_comparison_between_arrays_produces_a_real_bool_dtype_array_not_a_collapsed_scalar()
        => Assert.Equal("bool\nbool\n[False True]\narray([False True])", Run("""
            import numpy as np
            mask = np.array([1.0, 2.0]) > np.array([1.5, 1.5])
            print(mask.dtype)
            print(mask.dtype.name)
            print(str(mask))
            print(repr(mask))
            """));

    [Fact]
    public void A_single_uncahined_comparison_returns_a_real_ndarray_with_the_right_shape_not_a_bool()
        => Assert.Equal("ndarray\n(4,)", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            r = a > 2
            print(type(r).__name__)
            print(r.shape)
            """));

    [Fact]
    public void Every_comparison_operator_works_elementwise_between_two_arrays()
        => Assert.Equal(
            "[False False False False]\n[True True True True]\n[True True False False]\n" +
            "[True True False False]\n[False False True True]\n[False False True True]",
            Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            b = np.array([4.0, 3.0, 2.0, 1.0])
            print(str(a == b))
            print(str(a != b))
            print(str(a < b))
            print(str(a <= b))
            print(str(a > b))
            print(str(a >= b))
            """));

    [Fact]
    public void A_scalar_comparison_broadcasts_from_either_side()
        => Assert.Equal("[False False True True]\n[False False True True]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            print(str(a > 2))
            print(str(2 < a))
            """));

    [Fact]
    public void Array_from_a_list_of_real_Python_bools_infers_a_real_bool_dtype()
        => Assert.Equal("bool\n[True True False False]", Run("""
            import numpy as np
            m = np.array([True, True, False, False])
            print(m.dtype.name)
            print(str(m))
            """));

    [Fact]
    public void Logical_and_or_xor_and_invert_work_elementwise_on_real_bool_arrays()
        => Assert.Equal("[True False False False]\n[True True True False]\n[False True True False]\n[False False True True]", Run("""
            import numpy as np
            m1 = np.array([True, True, False, False])
            m2 = np.array([True, False, True, False])
            print(str(m1 & m2))
            print(str(m1 | m2))
            print(str(m1 ^ m2))
            print(str(~m1))
            """));

    [Fact]
    public void Logical_and_with_a_real_Python_bool_scalar_broadcasts_correctly()
        => Assert.Equal("[True False]\n[True True]", Run("""
            import numpy as np
            m = np.array([True, False])
            print(str(m & True))
            print(str(m | True))
            """));

    [Fact]
    public void Logical_ops_reject_a_non_bool_dtype_array_with_a_real_TypeError()
        => Assert.Equal("True", Run("""
            import numpy as np
            try:
                np.array([1.0, 2.0]) & np.array([1.0, 0.0])
                print(False)
            except TypeError:
                print(True)
            """));

    [Fact]
    public void Any_and_all_report_real_results_across_true_mixed_and_false_arrays()
        => Assert.Equal("True\nFalse\nTrue\nTrue\nFalse\nFalse", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            print((a > 3).any())
            print((a > 3).all())
            print((a > 0).any())
            print((a > 0).all())
            print((a > 10).any())
            print((a > 10).all())
            """));

    [Fact]
    public void Boolean_mask_read_returns_a_1D_array_of_the_selected_elements()
        => Assert.Equal("[3. 4.]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            print(str(a[a > 2]))
            """));

    [Fact]
    public void Boolean_mask_assign_supports_both_a_broadcast_scalar_and_a_matching_length_array()
        => Assert.Equal("[1. 2. 0. 0.]\n[1. 2. 100. 200.]", Run("""
            import numpy as np
            c = np.array([1.0, 2.0, 3.0, 4.0])
            c[c > 2] = 0.0
            print(str(c))

            d = np.array([1.0, 2.0, 3.0, 4.0])
            d[d > 2] = np.array([100.0, 200.0])
            print(str(d))
            """));

    [Fact]
    public void A_chained_comparison_still_collapses_to_a_real_bool_exactly_as_before_the_interpreter_change()
        => Assert.Equal("True\nFalse\n5\nTrue", Run("""
            print(1 < 2 < 3)
            print(1 < 2 < 1)
            x = 5
            print(1 < x < 10 and x)
            print(isinstance(1 < 2 < 3, bool))
            """));

    [Fact]
    public void Where_selects_elementwise_between_two_arrays_using_a_broadcasted_condition()
        => Assert.Equal("[0. 0. 3. 4.]\n[1. 20.]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            print(str(np.where(a > 2, a, 0.0)))
            print(str(np.where(np.array([True, False]), np.array([1.0, 2.0]), np.array([10.0, 20.0]))))
            """));
}
