// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;

namespace NDSharp.Tests;

/// <summary>Promotion/casting rules checked against fixtures dumped from real numpy 2.5.3
/// (tools/oracle venv: <c>np.promote_types</c>, <c>np.can_cast(.., 'safe')</c>) for all 196 pairs.</summary>
public class DTypeTests
{
    private static IEnumerable<string[]> Rows(string file)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", file))
            .Where(l => l.Length > 0).Select(l => l.Split(','));

    [Fact]
    public void Promote_matches_numpy_for_every_dtype_pair()
    {
        var rows = Rows("promote_types.csv").ToList();
        Assert.Equal(196, rows.Count);
        foreach (var r in rows)
            Assert.True(DTypes.FromName(r[2]) == DTypes.Promote(DTypes.FromName(r[0]), DTypes.FromName(r[1])),
                $"promote({r[0]}, {r[1]}) expected {r[2]} got {DTypes.Promote(DTypes.FromName(r[0]), DTypes.FromName(r[1])).Name()}");
    }

    [Fact]
    public void CanCastSafely_matches_numpy_for_every_dtype_pair()
    {
        foreach (var r in Rows("can_cast_safe.csv"))
            Assert.True((r[2] == "1") == DTypes.CanCastSafely(DTypes.FromName(r[0]), DTypes.FromName(r[1])),
                $"can_cast({r[0]}, {r[1]}) expected {r[2]}");
    }

    [Theory]
    [InlineData("float", DType.Float64)]
    [InlineData("int", DType.Int64)]
    [InlineData("u1", DType.UInt8)]
    [InlineData("single", DType.Float32)]
    [InlineData("bool_", DType.Bool)]
    public void FromName_understands_aliases(string name, DType expected) => Assert.Equal(expected, DTypes.FromName(name));

    [Fact]
    public void FloatResult_and_SumResult_follow_numpy()
    {
        Assert.Equal(DType.Float16, DTypes.FloatResult(DType.UInt8));
        Assert.Equal(DType.Float32, DTypes.FloatResult(DType.Int16));
        Assert.Equal(DType.Float64, DTypes.FloatResult(DType.Int32));
        Assert.Equal(DType.Float32, DTypes.FloatResult(DType.Float32));
        Assert.Equal(DType.Int64, DTypes.SumResult(DType.Int8));
        Assert.Equal(DType.UInt64, DTypes.SumResult(DType.UInt8));
        Assert.Equal(DType.Float32, DTypes.SumResult(DType.Float32));
    }
}
