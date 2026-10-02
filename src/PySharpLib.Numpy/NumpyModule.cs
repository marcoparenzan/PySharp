// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>Assembles the <c>numpy</c> module object: functions, constants, dtype/scalar classes and
/// the <c>linalg</c>/<c>random</c>/<c>fft</c> submodules. The numerics live in NDSharp; this is only
/// the Python-facing layer.</summary>
public static class NumpyModule
{
    public const string Version = "2.5.3-pysharp (NDSharp)";

    public static PyModule Create()
    {
        var m = new PyModule("numpy");
        m.Dict["__version__"] = Version;
        foreach (var (name, fn) in NumpyFunctions.All)
            m.Dict[name] = fn;

        // Constants
        m.Dict["pi"] = Math.PI;
        m.Dict["e"] = Math.E;
        m.Dict["inf"] = double.PositiveInfinity;
        m.Dict["Inf"] = double.PositiveInfinity;
        m.Dict["PINF"] = double.PositiveInfinity;
        m.Dict["NINF"] = double.NegativeInfinity;
        m.Dict["nan"] = double.NaN;
        m.Dict["NaN"] = double.NaN;
        m.Dict["NAN"] = double.NaN;
        m.Dict["euler_gamma"] = 0.5772156649015329;
        m.Dict["newaxis"] = PyNone.Instance;
        m.Dict["mgrid"] = GridObjects.MGrid;
        m.Dict["s_"] = GridObjects.IndexExpression;

        // Classes
        m.Dict["ndarray"] = Classes.NdArray;
        m.Dict["dtype"] = Classes.DTypeClass;
        m.Dict["generic"] = Classes.Generic;
        m.Dict["number"] = Classes.Number;
        m.Dict["integer"] = Classes.Integer;
        m.Dict["signedinteger"] = Classes.SignedInteger;
        m.Dict["unsignedinteger"] = Classes.UnsignedInteger;
        m.Dict["inexact"] = Classes.Inexact;
        m.Dict["floating"] = Classes.Floating;
        m.Dict["complexfloating"] = Classes.ComplexFloating;

        foreach (var dt in DTypes.All)
            m.Dict[dt == DType.Bool ? "bool_" : dt.Name()] = Classes.ScalarClass(dt);
        m.Dict["bool"] = Classes.ScalarClass(DType.Bool);
        foreach (var (alias, dt) in new[]
        {
            ("int_", DType.Int64), ("intp", DType.Int64), ("int_", DType.Int64), ("uint", DType.UInt64), ("uintp", DType.UInt64),
            ("intc", DType.Int32), ("uintc", DType.UInt32), ("byte", DType.Int8), ("ubyte", DType.UInt8),
            ("short", DType.Int16), ("ushort", DType.UInt16), ("longlong", DType.Int64), ("ulonglong", DType.UInt64),
            ("half", DType.Float16), ("single", DType.Float32), ("double", DType.Float64), ("float_", DType.Float64),
            ("csingle", DType.Complex64), ("cdouble", DType.Complex128), ("complex_", DType.Complex128),
        })
            m.Dict[alias] = Classes.ScalarClass(dt);

        m.Dict["linalg"] = BuildLinalg();
        m.Dict["random"] = NumpyRandom.Create();
        m.Dict["fft"] = NumpyFft.Create();
        m.Dict["exceptions"] = NumpyErrors.CreateModule();
        return m;
    }

    private static PyModule BuildLinalg() => NumpyLinalg.Create();
}
