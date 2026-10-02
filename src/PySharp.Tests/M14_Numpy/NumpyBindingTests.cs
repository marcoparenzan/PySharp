// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

/// <summary>What is specific to the C# binding of numpy (registration, .NET interop, scalar
/// representation). Numerical behavior is verified against real numpy by the golden snippets in
/// <c>Oracle/numpy</c> (see OracleTests) and by NDSharp.Tests.</summary>
public class NumpyBindingTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Numpy_is_not_built_in_it_needs_the_companion_registration()
    {
        var engine = new PyEngine(new StringWriter());
        var ex = Assert.Throws<PyRaise>(() => engine.Run("import numpy"));
        Assert.Contains("numpy", ex.Message);
    }

    [Fact]
    public void Numpy_imports_and_exposes_a_version_string_and_the_submodules()
        => Assert.Equal("True\nTrue\nTrue", Run("""
            import numpy
            import numpy.linalg
            from numpy import random
            print(isinstance(numpy.__version__, str))
            print(numpy.linalg is numpy.linalg)
            print(random.rand(2).shape == (2,))
            """));

    [Fact]
    public void To_clr_produces_a_typed_NET_array_of_the_element_type()
    {
        var engine = Py.NewEngine(new StringWriter());
        var module = engine.Run("""
            import numpy as np
            result = np.array([1.0, 2.0, 3.0]).to_clr()
            result_u8 = np.array([1, 2, 3], dtype=np.uint8).to_clr()
            result_2d = np.arange(4).reshape(2, 2).to_clr()
            """);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, Assert.IsType<double[]>(Assert.IsType<ClrObject>(module.Dict["result"]).Instance));
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<byte[]>(Assert.IsType<ClrObject>(module.Dict["result_u8"]).Instance));
        var twoD = Assert.IsType<long[,]>(Assert.IsType<ClrObject>(module.Dict["result_2d"]).Instance);
        Assert.Equal(3L, twoD[1, 1]);
    }

    [Fact]
    public void Np_array_accepts_host_NET_arrays_keeping_their_element_type()
    {
        var output = new StringWriter();
        var engine = Py.NewEngine(output);
        engine.SetVariable("doubles", new[] { 1.0, 2.0, 3.0 });
        engine.SetVariable("ints", new[] { 1, 2, 3 });
        engine.SetVariable("bytes", new byte[] { 7, 8 });
        engine.Run("""
            import numpy as np
            print(np.array(doubles).dtype, np.array(ints).dtype, np.array(bytes).dtype)
            print(np.array(doubles), np.array(ints), np.asarray(bytes) + 250)
            """);
        Assert.Equal("float64 int32 uint8\n[1. 2. 3.] [1 2 3] [1 2]", output.ToString().TrimEnd('\n'));
    }

    [Fact]
    public void Float64_int64_and_bool_results_are_plain_Python_values_other_dtypes_are_numpy_scalars()
        => Assert.Equal("float\nint\nbool\nuint8\nfloat32\nTrue\nTrue", Run("""
            import numpy as np
            print(type(np.arange(3.0).sum()).__name__)
            print(type(np.arange(3).sum()).__name__)
            print(type(np.arange(3).any()).__name__)
            print(type(np.array([1, 2], dtype=np.uint8)[0]).__name__)
            print(type(np.array([1, 2], dtype=np.float32).mean()).__name__)
            print(isinstance(np.arange(3.0).sum(), np.floating))
            print(isinstance(np.array([1], dtype=np.uint8)[0], np.integer))
            """));

    [Fact]
    public void Numpy_scalars_work_where_Python_ints_and_floats_are_expected()
        => Assert.Equal("5\n[0, 1, 2, 3, 4]\n2.0\n  7\n1.50\n4", Run("""
            import math
            import numpy as np
            n = np.uint8(5)
            print(n)
            print(list(range(n)))
            print(math.sqrt(np.float32(4)))
            print(f"{np.uint8(7):3d}")
            print(f"{np.float32(1.5):.2f}")
            print([10, 20, 30, 40][np.int32(3)] // 10)
            """));

    [Fact]
    public void Numpy_exceptions_are_real_Python_exception_classes()
        => Assert.Equal("AxisError\nTrue\nTrue\nUFuncTypeError\nTrue", Run("""
            import numpy as np
            try:
                np.sum(np.zeros(3), axis=2)
            except Exception as e:
                print(type(e).__name__)
                print(isinstance(e, ValueError))
                print(isinstance(e, np.exceptions.AxisError))
            try:
                a = np.array([1, 2])
                a += 0.5
            except Exception as e:
                print(type(e).__name__)
                print(isinstance(e, TypeError))
            """));
}
