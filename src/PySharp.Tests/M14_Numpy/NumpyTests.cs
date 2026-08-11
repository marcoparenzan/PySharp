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
}
