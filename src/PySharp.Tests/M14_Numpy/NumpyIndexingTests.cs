// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 3 indexing/slicing, upgraded to real strided
/// views by Phase 12.1 (basic indexing shares the source buffer instead of copying — see
/// NumpyViewTests.cs for dedicated view-semantics coverage). Covers integer indexing (incl.
/// negative, incl. N-D tuples), partial N-D indexing, slicing (incl. negative step), mixed
/// int/slice N-D indexing, and scalar/array assignment through the same `__setitem__` path.</summary>
public class NumpyIndexingTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Integer_index_on_a_1D_array_returns_a_real_Python_float_scalar_with_negative_index_support()
        => Assert.Equal("10.0\nfloat\n40.0", Run("""
            import numpy as np
            a = np.array([10.0, 20.0, 30.0, 40.0])
            print(a[0])
            print(type(a[0]).__name__)
            print(a[-1])
            """));

    [Fact]
    public void Out_of_range_integer_index_raises_a_real_IndexError()
        => Assert.Equal("True", Run("""
            import numpy as np
            a = np.array([10.0, 20.0, 30.0, 40.0])
            try:
                a[10]
                print(False)
            except IndexError:
                print(True)
            """));

    [Fact]
    public void A_full_ND_integer_tuple_index_returns_a_scalar_including_negative_indices()
        => Assert.Equal("6.0\n6.0", Run("""
            import numpy as np
            b = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(b[1, 2])
            print(b[-1, -1])
            """));

    [Fact]
    public void A_partial_index_on_an_ND_array_returns_a_real_view_sharing_the_source_buffer()
        => Assert.Equal("(3,)\n[1. 2. 3.]\n[[999. 2.]\n [3. 4.]]", Run("""
            import numpy as np
            b = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            row = b[0]
            print(row.shape)
            print(str(row))

            g = np.array([[1.0, 2.0], [3.0, 4.0]])
            sub = g[0]
            sub[0] = 999.0
            print(str(g))
            """));

    [Fact]
    public void A_1D_slice_supports_start_stop_step_including_a_negative_step_and_reversal()
        => Assert.Equal("[20. 30.]\n[40. 30. 20. 10.]\n[10. 30.]", Run("""
            import numpy as np
            a = np.array([10.0, 20.0, 30.0, 40.0])
            print(str(a[1:3]))
            print(str(a[::-1]))
            print(str(a[::2]))
            """));

    [Fact]
    public void An_ND_index_can_mix_integers_and_slices_across_axes()
        => Assert.Equal("[2. 3.]\n[2. 5.]\n[[1. 2.]]", Run("""
            import numpy as np
            b = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(str(b[0, 1:3]))
            print(str(b[:, 1]))
            print(str(b[0:1, 0:2]))
            """));

    [Fact]
    public void Scalar_assignment_works_for_a_1D_integer_index_and_an_ND_integer_tuple_index()
        => Assert.Equal("[1. 99. 3.]\n[[1. 42.]\n [3. 4.]]", Run("""
            import numpy as np
            c = np.array([1.0, 2.0, 3.0])
            c[1] = 99.0
            print(str(c))

            d = np.array([[1.0, 2.0], [3.0, 4.0]])
            d[0, 1] = 42.0
            print(str(d))
            """));

    [Fact]
    public void Slice_assignment_broadcasts_a_scalar_over_every_selected_element()
        => Assert.Equal("[1. 0. 0. 4.]", Run("""
            import numpy as np
            e = np.array([1.0, 2.0, 3.0, 4.0])
            e[1:3] = 0.0
            print(str(e))
            """));

    [Fact]
    public void Slice_assignment_with_a_matching_shape_array_writes_elementwise_and_a_mismatch_raises_ValueError()
        => Assert.Equal("[1. 100. 200. 4.]\nTrue", Run("""
            import numpy as np
            f = np.array([1.0, 2.0, 3.0, 4.0])
            f[1:3] = np.array([100.0, 200.0])
            print(str(f))
            try:
                f[1:3] = np.array([1.0, 2.0, 3.0])
                print(False)
            except ValueError:
                print(True)
            """));
}
