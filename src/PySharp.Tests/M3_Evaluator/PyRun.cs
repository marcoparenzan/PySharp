// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests;

/// <summary>Shared helper: runs Python and captures stdout.</summary>
public static class Py
{
    public static string Run(string source)
    {
        var writer = new StringWriter();
        var engine = NewEngine(writer);
        engine.Run(source, "<test>");
        return writer.ToString();
    }

    /// <summary>An engine with the opt-in companion modules the tests rely on (numpy) registered.</summary>
    public static PyEngine NewEngine(TextWriter? stdout = null)
    {
        var engine = new PyEngine(stdout);
        PySharpLib.Numpy.NumpyRegistration.Register(engine.Importer);
        PySharpLib.Cv2.Cv2Registration.Register(engine.Importer);
        PySharpLib.Pywt.PywtRegistration.Register(engine.Importer);
        PySharpLib.Matplotlib.MatplotlibRegistration.Register(engine.Importer);
        return engine;
    }

    /// <summary>Runs `print(expr)` and returns the output without the trailing newline.</summary>
    public static string Eval(string expr) => Run($"print({expr})").TrimEnd('\n');
}
