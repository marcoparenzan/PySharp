// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 12.1: real strided views. Basic indexing
/// (int/slice/`None`, not boolean masking), `reshape`/`ravel` (when the source is contiguous),
/// `.T`/`transpose()`, `expand_dims`, and `squeeze` now share the source buffer instead of copying
/// — a genuine behavior change from Phases 3/8, verified here against known real numpy semantics
/// (real numpy's own basic indexing/`.T` are views too). Boolean masking (`a[mask]`) and `flatten()`
/// still always copy, matching real numpy's own actual behavior for those two operations.</summary>
public class NumpyViewTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void A_slice_is_a_view_including_negative_step_and_a_step_other_than_one()
        => Assert.Equal("[10. 999. 30. 40.]\n[1. 2. 3. 100.]\n[1. 2. 999. 4. 5. 6.]", Run("""
            import numpy as np
            a = np.array([10.0, 20.0, 30.0, 40.0])
            a[1:3][0] = 999.0
            print(str(a))

            b = np.array([1.0, 2.0, 3.0, 4.0])
            b[::-1][0] = 100.0
            print(str(b))

            c = np.array([1.0, 2.0, 3.0, 4.0, 5.0, 6.0])
            c[::2][1] = 999.0
            print(str(c))
            """));

    [Fact]
    public void T_and_transpose_are_views_and_compose_correctly_through_chained_indexing()
        => Assert.Equal("[[1. 2.]\n [999. 4.]]\n[[1. 555.]\n [3. 4.]]\n[[1. 2.]\n [777. 4.]]", Run("""
            import numpy as np
            m = np.array([[1.0, 2.0], [3.0, 4.0]])
            m.T[0, 1] = 999.0
            print(str(m))

            m2 = np.array([[1.0, 2.0], [3.0, 4.0]])
            m2.transpose()[1, 0] = 555.0
            print(str(m2))

            j = np.array([[1.0, 2.0], [3.0, 4.0]])
            j.T[0, 1] = 777.0
            print(str(j))
            """));

    [Fact]
    public void Reshape_and_ravel_are_views_when_the_source_is_contiguous_but_copy_when_it_is_not()
        => Assert.Equal(
            "[111. 2. 3. 4. 5. 6.]\n[[1. 3.]\n [2. 4.]]\n[222. 3. 2. 4.]\n[[1. 3.]\n [2. 4.]]\n[888. 3. 2. 4.]", Run("""
            import numpy as np
            e1 = np.array([1.0, 2.0, 3.0, 4.0, 5.0, 6.0])
            e1.reshape(2, 3)[0, 0] = 111.0
            print(str(e1))

            e2 = np.array([[1.0, 2.0], [3.0, 4.0]]).T
            resh2 = e2.reshape(4)
            resh2[0] = 222.0
            print(str(e2))
            print(str(resh2))

            d2 = np.array([[1.0, 2.0], [3.0, 4.0]]).T
            rav2 = d2.ravel()
            rav2[0] = 888.0
            print(str(d2))
            print(str(rav2))
            """));

    [Fact]
    public void Flatten_and_boolean_masking_always_copy_even_when_the_source_is_a_view()
        => Assert.Equal("[[1. 3.]\n [2. 4.]]\n[333. 3. 2. 4.]\n[1. 2. 3. 4.]", Run("""
            import numpy as np
            f = np.array([[1.0, 2.0], [3.0, 4.0]]).T
            flat = f.flatten()
            flat[0] = 333.0
            print(str(f))
            print(str(flat))

            l = np.array([1.0, 2.0, 3.0, 4.0])
            masked = l[l > 2]
            masked[0] = 999.0
            print(str(l))
            """));

    [Fact]
    public void Expand_dims_and_squeeze_are_views()
        => Assert.Equal("[1. 444. 3.]\n[[1.]\n [555.]\n [3.]]", Run("""
            import numpy as np
            g = np.array([1.0, 2.0, 3.0])
            np.expand_dims(g, axis=0)[0, 1] = 444.0
            print(str(g))

            h = np.array([[1.0], [2.0], [3.0]])
            h.squeeze()[1] = 555.0
            print(str(h))
            """));

    [Fact]
    public void Copy_always_produces_a_real_independent_array_even_from_a_view()
        => Assert.Equal("[2. 3.]\n[1. 2. 3. 4.]", Run("""
            import numpy as np
            n = np.array([1.0, 2.0, 3.0, 4.0])
            nv = n[1:3]
            nc = nv.copy()
            nc[0] = 999.0
            print(str(nv))
            print(str(n))
            """));

    [Fact]
    public void Base_is_None_for_an_owning_array_and_not_None_for_a_view()
        => Assert.Equal("True\nTrue\nTrue\nTrue", Run("""
            import numpy as np
            k = np.array([1.0, 2.0, 3.0, 4.0])
            print(k.base is None)
            print(k[1:3].base is not None)
            print(k.copy().base is None)
            print(k.reshape(2, 2).base is not None)
            """));

    [Fact]
    public void Arithmetic_reductions_masking_matmul_concatenate_and_diagonal_all_read_correctly_through_a_view()
        => Assert.Equal(
            "[2. 4. 6.]\n9.0\n[1. 3. 5.]\n"
            + "[[10. 14.]\n [14. 20.]]\n"
            + "[1. 4.]\n5.0\n[2. 6.]\n"
            + "[[1. 3.]\n [2. 4.]\n [1. 3.]\n [2. 4.]]\n"
            + "[[1. 0. 0.]\n [4. 5. 6.]]", Run("""
            import numpy as np
            p = np.array([1.0, 2.0, 3.0, 4.0, 5.0, 6.0])
            pv = p[::2]
            print(str(pv + 1.0))
            print(pv.sum())
            print(str(pv))

            ww = np.array([[1.0, 2.0], [3.0, 4.0]])
            print(str(ww.T @ ww))

            xx = np.array([[1.0, 2.0], [3.0, 4.0]]).T
            print(str(np.diagonal(xx)))
            print(np.trace(xx))
            yy = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0], [7.0, 8.0, 9.0]]).T
            print(str(np.diagonal(yy, offset=-1)))

            vv = np.array([[1.0, 2.0], [3.0, 4.0]]).T
            print(str(np.concatenate([vv, vv], axis=0)))

            ab = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            row = ab[0]
            row[row > 1] = 0.0
            print(str(ab))
            """));

    [Fact]
    public void Boolean_masking_and_assignment_work_correctly_when_either_the_target_or_the_mask_is_itself_a_view()
        => Assert.Equal("[1. 3. 5.]\n[[1. 100. 200.]\n [4. 5. 6.]]", Run("""
            import numpy as np
            ss = np.array([1.0, 2.0, 3.0, 4.0, 5.0])
            full_mask = np.array([True, False, True, False, True, False, True])
            sliced_mask = full_mask[0:5]
            print(str(ss[sliced_mask]))

            tt = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            row0 = tt[0]
            row0[1:3] = np.array([100.0, 200.0])
            print(str(tt))
            """));

    [Fact]
    public void Astype_and_random_choice_read_correctly_through_a_view()
        => Assert.Equal("float64 [2. 3.]\nTrue", Run("""
            import numpy as np
            zz = np.array([1, 2, 3, 4])[1:3]
            zc = zz.astype(np.float64)
            print(zc.dtype.name, str(zc))

            np.random.seed(1)
            pool = np.array([10.0, 20.0, 30.0, 40.0, 50.0])[1:4]
            picked = np.random.choice(pool, size=10)
            print(all(v in (20.0, 30.0, 40.0) for v in picked.tolist()))
            """));
}
