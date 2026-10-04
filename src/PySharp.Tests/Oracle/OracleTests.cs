// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.Oracle;

/// <summary>Golden-output conformance: every <c>Oracle/&lt;lib&gt;/*.py</c> snippet is run through PySharp
/// and its stdout must equal the matching <c>.expected</c> file, which was produced by running the
/// very same snippet with REAL CPython + the real library (tools/oracle/make_golden.py). A failing
/// case is therefore a real divergence from numpy/OpenCV/..., not a stale hand-typed expectation.
/// To add coverage: drop a <c>.py</c> in the folder and run make_golden.py.</summary>
public sealed class PretrainedTheoryAttribute : TheoryAttribute
{
    public PretrainedTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("PYSHARP_ORACLE_PRETRAINED") != "1")
            Skip = "downloads pretrained weights (hundreds of MB): set PYSHARP_ORACLE_PRETRAINED=1 to run";
    }
}

public class OracleTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Oracle");

    public static IEnumerable<object[]> Snippets(string lib)
        => Directory.GetFiles(Path.Combine(Root, lib), "*.py").OrderBy(x => x)
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

    public static IEnumerable<object[]> Numpy() => Snippets("numpy");
    public static IEnumerable<object[]> Cv2() => Snippets("cv2");
    public static IEnumerable<object[]> Pywt() => Snippets("pywt");
    public static IEnumerable<object[]> Matplotlib() => Snippets("matplotlib");
    public static IEnumerable<object[]> Torch() => Snippets("torch");
    public static IEnumerable<object[]> Torchvision() => Snippets("torchvision");
    public static IEnumerable<object[]> Pandas() => Snippets("pandas");

    private static void RunCase(string lib, string name)
    {
        var dir = Path.Combine(Root, lib);
        var source = File.ReadAllText(Path.Combine(dir, name + ".py"));
        var expected = File.ReadAllText(Path.Combine(dir, name + ".expected"));
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(dir);
        string actual;
        try { actual = Py.Run(source); }
        finally { Directory.SetCurrentDirectory(previous); }
        Assert.Equal(expected.Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
    }

    [Theory, MemberData(nameof(Cv2))]
    public void Cv2_snippet_matches_real_opencv(string name) => RunCase("cv2", name);

    [Theory, MemberData(nameof(Pywt))]
    public void Pywt_snippet_matches_real_pywavelets(string name) => RunCase("pywt", name);

    [Theory, MemberData(nameof(Matplotlib))]
    public void Matplotlib_snippet_matches_real_matplotlib(string name) => RunCase("matplotlib", name);

    [Theory, MemberData(nameof(Pandas))]
    public void Pandas_snippet_matches_real_pandas(string name) => RunCase("pandas", name);

    [Theory, MemberData(nameof(Torch))]
    public void Torch_snippet_matches_real_torch(string name) => RunCase("torch", name);

    /// <summary>Pretrained-model snippets download torchvision weights (tens to hundreds of MB, cached in ~/.cache/torch/hub): opt-in.</summary>
    [PretrainedTheory, MemberData(nameof(Torchvision))]
    public void Torchvision_snippet_matches_real_torchvision(string name) => RunCase("torchvision", name);

    [Theory, MemberData(nameof(Numpy))]
    public void Numpy_snippet_matches_real_numpy(string name) => RunCase("numpy", name);
}
