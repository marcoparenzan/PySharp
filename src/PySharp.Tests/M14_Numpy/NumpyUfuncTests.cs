// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 7: universal functions (ufuncs). The unary ones
/// share a single "ufunc factory" (`ApplyUfunc`, built on Phase 4's `ElementwiseUnary`) that
/// returns a real Python `float` for a scalar argument and a real array for an `ndarray` argument,
/// matching real numpy's own scalar-ufunc behavior.</summary>
public class NumpyUfuncTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void A_ufunc_on_a_scalar_returns_a_real_Python_float_not_a_0D_array()
        => Assert.Equal("2.0\nfloat", Run("""
            import numpy as np
            r = np.sqrt(4.0)
            print(r)
            print(type(r).__name__)
            """));

    [Fact]
    public void Sqrt_exp_log_log10_and_abs_work_on_arrays()
        => Assert.Equal("[2. 3. 4.]\n1.0 2.718281828459045\n0.0 2.0\n[1. 2. 3.]", Run("""
            import numpy as np
            print(str(np.sqrt(np.array([4.0, 9.0, 16.0]))))
            print(np.exp(0.0), np.exp(1.0))
            print(np.log(1.0), np.log10(100.0))
            print(str(np.abs(np.array([-1.0, 2.0, -3.0]))))
            """));

    [Fact]
    public void Trig_functions_and_their_inverses_are_real()
        => Assert.Equal("0.0 1.0\n0.0\n1.5707963268 0.0 0.7853981634", Run("""
            import numpy as np
            print(round(np.sin(0.0), 10), round(np.cos(0.0), 10))
            print(round(np.tan(0.0), 10))
            print(round(np.arcsin(1.0), 10), round(np.arccos(1.0), 10), round(np.arctan(1.0), 10))
            """));

    [Fact]
    public void Floor_ceil_sign_and_clip_work_elementwise()
        => Assert.Equal("[1. -2. 2.]\n[2. -1. 3.]\n[-1. 0. 1.]\n[0. 0. 5. 5.]", Run("""
            import numpy as np
            print(str(np.floor(np.array([1.2, -1.2, 2.7]))))
            print(str(np.ceil(np.array([1.2, -1.2, 2.7]))))
            print(str(np.sign(np.array([-5.0, 0.0, 5.0]))))
            print(str(np.clip(np.array([-5.0, 0.0, 5.0, 10.0]), 0.0, 5.0)))
            """));

    [Fact]
    public void Round_uses_real_banker_s_rounding_matching_numpy_as_both_function_and_method()
        => Assert.Equal("[1. 2. -2.]\n3.14\n[2. 2. 4.]", Run("""
            import numpy as np
            print(str(np.round(np.array([1.25, 2.5, -1.5]), 0)))
            print(np.round(3.14159, 2))
            a = np.array([1.5, 2.5, 3.5])
            print(str(a.round()))
            """));

    [Fact]
    public void Clip_is_also_a_real_ndarray_method()
        => Assert.Equal("[2. 2.5 3.]", Run("""
            import numpy as np
            a = np.array([1.5, 2.5, 3.5])
            print(str(a.clip(2.0, 3.0)))
            """));

    [Fact]
    public void Minimum_maximum_and_power_are_real_broadcasted_binary_ufuncs()
        => Assert.Equal("[1. 2. 3.]\n[4. 5. 3.]\n[1. 4. 9.]\n[1. 3.]", Run("""
            import numpy as np
            print(str(np.minimum(np.array([1.0, 5.0, 3.0]), np.array([4.0, 2.0, 3.0]))))
            print(str(np.maximum(np.array([1.0, 5.0, 3.0]), np.array([4.0, 2.0, 3.0]))))
            print(str(np.power(np.array([1.0, 2.0, 3.0]), 2)))
            print(str(np.minimum(np.array([1.0, 5.0]), 3.0)))
            """));

    [Fact]
    public void Pi_e_inf_and_nan_are_real_constants()
        => Assert.Equal("3.141592653589793 2.718281828459045 inf nan\nTrue", Run("""
            import numpy as np
            print(np.pi, np.e, np.inf, np.nan)
            print(np.inf > 1e300)
            """));

    // A real, general-purpose interpreter bug found via this exact phase (np.nan != np.nan
    // printing False): PyOps.PyEquals short-circuited `==` on reference identity, which is wrong
    // for `double` — real Python/IEEE 754 never treats NaN as equal to itself, even the same
    // object. See PyOps.cs's own comment on the fix.
    [Fact]
    public void NaN_is_never_equal_to_itself_even_when_it_is_the_exact_same_object()
        => Assert.Equal("False\nTrue\nFalse\nFalse", Run("""
            x = float("nan")
            print(x == x)
            print(x != x)
            print(float("nan") == float("nan"))
            import numpy as np
            print(np.nan == np.nan)
            """));
}
