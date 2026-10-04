// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp.Frame;
using FIndex = NDSharp.Frame.Index;

namespace NDSharp.Frame.Tests;

/// <summary>Engine-level checks of the frame model. The printing rules are verified against real pandas by the oracle snippets
/// (PySharp.Tests/Oracle/pandas); these cover the C# API directly.</summary>
public class FrameCoreTests
{
    [Fact]
    public void Infer_follows_pandas_dtype_rules()
    {
        Assert.Equal("int64", Column.Infer(new object?[] { 1L, 2L }).DTypeName);
        Assert.Equal("float64", Column.Infer(new object?[] { 1L, 2.5 }).DTypeName);
        Assert.Equal("float64", Column.Infer(new object?[] { 1L, null }).DTypeName);
        Assert.Equal("bool", Column.Infer(new object?[] { true, false }).DTypeName);
        Assert.Equal("str", Column.Infer(new object?[] { "a", null }).DTypeName);
        Assert.Equal("object", Column.Infer(new object?[] { 1L, "a" }).DTypeName);
        Assert.Equal("object", Column.Infer(new object?[] { true, 1L }).DTypeName);
    }

    [Fact]
    public void Take_with_a_missing_position_promotes_int_to_float()
    {
        var c = Column.FromLongs(new long[] { 1, 2, 3 }).Take(new[] { 0, -1, 2 });
        Assert.Equal(Kind.Float, c.Kind);
        Assert.True(double.IsNaN(c.DoubleAt(1)));
    }

    [Fact]
    public void WithValues_keeps_the_dtype_when_the_value_fits_and_never_mutates_the_original()
    {
        var original = Column.FromLongs(new long[] { 1, 2, 3 });
        var same = original.WithValues(new[] { 1 }, new object?[] { 20L });
        Assert.Equal(Kind.Int, same.Kind);
        Assert.Equal(20L, same.LongAt(1));
        Assert.Equal(2L, original.LongAt(1));
        var promoted = original.WithValues(new[] { 1 }, new object?[] { 2.5 });
        Assert.Equal(Kind.Float, promoted.Kind);
    }

    [Fact]
    public void Index_lookup_treats_equal_numbers_as_the_same_label()
    {
        var ix = FIndex.Of(new object?[] { 1L, 2L, 2L });
        Assert.Equal(new[] { 1, 2 }, ix.Locs(2.0));
        Assert.False(ix.IsUnique);
        Assert.Empty(ix.Locs(7L));
    }

    [Fact]
    public void RangeIndex_slices_stay_ranges()
    {
        var r = FIndex.Range(10).Take(new[] { 2, 4, 6 });
        Assert.True(r.IsRange);
        Assert.Equal(2, r.RangeStart);
        Assert.Equal(2, r.RangeStep);
    }

    [Fact]
    public void Series_repr_matches_pandas_layout()
    {
        var s = new Series(Column.FromLongs(new long[] { 1, 22, 333 }), null, "n");
        Assert.Equal("0      1\n1     22\n2    333\nName: n, dtype: int64", Formatter.SeriesRepr(s, new DisplayOptions()));
    }

    [Fact]
    public void Float_columns_trim_zeros_together_and_switch_to_scientific_for_tiny_values()
    {
        var o = new DisplayOptions();
        Assert.Equal(new[] { " 1.0", " 2.5" }, Formatter.FormatFloats(new[] { 1.0, 2.5 }, o));
        Assert.Contains("e-", Formatter.FormatFloats(new[] { 1e-10, 1.0 }, o)[0]);
    }

    [Fact]
    public void Frame_selection_and_drop()
    {
        var df = new DataFrame(
            new[] { Column.FromLongs(new long[] { 1, 2 }), Column.FromStrings(new string?[] { "x", "y" }) },
            FIndex.OfStrings(new[] { "a", "b" }));
        Assert.Equal(2, df.NRows);
        Assert.Equal("y", df.GetColumn((object?)"b")[1]);
        var dropped = df.Drop(new object?[] { "a" }, columns: true);
        Assert.Equal(1, dropped.NCols);
        Assert.Throws<KeyNotFoundException>(() => df.GetColumn((object?)"zz"));
    }

    [Fact]
    public void Arithmetic_aligns_on_the_union_of_labels_and_promotes_to_float()
    {
        var a = new Series(Column.FromLongs(new long[] { 1, 2, 3 }), FIndex.OfStrings(new[] { "x", "y", "z" }));
        var b = new Series(Column.FromLongs(new long[] { 10, 20 }), FIndex.OfStrings(new[] { "y", "w" }));
        var r = Ops.Binary(BinOp.Add, a, b);
        Assert.Equal(new[] { "w", "x", "y", "z" }, r.Index.Items().Cast<string>());
        Assert.Equal("float64", r.DType);
        Assert.True(double.IsNaN(r.Values.DoubleAt(0)));
        Assert.Equal(12.0, r.Values.DoubleAt(2));
    }

    [Fact]
    public void Integer_division_keeps_python_floor_semantics_and_zero_divisor_goes_float()
    {
        var c = Column.FromLongs(new long[] { 7, -7 });
        Assert.Equal(new long[] { 3, -4 }, Ops.Binary(BinOp.FloorDiv, c, 2L).Longs);
        Assert.Equal(new long[] { 1, 2 }, Ops.Binary(BinOp.Mod, c, 3L).Longs);
        var z = Ops.Binary(BinOp.FloorDiv, c, 0L);
        Assert.Equal(Kind.Float, z.Kind);
        Assert.True(double.IsPositiveInfinity(z.DoubleAt(0)));
    }

    [Fact]
    public void Reductions_skip_nan_and_use_numpy_pairwise_sum()
    {
        var c = Column.FromDoubles(Enumerable.Range(1, 100).Select(i => 0.1 * i).ToArray());
        Assert.Equal(505.0, (double)Reduce.Scalar("sum", c)!);
        var withNan = Column.FromDoubles(new[] { 1.0, double.NaN, 3.0 });
        Assert.Equal(2.0, (double)Reduce.Scalar("mean", withNan)!);
        Assert.True(double.IsNaN((double)Reduce.Scalar("mean", withNan, skipna: false)!));
        Assert.Equal(2L, Reduce.Scalar("count", withNan));
    }

    [Fact]
    public void Sort_is_stable_and_places_nan_last()
    {
        var k = Column.FromDoubles(new[] { 2.0, double.NaN, 1.0, 2.0 });
        Assert.Equal(new[] { 2, 0, 3, 1 }, FrameOps.SortPositions(new[] { k }, new[] { true }));
        Assert.Equal(new[] { 0, 3, 2, 1 }, FrameOps.SortPositions(new[] { k }, new[] { false }));
    }

    [Fact]
    public void Info_text_matches_the_pandas_layout()
    {
        var df = new DataFrame(new[] { Column.FromLongs(new long[] { 1, 2 }) }, FIndex.OfStrings(new[] { "a" }));
        var text = Info.Describe(df);
        Assert.StartsWith("<class 'pandas.DataFrame'>\nRangeIndex: 2 entries, 0 to 1\nData columns (total 1 columns):\n #   Column  Non-Null Count  Dtype", text);
        Assert.Contains("memory usage: 148.0 bytes", text);
    }
}
