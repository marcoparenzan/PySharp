using PySharpLib;

namespace PySharp.Tests;

/// <summary>Shared helper: runs Python and captures stdout.</summary>
public static class Py
{
    public static string Run(string source) => PyEngine.CaptureOutput(source);

    /// <summary>Runs `print(expr)` and returns the output without the trailing newline.</summary>
    public static string Eval(string expr) => Run($"print({expr})").TrimEnd('\n');
}
