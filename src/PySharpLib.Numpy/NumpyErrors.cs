// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>numpy's own exception classes (<c>numpy.exceptions</c>, <c>numpy.linalg.LinAlgError</c>).</summary>
internal static class NumpyErrors
{
    /// <summary>numpy's AxisError derives from both ValueError and IndexError.</summary>
    public static readonly PyClass AxisError = new("AxisError", new List<PyClass> { PyErr.ValueErrorClass, PyErr.IndexErrorClass });
    public static readonly PyClass UFuncTypeError = new("UFuncTypeError", new List<PyClass> { PyErr.TypeErrorClass });
    public static readonly PyClass DTypePromotionError = new("DTypePromotionError", new List<PyClass> { PyErr.TypeErrorClass });
    public static readonly PyClass LinAlgError = new("LinAlgError", new List<PyClass> { PyErr.ValueErrorClass });

    public static PyModule CreateModule()
    {
        var m = new PyModule("numpy.exceptions");
        m.Dict["AxisError"] = AxisError;
        m.Dict["DTypePromotionError"] = DTypePromotionError;
        return m;
    }
}
