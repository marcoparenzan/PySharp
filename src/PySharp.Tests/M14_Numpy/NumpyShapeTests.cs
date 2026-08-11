// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 8: shape manipulation. `reshape`/`ravel`/
/// `expand_dims`/`squeeze`/`transpose`/`.T` are all real *views* sharing the source buffer
/// (verified live: mutating the result mutates the original — `transpose`/`.T` became real views in
/// Phase 12.1, see NumpyViewTests.cs for dedicated coverage there); `flatten` is the one deliberate
/// exception, always a real independent copy, matching real numpy's own actual behavior.</summary>
public class NumpyShapeTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Reshape_accepts_a_tuple_or_separate_ints_and_infers_a_single_negative_one_dimension()
        => Assert.Equal("[[0. 1. 2.]\n [3. 4. 5.]]\n[[0. 1.]\n [2. 3.]\n [4. 5.]]\n[[0. 1. 2.]\n [3. 4. 5.]]", Run("""
            import numpy as np
            a = np.arange(6)
            print(str(a.reshape(2, 3)))
            print(str(a.reshape((3, 2))))
            print(str(a.reshape(2, -1)))
            """));

    [Fact]
    public void Reshape_rejects_a_size_mismatch_with_a_real_ValueError()
        => Assert.Equal("True", Run("""
            import numpy as np
            try:
                np.arange(6).reshape(4, 2)
                print(False)
            except ValueError:
                print(True)
            """));

    [Fact]
    public void Reshape_and_ravel_are_real_views_sharing_the_source_buffer()
        => Assert.Equal("[999. 1. 2. 3. 4. 5.]\n[100. 2. 3. 4.]", Run("""
            import numpy as np
            a = np.arange(6)
            b = a.reshape(2, 3)
            b[0, 0] = 999.0
            print(str(a))

            m = np.array([1.0, 2.0, 3.0, 4.0])
            r = m.ravel()
            r[0] = 100.0
            print(str(m))
            """));

    [Fact]
    public void Flatten_is_a_real_independent_copy_unlike_ravel()
        => Assert.Equal("[1. 2. 3. 4.]", Run("""
            import numpy as np
            m = np.array([1.0, 2.0, 3.0, 4.0])
            f = m.flatten()
            f[0] = -1.0
            print(str(m))
            """));

    [Fact]
    public void T_and_transpose_reverse_axes_by_default_for_2D_and_permute_explicitly_for_3D()
        => Assert.Equal("[[1. 4.]\n [2. 5.]\n [3. 6.]]\n(3, 2)\n(3, 2, 4)", Run("""
            import numpy as np
            m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(str(m.T))
            print(m.transpose().shape)
            t3 = np.arange(24.0).reshape(2, 3, 4)
            print(t3.transpose(1, 0, 2).shape)
            """));

    [Fact]
    public void Concatenate_joins_along_an_existing_axis()
        => Assert.Equal("[[1. 2.]\n [3. 4.]\n [5. 6.]]\n[[1. 2. 1. 2.]\n [3. 4. 3. 4.]]", Run("""
            import numpy as np
            x = np.array([[1.0, 2.0], [3.0, 4.0]])
            y = np.array([[5.0, 6.0]])
            print(str(np.concatenate([x, y], axis=0)))
            print(str(np.concatenate([x, x], axis=1)))
            """));

    [Fact]
    public void Stack_joins_along_a_real_new_axis()
        => Assert.Equal("[[1. 2. 3.]\n [4. 5. 6.]]\n[[1. 4.]\n [2. 5.]\n [3. 6.]]", Run("""
            import numpy as np
            v1 = np.array([1.0, 2.0, 3.0])
            v2 = np.array([4.0, 5.0, 6.0])
            print(str(np.stack([v1, v2])))
            print(str(np.stack([v1, v2], axis=1)))
            """));

    [Fact]
    public void Vstack_promotes_1D_arrays_to_rows_and_hstack_picks_the_right_axis_by_ndim()
        => Assert.Equal("[[1. 2. 3.]\n [4. 5. 6.]]\n[1. 2. 3. 4. 5. 6.]\n[[1. 2. 1. 2.]\n [3. 4. 3. 4.]]", Run("""
            import numpy as np
            v1 = np.array([1.0, 2.0, 3.0])
            v2 = np.array([4.0, 5.0, 6.0])
            print(str(np.vstack([v1, v2])))
            print(str(np.hstack([v1, v2])))
            x = np.array([[1.0, 2.0], [3.0, 4.0]])
            print(str(np.hstack([x, x])))
            """));

    [Fact]
    public void Expand_dims_inserts_a_real_size_one_axis_at_the_given_position()
        => Assert.Equal("(1, 3)\n(3, 1)", Run("""
            import numpy as np
            v = np.array([1.0, 2.0, 3.0])
            print(np.expand_dims(v, axis=0).shape)
            print(np.expand_dims(v, axis=1).shape)
            """));

    [Fact]
    public void Squeeze_removes_all_size_one_axes_by_default_or_just_one_with_axis_and_rejects_a_non_size_one_axis()
        => Assert.Equal("(1, 1, 3)\n(3,)\n(2,)\nTrue", Run("""
            import numpy as np
            sq = np.array([[[1.0, 2.0, 3.0]]])
            print(sq.shape)
            print(sq.squeeze().shape)
            print(np.array([[1.0], [2.0]]).squeeze(axis=1).shape)
            try:
                np.array([[1.0, 2.0]]).squeeze(axis=1)
                print(False)
            except ValueError:
                print(True)
            """));

    [Fact]
    public void Newaxis_in_an_index_inserts_a_real_new_axis_and_composes_with_broadcasting()
        => Assert.Equal("(3, 1)\n(1, 3)\n[[1.]\n [2.]\n [3.]]\n[[2. 3. 4.]\n [3. 4. 5.]\n [4. 5. 6.]]", Run("""
            import numpy as np
            row = np.array([1.0, 2.0, 3.0])
            print(row[:, np.newaxis].shape)
            print(row[np.newaxis, :].shape)
            print(str(row[:, None]))
            print(str(row[:, None] + row[None, :]))
            """));
}
