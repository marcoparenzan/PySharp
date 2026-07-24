// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests;

/// <summary>Shared helper: runs Python and captures stdout.</summary>
public static class Py
{
    public static string Run(string source) => PyEngine.CaptureOutput(source);

    /// <summary>Runs `print(expr)` and returns the output without the trailing newline.</summary>
    public static string Eval(string expr) => Run($"print({expr})").TrimEnd('\n');
}
