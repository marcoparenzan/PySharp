// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

/// <summary>numpy shim (see NUMPY_PLAN.md) — Phase 11: interop &amp; conveniences. `tolist()`,
/// iteration (verified here but needed no new code — the existing `__getitem__`+`IndexError`
/// generic-iterator fallback already yields scalars for 1-D / sub-arrays for N-D), `float`/`int`/
/// `bool` coercion for size-1 arrays, a seedable (but not real-numpy-bit-identical) `np.random`, and
/// the two-way `.to_clr()`/`np.array(clr_array)` bridge to real .NET arrays.</summary>
public class NumpyInteropTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Tolist_produces_real_nested_Python_lists_and_a_0D_array_produces_the_bare_scalar()
        => Assert.Equal("[1.0, 2.0, 3.0]\nlist\n[[1, 2], [3, 4]]\n5.0", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0])
            print(a.tolist())
            print(type(a.tolist()).__name__)
            print(np.array([[1, 2], [3, 4]]).tolist())
            print(np.array(5.0).tolist())
            """));

    [Fact]
    public void Tolist_round_trips_through_array_construction()
        => Assert.Equal("True", Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0])
            print(np.array(a.tolist()).tolist() == a.tolist())
            """));

    [Fact]
    public void Iterating_a_1D_array_yields_real_Python_scalars_and_an_ND_array_yields_sub_arrays()
        => Assert.Equal("1.0 float\n2.0 float\n[1. 2.]\n[3. 4.]", Run("""
            import numpy as np
            for x in np.array([1.0, 2.0]):
                print(x, type(x).__name__)
            for row in np.array([[1.0, 2.0], [3.0, 4.0]]):
                print(str(row))
            """));

    [Fact]
    public void Float_and_int_convert_a_size_one_array_and_truncate_toward_zero_for_int()
        => Assert.Equal("3.5\n3\n-3", Run("""
            import numpy as np
            print(float(np.array([3.5])))
            print(int(np.array([3.7])))
            print(int(np.array([-3.7])))
            """));

    [Fact]
    public void Bool_reflects_the_single_elements_truthiness()
        => Assert.Equal("False\nTrue", Run("""
            import numpy as np
            print(bool(np.array([0.0])))
            print(bool(np.array([5.0])))
            """));

    [Fact]
    public void Float_and_int_reject_a_multi_element_array_with_TypeError_while_bool_raises_ValueError()
        => Assert.Equal("True\nTrue", Run("""
            import numpy as np
            try:
                float(np.array([1.0, 2.0]))
                print(False)
            except TypeError:
                print(True)
            try:
                bool(np.array([1.0, 2.0]))
                print(False)
            except ValueError:
                print(True)
            """));

    [Fact]
    public void Random_seed_makes_rand_reproducible_and_values_land_in_the_documented_ranges()
        => Assert.Equal(
            "True\n(3,)\nTrue\nfloat\n(4,)\nint64\nTrue\nint\nTrue\nTrue\nTrue", Run("""
            import numpy as np
            np.random.seed(42)
            r1 = np.random.rand(3)
            np.random.seed(42)
            r2 = np.random.rand(3)
            print(str(r1) == str(r2))
            print(r1.shape)
            print(all(0.0 <= v < 1.0 for v in r1.tolist()))
            print(type(np.random.rand()).__name__)
            rn = np.random.randn(4)
            print(rn.shape)
            ri = np.random.randint(0, 10, size=5)
            print(ri.dtype.name)
            print(all(0 <= v < 10 for v in ri.tolist()))
            scalar_i = np.random.randint(5)
            print(type(scalar_i).__name__)
            print(0 <= scalar_i < 5)
            c = np.random.choice(np.array([10.0, 20.0, 30.0]), size=5)
            print(all(v in (10.0, 20.0, 30.0) for v in c.tolist()))
            c2 = np.random.choice(5, size=3, replace=False)
            print(len(set(c2.tolist())) == 3)
            """));

    [Fact]
    public void To_clr_produces_a_real_NET_double_array()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        var module = engine.Run("""
            import numpy as np
            a = np.array([1.0, 2.0, 3.0])
            result = a.to_clr()
            """);
        var clr = Assert.IsType<ClrObject>(module.Dict["result"]);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, Assert.IsType<double[]>(clr.Instance));
    }

    [Fact]
    public void Np_array_accepts_a_real_NET_double_array_injected_from_the_host()
    {
        var output = new StringWriter();
        var engine = new PyEngine(output);
        engine.SetVariable("clr_values", new[] { 1.0, 2.0, 3.0 });
        engine.Run("""
            import numpy as np
            a = np.array(clr_values)
            print(a.dtype.name)
            print(str(a))
            """);
        Assert.Equal("float64\n[1. 2. 3.]", output.ToString().TrimEnd('\n'));
    }

    [Fact]
    public void Np_array_accepts_a_real_NET_int_array_and_infers_int64()
    {
        var output = new StringWriter();
        var engine = new PyEngine(output);
        engine.SetVariable("clr_values", new[] { 1, 2, 3 });
        engine.Run("""
            import numpy as np
            a = np.array(clr_values)
            print(a.dtype.name)
            print(str(a))
            """);
        Assert.Equal("int64\n[1 2 3]", output.ToString().TrimEnd('\n'));
    }
}
