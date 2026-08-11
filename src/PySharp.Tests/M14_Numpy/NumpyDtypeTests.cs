// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 9: dtypes &amp; promotion. Adds a real `Int64`
/// dtype (`long[]` buffer, `BigInteger` at the Python-visible boundary), `dtype=` construction,
/// `.astype()`, arithmetic promotion (`float64` &gt; `int64` &gt; `bool`, true division always
/// `float64`), Python-sign floor division/modulo, and a real bitwise mechanism for `&amp; | ^ ~`
/// that replaces the old bool-only "logical" one (bitwise on 0/1 values equals logical).</summary>
public class NumpyDtypeTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Array_infers_int64_for_all_int_lists_float64_if_any_leaf_is_float_and_bool_for_all_bool()
        => Assert.Equal("int64\n[1 2 3]\nfloat64\nbool\nfloat64", Run("""
            import numpy as np
            a = np.array([1, 2, 3])
            print(a.dtype.name)
            print(str(a))
            print(np.array([1.0, 2.0]).dtype.name)
            print(np.array([True, False]).dtype.name)
            print(np.array([1, 2.0]).dtype.name)
            """));

    [Fact]
    public void Dtype_keyword_selects_the_output_dtype_on_zeros_ones_arange_and_array_and_truncates_toward_zero()
        => Assert.Equal("int64 [0 0 0]\nint64 [1 1]\nint64 [0 1 2 3 4]\n[1 2]", Run("""
            import numpy as np
            z = np.zeros(3, dtype=np.int64)
            print(z.dtype.name, str(z))
            o = np.ones((2,), dtype='int64')
            print(o.dtype.name, str(o))
            ar = np.arange(5, dtype=np.int64)
            print(ar.dtype.name, str(ar))
            print(str(np.array([1.9, 2.1], dtype=np.int64)))
            """));

    [Fact]
    public void Astype_converts_and_always_returns_a_real_independent_copy()
        => Assert.Equal("float64 [1. 2. 3.]\nint64 [1 2 3]\n[1 2 3]", Run("""
            import numpy as np
            i = np.array([1, 2, 3])
            fi = i.astype(np.float64)
            print(fi.dtype.name, str(fi))
            back = fi.astype('int64')
            print(back.dtype.name, str(back))
            i2 = i.astype(np.int64)
            i2[0] = 999
            print(str(i))
            """));

    [Fact]
    public void Arithmetic_promotes_int_and_float_to_float_bool_and_bool_to_int_and_true_division_is_always_float()
        => Assert.Equal("[2.5 4.5 6.5]\nfloat64\nint64\n[2 2 0]\nint64\n[3 4 5]\nfloat64\n[0.5 1. 1.5]", Run("""
            import numpy as np
            ints = np.array([1, 2, 3])
            floats = np.array([1.5, 2.5, 3.5])
            print(str(ints + floats))
            print((ints + floats).dtype.name)
            bools = np.array([True, True, False])
            print((bools + bools).dtype.name)
            print(str(bools + bools))
            print((ints + 2).dtype.name)
            print(str(ints + 2))
            print((ints / 2).dtype.name)
            print(str(ints / 2))
            """));

    [Fact]
    public void Floordiv_and_mod_follow_python_sign_of_divisor_semantics_not_C_sharp_truncation()
        => Assert.Equal("[3 -4 -3]\n[1 1 -1]", Run("""
            import numpy as np
            print(str(np.array([7, -7, 8]) // np.array([2, 2, -3])))
            print(str(np.array([7, -7, 8]) % np.array([2, 2, -3])))
            """));

    [Fact]
    public void Bitwise_and_or_xor_invert_work_on_int_arrays_and_still_work_as_logical_ops_on_bool_arrays()
        => Assert.Equal("[2 8]\n[14 14]\n[12 6]\n[-1 -2 0]\n[True False False]\nbool", Run("""
            import numpy as np
            x = np.array([0b1010, 0b1100])
            y = np.array([0b0110, 0b1010])
            print(str(x & y))
            print(str(x | y))
            print(str(x ^ y))
            print(str(~np.array([0, 1, -1])))
            mtrue = np.array([True, True, False])
            mfalse = np.array([True, False, False])
            print(str(mtrue & mfalse))
            print((mtrue & mfalse).dtype.name)
            """));

    [Fact]
    public void Bitwise_ops_reject_float64_arrays_with_a_real_TypeError()
        => Assert.Equal("True", Run("""
            import numpy as np
            try:
                np.array([1.0]) & np.array([1.0])
                print(False)
            except TypeError:
                print(True)
            """));

    [Fact]
    public void Dtype_objects_are_real_singletons_and_compare_equal_to_a_matching_arrays_dtype()
        => Assert.Equal("True\nTrue\nTrue\nTrue\nint64", Run("""
            import numpy as np
            print(np.int64 is np.int64)
            print(np.array([1, 2]).dtype == np.int64)
            print(np.array([1.0]).dtype == np.float64)
            print(np.array([True]).dtype == np.bool_)
            print(np.int64.name)
            """));
}
