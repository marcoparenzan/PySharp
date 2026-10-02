// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Text.Json;
using NDSharp;

namespace NDSharp.Tests;

/// <summary>repr()/str() checked against strings dumped from real numpy 2.5.3
/// (Fixtures/format_cases.json, produced by the tools/oracle venv).</summary>
public class FormatTests
{
    private static readonly Dictionary<string, (string repr, string str)> Expected = Load();

    private static Dictionary<string, (string, string)> Load()
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "format_cases.json")));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => (p.Value.GetProperty("repr").GetString()!, p.Value.GetProperty("str").GetString()!));
    }

    private static NDArray Case(string id) => id switch
    {
        "f1" => NDArray.FromArray(new[] { 1.0, 2.0, 3.5 }),
        "i2d" => np.Reshape(np.Arange(0L, 6L), 2, 3),
        "sci" => NDArray.FromArray(new[] { 0.1, 0.25, 1e-5 }),
        "f32" => NDArray.FromArray(new[] { 1.5f, 2f, 3f, 4f }, 2, 2),
        "bool" => NDArray.FromArray(new[] { true, false }),
        "lin" => np.Linspace(0, 1, 5),
        "big" => NDArray.FromArray(new[] { 1e10, 1.0 }),
        "nan" => NDArray.FromArray(new[] { double.NaN, 1.5, double.PositiveInfinity }),
        "wrap" => np.Arange(0.0, 100.0, 1.0),
        "summ" => np.Arange(0L, 2000L),
        "u8" => np.Arange(0L, 10L, 1L, DType.UInt8),
        "d3" => np.Reshape(np.Arange(0L, 24L), 2, 3, 4),
        "empty" => np.Zeros(new[] { 0, 3 }),
        "zerod" => NDArray.Scalar(5.0),
        "third" => NDArray.FromArray(new[] { 1.0 / 3, 2.0 / 3, 1.0 }),
        "neg" => NDArray.FromArray(new[] { -1.5, 2.25, 100.0 }),
        "i8neg" => NDArray.FromArray(new long[] { -5, 10, 300 }),
        "f32third" => np.Divide(np.Arange(1.0, 5.0, 1.0, DType.Float32), NDArray.Scalar(3.0).AsType(DType.Float32)),
        _ => throw new ArgumentException(id),
    };

    public static IEnumerable<object[]> Ids => Expected.Keys.Select(k => new object[] { k });

    [Theory, MemberData(nameof(Ids))]
    public void Repr_matches_numpy(string id) => Assert.Equal(Expected[id].repr, ArrayFormat.Repr(Case(id)));

    [Theory, MemberData(nameof(Ids))]
    public void Str_matches_numpy(string id) => Assert.Equal(Expected[id].str, ArrayFormat.Str(Case(id)));
}
