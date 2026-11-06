// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using PySharpLib.Numpy;
using NDSharp;
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

/// <summary>The numpy reductions and scans run on the MathemaSharp kernels (through NDSharp). Every expression must reach the kernels and print its reference text: values as shortest
/// round-trip text, shape and Python type.</summary>
[Collection("NumpyReductionKernels")]
public class NumpyReductionKernelTests
{
    const string Setup = """
        import numpy as np
        a3 = np.sin(np.arange(1, 241).reshape(4, 5, 12) * 0.37) * 100.0
        v1000 = np.cos(np.arange(1000) * 0.001) * 3.0
        v7 = np.array([0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7])
        v129 = np.sin(np.arange(129.0))
        nan2 = np.array([[1.0, np.nan, 3.0], [4.0, 5.0, np.nan]])
        inf2 = np.array([[1.0, np.inf, 3.0], [np.inf, -np.inf, 2.0]])
        negz = np.array([-0.0])
        one = np.array([5.0])
        i3 = np.array([1, 2, 3])
        f32 = np.array([1.5, 2.5], dtype=np.float32)
        t = a3.transpose(2, 0, 1)
        sl = a3[:, ::2, :]
        z0 = np.array([])
        s0 = np.float64(3.5)
        """;

    static readonly string[] Plain =
    [
        "np.sum(a3)", "np.sum(a3, axis=0)", "np.sum(a3, axis=1)", "np.sum(a3, axis=2)", "np.sum(a3, axis=-1)", "np.sum(a3, axis=1, keepdims=True)", "np.sum(a3, keepdims=True)",
        "np.sum(v1000)", "np.sum(s0)", "np.sum(v7)", "np.sum(v129)", "np.sum(nan2)", "np.sum(inf2, axis=0)", "np.sum(negz)", "np.sum(one)", "np.sum(a3.reshape(20, 12), axis=0)",
        "np.prod(a3 / 100.0 + 1.0, axis=0)", "np.prod(v7)", "np.prod(inf2, axis=1)",
        "np.max(a3)", "np.max(a3, axis=1)", "np.max(nan2, axis=1)", "np.max(inf2)", "np.min(a3, axis=2)", "np.min(nan2)", "np.min(v1000)", "np.max(a3, axis=0, keepdims=True)", "np.amax(v129)",
        "np.mean(a3)", "np.mean(a3, axis=0)", "np.mean(a3, axis=2)", "np.mean(v1000)", "np.mean(nan2, axis=0)", "np.mean(v7)", "np.mean(a3, axis=1, keepdims=True)",
        "np.var(a3)", "np.var(a3, axis=0)", "np.var(a3, axis=2)", "np.var(a3, axis=1, ddof=1)", "np.var(v1000, ddof=2)", "np.var(v7)", "np.var(nan2, axis=1)", "np.var(one)", "np.var(a3, keepdims=True)",
        "np.std(a3)", "np.std(a3, axis=0)", "np.std(a3, axis=2, ddof=1)", "np.std(v1000)", "np.std(v129, ddof=1)", "np.std(inf2)",
        "np.cumsum(a3)", "np.cumsum(a3, axis=0)", "np.cumsum(a3, axis=1)", "np.cumsum(a3, axis=-1)", "np.cumsum(v1000)", "np.cumsum(nan2, axis=1)", "np.cumprod(v7)", "np.cumprod(a3 / 100.0 + 1.0, axis=2)",
        "a3.sum()", "a3.mean(axis=0)", "a3.std(axis=1)", "a3.var(ddof=1)", "a3.max(axis=2)", "a3.min()", "a3.cumsum(axis=1)", "v1000.sum()",
    ];

    // Other dtypes, dtype=, several axes, non-contiguous, empty, degenerate ddof, scalars: NDSharp now answers these with the kernels too (since 2026-11-06); same answers as its general engine.
    static readonly string[] Delegated =
    [
        "np.sum(i3)", "np.sum(f32)", "np.sum(a3, dtype=np.float32)", "np.sum(a3, axis=(0, 2))", "np.sum(t)", "np.sum(t, axis=0)", "np.sum(sl, axis=1)", "np.sum(z0)",
        "np.mean(i3)", "np.mean(a3, axis=(1, 2))", "np.mean(z0)", "np.max(i3)", "np.min(sl)", "np.var(one, ddof=1)", "np.var(v7, ddof=7)", "np.std(v7, ddof=8)", "np.var(t, axis=1)", "np.cumsum(i3)",
        "np.cumsum(t, axis=2)", "np.cumsum(sl)", "np.prod(i3)", "np.var(i3)",
    ];

    /// <summary>Calls the kernels answered during the last <see cref="Run"/>. NDSharp counts per execution context, so numpy calls of other
    /// test classes running in parallel do not disturb it.</summary>
    static long calls;

    static string Run(string expression)
    {
        using var scope = KernelPath.Begin();
        try
        {
            var output = new StringWriter();
            var engine = Py.NewEngine(output);
            engine.Run(Setup + $"\nr = {expression}\nx = np.asarray(r)\nprint(repr(x.tolist()), x.shape, type(r).__name__)\n");
            return output.ToString();
        }
        catch (Exception e) { return "error: " + e.GetType().Name + ": " + e.Message; }
        finally { calls = scope.Calls; }
    }

    // The reference texts (Fixtures/numpy-reductions-golden.txt) are what these expressions printed on 2026-11-06 on the kernels. At the array level the kernels were required to
    // agree bit for bit with NDSharp's former general engine (see NDSharp.Tests Golden); this file freezes what Python sees: values as shortest round-trip text, shape and type.
    static readonly Lazy<Dictionary<string, string>> Reference = new(() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "numpy-reductions-golden.txt"))
            .Select(l => l.Split('\t', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1].Replace("\\n", "\n")));

    static void Check(string expression, string actual)
    {
        if (Reference.Value.TryGetValue(expression, out var expected)) { Assert.Equal(expected, actual); return; }
        if (Environment.GetEnvironmentVariable("NDSHARP_GOLDEN") == "add" && Environment.GetEnvironmentVariable("NDSHARP_GOLDEN_FILE") is { } file)
        {
            File.AppendAllText(file, expression + "\t" + actual.Replace("\n", "\\n") + "\n");
            return;
        }
        Assert.Fail($"no reference text for '{expression}' in Fixtures/numpy-reductions-golden.txt (check it by hand, then add it with NDSHARP_GOLDEN=add and NDSHARP_GOLDEN_FILE)");
    }

    public static IEnumerable<object[]> PlainCases() => Plain.Select(e => new object[] { e });
    public static IEnumerable<object[]> DelegatedCases() => Delegated.Distinct().Select(e => new object[] { e });

    [Theory, MemberData(nameof(PlainCases))]
    public void PlainCallsRunOnTheKernelsAndKeepTheirReferenceText(string expression)
    {
        string actual = Run(expression);
        Assert.False(actual.StartsWith("error"), actual);
        Assert.True(calls > 0, $"{expression} did not reach the kernels");
        Check(expression, actual);
    }

    [Theory, MemberData(nameof(DelegatedCases))]
    public void OtherDtypesLayoutsAndAxesKeepTheirReferenceText(string expression)
    {
        string actual = Run(expression);
        Assert.True(calls > 0, $"{expression} did not reach the kernels");
        Check(expression, actual);
    }
}
