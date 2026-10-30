// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Runtime.CompilerServices;
using NDSharp;
using PySharpLib.Interpretation;

namespace PySharpLib.Numpy;

/// <summary>
/// <c>np.set_printoptions</c> state, one per engine (pandas keeps its display options the same way). NDSharp's own
/// <see cref="PrintOptions.Default"/> is a single process-wide object, so using it here made one interpreter's
/// <c>set_printoptions(precision=2)</c> change what every other interpreter in the process printed. The settings still persist across
/// <c>Run</c> calls of the same engine, which is what a notebook needs.
/// </summary>
internal static class NumpyPrint
{
    private static readonly ConditionalWeakTable<Interp, PrintOptions> PerEngine = new();

    public static PrintOptions For(Interp interp) => PerEngine.GetValue(interp, _ => new PrintOptions());
}

