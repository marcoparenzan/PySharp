// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 6: reductions (`sum`/`mean`/`min`/`max`/`prod`/
/// `std`/`var`/`argmin`/`argmax`/`cumsum`/`cumprod`), each with and without `axis=`, as both
/// instance methods and module-level `np.*` functions sharing the same underlying machinery.</summary>
public class NumpyReductionTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Sum_reduces_the_whole_array_as_both_a_method_and_a_module_level_function()
        => Assert.Equal("10.0\n10.0", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            print(a.sum())
            print(np.sum(a))
            """));

    [Fact]
    public void Sum_with_axis_reduces_a_2D_array_along_each_axis_independently()
        => Assert.Equal("[5. 7. 9.]\n[6. 15.]\n[5. 7. 9.]", Run("""
            import numpy as np
            m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(str(m.sum(axis=0)))
            print(str(m.sum(axis=1)))
            print(str(np.sum(m, axis=0)))
            """));

    [Fact]
    public void A_negative_axis_counts_from_the_end_exactly_like_real_numpy()
        => Assert.Equal("[6. 15.]", Run("""
            import numpy as np
            m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(str(m.sum(axis=-1)))
            """));

    [Fact]
    public void Mean_works_over_the_whole_array_and_per_axis()
        => Assert.Equal("2.5\n[2.5 3.5 4.5]\n[2. 5.]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(a.mean())
            print(str(m.mean(axis=0)))
            print(str(m.mean(axis=1)))
            """));

    [Fact]
    public void Min_and_max_work_over_the_whole_array_and_per_axis_including_module_level()
        => Assert.Equal("1.0\n4.0\n1.0\n4.0\n[1. 2. 3.]\n[3. 6.]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(a.min())
            print(a.max())
            print(np.min(a))
            print(np.max(a))
            print(str(m.min(axis=0)))
            print(str(m.max(axis=1)))
            """));

    [Fact]
    public void Prod_std_and_var_report_real_statistics()
        => Assert.Equal("24.0\n1.118033988749895\n1.25\n[1.5 1.5 1.5]\n[0.6666666666666666 0.6666666666666666]", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(a.prod())
            print(a.std())
            print(a.var())
            print(str(m.std(axis=0)))
            print(str(m.var(axis=1)))
            """));

    [Fact]
    public void Argmin_and_argmax_return_the_first_occurrence_and_support_axis()
        => Assert.Equal("1\n4\n[0. 0. 0.]\n[2. 2.]", Run("""
            import numpy as np
            b = np.array([3.0, 1.0, 4.0, 1.0, 5.0])
            m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(b.argmin())
            print(b.argmax())
            print(str(m.argmin(axis=0)))
            print(str(m.argmax(axis=1)))
            """));

    [Fact]
    public void Cumsum_and_cumprod_work_flattened_and_per_axis()
        => Assert.Equal(
            "[1. 3. 6. 10.]\n[1. 2. 6. 24.]\n[[1. 2. 3.]\n [5. 7. 9.]]\n[[1. 3. 6.]\n [4. 9. 15.]]",
            Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0, 4.0])
            m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
            print(str(a.cumsum()))
            print(str(a.cumprod()))
            print(str(m.cumsum(axis=0)))
            print(str(m.cumsum(axis=1)))
            """));

    [Fact]
    public void Min_and_max_on_an_empty_array_raise_a_real_ValueError_with_no_identity()
        => Assert.Equal("True", Run("""
            import numpy as np
            try:
                np.array([]).min()
                print(False)
            except ValueError:
                print(True)
            """));

    [Fact]
    public void Sum_and_prod_on_an_empty_array_use_their_real_identity_instead_of_raising()
        => Assert.Equal("0.0\n1.0", Run("""
            import numpy as np
            print(np.array([]).sum())
            print(np.array([]).prod())
            """));
}
