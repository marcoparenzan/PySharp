// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp.Frame;

namespace NDSharp.Frame.Tests;

public class CategoricalTests
{
    private static Column Strs(params string?[] v) => Column.FromStrings(v);

    [Fact]
    public void ToCategory_sorts_the_distinct_values_and_codes_missing_as_minus_one()
    {
        var c = Column.ToCategory(Strs("b", "a", null, "a"));
        Assert.Equal(Kind.Category, c.Kind);
        Assert.Equal(new[] { "a", "b" }, c.Categories.Strings);
        Assert.Equal(new[] { 1, 0, -1, 0 }, c.Codes);
        Assert.True(c.IsNa(2));
        Assert.Equal("a", c[3]);
        Assert.Equal("category", c.DTypeName);
    }

    [Fact]
    public void Explicit_categories_define_the_order_and_unknown_values_become_missing()
    {
        var c = Column.ToCategory(Strs("lo", "hi", "zz"), Strs("lo", "mid", "hi"), ordered: true);
        Assert.Equal(new[] { 0, 2, -1 }, c.Codes);
        Assert.True(c.Ordered);
    }

    [Fact]
    public void Ordered_comparison_uses_category_positions_and_unordered_comparison_is_rejected()
    {
        var ordered = Column.ToCategory(Strs("lo", "hi", "mid"), Strs("lo", "mid", "hi"), ordered: true);
        Assert.Equal(new[] { false, true, false }, Ops.Binary(BinOp.Gt, ordered, "mid").Bools);
        Assert.Equal(new[] { true, false, false }, Ops.Binary(BinOp.Eq, ordered, "lo").Bools);
        var plain = Column.ToCategory(Strs("a", "b"));
        Assert.Throws<FrameException>(() => Ops.Binary(BinOp.Lt, plain, "b"));
        Assert.Throws<FrameException>(() => Ops.Binary(BinOp.Add, plain, "b"));
    }

    [Fact]
    public void Setting_a_value_outside_the_categories_is_an_error()
    {
        var c = Column.ToCategory(Strs("a", "b"));
        var ok = c.WithValues(new[] { 0 }, new object?[] { "b" });
        Assert.Equal("b", ok[0]);
        var ex = Assert.Throws<FrameException>(() => c.WithValues(new[] { 0 }, new object?[] { "zz" }));
        Assert.Contains("new category (zz)", ex.Message);
    }

    [Fact]
    public void Concat_keeps_the_dtype_only_for_identical_categories()
    {
        var a = Column.ToCategory(Strs("a", "b"));
        var same = Column.Concat(new[] { a, a });
        Assert.Equal(Kind.Category, same.Kind);
        var other = Column.Concat(new[] { a, Column.ToCategory(Strs("q")) });
        Assert.Equal(Kind.Str, other.Kind);
    }

    [Fact]
    public void Series_repr_has_a_categories_footer()
    {
        var s = new Series(Column.ToCategory(Strs("b", "a"), Strs("a", "b"), ordered: true), null, "c");
        Assert.EndsWith("Name: c, dtype: category\nCategories (2, str): ['a' < 'b']", Formatter.SeriesRepr(s, new DisplayOptions()));
    }

    [Fact]
    public void Grouping_with_unobserved_categories_creates_empty_groups_in_category_order()
    {
        var key = Column.ToCategory(Strs("y", "x"), Strs("x", "y", "z"));
        var g = new Grouping(new[] { key }, new object?[] { "k" }, 2, observed: false);
        Assert.Equal(3, g.Count);
        Assert.Empty(g.Rows[2]);
        Assert.Equal(Kind.Category, g.ResultIndex().Labels.Kind);
    }

    [Fact]
    public void Cut_with_a_bin_count_nudges_the_lowest_edge_like_pandas()
    {
        var (col, bins) = Binning.Cut(new double[] { 1, 7, 5, 4, 6, 3 }, null, false, 3, true, null, false, 3, false, false, true);
        Assert.Equal(0.994, bins[0], 9);
        Assert.Equal(3, col.Categories.Length);
        Assert.Equal("(0.994, 3.0]", ((IntervalValue)col.Categories[0]!).Text());
        Assert.Equal(new[] { 0, 2, 1, 1, 2, 0 }, col.Codes);
    }

    [Fact]
    public void Cut_with_integer_edges_gives_integer_intervals_and_missing_outside()
    {
        var (col, _) = Binning.Cut(new double[] { 1, 12 }, new double[] { 0, 5, 10 }, true, null, true, null, false, 3, false, false, true);
        Assert.Equal("(0, 5]", ((IntervalValue)col.Categories[0]!).Text());
        Assert.Equal(new[] { 0, -1 }, col.Codes);
    }
}
