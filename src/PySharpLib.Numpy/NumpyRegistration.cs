// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Importing;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>Opt-in registration of the <c>numpy</c> module (and its <c>linalg</c>/<c>random</c>/<c>fft</c>
/// submodules), implemented as a Python binding over the native NDSharp array library. Call
/// <see cref="Register"/> once against a <see cref="PyEngine"/>'s <see cref="PyEngine.Importer"/> to
/// make <c>import numpy</c> work.</summary>
public static class NumpyRegistration
{
    public static void Register(Importer importer)
    {
        // The submodules are plain attributes of the one `numpy` module object, so
        // `import numpy.linalg` and `numpy.linalg` after `import numpy` give the same module.
        PyModule? numpy = null;
        PyModule Numpy() => numpy ??= NumpyModule.Create();
        importer.RegisterBuiltin("numpy", _ => Numpy());
        foreach (var sub in new[] { "linalg", "random", "fft" })
        {
            var name = sub;
            importer.RegisterBuiltin($"numpy.{name}", _ => (PyModule)Numpy().Dict[name]);
        }
    }
}
