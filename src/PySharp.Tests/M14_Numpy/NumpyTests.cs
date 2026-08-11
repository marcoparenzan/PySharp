// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 0 groundwork: `import numpy` succeeds and
/// exposes `__version__`. No `ndarray` yet; later phases add their own test classes in this same
/// folder as the shim grows.</summary>
public class NumpyTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Numpy_imports_and_exposes_a_shim_version_string()
        => Assert.Equal("True\nTrue", Run("""
            import numpy
            print(isinstance(numpy.__version__, str))
            print("PySharp shim" in numpy.__version__)
            """));

    // The rest of this class uses the internal `numpy._fromflat(flat, shape)` builtin — real
    // construction (`np.array`/`np.zeros`/...) is Phase 2, not built yet; `_fromflat` exists
    // purely so these Phase 1 (attributes/repr/str) tests can build an ndarray to exercise.

    [Fact]
    public void A_1D_array_reports_real_ndim_size_shape_dtype_and_length()
        => Assert.Equal("1\n3\n(3,)\nfloat64\nfloat64\n3", Run("""
            import numpy as np
            a = np._fromflat([1.0, 2.0, 3.5], [3])
            print(a.ndim)
            print(a.size)
            print(a.shape)
            print(a.dtype)
            print(a.dtype.name)
            print(len(a))
            """));

    [Fact]
    public void A_2D_array_reports_real_ndim_size_and_shape()
        => Assert.Equal("2\n6\n(2, 3)", Run("""
            import numpy as np
            b = np._fromflat([1.0, 2.0, 3.0, 4.0, 5.0, 6.0], [2, 3])
            print(b.ndim)
            print(b.size)
            print(b.shape)
            """));

    [Fact]
    public void A_0D_scalar_array_has_size_one_and_len_raises_TypeError()
        => Assert.Equal("0\n1\nTrue", Run("""
            import numpy as np
            s = np._fromflat([42.0], [])
            print(s.ndim)
            print(s.size)
            try:
                len(s)
                print(False)
            except TypeError:
                print(True)
            """));

    [Fact]
    public void Str_formats_a_1D_array_numpy_style_space_separated_with_a_trailing_dot_on_whole_numbers()
        => Assert.Equal("[1. 2. 3.5]", Run("""
            import numpy as np
            print(str(np._fromflat([1.0, 2.0, 3.5], [3])))
            """));

    [Fact]
    public void Str_formats_a_2D_array_as_nested_aligned_bracketed_rows()
        => Assert.Equal("[[1. 2. 3.]\n [4. 5. 6.]]", Run("""
            import numpy as np
            print(str(np._fromflat([1.0, 2.0, 3.0, 4.0, 5.0, 6.0], [2, 3])))
            """));

    [Fact]
    public void Repr_wraps_the_str_formatting_in_array()
        => Assert.Equal("array([1. 2. 3.])", Run("""
            import numpy as np
            print(repr(np._fromflat([1.0, 2.0, 3.0], [3])))
            """));

    [Fact]
    public void Fromflat_rejects_a_flat_list_whose_length_does_not_match_the_given_shape()
        => Assert.Equal("True", Run("""
            import numpy as np
            try:
                np._fromflat([1.0, 2.0], [3])
                print(False)
            except ValueError:
                print(True)
            """));
}
